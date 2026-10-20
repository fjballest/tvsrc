# ICTcotas User Manual

TradingView indicator (Pine Script v6, overlay) by franjballest. This manual describes the script as it is written in `ICTcotas.txt`.

## 1. Overview

ICTcotas draws higher timeframe PD arrays (price delivery arrays) on your chart so you can trade lower timeframes against daily, weekly and monthly reference levels. It draws:

| Element | Source candles | Drawn as |
| ------- | ----- | -------------- |
| Fair Value Gap (FVG) and Inversion FVG (IFVG) | Daily, weekly, monthly | High line, low line, CE line, optional shaded rectangle, labels |
| Volume Imbalance (VI) | Daily, weekly, monthly | High line, low line, CE line, optional shaded rectangle, labels |
| Wick CE (upper and lower) | Daily, weekly, monthly; optionally 1H and 4H | One CE line with a label |
| FVG by timeframe (and its IFVG) | 15m, 1H, 4H, D, W (your choice) | Outline box, CE line, formation marker, one label |

Every level extends from the candle where it formed to a few bars to the right of the current bar. When a level is mitigated (according to the rule you choose) it is removed from the chart. FVGs that are closed through can instead turn into IFVGs and keep being drawn with the opposite direction.

The indicator computes no entry signals and has no alerts. It is a map of levels, not a strategy.

### The two layers

- **Timeframe levels** (sections General through Wick CE in the settings): FVGs, VIs and wick CEs of the daily (D), weekly (W) and monthly (M) candles, drawn as horizontal lines. Weekly lines are thicker than daily ones and monthly lines thicker still.
- **FVGs by timeframe** (the last settings section): FVGs of 15m, 1H, 4H, D and W candles drawn as boxes, each with its own colors. This layer is on by default with 1H and 4H selected. It can also add wick CE lines for 1H and 4H.

## 2. Quick start

1. Add ICTcotas to the chart (Indicators, then search for ICTcotas in your scripts).
2. Use an intraday execution chart for NQ or DAX, for example 1m, 3m, 5m or 15m. All daily, weekly and monthly levels require a chart timeframe of 1D or lower (see section 9 for monthly).
3. Leave the defaults to start: D, W and M levels on, FVG / VI / wick CE on, 8 of each kept per timeframe, levels removed when fully filled by a wick, FVGs turning into IFVGs on a close through them, and 1H and 4H FVG boxes.
4. If the chart looks crowded, untick the kinds you do not use (for example Draw VIs), or reduce **keep**.
5. Scroll the chart to the left once or twice if weekly or monthly levels are missing: the script only finds levels as higher timeframe candles complete inside the loaded chart history (section 9).

### Recommended setups

| Use | Chart | Suggested settings |
| ------- | ----- | -------------- |
| Intraday NQ / DAX execution | 1m to 5m | Defaults. Optionally tick 15m in the by-timeframe section, or set **plus the next higher** to 2 |
| Intraday bias and planning | 15m | Defaults; 1H and 4H boxes give the intraday PD arrays, lines give D/W/M |
| Swing context | 1H or 4H | Defaults; tick D and W in the by-timeframe section only if you want box versions (they duplicate the D/W lines) |

### Platform time zone

The time zone of the chart does not change any level. Daily, weekly and monthly candles are the exchange's own candles for the symbol (for CME NQ the trading day starts at 18:00 New York time; for Eurex DAX it follows the Eurex session). Setting the chart to New York time (UTC-5 / UTC-4) is useful only for reading the chart in ICT terms (midnight open, kill zones); the indicator does not depend on it.

## 3. Reading the chart

### Colors and direction

Each timeframe has a bull and a bear color. Bullish levels (support, expected to hold price from below) use the bull color; bearish levels (resistance) use the bear color.

| Timeframe | Bull (default) | Bear (default) |
| ------- | ----- | -------------- |
| Daily lines | green | red |
| Weekly lines | teal | orange |
| Monthly lines | blue | purple |
| 15m boxes | lime | maroon |
| 1H boxes | green | red |
| 4H boxes | teal | orange |
| D boxes | blue | fuchsia |
| W boxes | navy | purple |

Lower wicks count as bullish and upper wicks as bearish. An IFVG takes the color of its new (flipped) direction: a bullish daily FVG that is inverted turns red.

### Line thickness

For timeframe levels the width is the base width of the kind (default 1) plus an extra amount per timeframe step: daily +0, weekly +1, monthly +2 with the default **extra line width per timeframe step** of 1. Thicker line means higher timeframe.

### Labels

Timeframe-level labels sit just to the right of each line end and read:

`<timeframe> <name><sign> <part>`

| Example | Meaning |
| ----- | -------------- |
| `D FVG hi` | Daily FVG, upper edge |
| `W FVG CE` | Weekly FVG, midpoint |
| `M IFVG lo` | Monthly FVG that has been inverted, lower edge |
| `D VI hi` | Daily volume imbalance, upper edge |
| `W UW CE` | CE of the longest upper wick in a weekly wick group |
| `D LW CE` | CE of the longest lower wick in a daily wick group |
| `1H UW CE` | Upper wick CE from 1H candles (by-timeframe wick option) |
| `D FVG+ hi` | Same as `D FVG hi` with **direction (+/-)** on: + bullish, - bearish |

The sign is added only to FVG, IFVG and VI labels, never to wick labels. All words (D, W, M, FVG, IFVG, VI, UW, LW, hi, lo, CE) can be changed in the settings. Box labels of the by-timeframe layer are described in section 7.

## 4. Fair Value Gaps and IFVGs (D / W / M)

### What you see

A daily, weekly or monthly FVG is drawn as three horizontal lines, its high (solid), low (solid) and CE (dotted), over a lightly shaded rectangle (88 percent transparent) in the timeframe color. The lines start at the middle candle of the three-candle pattern and run to the right of the current bar.

### What it is

A Fair Value Gap is a three-candle pattern where the wicks of the first and third candle do not overlap, leaving a price range that was delivered in one direction only.

- **Bullish FVG**: the low of the newest candle is above the high of the oldest candle. The gap runs from the oldest candle's high up to the newest candle's low.
- **Bearish FVG**: the high of the newest candle is below the low of the oldest candle. The gap runs from the newest candle's high up to the oldest candle's low.

The **CE** (consequent encroachment) is the midpoint of the gap.

With **extend FVG range to include VIs with candle 2** on (default), the FVG is widened when the first or third candle also forms a volume imbalance with the middle candle (body gap with overlapping wicks, at least the VI **min size**). The FVG then covers the gap plus that body gap, and the CE is recomputed. The VI itself is still drawn separately in the Volume Imbalances layer.

### Inversion (IFVG)

With **FVG becomes IFVG on a close through it** on (default), a chart candle that closes beyond the far edge of an FVG inverts it:

- a bullish FVG inverts when a chart candle closes below its low and becomes a bearish IFVG (resistance);
- a bearish FVG inverts when a chart candle closes above its high and becomes a bullish IFVG (support).

The label changes from FVG to IFVG and the color switches to the opposite direction. While this option is on, a normal (not inverted) FVG is never removed by the mitigation rule; it only stays or inverts. The IFVG, once created, is removed by the mitigation rule in General.

Note that inversion is tested on the closes of the chart timeframe, not on the closes of the FVG's own timeframe. A daily FVG inverts on a 5m chart when a 5m candle closes through it.

### Overlapping FVGs

When several FVGs of the same timeframe and direction overlap, **overlapping FVGs** decides what stays (IFVGs are never touched by this rule):

| Option | Effect |
| ----- | -------------- |
| Most recent (default) | A new FVG removes every older one it overlaps |
| Enclosing | If an existing FVG already contains the new one, the new one is discarded. Otherwise the new one replaces all older ones it overlaps |
| Keep all | Every FVG is drawn |

### How to use it

- Bullish FVGs below price are areas where a retracement can be expected to find support; the CE is the classic ICT reaction point and the low is the line in the sand.
- Bearish FVGs above price are resistance; look for rejections at the low edge or the CE.
- An IFVG is an FVG that failed. ICT traders use it as support or resistance in the new direction, often as a retest entry after a displacement through it.
- Weekly and monthly gaps are frequently the draw on liquidity for the week or month; daily gaps are the usual intraday targets and reaction zones.

## 5. Volume Imbalances (D / W / M)

### What you see

Like FVGs: high and low lines (solid), a dotted CE line and a shaded rectangle, labelled `VI`.

### What it is

A volume imbalance is a gap between the bodies of two consecutive candles while their wicks still overlap (so it is not a full gap).

- **Bullish VI**: the bottom of the newest candle's body is above the top of the previous candle's body, and the newest candle's low is not above the previous candle's high. The range is between the two bodies.
- **Bearish VI**: the mirror image.

The gap must be at least **min size** price points.

### How to use it

VIs act like small FVGs: price often returns to the body gap and reacts at its edges or CE. On the weekly and monthly charts a VI frequently forms at the open of a new candle (weekend or month-start gaps in the body), which makes them useful targets on Mondays and the first days of a month.

## 6. Wick CE (D / W / M, plus optional 1H / 4H)

### What you see

A single dashed line at the midpoint of a candle wick, labelled `UW CE` (upper wick) or `LW CE` (lower wick). There are no high, low or fill for wick levels.

### What it is

A wick counts when it is at least **min wick %** of the candle's full range (default 30 percent). Consecutive candles with a qualifying wick on the same side form a group, and only the CE of the longest wick of the group is drawn. A single qualifying wick is its own group.

As each new candle completes:

- if its wick on that side qualifies and is longer than the one stored, the line moves to the new wick (new price and new start point);
- if it qualifies but is not longer, the stored line stays;
- if it does not qualify, the group ends and the line stays where it is.

If the level of an open group has already been removed (mitigated), a longer wick later in the same group starts a new level.

Upper wick CEs are bearish (resistance) and use the bear color; lower wick CEs are bullish (support) and use the bull color.

### How to use it

ICT treats the 50 percent of a long wick as a level where price often reacts on a return. A weekly upper wick CE above price, for example, is a reasonable target and reaction point for a retracement into the rejected area. Grouping keeps the chart clean when several candles in a row show wicks on the same side.

With the default mitigation rule (Fully filled, wick), a lower wick CE is removed when price trades down to the wick's low, and an upper wick CE when price trades up to the wick's high. With **CE touched**, the line is removed as soon as the CE is touched.

The 1H and 4H wick CE options in the last settings section add the same kind of lines for those timeframes, using the Wick CE settings (they need **Draw wick CE** on). They use the 1H / 4H box colors and their labels start with `1H` or `4H`.

## 7. FVGs by timeframe (15m / 1H / 4H / D / W)

### What you see

Each FVG of a selected timeframe is a box in that timeframe's color:

- **Outline**: solid, width 1, no fill by default (fill can be turned on).
- **CE box**: a zero-height box at the midpoint, so it looks like a dotted line across the box.
- **Formation marker**: a short vertical line inside the box, from the high to the low, at the moment the FVG formed (the close of its third candle). Everything left of the marker is the pattern itself; everything right of it is time after the FVG was confirmed.
- **Label**: by default inside the box at its right end, at CE height, for example `1H FVG` or `4H IFVG`.

Inversion, the overlap rule and the VI extension work exactly as in section 4; the **FVG becomes IFVG**, **overlapping FVGs**, **extend FVG range** and IFVG / FVG label text settings of the Fair Value Gaps section apply here too. The mitigation rule of General applies to the IFVGs.

### Choosing timeframes

A timeframe is drawn when **Draw FVGs of selected timeframes** is on and either:

- it is ticked and is equal to or higher than the chart timeframe; or
- **plus the next higher** is above 0 and it is one of that many listed timeframes strictly above the chart timeframe, counting from the nearest.

Ticked timeframes below the chart timeframe are ignored. Example: on a 5m chart with nothing ticked and **plus the next higher** set to 2, the 15m and 1H FVGs are drawn; on a 1H chart the same setting draws 4H and D. This lets one setting follow you across chart timeframes.

### Label layout

| position | Where the label sits |
| ----- | -------------- |
| Right margin | At the right end of the box, text extending to the right outside the box |
| Inside left | At the left edge, text extending to the right inside the box |
| Inside right (default) | At the right end, text extending to the left inside the box |
| Center | Centered between the left and right ends |
| Left of box | At the left edge, text extending to the left outside the box |

**at** picks the vertical anchor: High, CE (default) or Low. The label text is built from the parts you enable: timeframe (`1H`), type (`FVG` / `IFVG`) and direction (`+` / `-`), for example `4H FVG-`. The **bold** option in the Labels section also applies here.

### How to use it

The 1H and 4H boxes are the intraday PD arrays for entries and targets inside the daily range: look for price to retrace into a 1H or 4H FVG in the direction of the D/W bias, and use the CE line as the reaction level. The formation marker shows how fresh a gap is. Ticking D or W here draws box versions of the same daily and weekly FVGs already shown as lines, so keep them off unless you prefer boxes.

## 8. Settings reference

The tables follow the order of the settings dialog. Several controls often share one row in the dialog. A blank label means the control has no caption (it sits next to the previous one).

### General

| Label | Default | What it does |
| ------- | ----- | -------------- |
| Daily | on | Levels from daily candles. Chart timeframe must be 1D or lower |
| Weekly | on | Levels from weekly candles. Chart timeframe must be 1W or lower |
| Monthly | on | Levels from monthly candles. Chart timeframe must be 1M or lower |
| extra line width per timeframe step | 1 (0 to 2) | Extra width added to weekly lines (1 step) and monthly lines (2 steps) over daily ones. 0 makes all timeframes the same width. Does not affect the by-timeframe layer |
| remove when | Fully filled (wick) | Mitigation rule for all levels (see table below). FVGs follow the IFVG rule instead while it is enabled; IFVGs, VIs and wick CEs always follow this rule |

Mitigation rules (tested on every chart bar):

| Option | A bullish level is removed when | A bearish level is removed when |
| ------- | ----- | -------------- |
| Never | never | never |
| CE touched | the bar's low is at or below the CE | the bar's high is at or above the CE |
| Fully filled (wick) | the bar's low is at or below the level's low | the bar's high is at or above the level's high |
| Fully filled (close) | the bar closes below the level's low | the bar closes above the level's high |

For a wick CE, "low" and "high" are the ends of the wick.

### Colors (by timeframe)

| Label | Default | What it does |
| ------- | ----- | -------------- |
| daily: bull | green | Bullish daily FVGs, IFVGs, VIs and lower wick CEs |
| bear | red | Bearish daily levels and upper wick CEs |
| weekly: bull | teal | Bullish weekly levels |
| bear | orange | Bearish weekly levels |
| monthly: bull | blue | Bullish monthly levels |
| bear | purple | Bearish monthly levels |

Fills use these colors with the transparency set in each section.

### Labels

| Label | Default | What it does |
| ------- | ----- | -------------- |
| Show labels | on | Labels at the right end of the D/W/M lines (and 1H/4H wick CE lines) |
| bold | on | Bold label text. Also applies to the by-timeframe labels |
| direction (+/-) | off | Appends + (bullish) or - (bearish) to FVG, IFVG and VI labels |
| size | normal | tiny, small, normal, large or huge, for line labels |
| offset | 2 (min 1) | Bars between the last bar and the right end of all lines, boxes and labels |
| text: daily | D | Timeframe text for daily levels |
| weekly | W | Timeframe text for weekly levels |
| monthly | M | Timeframe text for monthly levels |
| parts: high | hi | Text for the high line |
| low | lo | Text for the low line |
| CE | CE | Text for the CE line |

### Fair Value Gaps

| Label | Default | What it does |
| ------- | ----- | -------------- |
| Draw FVGs | on | D/W/M FVGs on or off |
| keep | 8 (1 to 20) | Maximum FVGs and IFVGs kept per timeframe, both directions together. When exceeded, the oldest stored one is removed |
| min size | 0.0 | Minimum gap size in price points (before the VI extension). For NQ try 5 to 10 points to drop tiny gaps |
| hi/lo | solid | Style of the high and low lines: solid, dashed, dotted |
| (blank) | 1 | Base width of the FVG lines: 1, 2 or 3 |
| CE | dotted | Style of the CE line |
| show: hi | on | Draw the high line and its label |
| lo | on | Draw the low line and its label |
| CE | on | Draw the CE line and its label |
| do not fill rectangles | off | When checked, no shaded rectangle, only the lines |
| transp | 88 (0 to 100) | Transparency of the rectangle fill. 100 is invisible |
| label | FVG | Name used in FVG labels (also in by-timeframe labels) |
| FVG becomes IFVG on a close through it | on | Enables inversion (section 4). When off, FVGs are removed by the General mitigation rule like any other level |
| label | IFVG | Name used for inverted FVGs (also in by-timeframe labels) |
| overlapping FVGs | Most recent | Most recent, Enclosing or Keep all (section 4). Also applies to the by-timeframe layer |
| extend FVG range to include VIs with candle 2 | on | Widens an FVG to include a VI between its middle candle and its first or third candle. Also applies to the by-timeframe layer |

### Volume Imbalances

| Label | Default | What it does |
| ------- | ----- | -------------- |
| Draw VIs | on | D/W/M volume imbalances on or off |
| keep | 8 (1 to 20) | Maximum VIs kept per timeframe |
| min size | 0.0 | Minimum body gap in price points. Also the minimum for a VI to extend an FVG |
| hi/lo | solid | Style of the high and low lines |
| (blank) | 1 | Base width of the VI lines: 1, 2 or 3 |
| CE | dotted | Style of the CE line |
| show: hi | on | Draw the high line and its label |
| lo | on | Draw the low line and its label |
| CE | on | Draw the CE line and its label |
| do not fill rectangles | off | When checked, no shaded rectangle |
| transp | 88 (0 to 100) | Transparency of the rectangle fill |
| label | VI | Name used in VI labels |

### Wick CE

| Label | Default | What it does |
| ------- | ----- | -------------- |
| Draw wick CE | on | Wick CE levels on or off (also required for the 1H / 4H wick options) |
| keep | 8 (1 to 20) | Maximum wick levels kept per timeframe, upper and lower together |
| min wick % | 30.0 (1 to 100) | Minimum wick length as a percentage of the candle range for the wick to count. Higher values keep only pronounced rejection wicks |
| line | dashed | Style of the wick CE line |
| (blank) | 1 | Base width of the wick CE line: 1, 2 or 3 |
| labels: upper | UW | Name used for upper wick levels |
| lower | LW | Name used for lower wick levels |

### FVGs and wick CE by timeframe (15m / 1H / 4H / D / W)

| Label | Default | What it does |
| ------- | ----- | -------------- |
| Draw FVGs of selected timeframes | on | Turns the box layer on or off |
| plus the next higher | 0 (0 to 5) | Also draws this many of the listed timeframes above the chart timeframe, nearest first. 0 means only the ticked ones |
| 15m | off | 15-minute FVGs |
| 1H | on | 1-hour FVGs |
| 4H | on | 4-hour FVGs |
| D | off | Daily FVGs as boxes |
| W | off | Weekly FVGs as boxes |
| keep per timeframe | 6 (1 to 20) | Maximum boxes (FVG and IFVG) kept per timeframe |
| min size | 0.0 | Minimum gap size in price points |
| 15m: bull / bear | lime / maroon | Colors of 15m boxes |
| 1H: bull / bear | green / red | Colors of 1H boxes and 1H wick CE lines |
| 4H: bull / bear | teal / orange | Colors of 4H boxes and 4H wick CE lines |
| D: bull / bear | blue / fuchsia | Colors of D boxes |
| W: bull / bear | navy / purple | Colors of W boxes |
| outline | solid | Style of the box border |
| (blank) | 1 | Border width: 1, 2 or 3 (also used for the CE box; the formation marker is one thinner when above 1) |
| no fill (outline only) | on | When checked the box has no fill, only the outline and CE box |
| transp | 88 (0 to 100) | Fill transparency when the fill is on |
| formation marker | on | Vertical line inside the box at the close of the FVG's last candle |
| CE box | on | Zero-height box (a line) at the CE |
| CE outline | dotted | Style of the CE box |
| Labels | on | Box labels on or off |
| size | normal | Box label size |
| show TF | on | Include the timeframe (15m, 1H, 4H, D, W) in the label |
| FVG / IFVG | on | Include the FVG / IFVG name |
| direction (+/-) | off | Include + or - |
| position | Inside right | Right margin, Inside left, Inside right, Center, Left of box (section 7) |
| at | CE | Vertical anchor of the label: High, CE or Low |
| wick CE: 1H | off | Adds 1H wick CE lines (needs Draw wick CE on and a chart of 1H or lower) |
| 4H | off | Adds 4H wick CE lines (needs Draw wick CE on and a chart of 4H or lower) |

The by-timeframe timeframe names in labels (15m, 1H, 4H, D, W) are fixed; the **text: daily / weekly / monthly** inputs do not change them.

## 9. Alerts, limits and behaviour

### Alerts

ICTcotas defines no alerts and no alert conditions. To be alerted at a level, draw a horizontal line on it or use TradingView's price alerts.

### When levels are found (history window)

- Levels are detected only when a higher timeframe candle completes while the script runs through the chart's loaded bars. At each completion, the script looks at the last three completed candles of that timeframe once. It does not scan the higher timeframe history further back.
- So a 1m chart that has loaded about two weeks of bars will show daily levels from those two weeks, the weekly levels from two or three week closes, and monthly levels only if a month start falls inside the loaded history. Scroll left (to load more bars) or use a higher chart timeframe to build more history. The **keep** limits are a maximum, not a guarantee.
- Lines never start before the first loaded bar of the chart; a level from an older candle is clipped to the left edge of the history.
- For the by-timeframe layer the first candle seen on the chart is skipped, so detection starts at the second candle boundary.

### Repainting and confirmation

- All levels come from completed higher timeframe candles (the data request uses offsets of one, two and three candles with lookahead), so a detected level does not repaint and does not appear before its candle closes.
- Inversion into an IFVG and the **Fully filled (close)** rule are checked on chart-bar closes. On the live bar they can show intrabar and revert if the bar does not close beyond the edge; they are final when the chart bar closes. Because they use the chart timeframe's closes, the result differs between, say, a 1m and a 15m chart.
- **CE touched** and **Fully filled (wick)** trigger as soon as the live bar's high or low reaches the level.
- Objects are drawn only on the last bar, and they are updated in place as new bars arrive.

### Timeframe requirements

- Daily levels need a chart of 1D or lower, weekly 1W or lower, monthly 1M or lower. A by-timeframe timeframe is used only when it is equal to or higher than the chart (or strictly higher when added by **plus the next higher**).
- When none of Daily, Weekly and Monthly is available (all unticked, or the chart timeframe is too high), the text "ICTcotas: use a chart timeframe of 1M or lower" appears in the bottom-right corner.
- Monthly on a 1M chart: the monthly check compares the chart timeframe with 30 days. TradingView may count a month as slightly more than 30 days, in which case monthly levels (and therefore everything) are off on a 1M chart. Use 1W or lower.

### Object limits and performance

- The script may use up to 500 lines, 500 labels and 500 boxes in total. When that is exceeded, TradingView deletes the oldest objects.
- With defaults, the D/W/M layer needs at most about 168 lines, 168 labels and 48 boxes, and the 1H and 4H boxes add 24 boxes, 12 lines and 12 labels, well within limits. Setting every **keep** to 20 with all timeframes and all kinds on can exceed 500 lines and labels.
- **keep** limits apply per kind and per timeframe, counting both directions; the oldest stored level of that kind and timeframe is removed first.
- The script makes three higher timeframe data requests (D, W, M) plus one per enabled by-timeframe timeframe. Enabling all five adds load time on long histories.

## 10. Troubleshooting and FAQ

**No levels at all.** Check that the chart timeframe is 1D or lower and that at least one of Daily, Weekly and Monthly is ticked. If the bottom-right warning is shown, one of those is the cause. Also check that the chart has enough history (section 9).

**No monthly (or weekly) levels on a 1m chart.** The loaded history probably does not contain a month (or week) start. Scroll left until more bars load, or open a 15m or 1H chart.

**An FVG stays on the chart even though price has traded through it.** With **FVG becomes IFVG on a close through it** on, an FVG is not removed by wicks; it waits for a chart candle to close beyond its far edge and then becomes an IFVG. Turn the option off if you want FVGs removed by the General rule.

**A level disappeared without being touched.** The **keep** limit removed the oldest level of that kind and timeframe, or a newer overlapping FVG replaced it under the **Most recent** or **Enclosing** overlap rule.

**A wick CE line jumped to a new price.** A later candle in the same wick group had a longer wick on the same side; the line moves to the longest one.

**My 15m boxes do not appear.** The 15m timeframe is ignored on charts above 15m. Also check **Draw FVGs of selected timeframes**.

**I see the daily FVGs twice.** The D option in the by-timeframe section draws daily FVGs as boxes on top of the daily lines. Untick it, or untick **Draw FVGs** in the Fair Value Gaps section.

**IFVG results differ between chart timeframes.** Inversion is tested on chart-bar closes, so a 1m close through a level inverts it on a 1m chart while a 15m chart may not see a 15m close beyond it.

**Labels overlap the price bars.** Increase **offset** in the Labels section, reduce label **size**, or switch the box labels to **Right margin**.

**Changing the time zone changed nothing.** Correct: levels follow the exchange's daily, weekly and monthly candles, not the chart time zone.

## 11. Glossary

| Term | Meaning |
| ----- | -------------- |
| PD array | Price delivery array: any reference area (FVG, VI, wick, order block) where price is expected to react |
| FVG | Fair Value Gap: three-candle pattern where candle 1 and candle 3 wicks do not overlap |
| IFVG | Inversion FVG: an FVG that price closed through; it now acts in the opposite direction |
| VI | Volume imbalance: gap between the bodies of two consecutive candles whose wicks still overlap |
| CE | Consequent encroachment: the 50 percent midpoint of a gap or wick |
| Wick CE | Midpoint of a candle wick; here the longest wick of a run of same-side wicks |
| UW / LW | Upper wick / lower wick |
| Mitigation | Price returning to a level and using it up; the level is then removed |
| Bullish / bearish level | Support below price / resistance above price |
| Formation marker | Vertical line in a by-timeframe box at the close of the FVG's third candle |
| Draw on liquidity | The level price is expected to move toward next |
| HTF | Higher timeframe |
