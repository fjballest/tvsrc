# FVGs + IFVG + OTE User Manual

TradingView indicator "FVGs+IFVG+OTE5" (Pine Script v6, overlay) by franjballest. This manual describes the script as it is written in `FVGS+IFVG+OTE.txt`. Where the design document and the code differ, this manual follows the code.

## 1. Overview

The indicator works on the chart timeframe. It finds fair value gaps (FVGs) on your execution chart, watches them for inversion, and tells you when an inverted FVG (IFVG) lines up with an ICT Optimal Trade Entry (OTE) on the chart timeframe or on the next two higher timeframes. It also draws the first presented FVG of the session open, the C.E. of large candle wicks, and a set of optional filters and tags taken from Nuno's Delivery Engine.

| Element | What it is | Drawn as |
| --- | --- | --- |
| FVG | Three-candle gap on the chart timeframe, bullish or bearish | Box (or box without fill, or high / low / C.E. lines), with a dotted C.E. line when it is large enough |
| 1st presented FVG (1PFVG) | First FVG after the session open (09:30 New York, 09:00 Madrid for DAX), on 1m to 3m charts | Cyan box (blue on Mondays, see section 5) extended to the right, with C.E. |
| Inverse 1PFVG | Optional: the first FVG of the opposite bias after the 1PFVG | Dark magenta box extended to the right |
| Wick C.E. | Midpoint of a large upper or lower candle wick | Short black horizontal segment |
| IFVG candidate | The latest bullish and bearish FVG, under watch for inversion | The FVG's own box restyled in IFVG colors, thicker border, no label |
| IFVG | An FVG that a candle closed through | The same box with a label: `IFVG+` (bullish) or `IFVG-` (bearish) |
| OTE IFVG | An IFVG that matches the 0.62 - 0.79 retracement of a leg | Label `IFVG+ OTE 5m+15m` (timeframes that matched), yellow border |
| OTE bands | The OTE zone of each tracked leg | Dashed boxes to the right of the last candle, one per timeframe |
| A+ tag | Delivery Engine setup: IFVG or FVG right after a liquidity sweep / MSS | `A+` in the label or on the FVG box |
| Signals | Optional markers where the alerts fire | `LONG` / `SHORT` triangles and `OTE` diamonds |

The indicator raises alerts for IFVG signals, OTE IFVG signals and, optionally, bounces on FVG levels and wick C.E. levels. It does not place orders and does not compute stops or targets.

## 2. Quick start

1. Add "FVGs+IFVG+OTE5" to the chart.
2. Use an intraday execution chart. For the full feature set on NQ or DAX use **1m or 3m** (1PFVGs are only tracked up to 3m) or **5m** (higher timeframe OTE on 15m and 1h).
3. Start with the defaults: all FVGs drawn, 1PFVGs, wick C.E. and IFVGs on, close-mode IFVGs, OTE detection on the chart and the next two higher timeframes, IFVG alerts on, all Delivery Engine features off.
4. Create one TradingView alert with the condition **"Any alert() function call"** on this indicator (see section 12). That alert delivers the IFVG and OTE IFVG messages.
5. If the chart is too busy, change **Draw** to `IFVGs` (only IFVG boxes and OTE bands) or `1PFVGs+IFVGs`, or set **FVG filter** to `sweeps`.

### Recommended setups

| Goal | Chart | Settings to change |
| --- | --- | --- |
| NY open on NQ, all tools | 1m or 3m | Defaults (TZ `Auto` gives 09:30 New York) |
| DAX open | 1m or 3m | Defaults (TZ `Auto` detects DAX / FDX / Germany and uses 09:00 Madrid) |
| Clean IFVG execution chart | 1m to 5m | **Draw** = `IFVGs`; optionally **Alert only OTE** |
| Only FVGs around liquidity sweeps | any intraday | **FVG filter** = `sweeps` or `sweeps&mid` |
| Higher-quality FVGs | any intraday | **Displacement FVGs only**, or just **Flag displacement FVGs** to see them thicker |
| A+ setups | 1m to 15m | **A+ setups** on; optionally **A+ only with the HTF structure** |

### Chart timeframes

| Chart | 1PFVG | HTF OTE trackers | Killzone filter (A+) |
| --- | --- | --- | --- |
| Seconds, 1m, 2m, 3m, 4m | Yes (minute charts up to 3m only) | 5m and 15m | Applied |
| 5m | No | 15m and 1h | Applied |
| 15m | No | 1h and 4h | Applied |
| 1h | No | 4h and D | Ignored |
| 4h | No | D only | Ignored |
| D and above | No | None (chart only) | Ignored |

### Platform time zone

The calculations do not depend on the chart's time zone. The 1PFVG session time and the killzones use the **TZ** input of the 1st Presented FVGs section (`Auto` = Europe/Madrid for DAX, America/New_York for everything else). You can set your TradingView chart to any time zone; New York (for NQ) or Madrid / Berlin (for DAX) makes the 1PFVG easiest to read on the time axis.

## 3. Reading the chart

### Colors (defaults)

| Element | Fill | Border / line |
| --- | --- | --- |
| Bullish FVG | Orange `#FF6D00`, 90% transparent | Orange, 20% transparent |
| Bearish FVG | Red `#f7525f`, 90% transparent | Red, 20% transparent |
| Day 1PFVG | Cyan `#00fbff`, 90% transparent | Teal `#0097a7` |
| Week 1PFVG | Blue `#2962ff`, 90% transparent | Blue |
| Inverse 1PFVG | Dark magenta `#880e4f`, 80% transparent | Dark magenta |
| Bearish IFVG (`IFVG-`, from a bullish FVG) | Aqua, 80% transparent | Aqua, 20% transparent, width 2 |
| Bullish IFVG (`IFVG+`, from a bearish FVG) | Orange, 80% transparent | Orange, 20% transparent, width 2 |
| OTE IFVG | IFVG fill | Yellow border |
| OTE band, matching | Yellow, 85% transparent | Yellow, dashed |
| OTE band, not matching | Gray, 92% transparent | Gray, dashed |
| Invalidated IFVG (inversion rules, Fade) | Gray, 85% transparent | Same gray, width 1 |
| Wick C.E. | n/a | Black, solid |
| Displacement candle (optional) | Yellow candle | n/a |

Note the IFVG colors: a bullish FVG that is closed through becomes a bearish IFVG and is drawn in aqua; a bearish FVG closed through becomes a bullish IFVG and is drawn in orange. In the settings the two swatches are on the line **Bullish / Bearish IFVG colors**: the first swatch (orange) colors the bullish IFVGs (`IFVG+`, long setups, also the `LONG` marker) and the second (aqua) colors the bearish IFVGs (`IFVG-`, short setups, also the `SHORT` marker).

### Borders

- Width 1: regular FVGs and 1PFVGs.
- Width 2: IFVG candidates and IFVGs.
- Width 3 (adjustable): FVGs whose middle candle is a displacement candle, when **Flag displacement FVGs** is on.

### Labels

IFVG labels are written inside the box, black, small, right-aligned and vertically centered by default.

| Label | Meaning |
| --- | --- |
| (no text, IFVG colors) | IFVG candidate, not confirmed yet |
| `IFVG+` | Bullish IFVG confirmed: a bearish FVG was closed through to the upside |
| `IFVG-` | Bearish IFVG confirmed: a bullish FVG was closed through to the downside |
| `IFVG- ×2` | Inversion rules on: the zone was inverted a second time (the direction alternates) |
| `IFVG+ OTE 5m` | OTE IFVG; the OTE matched on the 5m swings |
| `IFVG- OTE 3m+15m` | OTE matched on more than one timeframe on the same bar |
| `... A+` | Delivery Engine A+ setup |
| `A+` on a plain FVG box | A+ FVG (born from an MSS with displacement after a sweep) |

The parts can be switched off with **Label parts: OTE TF**, **+ / -** and **A+**. With all three off an OTE IFVG reads `IFVG OTE`. **Labels** off hides all IFVG text (it is drawn fully transparent).

OTE bands carry their own small text: the timeframe tag (for example `5m`) for the retrace band, and the timeframe tag plus `OTE` (for example `15m OTE`) for a prior-leg rejection band.

Optional labels: `SWING HIGH` / `SWING LOW` (blue, swing mode with **Swing labels** on), and the `LONG` / `SHORT` and `OTE` markers (with **Show signals** and **Signals**).

## 4. Chart-timeframe FVGs

### What you see

Boxes from the first candle of each gap to the candle that mitigated it. A box stops growing when the FVG is mitigated. Open FVGs keep growing to the current bar. FVGs taller than 20 points get a dotted C.E. line through the middle.

### What it is

On every new bar the script looks at the three previous candles (candle 1 is the newest):

- **Bullish FVG**: the low of candle 1 is above the high of candle 3.
- **Bearish FVG**: the high of candle 1 is below the low of candle 3.

The gap is valid when its wick-to-wick height is larger than **Size: min** (3 points) and smaller than **max** (10000 points). The drawn zone is the wick gap, widened to a candle body when there is also a gap between bodies next to it (a suspension block, in the code's words "include suspension blocks"); this also captures volume imbalances at the edges of the gap. The C.E. (consequent encroachment) is the midpoint of the drawn zone.

An FVG is **mitigated** when a candle closes or opens beyond its far side: below the low of a bullish FVG, above the high of a bearish FVG. Its box is then fixed at that candle.

The script keeps the last **keep** (200) bullish and the last 200 bearish FVGs; older ones are deleted.

### Merging near-identical FVGs

ICT price action often prints a second FVG at almost the same place as an existing one. With **Merge tol. %** at 10, a new FVG (of either bias) whose high and low are each within 10% of its height of an existing drawn FVG is not drawn. The existing box absorbs it: it is widened to the higher high and lower low, its C.E. moves, and if it had been mitigated it is opened again. The absorbed FVG still feeds the IFVG logic, so the merged box can become the IFVG box. A box currently shown as an IFVG is never widened. Set the tolerance to 0 to turn merging off.

### FVG filter (liquidity sweeps)

With **FVG filter** = `none` every FVG is drawn. The other modes draw only the FVGs that matter around a liquidity sweep:

- A short-term high / low is a pivot with **sweep pivots L, R** candles on each side (3 / 3). A sweep is a candle that trades above the last short-term high or below the last short-term low.
- `sweeps`: on a sweep, the last FVG created before it is revealed (drawn from where it started), and the first FVG created after it is drawn when it forms. Other FVGs are hidden.
- `sweeps&mid`: also the largest FVG whose start lies near the middle of the arm, the move from the last opposite short-term pivot to the sweep (within 25% of the arm's length on each side of its middle).
- Nothing is drawn before the first sweep. The values "one FVG after the sweep" and "25%" are fixed, not inputs.
- 1PFVGs are not filtered. The close-mode IFVG logic still uses every FVG: a hidden FVG that becomes an IFVG candidate gets its own IFVG box. In swing mode, IFVG candidates come only from the drawn FVGs, so the filter also decides which FVGs can become IFVGs.

### FVG style

- `Box`: filled box with a border.
- `Box, no fill`: border only.
- `Segments (hi / lo / C.E.)`: no box, three lines (high, low, C.E.). This uses up to three lines per FVG against the script's limit of 500 lines, so fewer FVGs stay visible.

### How to use it

- In a bullish context, look for price to retrace into a bullish FVG and hold the C.E. or the far side; the reverse for bearish FVGs.
- Mitigated FVGs (closed boxes) show where an imbalance has already been traded through; the most recent of them are the IFVG candidates (section 7).
- Use **FVG bounce alerts** if you want to be told when price touches the high, low or C.E. of an open FVG and closes away from it.

## 5. 1st Presented FVGs

### What you see

A cyan box (default) for the first FVG after the session open, extended to the right with its C.E. line, plus up to five previous days' 1PFVGs. Optionally, a dark magenta inverse 1PFVG.

### What it is

- **Session time**: **Time** (default `0930`) in the **TZ** time zone. With TZ = `Auto` the script uses 09:30 America/New_York, except for symbols whose ticker or description contains `DAX`, `FDX` or `Germany`, where it uses 09:00 Europe/Madrid (the **Time** input is then ignored). With any other TZ value the **Time** input is used as typed.
- **Which FVG**: the first FVG whose three candles all opened at or after the session time, detected within the same clock hour as the session time (for 09:30, until 09:59). If no FVG forms in that hour there is no 1PFVG that day.
- **Charts**: only minute charts of 3m or lower (1m, 2m, 3m).
- **Week 1PFVG**: on Mondays the 1PFVG, bullish or bearish, is drawn in the week colors (blue).
- **Inverse 1PFVG** (**1PFVG inverse**, off by default): the first FVG of the opposite bias after the 1PFVG, in the same hour.
- **Keep** (6): number of 1PFVGs kept; when a new one appears the oldest is removed. Inverse 1PFVGs share this list.
- 1PFVGs stay extended to the right even after price trades through them, until they drop out of the list. They are not affected by the FVG filter or the displacement filter.

### How to use it

The 1PFVG is the first imbalance of the session's delivery and is used as a reference for the day: a draw on liquidity, a support / resistance, and a bias tell (price respecting it versus closing through it). If you keep several days, earlier 1PFVGs that are still unfilled are reference levels for the coming sessions. A 1PFVG can also become an IFVG: its box keeps the 1PFVG colors and extension and only gets the IFVG label.

## 6. Wick C.E.

### What you see

Short black horizontal segments at the midpoint of large candle wicks.

### What it is

When a candle closes, its upper wick (high minus body top) and lower wick (body bottom minus low) are measured. A wick larger than **Min size** (20 points) gets a line at its C.E., the midpoint between the wick tip and the body. The line is **Segment width** candles long (0 = auto: 4 candles below 15m, 1 candle from 15m up). The last **Keep** (30) lines are kept.

### How to use it

The C.E. of a long wick is where price often reacts when it returns into the wick. Use them as targets or reaction levels; turn on **alerts on bounces** to be alerted when price touches a wick C.E. and closes away from it.

## 7. IFVGs

An IFVG (inversion FVG) is an FVG that price closes through. A bullish FVG closed through to the downside becomes a bearish IFVG, expected to act as resistance on a retest; a bearish FVG closed through to the upside becomes a bullish IFVG, expected to act as support.

The IFVG never gets a second box: the FVG's own box is restyled. If the FVG is not drawn (hidden by the draw mode or the filter, or merged), a box is created for it.

### Close mode (default)

- **Candidates**: the newest bullish FVG is always the bearish IFVG candidate, and the newest bearish FVG the bullish candidate. The two candidates are shown in IFVG colors, without a label. When a newer FVG of the same bias forms, it takes over and the previous candidate is released (kept as an IFVG if it was confirmed, otherwise returned to a plain FVG).
- **Confirmation** (on a closed candle): with **IFVG confirmation** = `Close`, a close below the low of the bearish candidate confirms `IFVG-`, a close above the high of the bullish candidate confirms `IFVG+`. With `Full body`, both open and close must be beyond the FVG.
- **Signal**: the confirming candle is the signal candle: `SHORT` / `LONG` marker (if enabled) and alert `SHORT IFVG <tf>` / `LONG IFVG <tf>`.
- **Cancellation** (inversion rules off): if price then closes back through the other side (above the high of an `IFVG-`, below the low of an `IFVG+`), the label is removed and the OTE tracking stops. The candidate stays under watch and can be confirmed again.

### Swing mode (**Swings (else closes)** on)

- One IFVG is under watch at a time.
- A swing high or low is a pivot with **Swing pivot left / right** candles (10 / 3). With **Significant swing** on, a swing counts only when its distance to the last opposite swing is at least **min swing (ATR)** (4) times the ATR (**ATR length** 14).
- A significant swing high selects the last drawn bullish FVG that starts before it as the bearish IFVG candidate; a significant swing low selects the last drawn bearish FVG as the bullish candidate.
- Confirmation and cancellation follow the same close (or full body) rules, on closed candles.
- **Signal**: not on the confirming candle but on a later retest: for an `IFVG-`, a candle whose high reaches into the IFVG (at or above its low, below its high); for an `IFVG+`, a candle whose low reaches into it (at or below its high, at or above its low).
- **Swing labels** draws `SWING HIGH` / `SWING LOW` at the significant swings (they appear 3 candles late, the pivot right length).

### Kept IFVGs

With **Keep confirmed IFVGs, max** on (default, max 20), a confirmed IFVG stays drawn as an IFVG box, with its label, after its candidate role is taken over, and keeps extending to the current bar. It is returned to the plain FVG box when a candle closes back through it (above the high of an `IFVG-`, below the low of an `IFVG+`), or when there are too many (the oldest goes once the count passes the max plus 3). With the option off, only the current candidates are shown as IFVGs.

### Inversion rules (close mode only)

With **Inversion rules, max inversions** on (off by default; max 1):

- The first close through the zone is the IFVG (inversion 1).
- Each later close through the zone in the opposite direction inverts it again, up to the max: inversion 2 is an IFVG in the other direction, labelled `×2` and recolored, inversion 3 flips again, and so on. Each inversion raises the IFVG signal and alert in its direction.
- The next close through after the max **invalidates** the zone. **Invalidated IFVG** = `Fade` freezes the box in the gray fade color (it stops growing, label removed); `Back to FVG` restores the plain FVG box.
- OTE is tracked only for the first inversion.

With the default max of 1, the first close back through the IFVG invalidates it instead of just cancelling the confirmation.

### Draw modes

| Draw | Shows | Hides |
| --- | --- | --- |
| `all` | Everything | Nothing |
| `1PFVGs` | 1PFVGs; regular FVGs are discarded | Regular FVGs (IFVG candidate boxes still appear while **IFVGs** is on) |
| `IFVGs` | IFVG boxes and OTE bands | All FVGs including 1PFVGs, wick C.E., swing labels, signal markers, A+ text on FVGs |
| `1PFVGs+IFVGs` | 1PFVGs, IFVG boxes and OTE bands | Regular FVGs, wick C.E., swing labels, signal markers, A+ text on FVGs |

In the IFVG modes the hidden objects are drawn fully transparent, not deleted, and all alerts keep working.

### How to use it

- An `IFVG+` says that sellers failed to defend a bearish FVG; look for longs when price retests the IFVG from above and holds. An `IFVG-` is the mirror: shorts on a retest from below.
- The close-mode signal comes at the inversion itself, so it is an early warning: wait for the retest (or use swing mode, which signals on the retest).
- Prefer IFVGs that formed after a sweep of liquidity and in a killzone; that is what the A+ tag checks (section 9).
- Use **Full body** confirmation if you get too many inversions from candles that only close a tick beyond the FVG.

## 8. OTE IFVGs

ICT's Optimal Trade Entry is the 0.62 - 0.79 retracement of a price leg (62% to 79% of the leg, measured back from its end). The indicator flags an IFVG as an **OTE IFVG** when it lines up with an OTE on the chart timeframe or on one of the next two higher timeframes. An IFVG is flagged once. The label then lists the timeframes that matched and the border turns yellow; the `OTE` diamond and the `LONG IFVG+OTE <tf>` / `SHORT IFVG+OTE <tf>` alert fire.

Each IFVG under watch is followed separately on the chart timeframe, the next higher timeframe and the second higher timeframe, and each runs two checks.

### Check 1: prior-leg rejection (on the confirming candle)

**Prior-leg rejection** (on by default) measures the leg **before** the FVG:

- For a bearish IFVG: from the last swing high before the FVG down to the lowest low up to the FVG (the down leg that preceded it).
- For a bullish IFVG: from the last swing low before the FVG up to the highest high up to the FVG.

If the candle that confirms the IFVG wicks into that leg's OTE band and closes back away from it (below the upper OTE level for a bearish IFVG, above the lower level for a bullish one), the IFVG is flagged on the confirming candle. The IFVG itself does not need to be inside the band. A dashed band labelled `<tf> OTE` is drawn.

- **any candle**: also count the furthest wick of any candle between the FVG and the confirming candle.
- **cap wick**: the wick must not go beyond the upper OTE level (0.79). Off: it may go deeper, as long as it does not take out the leg's origin.

### Check 2: retrace into the IFVG (after the confirmation)

After the confirmation, the leg is measured from its origin to the extreme reached since the inversion:

| | Bearish IFVG | Bullish IFVG |
| --- | --- | --- |
| Leg origin | Highest high from the FVG to the confirmation (close mode); the swing high (swing mode) | Lowest low from the FVG to the confirmation (close mode); the swing low (swing mode) |
| Leg end | Lowest low since the confirmation | Highest high since the confirmation |
| OTE band | 0.62 to 0.79 of the leg measured up from its low | 0.62 to 0.79 of the leg measured down from its high |
| Trigger | A later candle's high reaches the IFVG while the IFVG matches the band | A later candle's low reaches the IFVG while the IFVG matches the band |

On the higher timeframes the leg origin is moved to the latest higher-timeframe swing when that swing lies beyond it. **match** decides when the IFVG matches the band: `Overlap` (any overlap), `Fully inside` (the whole IFVG inside the band) or `C.E. inside` (the IFVG midpoint inside the band). The band is updated every bar as the leg extends; it is yellow while the IFVG matches and gray while it does not.

### Higher-timeframe ladder

The ladder is 3m (or lower), 5m, 15m, 1h, 4h, D. The chart sits on the highest rung that is at or below its timeframe, and the next two rungs are used (see the table in section 2). **Next higher TF** and **2nd higher TF** switch them on or off. Their swings are pivots of **HTF pivot left / right** (5 / 2) higher-timeframe candles, taken only from closed higher-timeframe candles.

### OTE bands

The bands are drawn to the right of the last candle, **start** (5) candles after it and **width** (8) candles wide, and move with the chart. Up to two bands per timeframe can exist for each IFVG under watch (the retrace band and the prior-leg rejection band), and the bands of the bullish and the bearish candidate share the same space, so read the timeframe tag on each band. Bands are removed when the IFVG is cancelled or replaced.

### How to use it

- An OTE IFVG combines the two ICT ideas: the inversion shows the change in order flow, the OTE says the retracement is at a premium (for shorts) or a discount (for longs).
- A match on a higher timeframe (for example `OTE 15m` on a 3m chart) carries more weight than a chart-only match.
- If you only want to be alerted on these, turn on **Alert only OTE**.

## 9. Delivery Engine (Nuno)

These features come from Nuno's Delivery Engine (V20, updated to V29). All are off by default.

### Displacement

A candle is a **displacement candle** when its range (open to close, or high to low) is larger than **strength** (4.0) times the standard deviation of the last **Length** (100) candle ranges.

- **Displacement FVGs only**: an FVG is created only when its middle candle is a displacement candle. This applies to the drawn FVGs and to the IFVG candidates. FVGs that form in the 1PFVG window are not filtered.
- **Flag displacement FVGs, border width**: FVGs whose middle candle is a displacement candle are drawn with this border width (3), with or without the filter.
- **Color displacement candles**: colors (yellow) the middle candle of each FVG that has a displacement middle candle, with or without the filter. This coloring checks only the gap itself, not the FVG size limits.

### A+ setups

With **A+ setups** on:

- **Swing liquidity**: swing highs (buy-side liquidity) and swing lows (sell-side liquidity) are pivots with **swing length** (5) candles on each side. They stay active until price trades beyond them; that is a sweep.
- **A+ IFVG**: an `IFVG+` confirmed within **within** (50) candles after a swing low was swept, or an `IFVG-` within 50 candles after a swing high was swept. The label gets `A+`.
- **A+ FVG**: a bullish FVG born from a market structure shift (MSS) up after a swing low was swept, or a bearish FVG from an MSS down after a swing high was swept. The chart structure uses its own swings of **MSS swing length** (3) candles on each side. An MSS is a close beyond the last swing high / low against the structure, with a displacement candle in its direction (range above **MSS displacement** 2.0 standard deviations) among the last **candles** (2) candles. A break against the structure without such a candle is a CHoCH; a later break in the same direction with displacement, within 50 candles, is taken as the MSS. When the MSS happens within 50 candles after the sweep, the drawn FVGs in its direction born since the sweep, and the ones created in the next 3 candles, get `A+` on their boxes.
- **A+ only inside killzones** (on): the IFVG confirmation or the MSS must happen inside a killzone, in the TZ of the 1st Presented FVGs. Ignored on charts above 15m.
- **A+ only with the HTF structure** (off, 60 = 1h): the structure of that timeframe (last closed candle) must agree: bullish after a close above its last swing high, bearish after a close below its last swing low (swings of **swing length** candles). Ignored when the selected timeframe is not higher than the chart.

Killzones used:

| Killzone | TZ = America/New_York (and any TZ other than Madrid) | TZ = Europe/Madrid |
| --- | --- | --- |
| Asia | 20:00 - 00:00 | 23:00 - 06:00 |
| London | 02:00 - 05:00 | 08:00 - 13:00 |
| NY AM | 07:00 - 11:30 | 13:30 - 18:00 |
| NY PM | 13:30 - 16:00 | 19:00 - 22:00 |

If you type your own TZ (for example Europe/Berlin), the New York hours are used in that time zone.

### How to use it

The A+ sequence is the ICT delivery model: liquidity is taken (sweep), price shifts structure with displacement (MSS), and the FVG or IFVG left by that shift is the entry. Use the A+ tag to pick which IFVGs to trade, and the HTF option to trade only in the direction of the higher timeframe.

## 10. Breakers

This feature is on by default. It may be removed in a future version.

### What it is

- **Bullish breaker:** a swing low, a swing high, then a lower swing low. The breaker zone is the last up-close candle making the high (searched from the swing high back towards the first low). With **Zone** set to *Whole sequence*, it also includes the consecutive up-close candles before that one.
- **Bearish breaker:** a swing high, a swing low, then a higher swing high. The zone is the last down-close candle making the low, or the whole sequence of down-close candles there.
- Swings are pivots with **Swing length** (3) candles on each side, so a breaker is known 3 candles after its second low / high.

### Entries and the breaker rectangle

- **Long:** price closes above a bullish breaker zone, makes a swing high above it (a pivot of **Swing length** candles, formed after the break), and then pulls back into the zone (a low at or below the zone high).
- **Short:** price closes below a bearish breaker zone, makes a swing low below it, and then pulls back into the zone (a high at or above the zone low).
- A small move beyond the zone and straight back is not a pullback: without that swing the breaker keeps waiting.
- When, on a pullback candle, a live bullish FVG or the confirmed `IFVG+` (long), or a live bearish FVG or the confirmed `IFVG-` (short), overlaps the zone, the breaker is drawn: a dashed rectangle over the zone, with its mid line, from the breaker candle(s) to that pullback candle. It is drawn once and not extended.
- On that pullback, the label of an overlapping confirmed `IFVG+` (bullish breaker) or `IFVG-` (bearish breaker) turns **bold**, and an overlapping unmitigated FVG of the same direction that has no label (not an IFVG, no `A+`) gets the label `b+` (bullish) or `b-` (bearish).
- A drawn breaker is removed (rectangle and mid line deleted) only if the pullback itself trades below its low (bullish) or above its high (bearish), before a candle closes out of the zone again in the breaker direction. Otherwise the rectangle stays on the chart for reference.
- A breaker not drawn yet is dropped when, after the break, a candle closes through the other side of its zone. Only the last **keep** (3) breakers of each direction are followed.

### How to use it

An FVG or IFVG inside a breaker rectangle sits on a breaker that price has just come back to, after breaking it. Use the tag to prefer IFVG entries backed by a breaker in the direction of the trade.

## 11. Settings reference

Inputs on one line in the settings dialog are listed together; a field without its own label is shown as "(2nd field)" and so on. Sizes are in price points.

### Enable / Draw / Filter

| Label | Default | What it does |
| --- | --- | --- |
| 1PFVGs | On | Track and draw the 1st presented FVGs. |
| IFVGs | On | Detect, draw and alert IFVGs (needed for OTE). |
| Wick C.E. | On | Draw the C.E. of large candle wicks. |
| Draw | `all` | `all`, `1PFVGs`, `IFVGs`, `1PFVGs+IFVGs` (see section 7, Draw modes). Alerts keep working in every mode. |
| FVG filter | `none` | `none`, `sweeps`, `sweeps&mid` (see section 4). Does not filter 1PFVGs or the close-mode IFVG logic. |

### FVG Settings

| Label | Default | What it does |
| --- | --- | --- |
| Historic compute | Off | Off: keeps the last FVGs for real-time use; scroll-safe, older FVGs are discarded. On: computes FVGs up to the right edge of the visible chart; good for studying history, slower when scrolling. |
| FVG bounce alerts | Off | Alerts when price touches the high, low or C.E. of an open FVG and closes away from it. |
| Size: min | 3 (1 - 1000) | Minimum FVG height (wick gap) to be valid. |
| max | 10000 (min 100) | Ignore FVGs larger than this (bad data with huge gaps). |
| keep | 200 (2 - 500) | Number of bullish and of bearish FVGs kept. |
| Merge tol. % | 10 (0 - 50) | A new FVG whose high and low are within this % of its height of an older drawn FVG is merged into it. 0 = off. |
| sweep pivots L, R (2nd and 3rd field) | 3, 3 | Pivot lengths of the short-term highs / lows used by the FVG filter. |
| FVG style | `Box` | `Box`, `Box, no fill`, `Segments (hi / lo / C.E.)`. |
| C.E.: min height | 20 (10 - 1000) | Minimum FVG height to draw its C.E. line. |
| width | 2 (1 - 3) | C.E. line width. |
| style | `dotted` | C.E. line style: `solid`, `dashed`, `dotted`. |
| Long FVG color / border | `#FF6D00` 90% / 20% transparent | Bullish FVG fill and border. |
| Short FVG color / border | `#f7525f` 90% / 20% transparent | Bearish FVG fill and border. |

### 1st Presented FVGs

| Label | Default | What it does |
| --- | --- | --- |
| Time | `0930` | Session open, as HHMM. Ignored for DAX symbols with TZ `Auto` (09:00 is used). |
| TZ | `Auto` | Time zone of the session time and of the killzones. `Auto`: 09:00 Europe/Madrid for DAX, 09:30 America/New_York otherwise. Any TradingView time zone string (for example `America/New_York`) can be typed. |
| Keep | 6 | Number of 1PFVGs kept. |
| Day 1PFVG / border | `#00fbff` 90% / `#0097a7` 20% | Day 1PFVG fill and border. |
| Week 1PFVG / border | `#2962ff` 90% / 20% | Monday 1PFVG fill and border. |
| 1PFVG inverse | Off | Also draw the first FVG of the opposite bias after the 1PFVG. |
| color / border | `#880e4f` 80% / 20% | Inverse 1PFVG fill and border. |

### Wick Settings

| Label | Default | What it does |
| --- | --- | --- |
| Keep | 30 | Number of wick C.E. lines kept. |
| Min size | 20 | Minimum wick size to draw its C.E. |
| Segment width | 0 | Line length in candles; 0 = auto (4 below 15m, 1 otherwise). |
| C.E. line style | `solid` | `solid`, `dashed`, `dotted`. |
| (2nd field: color) | Black | Line color. |
| alerts on bounces | Off | Alert when price touches a wick C.E. and closes away from it. |

### IFVG Settings

| Label | Default | What it does |
| --- | --- | --- |
| Alerts on IFVGs | On | Enables the IFVG and OTE IFVG alerts. |
| Show signals | Off | Debug: `LONG` / `SHORT` markers where the IFVG alerts fire. |
| Labels | On | Show the text labels on the IFVG boxes. |
| Label parts: OTE TF | On | Timeframes of the OTE match in the label (for example `5m+15m`). |
| + / - | On | The sign in `IFVG+` / `IFVG-`. |
| A+ | On | The `A+` tag of the Delivery Engine setups (IFVG labels and FVG boxes). |
| Keep confirmed IFVGs, max | On, 20 (1 - 100) | Keep confirmed IFVGs drawn as IFVGs until a close back through them, up to about this number. Off: only the current candidates are shown as IFVGs. |
| IFVG confirmation | `Close` | `Close`: the close is beyond the FVG. `Full body`: open and close both beyond it. |
| Inversion rules, max inversions | Off, 1 (1 - 10) | Close mode only. Each close through the zone inverts it again up to the max; the next one invalidates it. |
| Invalidated IFVG | `Fade` | With inversion rules: `Fade` (freeze in the fade color) or `Back to FVG`. |
| (2nd field: color) | Gray, 85% transparent | Fade color. |
| Swings (else closes) | Off | On: swing mode, a single IFVG located using swings. Off: close mode. |
| Significant swing | On | Swing mode: only swings of at least min swing x ATR from the last opposite swing. |
| Swing labels | Off | Swing mode: `SWING HIGH` / `SWING LOW` labels. |
| Swing pivot left / right | 10 / 3 | Swing pivot lengths. Also used to find the leg before the FVG for the OTE prior-leg rejection. Larger values ignore smaller internal swings. |
| ATR length / min swing (ATR) | 14 / 4.0 | ATR length, and the minimum swing size in ATRs. |
| Bullish / Bearish IFVG colors | Orange / Aqua | First swatch: bullish IFVGs (`IFVG+`, `LONG` marker). Second: bearish IFVGs (`IFVG-`, `SHORT` marker). |
| Transparency: fill / border | 80 / 20 | IFVG box transparency. |
| Text size / align H, V | `small`, `right`, `center` | Label size (`auto`, `tiny`, `small`, `normal`, `huge`) and alignment. |

### IFVG + OTE (ICT Optimal Trade Entry)

| Label | Default | What it does |
| --- | --- | --- |
| Detect OTE IFVGs | On | Needs **IFVGs**. Flags confirmed IFVGs that match an OTE. |
| Alert only OTE | Off | Suppress the plain IFVG alerts; only OTE IFVG alerts fire. |
| Signals | Off | `OTE` diamond markers. |
| OTE retracement: lower | 0.62 | Lower OTE level. |
| upper | 0.79 | Upper OTE level. |
| match | `Overlap` | `Overlap`, `Fully inside`, `C.E. inside` (see section 8). |
| Prior-leg rejection | On | Check 1: confirming candle rejects the OTE of the leg before the FVG. |
| any candle | Off | Also count wicks of any candle between the FVG and the confirmation. |
| cap wick | Off | The wick must not go beyond the upper OTE level. |
| Next higher TF | On | Track the OTE on the next rung of the ladder. |
| 2nd higher TF | On | Track the OTE on the second rung. |
| HTF pivot left / right | 5 / 2 | Pivot lengths, in higher-timeframe candles. |
| Band: draw / start / width | On / 5 / 8 | Draw the OTE bands, starting this many candles right of the last candle, this many candles wide. |
| Colors: band / border / text | Yellow 85% / Yellow / Black | Matching band fill, OTE IFVG border and band border, band text. |

### Breakers

| Label | Default | What it does |
| --- | --- | --- |
| Breakers at FVG / IFVG pullbacks | on | Finds breakers and draws those that an FVG / IFVG overlaps on the pullback (section 10) |
| Zone | Last candle | *Last candle*: the last up-close (bullish) / down-close (bearish) candle making the high / low. *Whole sequence*: that candle and the consecutive ones of the same color before it |
| Swing length / keep | 3 / 3 | Pivot length (left and right) of the swings, and breakers followed per direction |
| Rectangle, (color), (style), width | on / fuchsia / dashed / 1 | Draw the breaker rectangle and its mid line, with this color, line style and width. Off: nothing is drawn, but the bold IFVG labels and the b+ / b- labels are still set |

### Delivery Engine (Nuno)

| Label | Default | What it does |
| --- | --- | --- |
| Displacement FVGs only | Off | Only FVGs whose middle candle is a displacement candle. |
| (2nd field: range) | `Open to Close` | Candle range used: `Open to Close` or `High to Low`. |
| Length / strength (x std dev) | 100 / 4.0 | Standard deviation length and multiple for a displacement candle. |
| Color displacement candles | Off, yellow | Color the middle candle of displacement FVGs. |
| Flag displacement FVGs, border width | Off, 3 (1 - 4) | Thicker border for displacement FVGs. |
| A+ setups | Off | Tag A+ IFVGs and A+ FVGs. |
| swing length / within | 5 / 50 | Pivot length of the swing liquidity; candles allowed between the sweep and the IFVG / MSS (also the CHoCH-to-MSS window). |
| MSS swing length | 3 (1 - 20) | Swing length of the structure broken by the MSS. Same as swing length = Delivery Engine V20 behaviour. |
| MSS displacement (x std dev) / candles | 2.0 / 2 | Strength of the MSS displacement candle, and how many candles before the break it may be. |
| A+ only inside killzones | On | Sweep-to-IFVG / MSS inside Asia, London, NY AM or NY PM. Ignored above 15m. |
| A+ only with the HTF structure | Off, `60` | Only A+ when the structure of that timeframe agrees. Ignored when not higher than the chart. |

## 12. Alerts

All alerts use Pine's `alert()` function. In TradingView, create an alert on this indicator with the condition **"Any alert() function call"**. Each message fires at most once per bar.

| Message | When | Enabled by |
| --- | --- | --- |
| `LONG IFVG <tf>` | IFVG+ signal: confirmation (close mode) or retest (swing mode), and each bullish inversion with inversion rules | **Alerts on IFVGs**; suppressed by **Alert only OTE** |
| `SHORT IFVG <tf>` | Same for IFVG- | Same |
| `LONG IFVG+OTE <tf>` | A bullish IFVG is flagged as OTE IFVG | **Alerts on IFVGs** and **Detect OTE IFVGs** |
| `SHORT IFVG+OTE <tf>` | Same for bearish | Same |
| `bounce on FVG hi` | Price touches the high of an open FVG and closes away | **FVG bounce alerts** |
| `bounce on fvg lo` | Same for the low | **FVG bounce alerts** |
| `bounce on CE` | Same for the C.E. | **FVG bounce alerts** |
| `bounce on wick C.E.` | Price touches a wick C.E. and closes away | **alerts on bounces** (Wick Settings) |

`<tf>` is the chart timeframe, for example `1m`, `5m`, `1h`, `D`, `30s`. A "bounce" is a fixed rule: the low (or high) within 2 points of the level and the close at least 10 points away from that low (or high). These values suit NQ and DAX; on instruments with a much smaller price scale the bounce alerts will not fire.

A prior-leg OTE rejection fires on the confirming candle, so `LONG IFVG` and `LONG IFVG+OTE` can arrive together.

## 13. Limits, performance and repainting

### Object limits

- The script allows 500 boxes, 500 lines and the default number of labels (TradingView removes the oldest objects beyond these limits).
- Boxes are shared by FVGs (up to 200 bullish and 200 bearish), 1PFVGs, IFVG boxes and OTE bands. Lines are shared by C.E. lines, wick C.E. lines and the segments style (two or three lines per FVG).
- Swing labels are limited by TradingView's default label count, so only the most recent ones remain.

### History window

- With **Historic compute** off, the chart keeps the last **keep** FVGs of each bias; when you scroll far back, older FVGs are gone. Turn it on to study history: the script then computes FVGs up to the visible right edge and recalculates when you scroll.
- A kept IFVG disappears when its FVG drops out of the **keep** window.
- The OTE leg search looks back at most about 4,000 candles and the script references up to 5,000 bars back.

### Repainting and confirmation

- FVGs are detected from closed candles only and do not repaint. With the FVG filter, an FVG can be revealed later (at the sweep), drawn from where it started.
- IFVG confirmation, swing-mode confirmation and inversions are evaluated on closed candles.
- Everything that confirms, cancels, signals or alerts is evaluated on closed candles only: IFVG confirmations, cancellations and inversions, the swing-mode retest signal, the OTE retrace trigger and the FVG / wick bounce alerts. Nothing fires before the candle closes.
- Higher-timeframe swings (OTE ladder) and the HTF structure (A+) are taken only from closed higher-timeframe candles, so they do not repaint. A higher-timeframe swing is known only after its right-side candles have closed (2 HTF candles by default).
- Swings are confirmed **right** candles after the pivot (3 for the IFVG swings, 3 for the filter and MSS, 5 for the liquidity), so swing-based decisions are made with that delay.

### Performance

The script makes three `request.security` calls (two OTE ladder timeframes and the HTF structure). Mapping a higher-timeframe swing to the chart and measuring the prior leg can scan thousands of candles, which may slow loading on long histories with many FVGs. If the indicator is slow, turn off **Historic compute**, reduce **keep**, or switch off the higher-timeframe trackers.

## 14. Troubleshooting and FAQ

**I see no 1PFVG.** The chart must be a minute chart of 3m or lower, and an FVG must form within the session hour (09:30 - 09:59 New York, 09:00 - 09:59 Madrid for DAX). Check **TZ**: with `Auto`, non-DAX symbols use New York. If your DAX symbol is not detected (ticker or description without `DAX`, `FDX` or `Germany`), set TZ to `Europe/Madrid` and Time to `0900`.

**The Monday 1PFVG is cyan, not blue.** The week color is applied by the exchange's day of week; check that the 1PFVG formed on Monday in the exchange time zone.

**There are boxes in IFVG colors without a label.** These are the IFVG candidates: the newest bullish and bearish FVG under watch. They get a label when a candle closes through them.

**I chose Draw = 1PFVGs but still see other boxes.** Regular FVGs are discarded, but the IFVG candidates and IFVGs are still drawn while **IFVGs** is on. Turn **IFVGs** off for 1PFVGs only.

**No FVGs are drawn with the FVG filter.** Nothing is shown before the first sweep, and then only one FVG on each side of each sweep. Lower **sweep pivots** for more frequent sweeps, or use `none`.

**No FVGs at all.** Check **Size: min** (3 points), and whether **Displacement FVGs only** is on: with strength 4.0 very few candles qualify. Try a lower strength (for example 2.0).

**No OTE labels.** **IFVGs** and **Detect OTE IFVGs** must be on. On D and above there are no higher-timeframe trackers. Prior-leg rejection needs a swing (Swing pivot left / right) before the FVG; with large pivot lengths there may be none nearby. Try **match** = `Overlap`.

**The OTE bands overlap.** The bands of all trackers and of both candidates are drawn in the same space right of the last candle. Read the timeframe tag, or turn off **Band: draw**.

**The IFVG label disappeared.** The IFVG was cancelled (a close back through it), invalidated (inversion rules), or replaced while **Keep confirmed IFVGs** is off.

**The Invalidated IFVG setting does nothing.** It only applies with **Inversion rules** on. Without them, a kept IFVG that is closed back through is always restored to its FVG box.

**I get both an IFVG and an IFVG+OTE alert.** Use **Alert only OTE** to keep only the OTE alerts.

**Bounce alerts never fire.** The bounce rule uses fixed 2 / 10 point distances suited to NQ and DAX.

**A+ never appears.** **A+ setups** must be on; with **A+ only inside killzones** the event must be inside a killzone in the 1PFVG TZ; with **A+ only with the HTF structure** the HTF must agree. The sweep must be at most **within** (50) candles before.

## 15. Glossary

| Term | Meaning |
| --- | --- |
| FVG | Fair value gap: a three-candle imbalance where the first and third candles' wicks do not overlap. |
| C.E. | Consequent encroachment: the midpoint of an FVG or of a wick. |
| Suspension block | Body gap next to an FVG; the drawn zone is widened to include it. |
| Mitigation | Price trading through the far side of an FVG (here, a close or open beyond it). |
| 1PFVG | First presented FVG: the first FVG after the session open. |
| IFVG | Inversion FVG: an FVG closed through, expected to act in the opposite direction. |
| OTE | Optimal Trade Entry: the 0.62 - 0.79 retracement of a leg. |
| Leg | A move from a swing high to a swing low or the reverse. |
| HTF | Higher timeframe. |
| Swing high / low, pivot | A high (low) with a given number of lower highs (higher lows) on each side. |
| Liquidity, BSL / SSL | Resting orders above swing highs (buy-side) and below swing lows (sell-side). |
| Sweep | Price trading beyond a swing high or low, taking its liquidity. |
| Displacement | A strong candle, here a range above a multiple of the standard deviation of recent ranges. |
| MSS | Market structure shift: a close beyond the last swing against the structure, with displacement. |
| CHoCH | Change of character: a break against the structure without displacement. |
| Killzone | ICT session window: Asia, London, NY AM, NY PM. |
| A+ | Delivery Engine tag for an IFVG or FVG that follows a sweep (and MSS) under the chosen conditions. |
| ATR | Average true range. |
