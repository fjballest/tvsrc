# Tradovate ports

JavaScript custom indicators for Tradovate, ported from three TradingView indicators:

| File | Port of |
| --- | --- |
| ICTlevels.js | ICTlevels.txt |
| ICTcotas.js | ICTcotas.txt |
| FVGSIFVGOTE.js | FVGS+IFVG+OTE.txt |

## Install

1. In Tradovate, open Code Explorer and create a new indicator.
2. Paste the contents of the `.js` file and save.
3. Add the indicator to a chart.

## Status

- Each file ran without errors on several weeks of made-up bars in a mock of Tradovate's indicator API.
- None has been loaded in Tradovate yet.

## Main differences from the Pine versions

Each file's header has the full list. The main ones:

- Tradovate indicators cannot request other timeframes, so the higher-timeframe candles are built from the chart bars, with the futures day starting at 18:00 New York. Load enough history: for example about 4 months for the monthly lines in ICTlevels.
- Tradovate indicators cannot raise alerts, so FVGSIFVGOTE exposes its signals as hidden plots (`signal`, `oteSignal`, `bounce`) instead.
- Pine's 500-object limit is gone.
- The time zone choice is a market setting: US, EU or Auto.
- These ports are kept in sync with the Pine scripts: every change to a Pine source is also made here.
