# ICTScalp: Results So Far

Summary of the ICTScalp backtests, kept up to date as the script changes. Figures are NQ points per trade on MNQ, before costs (a round trip of commission and 1 tick of slippage each way is about 1 point).

Last update: 2026-11-27. Section 0 is the five-year backtest that set the current Auto. Sections 1 to 5 are the earlier TradingView figures (script commit 38db50b): they cover only the few weeks or months of history the chart loads, which turned out to be the best stretch of the five years, so their averages are much higher than the long-run ones.

## 0. Five years of 1m data (2022-01 to 2026-11)

Data: the day journal's MNQ 1m candles (back-adjusted, 2022-01-02 to 2026-11-24); 2m, 3m and 5m are made from them. The real ICTScalpStrategy.txt was run on them by the day journal's Pine strategy tester (pyjou, `python3 backtest.py`), with TradingView's order fills (strategy orders, one position at a time).

Auto before and after this update:

| Chart | Old Auto | Trades | Pts/trade | PF | New Auto | Trades | Pts/trade | PF |
| ----- | -------- | ------ | --------- | -- | -------- | ------ | --------- | -- |
| 1m | after a consolidated session | 1044 | +0.95 | 1.06 | NY PM only | 591 | +2.42 | 1.15 |
| 2m | PO3 4H + 1H | 399 | +2.18 | 1.12 | PO3 1H | 684 | +3.18 | 1.18 |
| 3m | PO3 5m | 516 | -0.21 | 0.99 | took liquidity off | 1768 | +2.16 | 1.13 |
| 5m | PO3 4H + 1H | 378 | +6.37 | 1.27 | PO3 4H + 1H, NY AM only | 204 | +15.66 | 1.65 |

New Auto by year (net points):

| Chart | 2022 | 2023 | 2024 | 2025 | 2026 |
| ----- | ---- | ---- | ---- | ---- | ---- |
| 1m | +830 | +3 | -93 | -57 | +750 |
| 2m | +432 | +203 | +428 | -633 | +1744 |
| 3m | +1791 | +318 | +377 | +247 | +1077 |
| 5m | +87 | -368 | +463 | -6 | +3019 |

The new choices were picked on 2022-2024 (with a Python port of the script that gives the same trades) and checked on 2025-2026. With the old Auto, 5m made all its profit in 2026 and lost about 220 points from 2022 to 2025.

What the five years say about the parts of the setup (Auto off, points per trade):

- The 15m turtle-soup triangles are the edge. Without them every timeframe loses (about -0.1 to -0.6 a trade, with 5 to 8 times more trades).
- The trailing stop after 50 points matters on 2m and 5m. Without it they lose about 1.7 a trade.
- The previous-session filter and the 4H + 1H PO3 pair, which looked best on the TradingView window, do not hold up on 1m, 2m and 3m.
- On 5m, London and NY PM lose (-2.4 and -2.3 a trade) while NY AM makes +5.6.
- Quadrant reactions and took liquidity add little; took liquidity is in the way on 3m.
- After about 1 point of costs, only 5m makes clearly more than costs. 1m to 3m make 1 to 2 points a trade net, and some years are negative.

## 1. Current default setup

With **Auto** on (the default), the script picks the best settings found for the chart timeframe; the rest of the setup is common to all timeframes.

| Chart | Auto settings |
| ----- | ------------- |
| 1m | NY PM killzone only; PO3 off; previous session filter off |
| 2m | PO3 Mandatory on the 1H candle |
| 3m | Took liquidity off; PO3 off |
| 5m | NY AM killzone only; PO3 Mandatory on the 4H and 1H candles |
| Other | The inputs as set |

| Part | Setting |
| ---- | ------- |
| Trigger | FVG return only (IFVG leaving, IFVG return and sweep + OTE off) |
| HTF bias (4H) | Cotas triangles Mandatory, pattern "15m turtle soup"; quadrant reactions Consider; the rest off |
| LTF confirmations | Took liquidity Mandatory; the rest off |
| Back checks | EQH / EQL behind the entry taken (backEq), highs / lows inside the FVG taken (inFvgHi) |
| Time | ICT killzones (London, NY AM, NY PM) and hourly macro :50 to :10, New York time |
| Stop | Past the short-term high / low since the FVG, 2 points margin |
| Take profit | Risk ratio 1:2; candle trailing stop once 50 points in favour (the TP is then dropped) |
| Previous session filter | Off with Auto on 1m to 5m, else the input (Off by default; see section 4, Sessions) |
| PO3 | Set by Auto on 1m to 5m, else the input (Off by default; see section 4, PO3) |

## 2. Results of the old Auto on TradingView (before 27 Nov)

| LTF | Trades | Win | Avg win | Avg loss | Points | Avg per trade |
| --- | ------ | --- | ------- | -------- | ------ | ------------- |
| 1m | 30 | 63% | +40.4 | -28.5 | +454 | **+15.1** |
| 2m | 26 | 62% | +76.7 | -38.2 | +846 | **+32.6** |
| 3m | 43 | 60% | +69.7 | -39.4 | +1142 | **+26.6** |
| 5m | 29 | 66% | +141.6 | -63.0 | +2060 | **+71.0** |

Without Auto (PO3 and the previous session filter off): 1m +8.6 (62), 2m +15.1 (75), 5m +20.4 (95).

## 3. How we got here

Each step kept only what improved the results; what did not work was removed from the script.

| Step | 1m | 2m | 5m |
| ---- | -- | -- | -- |
| Old defaults (IFVG / sweep triggers, OTE, breaker, MB, HTF OTE and FVG draw) | +1.9 (376) | -2.75 (316) | +0.75 (312) |
| FVG return + 15m turtle soup triangles (config P) | +2.96 (207) | +1.31 (247) | +10.9 (355) |
| + took liquidity Mandatory | +3.8 (194) | +2.7 (208) | +14.6 (265) |
| + hourly macro :50-:10 with killzones | +8.2 (76) | +9.3 (86) | +19.7 (106) |
| + quadrant reactions | +8.6 (62) | +15.1 (75) | +20.4 (95) |
| + Auto: after a consolidation (1m), PO3 4H + 1H (2m, 5m) | +15.1 (30) | +32.6 (26) | +71.0 (29) |

Avg points per trade, trades in brackets. Total points are highest on 5m without the time filter (+3,877 with 265 trades), so the time filter trades total points for quality.

## 4. Findings by topic

### HTF triangles (ICTcotas)

- A turtle soup (sweep) on 15m in the trade direction, with 1H and 4H not against it, beat every other pattern tried: all agree, 4H continuation, and 4H sweep with the rest continuing.
- Mixing the triangles with the other HTF indications (OTE rejection, FVG draw, blocks, 0.5 rejections) as Consider made results negative, so those are off by default.

### Time (ICT macros)

| Filter | 1m | 2m | 5m |
| ------ | -- | -- | -- |
| Killzones only | +3.8 (194) | +2.7 (208) | +14.6 (265) |
| ICT macro list, no killzones | +5.5 (64) | +13.0 (76) | +15.2 (94) |
| Hourly :50-:10, no killzones | +6.4 (152) | +7.8 (159) | +6.7 (210) |
| Hourly :50-:10 with killzones | +8.2 (76) | +9.3 (86) | +19.7 (106) |

The ICT macro list is 02:33-03:00, 04:03-04:30, 08:50-09:10, 09:50-10:10, 10:50-11:10, 11:50-12:10, 13:10-13:40 and 15:15-15:45 (New York).

### Quadrant reactions

A closed candle wicks to the 0, 25, 50, 75 or 100% level of a zone and closes back on the trade side, within the last 10 candles. Zones: last NWOG, last RTHOG, last 1H and 4H FVGs, last 1H and 4H candle wicks.

| Zones | 1m | 2m | 5m |
| ----- | -- | -- | -- |
| All (default) | +10.1 (60) | +15.1 (75) | +20.4 (95) |
| NWOG and RTHOG only | +0.7 (12) | +14.4 (20) | +51.1 (32, 66% win) |
| HTF FVGs and wicks only | +10.8 (59) | +14.0 (69) | +11.0 (92) |

NWOG / RTHOG quadrants on 5m stand out, but with too few trades to rely on yet.

### Stops and take profit

| Variant | Result | Status |
| ------- | ------ | ------ |
| Candle trailing after 50 points, RR 2 | Best of those tried | Default |
| Structure trailing (new short-term highs / lows and FVGs after the entry) | 1m +1.7, 5m +4.9 with HTF TP; 5m +7.9 with RR 2, vs +10.9 | Removed |
| Max stop 40, wait for a retracement | 1m +6.4, 2m +10.7, 5m -2.2 | Removed |
| Max stop 80, wait for a retracement | 1m +8.3, 2m +11.6, 5m +15.0 | Removed |
| Max stop 80, skip (wait 1 bar) | 1m +7.1, 5m +14.4 | Removed |
| Max stop 40, closer stop (widest of swing, candle 2 and wicks, candle 2, FVG edge within it) | 1m +6.4, 2m +3.6, 5m -1.5 | Removed |
| Max stop 80, closer stop | 1m +8.0, 5m +5.1 | Removed |

Wide-stop trades are net winners in this sample: waiting for a retracement, using a closer structure stop, or skipping them all lowered the results on every timeframe.

### Sessions: trend vs consolidation

A session (Asia, London, NY AM, NY PM) trended when its net move |close - open| is at least half its range, otherwise it consolidated.

Previous session filter on the default setup:

| Entries | 1m | 2m | 5m |
| ------- | -- | -- | -- |
| All (default) | +8.6 (62) | +15.1 (75) | +20.4 (95) |
| Only after a consolidating session | **+15.1 (30)** | **+21.5 (44)** | +14.0 (50) |
| Only after a trending session | +2.5 (32) | +6.0 (31) | **+34.0 (41)** |
| Predict: default setup after a range, range setup after a trend | +5.2 (80) | +8.3 (93) | +16.5 (104) |

Exploration split by what the session turned out to be (hindsight, 5m):

- Trending sessions: FVG return + took liquidity leads (248 trades, +11.7); with the previous session consolidated (pC) and a macro, +24.4 (86).
- Consolidating sessions: IFVG return + HTF FVG draw leads, best with the previous session trended (pT): 33 trades, 73% win, +35.0.

The range setup of Predict (IFVG return + HTF FVG draw after a trending session) makes about +20 per trade on 5m but loses on 1m (-0.8) and 2m (-3.6). On 1m and 2m the default (trend) setup is clearly better after a consolidating session, which fits the idea that a trend follows a consolidation. These filters are kept as options and are off by default: the samples are small and the best choice differs by timeframe.

### PO3 (power of three)

On the candle in progress of the chosen timeframe: short when it already traded at least 10 points above its open (the manipulation wick) and price is now below the open (the expansion down); long the other way around. Tested as Mandatory on top of the default setup:

| PO3 timeframe | 1m | 2m | 5m |
| ------------- | -- | -- | -- |
| Off (default) | +8.6 (62) | +15.1 (76) | +22.4 (87) |
| D | +8.1 (22) | +14.1 (30) | +0.1 (30) |
| 4H | +8.3 (30) | **+27.7 (36)** | **+55.4 (38), 61% win, +2,106 points** |
| 1H | +7.5 (41) | +19.2 (45) | +35.0 (57) |
| 15m | +7.9 (52) | +15.1 (62) | +32.0 (69), +2,208 points |

More timeframes, and pairs (both candles in progress have to show the PO3), Mandatory on top of the default setup with the previous session filter off:

| PO3 timeframe | 1m | 2m | 3m | 5m |
| ------------- | -- | -- | -- | -- |
| 4H | +8.3 (30) | +27.7 (36) | +12.6 (37) | +55.4 (38) |
| 2H | +1.6 (33) | +25.3 (39) | +14.9 (42) | +40.2 (44) |
| 1H | +7.5 (41) | +19.2 (45) | +7.9 (53) | +35.0 (57) |
| 30m | +1.7 (40) | +4.7 (44) | +7.9 (54) | +27.0 (54) |
| 15m | +7.9 (52) | +15.1 (62) | +12.1 (63) | +32.0 (69) |
| 10m | +4.7 (41) | +14.7 (48) | +10.6 (58) | +32.5 (60) |
| 5m | +10.4 (37) | +15.2 (37) | **+26.6 (43)** | |
| 3m | -0.5 (31) | +19.8 (40) | | |
| 4H + 1H | +12.9 (24) | **+32.6 (26)** | +10.5 (30) | **+71.0 (29), 66% win** |
| 4H + 30m | | +13.0 (22) | | +65.3 (24) |
| 4H + 15m | +7.4 (25) | +29.9 (28) | +13.0 (33) | +67.2 (31) |
| 4H + 5m | +14.5 (18) | +22.6 (20) | +34.0 (23) | |
| 1H + 15m | +4.6 (41) | | | |

- On 2m and 5m, the 4H PO3 confirmed by the 1H PO3 is the best: fewer trades, higher average, about the same total on 5m (+2,060 vs +2,106).
- On 3m, the 5m PO3 works best (+1,142 points); 4H + 5m averages more (+34.0) but with only 23 trades.
- On 1m, PO3 alone does not beat the previous session filter (after a consolidation, +15.1 with 30 trades). Both together leave too few trades (5m PO3: +14.4 with 17; 4H + 1H: +21.9 with 10).
- The daily, 30m and 3m PO3s are the weakest.

Auto uses these results (section 1).

### Other things tried and removed

| Idea | Result |
| ---- | ------ |
| Block rejection trigger | Worse than without |
| OTE-or-liquidity gate | Worse than without |
| Cool-down after a loss | +1.37 vs +1.9 on 1m |
| HTF IFVG re-leave | -3.46; -17.57 with a 2-candle hold |

## 5. Open ideas

- NWOG / RTHOG quadrants on 5m, once there is more history.
- Previous session filter per timeframe: after a consolidation on 1m / 2m, after a trend on 5m.
- 5m with PO3 4H + 1H combined with the previous session filter.
- A 3m baseline without PO3, to see how much the 5m PO3 adds there.
- Checking the setup on other instruments (ES, DAX) and other history windows.
