# ICTlevels user manual

ICTlevels is a TradingView overlay indicator (Pine Script v6) that draws ICT time-based reference levels on intraday charts of NQ (New York time) and DAX (Madrid time). It draws levels only: it gives no buy or sell signals, plots no series and has no alerts. Reading the levels and deciding what to do with them is up to you.

This manual describes what the indicator draws, how to read each element, and every setting in the settings dialog.

## 1. Overview

On a chart of 1 hour or lower, ICTlevels can draw:

| Feature | What it draws | Needs chart timeframe |
| --- | --- | --- |
| Day and week levels | Day and week vertical separators, day high and low, equilibrium (eq), midnight open (M.O.), "day high" / "week high" labels, REHs / RELs | 1h or lower |
| Month levels | High, low and close of the last three completed months | 1h or lower |
| TGIF | 20% and 30% retracement levels of a displaced week, on Friday (or Monday) | 1h or lower |
| Sessions (killzones) | Asia, London, NY AM and NY PM highs and lows, optional rectangles | 1h or lower |
| Premarket range | Premarket high and low with start and end lines, quadrants, CE and standard deviation projections | 5m or lower |
| NDOG / NWOG | New day and new week opening gaps, optionally combined when they sit at similar prices | 1h or lower |
| RTHOG | Regular trading hours opening gap: settlement, open, CE, quadrants and standard deviations | 15m or lower |
| EQH / EQL | Unswept equal highs and lows from chart swings and from daily swings | 1h or lower |
| Status table | Active session, macro and silver bullet names, object cap note | 1h or lower |

On 2h, 4h, daily or higher charts the indicator draws nothing.

## 2. Quick start

1. Add **ICTlevels** to the chart.
2. Set the TradingView platform time zone (chart settings, or the clock at the bottom right of the chart) to the market's time zone:
    - NQ: **(UTC-4/-5) New York**
    - DAX: **(UTC+1/+2) Madrid** (any Central European zone that follows the same daylight saving rules gives the same clock)

    The script always computes its times in New York or Madrid time, whatever the chart time zone is. Setting the platform to the same zone only makes the chart clock you read match the times the script uses.
3. Leave **Zone: US / EU / Auto** on **Auto**. Auto selects EU (Madrid times) when the symbol description or ticker contains "DAX", the description contains "GERMANY" or the ticker contains "FDX"; anything else is treated as US (New York times). If your DAX symbol is named differently, set the zone to **EU** by hand.
4. Choose a chart timeframe:

| Timeframe | What you get |
| --- | --- |
| 1m to 5m | Everything, including the premarket range. Session rectangles are hidden by default at 5m and below. |
| 6m to 15m | Everything except the premarket range. RTHOG is still drawn. |
| 16m to 1h | Day, week, month, TGIF, sessions, NDOG/NWOG, EQH/EQL and the table. No premarket range, no RTHOG. |
| Above 1h | Nothing. |

5. Leave the defaults on for a first look. On 5m or lower charts the default settings keep 46 days instead of 60 (see section 7); no cap warning is shown for that.

## 3. Market times used by the script

All times below are in the script's time zone: New York for US, Madrid for EU. Windows include their start and exclude their end.

| Window | US (New York) | EU (Madrid) |
| --- | --- | --- |
| Regular trading hours (RTH, used by RTHOG) | 09:30-16:15 | 09:00-17:30 |
| New day opening gap window (NDOG/NWOG) | 17:00-18:00 | 23:00-00:00 |
| Asia session | 20:00-00:00 | 23:00-06:00 |
| London session | 02:00-05:00 | 08:00-13:00 |
| NY AM session | 07:00-11:30 | 13:30-18:00 |
| NY PM session | 13:30-16:00 | 19:00-22:00 |
| Premarket range (Auto) | 07:00-09:00 | 06:30-08:30 |
| Silver bullet | 10:00-11:00 and 14:00-15:00 | 10:00-11:00 |

Macros are ten fixed windows per market:

| # | US macro | US time | EU macro | EU time |
| --- | --- | --- | --- | --- |
| 1 | NYpre | 07:40-08:10 | Dax7 | 07:00-07:10 |
| 2 | NYAM.1 | 08:50-09:10 | Dax8 | 07:50-08:10 |
| 3 | NYAM.2 | 09:50-10:10 | DaxOpen | 08:50-09:10 |
| 4 | NYAM.3 | 10:50-11:10 | Dax10 | 09:55-10:10 |
| 5 | NY.lunch | 11:50-12:10 | Dax11 | 10:50-11:10 |
| 6 | NYPM | 13:10-13:40 | DaxTapas | 12:45-13:10 |
| 7 | NYLast | 15:15-15:30 | Dax17 | 16:50-17:10 |
| 8 | MOC | 15:45-16:00 | NYopen | 15:15-15:40 |
| 9 | LDN.1 | 02:30-03:00 | Dax19 | 18:50-19:10 |
| 10 | LDN.2 | 04:03-04:30 | DaxLunch | 13:50-14:10 |

The EU "NY" windows (NY AM, NY PM, the NYopen macro) are fixed Madrid clock times. The US and Europe change daylight saving time on different dates, so for about two weeks in spring and one week in autumn the real New York open falls one hour away from these Madrid times.

None of these windows can be changed in the settings, except the premarket range (Manual mode).

## 4. Features

### 4.1 Day and week levels

**What you see**

- **Day vertical lines**: a dotted blue vertical line at the first bar of each day. A day is a calendar day in the script's time zone, from midnight to midnight. On NQ the Sunday evening session (from 18:00) is a short day of its own.
- **Week vertical lines**: the first day of each week gets a dashed line instead of the dotted one, in the same color. The week starts with the first Sunday bar (NQ) or the first Monday bar after a Friday (DAX).
- **Day high and low** (green and red, solid): a line from the bar that made the day's extreme, extending right until price trades through it. Once taken, the line stops at the bar that took it and stays on the chart as history. The current day's lines grow with the day.
- **Day labels** on untaken highs and lows: "day high" / "day low", or "week high" / "week low" when the level is at or beyond the extremes of the earlier days of the same week. The label disappears when the level is taken.
- **Equilibrium** ("eq", dashed blue): the midpoint of the day's high and low. The current day's eq moves with the day. A completed day's eq extends through the following day and stops at its end.
- **Midnight open** ("M.O.", dashed black): the open of the first bar of the day, drawn across the day.

**How to use it**

Untaken day highs and lows are buy-side and sell-side liquidity: price tends to be drawn to them, and a sweep of a previous day's high or low is a common setup trigger. The "week high" / "week low" labels show which untaken levels are also the week's extremes so far. The midnight open is ICT's reference for the day's premium and discount: in a bullish day you look to buy below it, in a bearish day to sell above it. The previous day's equilibrium is a common reaction level the next day.

### 4.2 REH and REL labels

**What you see**

When two or more untaken day highs are within the tolerance of each other (default 0.05 x the daily ATR(14)), all their lines stay, but only the highest of them keeps a label, which then reads **day REHs** (relative equal highs). The others lose their label. Lows work the same way: the lowest keeps a **day RELs** label. When the prices are exactly equal, the most recent day keeps the label.

**How to use it**

Relative equal highs and lows are resting liquidity that price often runs before reversing. A "day REHs" label marks a cluster of stops above nearby highs; the label sits on the outermost one, which is the price that has to trade for the whole cluster to be taken.

REH/REL labels need "label unbroken day max/min" and the day max/min lines to be on.

### 4.3 Month levels

**What you see**

Three lines for each of the last three completed months: **max**, **min** and **close** (green, red, black, solid, width 3), labelled for example "max Sep", "min Sep", "close Sep". The high and low lines start at the start of their month, the close line at the start of the following month, and all extend to the right. The current month is not drawn. The lines roll over automatically when a new month starts.

**How to use it**

Previous month highs and lows are higher timeframe liquidity and targets. The monthly close is a reference for whether price is trading above or below the last monthly settlement.

### 4.4 TGIF levels

ICT's TGIF ("thank God it's Friday") idea: when the week has made a strong one-directional move (a displacement) and has not yet formed its final wick, a retracement of 20% to 30% of the week's range is expected on Friday, or on Monday when it did not happen on Friday.

**What you see**

Two dashed lines labelled **TGIF-20%** and **TGIF-30%**:

- In a bullish week they are measured down from the week high and drawn in the "down" color (red by default).
- In a bearish week they are measured up from the week low and drawn in the "up" color (green by default).

**When they appear**

- **Friday.** On the first bar of Friday the week so far (from the start of the week to Thursday's last bar) is tested. It qualifies when its body (close minus open, in the direction of the move) is at least 0.6 of its range and the close is still closer to the extreme than the 20% level, which means the final wick has not formed. If it qualifies, the levels are drawn from the start of Friday. While price has not reached the 20% level, they follow new week highs (lows), so they move up (down) with the week. Once price touches the 20% level they stay fixed.
- **Monday.** When the new week opens, the completed week is tested again. If it closed displaced and the Friday retracement did not happen (the Friday levels were not armed, or were armed in the same direction but the 20% level was never reached), the levels of the final week are drawn from the start of the new week until Tuesday starts. Monday levels are fixed.
- At any other time there are no TGIF lines. Friday's levels stay visible over the weekend until the new week opens, where they are either replaced by Monday levels or removed.

**How to use it**

The 20%-30% band is the zone where a Friday (or Monday) counter-move into the week's range is expected. Use it as a target for a retracement trade after a displaced week, or as a zone where the retracement may end and the weekly trend resume.

### 4.5 Sessions (killzones)

**What you see**

For each Asia, London, NY AM and NY PM session (times in section 3):

- **Session high and low lines** (green and red, solid, width 1) from the bar that made the extreme, extending right until price takes the level. Taken levels keep their line up to the bar that took them.
- **Labels** on untaken levels: "Asia hi", "Asia lo", "London hi", "London lo", "NY AM hi", "NY AM lo", "NY PM hi", "NY PM lo". The label disappears when the level is taken.
- **Session rectangles** (optional): a shaded box from the session start to its end, between its high and low, in the session color (Asia red, London green, NY blue) at 75% transparency. By default rectangles are hidden on 5m and lower charts.

The current session's lines and rectangle grow while it is in progress.

**How to use it**

ICT's killzones are the windows where the day's main moves tend to start. The Asia range high and low are often raided in London; the London high or low is often raided in the NY AM session. Untaken session highs and lows (the ones still labelled) are the liquidity pools to watch for a sweep and reversal, or as targets.

The "keep" count applies to all sessions together: the default of 20 is about five trading days of four sessions.

### 4.6 Premarket range

Needs a chart timeframe of 5 minutes or lower.

**What you see**

- **Start and end vertical lines** of the range, in the day line style (dotted blue).
- **Pre-mkt hi / Pre-mkt lo** lines and labels, in the session max/min style, from the extreme to the bar that takes it, stopping at the market close (16:15 New York, 17:30 Madrid).
- For the most recent range (or the last N ranges, setting "quadrants/stddevs for the last"):
  - **Quadrants**: lines at 25%, 50% and 75% of the range, labelled "PMKT dq", "PMKT ce" and "PMKT uq" with the date (for example "PMKT ce 20 Oct"). Drawn only when a quarter of the range is larger than the minimum size (default 20 points, so the range must be over 80 points).
  - **Standard deviations**: lines above the high and below the low, labelled "PMKT +1dev", "PMKT -1dev", "PMKT +2dev" and so on, 4 per side by default. The step is the range divided by 8, 4, 2 or 1: the first of these that is larger than the minimum size (default 20 points). If even the whole range is not larger than the minimum, no deviations are drawn.
  - **PMKT hi / PMKT lo**: whenever any quadrant or deviation line is drawn, the range high and low are also drawn as solid lines in the premarket color (red by default), labelled for example "PMKT hi 20 Oct".
  - These lines start a couple of bars before the end of the range (4 bars on 1m or lower, 2 bars otherwise) and extend to the right margin, but never past the market close of that day.

While the range is still forming, its lines, quadrants and deviations move with it.

**How to use it**

The premarket range (07:00-09:00 New York for NQ, 06:30-08:30 Madrid for DAX in Auto mode) is the dealing range before the open. Its CE (50%) splits it into premium and discount. Its standard deviation projections are common targets for the expansion after the open: the first and second deviations are frequent reaction and take-profit levels, and runs to the fourth deviation happen on trend days.

### 4.7 NDOG and NWOG

Opening gaps of the daily futures break. For NQ the market stops at 17:00 and reopens at 18:00 New York time. For EU the script uses the window 23:00-00:00 Madrid.

**What you see**

- **NDOG** (new day opening gap, magenta #C32E7D, width 1): two lines, at the last price before the break (settlement) and the first price after it (open), from the settlement bar to the right margin. Labels read for example "NDOG 20 Oct.hi" and "NDOG 20 Oct.lo"; the date is the date of the reopening.
- **NWOG** (new week opening gap, maroon, width 2): the same for the gap between Friday's close and the week's first open, for example "NWOG 18 Oct.hi".
- When the high and low of a gap are within the tolerance (0.05 x daily ATR by default), a single label is placed in the middle, without ".hi" / ".lo".
- **Gap rectangles** (optional, off by default): a shaded box between the two lines.

**Combined gaps** (on by default):

- Several NDOGs (or NWOGs) whose ranges overlap or lie within the tolerance of each other are drawn once, using the outer high and low of the whole group, starting at the oldest gap. The label shows how many: "NDOGx3 14 Oct", "NWOGx2 4 Oct". The date is that of the oldest gap.
- A NWOG with an NDOG (or group of NDOGs) whose high and low are both within the tolerance of the NWOG's high and low is labelled "NWOG+NDOG", "NWOG+NDOGx2", "NWOGx2+NDOG" and so on, and those NDOG lines are not drawn.

The default keeps the last 10 NDOGs and the last 10 NWOGs.

**How to use it**

Opening gaps are price ranges the market jumped over. ICT treats their high, low and midpoint as support and resistance and as magnets for price, and the NWOG in particular as a level that can hold for weeks. Combined labels highlight gaps at the same price, which reinforce each other.

### 4.8 RTHOG

The regular trading hours opening gap is the range between the settlement of the regular session (the close of the last bar before 16:15 New York / 17:30 Madrid) and the next regular open (09:30 New York / 09:00 Madrid). Needs a chart timeframe of 15 minutes or lower.

**What you see**

- **At the close**: a line at the settlement price labelled **new RTHOG settlement**, in the "this" color (purple #6B17E1).
- **From 15 minutes before the open**: in real time, the gap is drawn using the current price as a provisional open (a hint). The open, CE, quadrants and deviations follow the price until the open bar.
- **On the open bar**: the gap is fixed with the real open price:
  - The open line is labelled "RTHOG hi" (gap up) or "RTHOG lo" (gap down) with the date; the settlement line is relabelled with the other one.
  - **CE**: the midpoint of the gap, "RTHOG ce 20 Oct".
  - **Quadrants**: 25% and 75% of the gap, "RTHOG dq" and "RTHOG uq", only when a quarter of the gap is larger than the minimum size (default 25 points, so the gap must be over 100 points).
  - **Standard deviations**: "RTHOG +1dev", "RTHOG -1dev" and so on above the higher edge and below the lower edge, 4 per side by default, dotted. The step is the gap divided by 8, 4, 2 or 1, the first that is larger than the minimum size (default 25 points).
  - Optional rectangle between settlement and open.
- **After the next close**: the gap becomes the previous one. It is redrawn in the "old" color (cyan #17E1DA) and loses its quadrants and deviations, keeping settlement, CE and open.
- **Older gaps** are reduced to short ticks around the settlement and the open, labelled "rthog.cl", "rthog.ce" and "rthog.op" in black text. With "extend old RTHOG lines" the ticks at the open are extended to the right.

The script keeps up to the "keep" count of RTHOGs (10 by default) and only computes them for the last 5000 bars. Sunday bars are ignored.

**How to use it**

The RTHOG is a key ICT reference for the cash session: its CE is a frequent reaction level after the open, and a gap is often filled (price trades back to the settlement) during the session. The deviation projections of the gap are used as targets for the expansion.

### 4.9 Combined deviation labels (RTHOG and premarket)

When both the current RTHOG and the latest premarket range have deviation lines (5m or lower charts), their labels are compared. When an RTHOG deviation and a premarket deviation are within the tolerance (default 0.05 x daily ATR), one label is shown for both, for example **RTHOG +2 PMKT +3**, placed on the RTHOG line; the premarket line's label is emptied. These confluences are stronger targets than either line alone.

### 4.10 EQH and EQL

**What you see**

Bold labels **EQH** (green) and **EQL** (red) on lines at equal highs and equal lows that price has not traded through yet, and **EQH D** / **EQL D** for equal levels found on daily swings. The line starts at the first swing of the pair and extends to the right margin.

How a level is found:

- **Chart swings**: a swing high (low) is a bar whose high (low) is the highest (lowest) of the 3 bars on each side (setting "pivot"). When a new swing is within 0.10 x ATR(14) of an earlier untaken swing (setting "tolerance x ATR"), the two form an equal level, at the more extreme of the two prices.
- **3-candle swings** (on by default): a new swing also forms an EQH (EQL) with an earlier, untaken 3-candle swing (one candle on each side) whose price is exactly the same, or at most 3 points beyond it (above for highs, below for lows). The level is then drawn at the 3-candle swing's price, from that swing. This catches equal levels that the ATR tolerance or the pivot length miss, such as a small swing low followed later by a pivot low at the same price.
- **Daily swings** ("also from the daily chart"): the same logic on daily bars, with its own pivot length (3) and the daily ATR. Only completed daily bars are used.

A level is **removed** as soon as price trades beyond it: equal levels are removed when any bar trades above (below) them, even by one tick. Unconfirmed single swings get the tolerance as slack. Unlike day and session levels, swept equal levels do not remain on the chart.

The 5 most recent equal levels per side are kept (setting "keep per side").

**How to use it**

Equal highs and lows are obvious stop clusters and ICT's classic liquidity targets. Look for price to run an EQH or EQL before a reversal, or use the untaken ones as targets in the direction of the bias. Daily EQH / EQL are higher timeframe liquidity.

Equal levels appear only after the second swing is confirmed, that is 3 bars (the pivot length) after it on the chart timeframe, or a few days later for daily swings.

### 4.11 Status table and macros

**What you see**

A small table at the top right of the chart, updated on the last bar:

| Row | Content | Default color |
| --- | --- | --- |
| 1 | Name of the active session: Asia, London, NY AM or NY PM | blue |
| 2 | Name of the active macro (section 3), for example "NYAM.2" | orange |
| 3 | "S.Bullet" during a silver bullet window | red |
| 4 | "object cap: N days" when the object budget reduced a count (small, orange) | orange |

Rows are empty when nothing is active. The premarket range is not shown in the table.

Optionally ("flag macros with background color", off by default) the chart background is tinted during every macro window, on all bars, so you can see past macros on the chart.

**How to use it**

ICT macros are short windows in which the algorithm is expected to seek liquidity or rebalance an imbalance; silver bullet windows are one-hour windows for a fair value gap entry toward liquidity. The table tells you at a glance whether you are inside one.

### 4.12 Label reference

| Label | Meaning |
| --- | --- |
| day high, day low | Untaken high or low of a day |
| week high, week low | Untaken day extreme that is also the week's extreme so far |
| day REHs, day RELs | Outermost of a cluster of untaken relative equal day highs (lows) |
| eq | Day equilibrium (midpoint of high and low) |
| M.O. | Midnight open |
| max Sep, min Sep, close Sep | High, low and close of a past month |
| TGIF-20%, TGIF-30% | TGIF retracement levels |
| Asia hi, London lo, NY AM hi, NY PM lo... | Untaken session high or low |
| Pre-mkt hi, Pre-mkt lo | Untaken premarket high or low |
| PMKT hi / lo / dq / ce / uq + date | Premarket range high, low, 25%, 50%, 75% |
| PMKT +1dev, PMKT -1dev... | Premarket standard deviation projections |
| NDOG 20 Oct.hi / .lo | New day opening gap edges (date of the reopening) |
| NWOG 18 Oct.hi / .lo | New week opening gap edges |
| NDOGx3, NWOGx2 | Several gaps combined into one level |
| NWOG+NDOG | NWOG with NDOGs at the same levels |
| new RTHOG settlement | Settlement of the regular session, gap not yet open |
| RTHOG hi / lo / ce / dq / uq + date | Edges, midpoint and quadrants of the RTHOG |
| RTHOG +1dev, RTHOG -1dev... | RTHOG standard deviation projections |
| RTHOG +2 PMKT +3 | An RTHOG deviation and a premarket deviation at the same price |
| rthog.cl, rthog.ce, rthog.op | Ticks of older RTHOGs: settlement, midpoint, open |
| EQH, EQL | Untaken equal highs or lows on the chart timeframe |
| EQH D, EQL D | Untaken equal highs or lows on daily swings |

## 5. Settings reference

Settings are listed in the order they appear in the settings dialog. Several settings share a row; a setting with no label of its own is shown as "(no label)" and sits on the same row as the setting above it. Line styles are solid, dashed or dotted; widths are 1, 2 or 3; text sizes are tiny, small, normal, large or huge.

### 5.1 Time & Settings

| Label in dialog | Default | What it does |
| --- | --- | --- |
| Zone: US / EU / Auto | Auto | US uses New York times (NQ), EU uses Madrid times (DAX). Auto picks EU when the symbol looks like DAX (see Quick start), otherwise US. Changes every time window in section 3. |
| compute for historic data use | off | Off: levels are computed up to the last bar, for live trading. On: levels are computed up to the rightmost visible bar, so you can scroll back and see the levels as they were at that point. The script recomputes every time you scroll. |
| offset for margin labels | 2 (min 1) | Number of bars to the right of the last bar where margin labels and line ends are placed: gaps, RTHOG, premarket quadrants and deviations, TGIF, EQH/EQL and month labels. Day, session and premarket high/low labels and the eq and M.O. labels are always 2 bars to the right. |
| show object cap warning | on | Shows "object cap: N days" in the table when the day, session or gap counts were reduced to fit the 500-object limits. The day cap on 5m and lower charts is not warned about. |

### 5.2 Month Settings

| Label in dialog | Default | What it does |
| --- | --- | --- |
| Draw max/min/close for last 3 months | on | Draws the high, low and close of the last three completed months. |
| max | green | Color of the month high lines and labels. |
| min | red | Color of the month low lines and labels. |
| close | black | Color of the month close lines and labels. |
| line style | solid | Style of the month lines. |
| (no label, width) | 3 | Width of the month lines. |

### 5.3 Day Settings

| Label in dialog | Default | What it does |
| --- | --- | --- |
| Number of days (capped automatically by the object budget) | 60 (min 1) | How many days of day levels to keep. The script lowers it automatically when the enabled options would exceed the object limits (section 7); with the defaults, 5m and lower charts keep 46 days. The tooltip says so. |
| day vertical lines | on | Draws a vertical line at the start of each day. Also required for the week vertical lines. |
| (no label, style) | dotted | Style of the day vertical lines. |
| (no label, width) | 2 | Width of the day vertical lines. |
| (no label, color) | blue | Color of the day and week vertical lines and of the premarket start and end lines. |
| week vertical lines (style) | dashed | Style used for the vertical line of the first day of each week. |
| (no label, width) | 2 | Width of the week vertical lines. |
| day | on | Draws the day high and low lines. Also required for the day labels and REH/REL. |
| max | green | Color of the day high lines and labels. |
| min | red | Color of the day low lines and labels. |
| (no label, style) | solid | Style of the day high and low lines. |
| (no label, width) | 2 | Width of the day high and low lines. |
| label unbroken day max/min | on | Labels day highs and lows that have not been taken ("day high", "week high", ...). |
| size | tiny | Text size of those labels. |
| REH/REL labels | on | Groups untaken day highs (lows) that are close to each other and labels only the outermost as "day REHs" ("day RELs"). |
| tolerance x daily ATR | 0.05 | Maximum distance between two day highs (lows) to be relative equal, as a fraction of the daily ATR(14). Higher values group more levels. |
| equilibrium | on | Draws the day equilibrium (midpoint of high and low) with an "eq" label. |
| (no label, style) | dashed | Style of the eq line. |
| (no label, width) | 1 | Width of the eq line. |
| (no label, color) | blue | Color of the eq line and label. |
| Day Open | on | Draws the midnight open line with an "M.O." label. |
| (no label, style) | dashed | Style of the midnight open line. |
| (no label, width) | 1 | Width of the midnight open line. |
| (no label, color) | black | Color of the midnight open line and label. |

### 5.4 TGIF

| Label in dialog | Default | What it does |
| --- | --- | --- |
| TGIF levels % | on | Draws the TGIF levels on Friday, or on Monday, while they apply. |
| (no label, first level) | 0.2 (0.01 to 1.0) | First retracement level as a fraction of the week range (20%). Also the "no final wick" test: the week close must be closer to the extreme than this fraction. The label shows the value in percent. |
| (no label, second level) | 0.3 (0.01 to 1.0) | Second retracement level (30%). |
| TGIF displacement: body of the week x range | 0.6 (0.1 to 1.0) | Minimum body of the week candle as a fraction of its range for the week to count as a displacement. Lower values give TGIF levels more often. |
| colors: up | green | Color of the levels after a bearish week (retracement up from the week low). |
| down | red | Color of the levels after a bullish week (retracement down from the week high). |
| (no label, style) | dashed | Style of the TGIF lines. |
| (no label, width) | 2 | Width of the TGIF lines. |
| text size | normal | Size of the TGIF labels. |

### 5.5 Session Settings

| Label in dialog | Default | What it does |
| --- | --- | --- |
| Draw sessions, keep | on | Draws the Asia, London, NY AM and NY PM sessions and their high and low. |
| (no label, count) | 20 (min 3) | How many sessions to keep, all four kinds together. Values above 40 are reduced to 40. Also the number of premarket ranges kept. |
| Draw session rectangles | on | Draws a shaded box for each session. |
| transparency | 75 | Transparency of the session boxes (0 opaque, 100 invisible). |
| No session rectangles at or under | on | Hides the session boxes on low timeframes. |
| timeframe | 5 (5 minutes) | Boxes are hidden when the chart timeframe is at or below this value and below 15 minutes. Setting a timeframe of 15m or more hides them on every chart below 15m. |
| ses. | on | Draws the session high and low lines (also used for the premarket high and low). |
| max | green | Color of the session (and premarket) high lines and labels. |
| min | red | Color of the session (and premarket) low lines and labels. |
| (no label, style) | solid | Style of the session high and low lines. |
| (no label, width) | 1 | Width of the session high and low lines. |
| Label unbroken session max/min | on | Labels session and premarket highs and lows that have not been taken ("London hi", "Pre-mkt lo", ...). |
| size | tiny | Text size of those labels. |
| colors: asia | red | Box color of the Asia session. |
| london | green | Box color of the London session. |
| NY | blue | Box color of the NY AM and NY PM sessions. |

### 5.6 Gaps and Ranges

| Label in dialog | Default | What it does |
| --- | --- | --- |
| Draw NDOG/NWOG/RTHOGs | on | Master switch for all opening gaps. |
| keep | 10 (min 1) | Number of NDOGs, of NWOGs and of RTHOGs to keep (each). Values above 15 are reduced to 15. |
| NDOGs | on | Draws new day opening gaps. |
| NWOGs | on | Draws new week opening gaps. |
| RTHOGs (timeframes up to 15m) | on | Draws regular trading hours opening gaps (15m or lower charts). |
| RTHOG quadrants | on | Draws the 25% and 75% lines of the current RTHOG. |
| min size | 25.0 | Minimum distance in points between quadrant lines; quadrants are drawn only when a quarter of the gap is larger. |
| RTHOG stddevs | on | Draws standard deviation projections of the current RTHOG. |
| min size | 25.0 | Minimum step in points; the step is the first of gap/8, gap/4, gap/2 or the whole gap that is larger than this, otherwise none are drawn. |
| number of stddevs to draw above/below | 4 | Deviation lines per side. Keep it at 1 or more; to hide them, turn off "RTHOG stddevs". |
| Draw gap rectangles | off | Draws shaded boxes for NDOG, NWOG and RTHOG gaps. |
| transp | 90 | Transparency of the gap boxes. |
| Combine gaps at similar levels | on | Draws nearby NDOGs (NWOGs) once with their outer levels ("NDOGx3"), and merges NDOGs into a NWOG at the same levels ("NWOG+NDOG"). |
| tolerance x daily ATR | 0.05 | Maximum distance between levels to be combined, as a fraction of the daily ATR(14). Also decides when a gap is so small that it gets a single label in the middle. |
| NDOG (style) | solid | Style of the NDOG lines. |
| (no label, width) | 1 | Width of the NDOG lines. |
| (no label, color) | #C32E7D (magenta) | Color of the NDOG lines, labels and boxes. |
| NWOG (style) | solid | Style of the NWOG lines. |
| (no label, width) | 2 | Width of the NWOG lines. |
| (no label, color) | maroon | Color of the NWOG lines, labels and boxes. |
| RTHOG (style) | solid | Style of the RTHOG settlement, open, CE lines and old ticks. |
| (no label, width) | 2 | Width of all RTHOG lines. |
| old | #17E1DA (cyan) | Color of the previous RTHOG and of the older ticks. |
| this | #6B17E1 (purple) | Color of the current RTHOG and of the new settlement. |
| RTHOG quadrant line | solid | Style of the RTHOG quadrant lines. |
| PO3 line | dotted | Style of the RTHOG standard deviation lines. |
| extend old RTHOG lines | off | Extends to the right the open-side ticks (settlement, CE and open) of older RTHOGs. |

### 5.7 Premarket Range

| Label in dialog | Default | What it does |
| --- | --- | --- |
| Draw premarket range (m5 or less) | on | Draws the premarket range on 5m or lower charts. |
| quadrants/stddevs for the last | 1 (1 to 5) | How many of the most recent ranges get quadrant and deviation lines. Older ranges keep only their high and low. |
| range | Auto | Auto: 07:00-09:00 New York (US) or 06:30-08:30 Madrid (EU). Manual: uses the "manual range" setting. |
| manual range | 07:00-09:00 | The range window in Manual mode, in the script's time zone (New York for US, Madrid for EU). |
| PMKT quadrants | on | Draws the 25%, 50% (CE) and 75% lines. |
| min size | 20.0 | Minimum distance in points between quadrant lines; drawn only when a quarter of the range is larger. |
| PMKT stddevs | on | Draws standard deviation projections above and below the range. |
| min size | 20.0 | Minimum step in points; the step is the first of range/8, range/4, range/2 or the whole range that is larger than this, otherwise none are drawn. |
| number of stddevs to draw above/below | 4 (min 1) | Deviation lines per side. |
| line | red | Color of all PMKT lines and labels (hi, lo, quadrants, CE, deviations). |
| (no label, width) | 2 | Width of the PMKT lines. |
| quadrant | solid | Style of the 25% and 75% lines. |
| CE | solid | Style of the 50% line. |
| stddev | dotted | Style of the deviation lines. |
| label size | normal | Text size of the PMKT labels. |
| combine RTHOG labels, tolerance x daily ATR | 0.05 | When an RTHOG deviation and a premarket deviation are within this fraction of the daily ATR(14), one label is shown for both ("RTHOG +2 PMKT +3"). |

### 5.8 Equal Highs / Lows

| Label in dialog | Default | What it does |
| --- | --- | --- |
| Draw EQH / EQL | on | Draws untaken equal highs and lows of the chart timeframe. |
| also from the daily chart | on | Also finds equal highs and lows on daily swings ("EQH D", "EQL D"), completed daily bars only. |
| pivot | 3 (min 1) | Bars on each side of a swing on the chart timeframe. Larger values find fewer, more significant swings, and confirm them later. |
| daily pivot | 3 (min 1) | Bars on each side of a swing on the daily chart. |
| tolerance x ATR | 0.10 | Maximum distance between two swings to be equal, as a fraction of the ATR(14) of the same timeframe. |
| keep per side | 5 (1 to 20) | Number of equal highs and of equal lows kept (for the chart and the daily, each). |
| also with 3-candle swings, max points beyond | on | A new swing also forms an equal level with an earlier untaken 3-candle swing at the same price or at most the given points beyond it. |
| (no label, points) | 3.0 | Maximum distance in points for the 3-candle swing match. 0 means exactly equal only. |
| EQH | green | Color of EQH lines and labels. |
| EQL | red | Color of EQL lines and labels. |
| line | solid | Style of the EQH / EQL lines. |
| (no label, width) | 2 | Width of the EQH / EQL lines. |
| label size | normal | Text size of the EQH / EQL labels. |

### 5.9 Macros and Events

| Label in dialog | Default | What it does |
| --- | --- | --- |
| Show current session name | on | Shows the active session name in the top-right table. |
| (no label, color) | blue | Text color of the session name. |
| Show current macro name | on | Shows the active macro name in the table. |
| (no label, color) | orange | Text color of the macro name. |
| flag macros with background color | off | Tints the chart background during every macro window. |
| (no label, color) | black, 90% transparent | Background tint color. |
| Show active silver bullet | on | Shows "S.Bullet" in the table during a silver bullet window. |
| (no label, color) | red | Text color of the silver bullet note. |

## 6. Alerts

ICTlevels defines **no alerts**: it has no alert conditions and makes no alert calls, and it plots no series, so there is nothing to select in TradingView's "Create alert" dialog for this indicator. To be alerted at a level, draw a horizontal line or price alert on it by hand.

## 7. Limits, performance and repainting

### Object limits

TradingView allows a script at most 500 lines, 500 labels and 500 boxes. ICTlevels never lets the limit delete objects silently. Before drawing, it reserves room for every enabled module (months, gaps, RTHOG, sessions, premarket ranges and deviations, EQH/EQL, TGIF, plus 10 spare) and then fits as many days as possible into what is left:

- "Draw sessions, keep" is capped at 40 and "keep" for gaps at 15.
- "Number of days" is lowered to what fits, never below 1.
- When anything was reduced (except the expected day cap on 5m and lower charts), the table shows "object cap: N days" and the Pine Logs show the old and new day, session and gap counts.

With the default settings, 5m and lower charts are capped at **46 days** (the premarket range needs room); 15m and 1h charts keep all 60 days. On 5m and lower charts the day cap is expected, so it does not show the cap warning; a session or gap cap still does. To keep more days, turn off modules you do not use or lower the session or gap counts.

### History window

To stay fast, the script processes only a window of recent history: about 1.5 times the number of days kept (the larger of days and gap "keep", plus 5), counted back from now, or from the rightmost visible bar when "compute for historic data use" is on. Bars before the window are ignored, so no levels are drawn there. RTHOG is also limited to the last 5000 bars. On 1m charts the history TradingView loads is usually shorter than the window anyway.

### Repainting and confirmation

- Levels of the current day, session, week and premarket range move while they form. This is by design: a completed day, session or range does not change.
- The RTHOG hint moves with price during the 15 minutes before the open and is fixed on the open bar. On historical bars only the final RTHOG is shown.
- Friday TGIF levels follow the week's extreme until the 20% level is reached; after that, and on Monday, they are fixed. Whether they appear at all is decided at the start of Friday (or of the week) and does not change.
- EQH / EQL appear only after the swing is confirmed ("pivot" bars later). Daily EQH / EQL and month levels use completed bars only and do not repaint.
- Swept equal levels are deleted; swept day and session levels keep their line up to the bar that took them, but lose their label.
- Most drawing happens on the last bar. With "compute for historic data use" on, everything is recomputed for the rightmost visible bar each time you scroll, which can be slow on 1m charts.

## 8. Troubleshooting and FAQ

**Nothing is drawn.** The chart timeframe is above 1 hour. Use 1h or lower.

**The premarket range is missing.** It needs 5m or lower and "Draw premarket range" on. Quadrants and deviations are drawn only when the range is large enough (over 80 points for quadrants and over 20 points for deviations with the defaults); otherwise only the Pre-mkt high and low lines are shown, without the red PMKT hi / lo lines.

**There is no RTHOG.** It needs 15m or lower. Quadrants need a gap over 100 points with the defaults, and deviations a gap over 25 points. RTHOGs are computed only for the last 5000 bars.

**NDOGs or NWOGs do not appear.** The gap is completed only on a bar that opens exactly at 18:00 New York (US) or 00:00 Madrid (EU), and its first edge comes from the bar that ends at 17:00 / 23:00 or the bar that opens then. If your symbol's data has no bars at those times, no gaps are drawn.

**The session and macro times look one hour off.** Check the platform time zone (New York for NQ, Madrid for DAX) and the Zone setting: with Auto, a DAX symbol whose name does not contain DAX, GERMANY or FDX is treated as US. On DAX, the "NY" windows are fixed Madrid times and are one hour off during the weeks when US and European daylight saving dates differ.

**The table says "object cap: N days".** A day count was reduced on a chart above 5m, or the session or gap counts were reduced. Turn off unused modules, lower "keep" counts or the number of deviations, or turn off "show object cap warning" to hide the note.

**Levels disappear when I scroll back.** Old bars are outside the history window. Turn on "compute for historic data use" to compute the levels for the rightmost visible bar.

**Some labels are invisible on a dark chart.** The month close, the Day Open and their labels are black by default; change their colors. The text of the old RTHOG ticks ("rthog.cl", "rthog.ce", "rthog.op") is always black and cannot be changed.

**An EQH disappeared.** Price traded above it (even by one tick), so the liquidity was taken and the level is removed.

**Only one of several close day highs has a label.** That is the REH/REL logic: the highest high of the cluster is labelled "day REHs" and the others keep their lines without a label. Turn off "REH/REL labels" to label each one.

**Session boxes are missing on the 5m chart.** "No session rectangles at or under" is on with 5 minutes. Turn it off or set a lower timeframe.

**I set the RTHOG "number of stddevs" to 0 and still see deviations.** This input has no minimum and 0 does not disable the lines; use the "RTHOG stddevs" checkbox instead.

**Can I change the session, macro or silver bullet times?** No. Only the premarket range can be set by hand (Manual mode).

**Can I get alerts?** No; see section 6.

## 9. Glossary

| Term | Meaning |
| --- | --- |
| ATR(14) | Average true range over 14 bars, used to scale tolerances. "Daily ATR" is the ATR of the last completed daily bar. |
| CE | Consequent encroachment: the 50% level of a gap or range. |
| Displacement | A strong, one-directional move with a large body relative to its range. |
| EQH / EQL | Equal highs / equal lows: two or more swings at about the same price, where stops rest. |
| Equilibrium | The midpoint of a range; above it is premium, below it is discount. |
| Killzone | A session window in which ICT expects the main moves: Asia, London, NY AM, NY PM. |
| Liquidity | Resting orders (stops) above highs and below lows that price tends to seek. |
| Macro | A short ICT time window in which price is expected to seek liquidity or rebalance. |
| Midnight open | The open of the first bar after midnight New York (or Madrid) time. |
| NDOG | New day opening gap: between the close before the daily break and the open after it. |
| NWOG | New week opening gap: between Friday's close and the week's first open. |
| PO3 / stddev | Power of three; here the standard deviation projections, multiples of a range's size measured from its edges. |
| Premarket range | The range traded before the regular open (07:00-09:00 New York or 06:30-08:30 Madrid by default). |
| Quadrant | The 25% and 75% levels of a range or gap. |
| REH / REL | Relative equal highs / lows: highs (lows) close to each other within a tolerance. |
| RTHOG | Regular trading hours opening gap: between the regular session settlement and the next regular open. |
| Settlement | The last price of the regular session as used by the script (close of the last bar before 16:15 New York or 17:30 Madrid). |
| Silver bullet | A one-hour ICT window (10:00-11:00, and 14:00-15:00 for US) for a fair value gap entry. |
| Sweep / taken | Price trading through a level, taking the liquidity resting there. |
| TGIF | "Thank God it's Friday": the expected 20%-30% retracement of a displaced week on Friday or Monday. |
