# IFVGCISDbias design document

Oct 18, 2026 · @Francisco

## Overview

IFVGCISDbias ("IFVG+CISD Bias" on the chart) is a standalone Pine Script v6 overlay that draws no levels. It shows a single table with one column per timeframe (1W, 1D, 4H, 1H, 15m, 5m, 3m and the chart timeframe) and two rows of states:

| Row | Label | What it shows |
| --- | --- | --- |
| IFVG bias | `IFVG` | Which side has a confirmed inversion fair value gap: IFVG+ only is bullish (▲), IFVG- only is bearish (▼), both or none is neutral (●) |
| Delivery | `DLV` | Order flow read from candle opens and closes, CISD style: bullish (▲), bearish (▼) or not yet defined (●). The tooltip says when the range is locked |

Each cell has a tooltip with the timeframe and the state in words. The script computes no signals and raises no alerts.

The IFVG logic is the close-based IFVG logic of the FVGs indicator (also used by FVGs+IFVG+OTE), reduced to the last candidate of each side. The delivery logic is that of the CISD indicator.

## Timeframes and data sources

Both states are computed on each timeframe's own candles by `secPair(tf)`:

```
request.security(syminfo.tickerid, tf, [ifvgCode()[1], deliveryCode()[1]], lookahead = barmerge.lookahead_on)
```

The `[1]` offset with `lookahead_on` returns the state at the close of the last completed candle of `tf`, so the table does not repaint. The forming candle never changes a cell.

| Timeframe | Shown when |
| --- | --- |
| 1W, 1D, 4H, 1H, 15m, 5m, 3m | Its tick box is on and it is greater than or equal to the chart timeframe (`v1` to `v7`) |
| Chart timeframe | "Chart timeframe" is on and the chart timeframe is not already one of the shown ones (`vCur`) |

There are always eight `secPair` calls. A disabled or hidden timeframe requests the chart timeframe instead, so the tuple exists and its result is ignored. The chart timeframe call (`bC`, `dC`) is always made.

Columns are sorted from the longest timeframe to the shortest with `array.sort_indices` on their length in seconds, so the chart timeframe falls in its natural place. `tfName()` writes the chart timeframe like the others: minutes as `5m`, whole hours as `2h`, seconds in lower case, and `D`, `W`, `M` as `1D`, `1W`, `1M`.

## Inputs

| Group | Inputs and effect |
| --- | --- |
| Timeframes | 1W, 1D, 4H, 1H, 15m, 5m, 3m tick boxes; Chart timeframe |
| Rows | IFVG bias; Delivery; Row labels (adds a header column or row with `IFVG` and `DLV`) |
| Table | Position (nine screen positions) and orientation (Horizontal: timeframes as columns; Vertical: timeframes as rows); text size; bullish, bearish and neutral colors; text and background colors |
| IFVG | FVG size min / max in points (defaults 3 and 10000). Same meaning as in FVGs: smaller gaps are not FVGs, larger ones are treated as bad data |
| Delivery | Alternating changes to lock the range (`lockN`, default 2, minimum 2); defer CISD until the low / high is taken (`deferOn`, default on); exclude inner candles (`exclInner`, default on) with the reference range as Body or High-Low (`innerRange`) |

## IFVG bias

### FVG detection

`longFVGHL()` and `shortFVGHL()` take candles 1, 2 and 3 (offsets `[1]` to `[3]`, so candle 1 is the newest completed one) and return the gap `[hi, lo]` and the raw wick gap `[fhi, flo]`.

- Long (bullish) FVG: `lo = high[3]`, `hi = low[1]`. When the bodies leave a volume gap with candle 2 (body of 3 below body of 2, or body of 1 above body of 2), the edge moves to the body, so the gap includes the volume imbalance. This matches the suspension-block handling of FVGs.
- Short (bearish) FVG: the mirror image, `lo = high[1]`, `hi = low[3]`.

The size test uses the raw gap: `minfvg < fhi - flo < maxfvg`. A negative raw gap (no FVG) fails the test.

### Candidates and confirmation

`ifvgCode()` keeps one candidate per side in `var` state:

| Candidate | Comes from | Confirmed when | Cancelled when |
| --- | --- | --- | --- |
| IFVG- (bearish) | Last long FVG (`bearHi`, `bearLo`) | A close below `bearLo` | Once confirmed, a close back above `bearHi` |
| IFVG+ (bullish) | Last short FVG (`bullHi`, `bullLo`) | A close above `bullHi` | Once confirmed, a close back below `bullLo` |

A new FVG of the same kind replaces the candidate and clears its confirmation. A cancelled candidate can confirm again on a later close through it.

The function returns `(bullConf ? 1 : 0) + (bearConf ? 2 : 0)`: 0 none, 1 IFVG+ only, 2 IFVG- only, 3 both. Both confirmed is shown as neutral, with its own tooltip.

## Delivery

### States

`deliveryCode()` keeps one state machine in `var` variables:

| Variable | Meaning |
| --- | --- |
| `st` | 1 bullish, -1 bearish, 0 not yet defined |
| `ref`, `refLo`, `refHi` | Open, low and high of the reference candle: the last up-close candle while bullish, the last down-close candle while bearish |
| `conf` | A continuation happened since the last change of state |
| `flips` | Consecutive changes of state with no continuation in between |
| `sTop`, `sBot` | Top and bottom open of the opens involved in the current run of flips |
| `inRng`, `rTop`, `rBot` | The range is locked, and its top and bottom opens |
| `pend`, `pOpen`, `pLvl` | Deferred CISD: -1 bearish waiting for `pLvl` (the low of the reference) to be taken, 1 bullish waiting for the high; `pOpen` is the open that was closed through |
| `pNewOpen`, `pNewHi`, `pNewLo` | The candle that will become the new reference if the deferred change fires |
| `lastUpLo/Hi`, `lastDnLo/Hi` | Range (body or high-low) of the last up-close and down-close candles, used for inner candles |

It returns `(st + 1) + (inRng ? 3 : 0)`: 0 bearish, 1 undefined, 2 bullish, plus 3 when the range is locked. The table decodes it with `dc % 3 - 1` and `dc >= 3`.

### Per candle

Doji candles (close equal to open) are neither up nor down: they never set a reference or change state.

1. **Inner candles.** With `exclInner`, a candle whose open and close lie within the range of the previous candle is inner. A down-close candle is also inner when it lies within the last up-close candle, and an up-close candle when it lies within the last down-close candle. The range is the body or the high-low, per `innerRange`. Inner candles do nothing at all.
2. **Undefined.** The first up- or down-close candle sets the state, the reference and `conf = true`.
3. **Range locked.** Only an up-close candle closing above `rTop` (bullish) or a down-close candle closing below `rBot` (bearish) changes the state. That unlocks the range, clears the flip count and any pending change, and makes the breaking candle the reference. Any other candle widens the range to include its open.
4. **Bullish.** An up-close candle becomes the new reference. If its open is above the old reference it is a continuation: `conf = true`, `flips = 0`. A down-close candle closing below `ref` is a bearish CISD. With `deferOn` and its low not below `refLo`, the change is deferred (`pend = -1`). On later candles, the change fires when a close below `pOpen` comes with a low below `pLvl`; it is dropped when a candle closes back above `pOpen` or closes up. While pending, each new down-close candle becomes the candidate reference.
5. **Bearish.** The mirror image of 4.

### Flips and the locked range

On a change of state, if a continuation happened since the last change, the run starts again: `flips = 1` and `sTop`, `sBot` are the broken open and the new reference open. Otherwise `flips` grows and the span widens to include both opens. When `flips` reaches `lockN`, the range locks with `rTop = sTop` and `rBot = sBot`.

## Table

`table.new` is called once with its size fixed from the inputs at that time. The size along the timeframe axis is the number of visible timeframes plus the header, and the other axis is one plus the number of state rows. `put(slot, r, ...)` maps a timeframe slot and a row to a cell and swaps the coordinates for the vertical layout. Slot -1 is the header and row -1 is the timeframe label.

All cells are written on `barstate.islast` only.

## Function reference

| Function | Purpose |
| --- | --- |
| `getsize(s)`, `getpos(s)` | Turn input strings into `size.*` and `position.*` constants |
| `longFVGHL(...)`, `shortFVGHL(...)` | Gap of a bullish or bearish FVG from three candles, with and without the volume-gap extension |
| `ifvgCode()` | IFVG candidate and confirmation state on the current series; returns 0 to 3 |
| `deliveryCode()` | Delivery state machine on the current series; returns 0 to 5 |
| `secPair(tf)` | Both states for the last completed candle of `tf` |
| `tfName(s)` | Label for the chart timeframe |
| `put(slot, r, txt, tc, tip)` | Write one cell in either orientation |

## Limitations and extension points

### Limitations

- `minfvg` is in points, with one default for every timeframe and symbol. The 3-point default suits NQ intraday; DAX, higher timeframes and other symbols may need other values.
- Only the last FVG of each side is a candidate. An older FVG that inverts later is not seen once a newer one of the same kind exists.
- While the range is locked, `rTop` and `rBot` widen with the open of every candle (inner candles excluded), so a long consolidation can push the unlock levels far apart.
- Weekly and daily states need enough history on those timeframes. On a short chart the first columns can stay neutral.
- The table holds no history: it shows the current state only, so the states cannot be backtested from the chart.
- There are always eight `request.security` calls, even when timeframes are hidden.

### Extension points

- Alerts on a change of IFVG bias or delivery for a chosen timeframe. The codes are already numeric per timeframe, so it is a comparison with the previous bar.
- A combined row (bias and delivery agree) to read alignment at a glance.
- Per-timeframe FVG minimum sizes, as ICTcotas does for its FVGs by timeframe.
