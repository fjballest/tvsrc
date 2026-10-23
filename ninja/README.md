# NinjaTrader 8 ports

NinjaScript (C#) versions of three TradingView indicators:

| File | Port of |
| --- | --- |
| ICTlevels.cs | ICTlevels.txt |
| ICTcotas.cs | ICTcotas.txt |
| FVGSIFVGOTE.cs | FVGS+IFVG+OTE.txt (C# class names cannot contain `+`) |

## Install

1. Copy the `.cs` files to `Documents\NinjaTrader 8\bin\Custom\Indicators`.
2. Open NinjaTrader 8 > New > NinjaScript Editor and press F5 to compile.
3. Add the indicators to a chart (intraday; 1m to 5m recommended for FVGSIFVGOTE).

NinjaTrader regenerates the "NinjaScript generated code" block of each file when it compiles; it is not included in the sources on purpose.

## Status

- The files compile against NinjaTrader API stubs written from memory. That catches C# mistakes, but the real compile may still report an API mismatch. Send the compile errors back to get them fixed.
- None has been run in NinjaTrader yet.

## Main differences from the Pine versions

Each file's header has the full list. The main ones:

- The D / W / M and 15m / 1H / 4H data are loaded as extra data series. Those candles follow your NinjaTrader trading-hours template, so they can differ from TradingView's.
- Box labels are separate text objects, because NinjaTrader rectangles cannot hold text.
- Pine's 500-object limit is gone.
- The time zone choice is a market setting: US, EU or Auto.
- These ports are kept in sync with the Pine scripts: every change to a Pine source is also made here.
