# ICTScalp User Manual

TradingView indicator (Pine Script v6, overlay) by franjballest. This manual describes the script as it is written in `ICTScalp.txt`. Its strategy version, `ICTScalpStrategy.txt` (TradingView name "ICTScalp strategy"), is the same script with orders (section 9). The backtest figures behind the default settings are in `ICTScalp-results.md`.

## 1. Overview

ICTScalp gives **enter-long / enter-short signals** for scalping NQ (and other index futures) on 1m to 5m charts. A signal needs two things to agree:

- a **higher timeframe (HTF) bias**: where price is likely to go, read on closed HTF candles (4H by default), and
- a **lower timeframe (LTF) entry**: a trigger on the chart timeframe in that direction, with its confirmations.

Both are made of **indications**. Each one is set to **Off**, **Consider** or **Mandatory**:

| Mode | Meaning |
| ---- | ------- |
| Off | Not used |
| Consider | Counts towards "at least N" |
| Mandatory | Always required |

A direction is allowed when every Mandatory indication agrees and at least N of the Consider ones agree (N is capped at the number of Consider ones). Entry triggers work a bit differently: at least one trigger in use has to fire on the entry candle, and every Mandatory one.

On top of that, time filters (killzones, macros, a time interval) and two "back checks" keep entries out of places where price is likely to come back against the trade first.

ICTScalp draws:

| Element | Drawn as |
| ------- | -------- |
| Entry signal | "LONG" triangle below the candle / "SHORT" triangle above it |
| Armed setup | Small diamond below (long) / above (short) the candle |
| Conditions table | Small table, one cell per condition, ▲ / ▼ / ○ |
| Armed report | Text cell next to the conditions table, while armed |
| HTF OTE lines and draw targets | Lines of the HTF legs and the HTF FVG drawing price |
| Trades (optional) | Entry, stop and take profit segments or boxes, labels, results table, exploration table |

Everything is evaluated on **closed candles**: a signal appears when its candle closes and does not repaint.

## 2. Quick start

1. Add ICTScalp to the chart (Indicators, then search for ICTScalp in your scripts).
2. Use a 1m, 2m, 3m or 5m chart of NQ / MNQ (ES, YM, RTY and DAX also work; sizes are scaled, section 8).
3. Leave **Auto** on (the default). It sets the PO3 and previous-session options found best for 1m, 2m, 3m and 5m (section 4).
4. Watch the **conditions table** (bottom center). When its background turns yellow the setup is **armed**: everything agrees and only the entry trigger is missing. A diamond marks those candles.
5. A **LONG** / **SHORT** triangle is the entry: at the close of that candle. To see where the stop and target would be, turn on **Show trades** (Show Trades settings).
6. To check how the settings did on the loaded history, turn on **Show trades** and **Simulate trades**: the results table (top right) gives trades, win rate and points.
7. For alerts, create a TradingView alert on ICTScalp with "Any alert() function call" (entries and armed setups), or pick one of its alert conditions (section 10).

### Recommended use

| Chart | Notes |
| ----- | ----- |
| 1m | Auto: entries only after a session that consolidated. Fewest points per trade, tightest stops |
| 2m, 5m | Auto: PO3 Mandatory on the 4H and 1H candles in progress |
| 3m | Auto: PO3 Mandatory on the 5m candle in progress |
| 15m and above | Auto does nothing; the inputs apply as set. Not the intended use |

The default setup on any of them:

| Part | Default |
| ---- | ------- |
| Trigger | FVG return |
| HTF bias (4H) | Cotas triangles Mandatory ("15m turtle soup"), quadrant reactions Consider, N = 1 |
| LTF confirmations | Took liquidity Mandatory |
| Back checks | EQH / EQL behind the entry within 30 points; highs / lows inside the FVG taken |
| Time | London, NY AM and NY PM killzones, and hourly macros :50 to :10 (New York time) |
| Stop | Past the short-term high / low since the FVG, plus 2 points |
| Take profit | 1:2; trailing stop once 50 points in favour |

### Time zone

The chart time zone does not change anything. The killzones use New York time (US symbols) or Madrid time (DAX), as in ICTlevels; macros always use New York time.

## 3. Reading the chart

### Entry signals

A teal **LONG** triangle below the candle, or a red **SHORT** one above it, marks a candle that closed with every condition met. The entry is the close of that candle. With Show trades on, the stop and take profit are drawn from there (section 7).

### Armed diamonds

A small diamond below (long) or above (short) a closed candle means the setup is **armed** on that candle: the HTF bias, the time filters, the previous-session filter and "Took liquidity" all agree, and only the entry trigger (an FVG return) is missing. Armed usually comes a few candles before an entry; it is the moment to look for an FVG to come back to. Not every armed setup ends in an entry.

### Conditions table

One cell per condition of the setup, showing its state **now** (on the last closed candle):

| Cell | Shows |
| ---- | ----- |
| P3H4, P3H1 (PO3 cells) | PO3 of each PO3 timeframe in use, Auto included (P3D, P3H4, P3H1, P3m15, P3m5...): ▲ long, ▼ short, ○ none |
| Quad | A quadrant reaction within the last bars: ▲ / ▼ / ○ |
| Liq | A short-term low (▲) or high (▼) taken within the last bars |
| Ses | The last session trended (T) or consolidated (C); bright when the previous-session filter allows entries |
| Time | ● inside the killzones and macros in use, ○ outside |
| Armed | ▲ / ▼ while armed |

While armed, the whole table turns **yellow**. The table can go in any of 9 positions (default bottom center).

### Armed report

While armed, a small text cell next to the table (on its right, or on its left when the table is on the right of the chart) says what armed it, for example:

`L Tri Q:NW,FVG P3H4 P3H1 SSL NYAM :50`

| Part | Meaning |
| ---- | ------- |
| L / S | Long / short (both can show, in orange, when both are armed) |
| Tri, hOTE, hFVG, hLiq, hBlk, h50 | HTF indications that agree (cotas triangles, OTE rejection, FVG draw, liquidity draw, respected blocks, 0.5 rejections) |
| Q:NW,RTH,FVG,Wk | Quadrant reaction, with the zones that reacted: NWOG, RTHOG, HTF FVGs, HTF wicks |
| P3H4 P3H1 | PO3 timeframes that agree |
| SSL / BSL | Sell-side (lows) / buy-side (highs) liquidity taken |
| pC / pT | The previous session consolidated / trended (only when that filter is on) |
| LDN, NYAM, NYPM, Asia | Killzone of the candle |
| :50 / mac | Inside an hourly macro / an ICT macro |

### HTF OTE lines and draw targets

With "Show the HTF OTE lines and draw targets" on, the 0.62 / 0.705 / 0.79 levels of the HTF major leg (solid) and minor leg (dotted), the near edge of the HTF FVG drawing price up (red) or down (green), and the zone of the respected HTF breaker / mitigation block giving the direction (teal long, maroon short). They only show when the indications that use them are on.

## 4. Auto

**Auto** (on by default) sets a few options from the backtests on MNQ1!, by chart timeframe; the inputs for those options are then ignored:

| Chart | PO3 | PO3 timeframe(s) | Entries only after a session that |
| ----- | --- | ---------------- | --------------------------------- |
| 1m | Off | (none) | Consolidated |
| 2m | Mandatory | 4H and 1H | Off |
| 3m | Mandatory | 5m | Off |
| 5m | Mandatory | 4H and 1H | Off |
| Other | The inputs | The inputs | The input |

Everything else (triggers, other indications, stops, time) uses the inputs in every case. Turn Auto off to set PO3 and the session filter by hand.

## 5. How a signal is built

### HTF bias indications

Computed on closed HTF candles (Higher timeframe, 240 = 4H by default; it has to be above the chart timeframe).

| Indication | Gives long when... (short: the mirror) | Default |
| ---------- | -------------------------------------- | ------- |
| OTE rejection | An HTF candle wicks into the OTE band (0.62-0.79) of the up leg and closes back out of it. Also for the minor leg (the retracement). Holds until its leg breaks | Off |
| FVG draw | The nearest HTF FVG price has not come back to is a bearish FVG above | Off |
| Liquidity draw | An HTF swing low was taken (price heads to the highs) | Off |
| Cotas triangles | The ICTcotas turtle soup / continuation triangles (15m, 1H, 4H) point up, per the pattern chosen (default: the 15m triangle is a turtle soup up) | Mandatory |
| Respected breakers / MBs | The last respected breaker or mitigation block (15m by default) is bullish and still holds | Off |
| 0.5 rejections | At least N HTF candles of the same pullback wick to the 0.5 of an up leg and close back above | Off |
| Quadrant reactions | Within the last bars, a candle's low tagged the 0, 25, 50, 75 or 100% level of a zone and it closed above it. Zones: last NWOG, last RTHOG, last bull / bear FVG of 1H and of the HTF, upper / lower wicks of the last 1H and HTF candles (zones under 4 NQ points ignored) | Consider |
| PO3 | The candle in progress of the PO3 timeframe traded at least "min" points below its open (manipulation) and price is now above the open (expansion). With "PO3 also on", both candles have to show it | Off (Auto sets it) |

"At least N of the considered" (default 1) applies to the Consider ones. With every HTF indication Off, both directions are allowed.

### LTF entry triggers

At least one trigger in use has to fire on the entry candle.

| Trigger | Fires when | Default |
| ------- | ---------- | ------- |
| IFVG leaving | A candle closes through an FVG, confirming it as an IFVG (close mode of FVGs+IFVG+OTE) | Off |
| IFVG return | A later candle comes back into the confirmed IFVG without passing its far edge; one entry per confirmation | Off |
| Sweep and OTE rejection | A short-term low is taken, price goes up at least "min leg" points, and a candle wicks back into the OTE band of that leg and closes above 0.79; stop past the leg low | Off |
| FVG return | A candle comes back into a chart FVG of the trade direction and closes at or beyond its CE; one entry per FVG | Consider |

### LTF confirmations

| Confirmation | True when | Default |
| ------------ | --------- | ------- |
| OTE | The IFVG is an OTE IFVG, or the FVG matches the OTE band of its leg | Off |
| Breaker / Mitigation block | The FVG / IFVG overlaps a breaker / mitigation block of the trade direction that broke and made a swing beyond it | Off |
| Took liquidity | A candle took the last short-term low (long) / high (short) within the last 20 candles | Mandatory |
| HTF level rejection | A candle within the last 3 rejected an ICTcotas level (wick through, body on the trade side) | Off |
| HTF OTE line rejection | Same, with the 0.62 / 0.705 / 0.79 lines of the HTF legs | Off |
| EQH / EQL to target | An EQH not taken yet above (long) / EQL below (short) | Off |
| Delivery | The delivery of the chart timeframe (IFVGCISDbias) matches the trade | Off |

"At least N of the considered confirmations" (default 1) applies to the Consider confirmations.

### Back checks

Two checks on every entry (and on the exploration), on by default:

- **Wait for the EQH / EQL behind the entry**: no short while an untaken EQH lies within 30 points above the entry; no long with an untaken EQL within 30 points below. Price tends to go and take it first.
- **Wait for the highs / lows inside the FVG to be taken**: no short on an FVG while an untaken short-term high lies inside it; no long while an untaken short-term low lies inside it. The entry comes on a later trigger, once it was taken.

### Time filters

All the ones in use have to be met:

| Filter | Default |
| ------ | ------- |
| Only inside this time interval | Off (09:30-11:30) |
| Only inside ICT killzones | On: London, NY AM, NY PM (Asia off) |
| Only inside ICT macros | Hourly :50 to :10 (New York time); or the ICT macros list; or Off |
| Entries only after a session that | Off, Consolidated, Trended or Predict (Auto sets it on 1m to 5m) |

Killzones as in ICTlevels. US (New York time): Asia 20:00-00:00, London 02:00-05:00, NY AM 07:00-11:30, NY PM 13:30-16:00. EU (Madrid time, DAX): Asia 23:00-06:00, London 08:00-13:00, NY AM 13:30-18:00, NY PM 19:00-22:00.

A session (Asia, London, NY AM, NY PM) **trended** when |close - open| is at least half its range (the "Session trend" setting), otherwise it **consolidated**. With "Predict", after a consolidating session the normal setup is used, and after a trending one a range setup (IFVG return with the HTF FVG draw in the trade direction).

## 6. Using it

- Bias first: if the conditions table shows ○ on the HTF cells, no entry will come. Wait.
- When the table turns yellow and diamonds appear, look for a chart FVG in the armed direction that price could come back to.
- The entry triangle prints at the close of the candle that returned into the FVG and closed beyond its CE. With Show trades on, the stop is drawn past the short-term swing since the FVG and the target at 1:2.
- Once 50 points in favour (NQ), the default simulation trails the stop to each closed candle's low (long) / high (short) and drops the target. Do the same by hand, or keep the 1:2 target.
- Stop scalping when the killzone ends; outside killzones and macros the table shows ○ in Time and no entry comes.

## 7. Trades, simulation and exploration

### Show trades

Draws, for each entry, a gray segment at the entry price, a red one at the stop and a green one at the take profit, with optional boxes (red: risk, green: reward) and a label with the points to stop and target and the indications that were true.

**Stop past**: the IFVG far edge, candle 2 of the FVG, candle 2 and the IFVG wicks, or (default) the short-term high / low formed from the start of the FVG to the entry; plus "points past that level" (2).

**Take profit**: risk ratio 1:N (default 1:2), the next short-term high / low at least 1:N away, the HTF FVG of the draw (its near candle or near edge), or the next EQH / EQL.

### Simulate trades

Follows each trade until its stop or take profit; a candle reaching both counts as the stop. Optional **breakeven** (stop to entry once a candle closes in profit by the given points) and **trailing stop** (default on, after 50 points: the stop follows each closed candle's low / high and the take profit is dropped). Each trade is marked WIN / LOSS / BE with its points.

### Results table

With Simulate trades on, the results table (default top right) has a row **All** and one row per indication in use: trades, win %, loss %, average win, average loss, points and average points per trade, counting the trades where that indication was true. Mandatory ones are marked (M).

### Exploration

"Explore combinations" ignores the Off / Consider / Mandatory settings and simulates every trigger alone and with every one or two confirmations (LTF and HTF) that were true, with the stop, target and time settings above. A second table (bottom right) lists the 15 combinations with the most points among those with at least "min trades". Names use the short forms of the armed report, plus:

| Short | Meaning |
| ----- | ------- |
| IFVGl, IFVGr, FVGr, SwOTE | Triggers: IFVG leaving, IFVG return, FVG return, sweep + OTE |
| OTE, B, MB, Liq, Lvl, OTEl, EQ, DLV | LTF confirmations |
| hOTE, hFVG, hLiq, Tri, hBlk, h50, Quad, PO3 | HTF indications |
| Mac, MacH | Inside an ICT macro / an hourly macro |
| pT, pC | Previous session trended / consolidated |
| P3D, P34, P31, P315 | PO3 on D, 4H, 1H, 15m |
| T4s, T4c, T1s, T1c, T15s, T15c, Tall, T4s+LC | ICTcotas triangle patterns |

"Exploration: trades of" can count only trades of sessions that trended or consolidated. That looks ahead (it is known only when the session ends): use it to study, not to trade. The exploration is slow; turn it off when not in use.

## 8. Settings reference

Only the main groups are listed; every input has a tooltip on TradingView.

| Group | Settings |
| ----- | -------- |
| Auto | Auto on / off (section 4) |
| HTF bias | Higher timeframe; the HTF indications and their parameters (triangles: agree, pattern, timeframes; blocks: timeframe, max bars; 0.5: at least; quadrants: bars and zones; PO3: timeframe, min points, second timeframe); at least N; show the HTF lines |
| LTF entries | Triggers, confirmations and their parameters (min leg, within, short-term pivots L / R); back checks; at least N |
| EQH / EQL | Pivot length, tolerance (x ATR), kept per side, 3-candle swings (ICTlevels settings) |
| Breakers, Mitigation blocks | Zone (last candle / whole sequence, full candle / body), swing length, kept, max bars to the pullback (FVGs+IFVG+OTE settings) |
| Time | Zone (US / EU / Auto), time interval, killzones, session trend ratio, previous-session filter, macros |
| Show Trades | Show / simulate trades, stop and take profit, breakeven, trailing, boxes, labels, results table, exploration, colors |
| Signals | Colors, alerts, conditions table and position, armed diamonds, armed report |
| IFVG, OTE | FVG size, NQ scaling, swing pivots, invalidation hold; OTE levels, match, prior-leg rejection, HTF OTE (FVGs+IFVG+OTE settings) |
| HTF levels | Daily / weekly / monthly FVG, VI, wick CE levels and FVGs by timeframe (ICTcotas settings), used by the HTF level rejection |
| Delivery | FVG size, lock, defer CISD, inner candles (IFVGCISDbias settings) |

**Scale sizes using NQ sizes as reference** (IFVG group, on by default): sizes in points (minimum FVG, stop margin, PO3 min, min leg, back check distance, breakeven and trailing points) are given for NQ and scaled by a fixed factor for other instruments: ES 0.2, YM 1.3, RTY 0.1, DAX 0.7 (and their micros and CFDs).

## 9. The strategy version

"ICTScalp strategy" (`ICTScalpStrategy.txt`) is generated from ICTScalp by `tools/mkstrategy.py` and has the same inputs and logic. It places real strategy orders so TradingView's Strategy Tester can report the results:

- Entries fill at the close of the signal candle, with the stop and take profit of Show trades.
- One position at a time: a signal in the same direction is ignored while in a position; an opposite one reverses it.
- Breakeven and the trailing stop (Show Trades settings) manage the exit order after each closed candle, as in the simulation.
- It has no alert conditions (alert() calls stay).

With the same settings it gives the same trades as the simulated trades of the indicator. Keep both in sync: after changing ICTScalp.txt, regenerate the strategy and save both on TradingView.

## 10. Alerts, limits and behaviour

### Alerts

With **Alerts** on, the script calls alert() once per bar close for:

- an entry: "ICTScalp LONG / SHORT ticker interval"
- the setup becoming armed (when the armed diamonds are on): "ICTScalp long / short armed ticker interval"

Create an alert on ICTScalp with "Any alert() function call" to get them all. The alert conditions "ICTScalp long", "ICTScalp short", "ICTScalp long armed" and "ICTScalp short armed" are also available one by one.

### Repainting

Signals, diamonds and the table use closed candles only; HTF values are those of the last closed HTF candle (except PO3 and the triangles, which read the HTF candle in progress at each chart candle close, like ICTcotas). A signal never disappears once its candle closed. The exploration's session filter looks ahead (section 7).

### Performance

ICTScalp is a large script. Next to many other scripts on one layout it can hit TradingView's "Calculation timed out". The ICTcotas level copy is only computed when the HTF level rejection or the exploration use it, and the armed check only runs inside the time windows. If it still times out, turn off the exploration and Show trades, or remove other scripts from that pane.

### Copies of other scripts

ICTScalp contains copies of parts of FVGs+IFVG+OTE (FVGs, IFVGs, OTE, breakers, mitigation blocks), ICTcotas (levels, triangles), ICTlevels (sessions, EQH / EQL) and IFVGCISDbias (delivery). Their settings groups mirror those scripts; change them the same way to keep both in agreement.

## 11. Troubleshooting and FAQ

| Problem | Answer |
| ------- | ------ |
| No signals at all | Check the conditions table: HTF cells ○ or Time ○ mean nothing can fire. On 1m with Auto, entries also need a previous session that consolidated |
| Too few signals | Set Cotas triangles to Consider, or the pattern to "Agree"; turn off the macro filter; turn off Auto's PO3 by turning Auto off |
| The table never turns yellow | Took liquidity is Mandatory: a short-term low / high has to be taken within the last 20 candles |
| "Calculation timed out" | See section 10, Performance |
| Results differ from the results document | The chart history moves with new candles; the figures are from one window on MNQ1! |
| A copy from an indicator template does not update | Template copies are pinned to their version: remove it and add the script again |

## 12. Glossary

| Term | Meaning |
| ---- | ------- |
| FVG | Fair value gap: three candles where the wicks of candles 1 and 3 do not overlap |
| IFVG | Inversion FVG: an FVG that price closed through, now acting the other way |
| CE | Consequent encroachment: the midpoint of an FVG or wick |
| OTE | Optimal trade entry: the 0.62-0.79 retracement band of a leg |
| PO3 | Power of three: accumulation, manipulation (a move against the bias from the open) and expansion |
| NWOG / RTHOG | New week opening gap / regular trading hours opening gap |
| EQH / EQL | Relative equal highs / lows (liquidity) |
| SSL / BSL | Sell-side / buy-side liquidity (below lows / above highs) |
| Killzone | ICT session window: Asia, London, NY AM, NY PM |
| Macro | Short time window where ICT expects a move: hourly :50 to :10, or the ICT macro list |
| Armed | Every condition except the entry trigger agrees |
