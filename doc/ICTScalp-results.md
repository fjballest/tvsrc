# ICTScalp: Results So Far

Summary of the ICTScalp backtests on TradingView, kept up to date as the script changes. All figures are on MNQ1! (points per trade, NQ points) over the chart history TradingView loads for each timeframe, from the simulated trades table of the script. They come from one history window on one instrument, so they show which way to go, not a proven edge. The 1m history moves as new candles come in (1m went from +10.1 with 60 trades to +8.6 with 62 during the day).

Last update: 2026-11-25, script commit 47b041b (TradingView version 35).

## 1. Current default setup

| Part | Setting |
| ---- | ------- |
| Trigger | FVG return only (IFVG leaving, IFVG return and sweep + OTE off) |
| HTF bias (4H) | Cotas triangles Mandatory, pattern "15m turtle soup"; quadrant reactions Consider; the rest off |
| LTF confirmations | Took liquidity Mandatory; the rest off |
| Back checks | EQH / EQL behind the entry taken (backEq), highs / lows inside the FVG taken (inFvgHi) |
| Time | ICT killzones (London, NY AM, NY PM) and hourly macro :50 to :10, New York time |
| Stop | Past the short-term high / low since the FVG, 2 points margin |
| Take profit | Risk ratio 1:2; candle trailing stop once 50 points in favour (the TP is then dropped) |
| Previous session filter | Off (see section 4, Sessions) |

## 2. Results of the default setup

| LTF | Trades | Win | Avg win | Avg loss | Points | Avg per trade |
| --- | ------ | --- | ------- | -------- | ------ | ------------- |
| 1m | 62 | 61% | +42.1 | -44.5 | +533 | **+8.6** |
| 2m | 75 | 56% | +57.0 | -38.2 | +1132 | **+15.1** |
| 5m | 95 | 48% | +86.3 | -41.3 | +1942 | **+20.4** |

## 3. How we got here

Each step kept only what improved the results; what did not work was removed from the script.

| Step | 1m | 2m | 5m |
| ---- | -- | -- | -- |
| Old defaults (IFVG / sweep triggers, OTE, breaker, MB, HTF OTE and FVG draw) | +1.9 (376) | -2.75 (316) | +0.75 (312) |
| FVG return + 15m turtle soup triangles (config P) | +2.96 (207) | +1.31 (247) | +10.9 (355) |
| + took liquidity Mandatory | +3.8 (194) | +2.7 (208) | +14.6 (265) |
| + hourly macro :50-:10 with killzones | +8.2 (76) | +9.3 (86) | +19.7 (106) |
| + quadrant reactions (current default) | +8.6 (62) | +15.1 (75) | +20.4 (95) |

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
- Checking the setup on other instruments (ES, DAX) and other history windows.
