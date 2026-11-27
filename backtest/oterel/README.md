# OTE rejection to RELs / REHs: five-year backtest (MNQ 1m, Jan 2022 to Nov 2026)

Setup (from Francisco's screenshot, 2026-11-27): price retraces into an OTE, wicks it without going past the
0.705 level by more than one tick, an FVG or IFVG supports the entry in the same zone, relative equal lows are
below the entry (short). Stop past the OTE zone, take profit at the RELs. Longs are the mirror.

## Rules as coded (oterel.py)

- **Leg / fib**: OTEFibWick "fib retracements for OTE" on swings (pivots 10 left / 3 right on the chart
  timeframe, the 0 / 100 extended by new extremes, one update per bar as in the Pine). Swing high before swing
  low = down leg, short setups; the other way, long setups.
- **OTE wick**: the extreme of the retracement since the leg end reaches 0.62 and never goes past 0.705 + 1 tick.
  Wick only (default): no candle of the retracement closes beyond 0.62.
- **FVG / IFVG support**: a live bearish FVG, or a bullish FVG inverted by a close below it (IFVG), overlapping
  the 0.62 to 0.79 band, that the retracement traded into. FVGs of at least 1 point, killed by a close through.
- **Rejection / entry**: within 5 bars of the last touch of 0.62, a bearish candle closing back below 0.62: enter
  at its close.
- **Target**: the nearest live REL below the entry: two 3/3 pivot lows within 0.10 x ATR(14) (ICTlevels EQL
  rule), not swept, formed in the last 24 hours; the target is the lower of the two lows, filled when price trades
  one tick through it.
- **Stop**: one tick past 0.79 ("79") or one tick past the leg high/low ("100").
- **Other**: flat at the 17:00 NY close if neither is hit; stop first when one bar hits both; one trade at a
  time per timeframe, one per leg. Cost 1 point per trade (2 points in the sensitivity check).

## Results (all sessions, stop 0.79, wick only)

| TF | trades | win % | pts/trade | PF | max DD pts |
|----|-------:|------:|----------:|---:|-----------:|
| 1m | 4802 | 23.5 | -0.67 | 0.93 | 5445 |
| 2m | 1928 | 25.4 | +0.56 | 1.04 | 2095 |
| 3m | 1145 | 27.7 | +1.63 | 1.11 | 1745 |
| 5m |  570 | 31.2 | -0.49 | 0.97 | 1386 |

Stop at the leg end (100) instead: 1m PF 0.97, 2m 1.06, 3m 1.05, 5m 0.93. Requiring RR >= 1 changes little.
Median planned RR is 3 to 5; most trades stop out, the winners pay big.

By session (stop 0.79): Asia and London lose on every timeframe (PF 0.66 to 1.01). NY AM (07:00 to 12:00) is the
only consistent pocket:

| NY AM only | trades | pts/trade | PF | 2022 | 2023 | 2024 | 2025 | 2026 |
|-----------|-------:|----------:|---:|-----:|-----:|-----:|-----:|-----:|
| 3m stop 0.79 | 255 | +14.0 | 1.83 | 1.25 | 2.16 | 2.09 | 1.87 | 2.06 |
| 3m stop 100  | 250 | +15.3 | 1.70 | 1.03 | 1.43 | 1.83 | 1.97 | 2.46 |
| 2m stop 0.79 | 426 | +3.8 | 1.21 | 0.86 | 1.32 | 1.67 | 1.15 | 1.25 |
| 1m stop 0.79 | 991 | -2.2 | 0.85 | 0.69 | 1.03 | 1.20 | 1.09 | 0.60 |
| 5m stop 0.79 | 132 | +0.2 | 1.01 | 0.92 | 2.18 | 0.89 | 1.75 | 0.40 |

(year columns: PF)

## Robustness (sens.py, 3m NY AM, stop 0.79)

Holds with rejection window 3 or 10, FVG min 0.5 or 3, overshoot 0 or 4 ticks, no wick-only rule (PF 1.72),
2 points cost (PF 1.74), REL tolerance 0.05 or 0.2 ATR (PF 1.80 / 1.45). It weakens with RELs no older than 6
hours (PF 1.39) or of any age (PF 1.07), and with other swing lengths (5/2: PF 1.05, 15/3: 1.53). Without its 5
best trades it still makes +6.1 pts/trade. The 2m NY AM result does not survive dropping its 5 best trades.

## Caveats

- NY AM / 3m was picked after looking at the breakdown: treat it as a hypothesis to forward test, not a result.
- Fills at the bar close for entries and at the level for stops / targets; no slippage beyond the cost.
- "RELs in front" read as the nearest unswept REL below the entry formed in the last 24 hours; older RELs give
  far targets and the edge disappears.
- The data is a continuous back-adjusted MNQ series (prices differ from the charts of the time).
