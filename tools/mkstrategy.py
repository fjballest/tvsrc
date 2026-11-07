# Builds ICTScalpStrategy.txt from ICTScalp.txt: same code, strategy() declaration, orders at the mark.
# usage: python3 mkstrategy.py ICTScalp.txt ICTScalpStrategy.txt
import sys
src, dst = sys.argv[1], sys.argv[2]
s = open(src).read()

def rep(old, new):
    global s
    assert s.count(old) == 1, old
    s = s.replace(old, new)

rep('indicator("ICTScalp", overlay = true, max_bars_back = 5000, max_lines_count = 500, max_labels_count = 500,\n'
    '     max_boxes_count = 500)',
    'strategy("ICTScalp strategy", overlay = true, max_bars_back = 5000, max_lines_count = 500, max_labels_count = 500,\n'
    '     max_boxes_count = 500, process_orders_on_close = true, pyramiding = 0, default_qty_type = strategy.fixed,\n'
    '     default_qty_value = 1)')

rep('// ICTScalp: enter-long / enter-short signals only, to experiment with scalping conditions.',
    '// ICTScalp strategy: ICTScalp as a strategy, to backtest the scalping conditions.\n'
    '// Generated from ICTScalp.txt: the code is the same except the strategy() declaration and the orders\n'
    '// at the "strategy orders" mark. Entries fill at the close of the signal candle, with the stop and take\n'
    '// profit of Show Trades. One position at a time: a signal in the same direction is ignored while in a\n'
    '// position, an opposite one reverses it. Breakeven and trailing stop (Show Trades settings) manage the\n'
    '// exit order after each closed candle. alertcondition() lines are left out (not allowed in strategies).')

rep('''// ============================================================ strategy orders
''', '''// ============================================================ strategy orders
// stop and take profit of the open position; with breakeven / trailing on, the stop is managed after
// each closed candle like the simulated trades (manageStop) and the exit order is updated
var float pgStop = na
var float pgTp = na
var bool pgTrail = false
var float pgBest = na
int pgDir = int(math.sign(strategy.position_size))
if pgDir != 0 and not na(pgStop) and (beOn or trOn) and barstate.isconfirmed
    pgBest := na(pgBest) ? (pgDir == 1 ? high : low) : (pgDir == 1 ? math.max(pgBest, high) : math.min(pgBest, low))
    [pgS, pgT] = manageStop(pgDir, strategy.position_avg_price, pgStop, pgTrail, pgBest)
    pgStop := pgS
    pgTrail := pgT
    if pgDir == 1
        strategy.exit("Long exit", "Long", stop = pgStop, limit = pgTrail ? na : pgTp)
    else
        strategy.exit("Short exit", "Short", stop = pgStop, limit = pgTrail ? na : pgTp)
if goShort
    [sStp, sTp, sOk] = tradeLevels(-1, entS)
    if sOk and strategy.position_size >= 0
        strategy.entry("Short", strategy.short)
        strategy.exit("Short exit", "Short", stop = sStp, limit = sTp)
        pgStop := sStp
        pgTp := sTp
        pgTrail := false
        pgBest := na
if goLong
    [lStp, lTp, lOk] = tradeLevels(1, entL)
    if lOk and strategy.position_size <= 0
        strategy.entry("Long", strategy.long)
        strategy.exit("Long exit", "Long", stop = lStp, limit = lTp)
        pgStop := lStp
        pgTp := lTp
        pgTrail := false
        pgBest := na
''')

# alertcondition() is not allowed in strategies; alert() calls stay
rep('''alertcondition(goShort, "ICTScalp short", "ICTScalp SHORT {{ticker}} {{interval}}")
alertcondition(goLong, "ICTScalp long", "ICTScalp LONG {{ticker}} {{interval}}")
''', "")

open(dst, 'w').write(s)
print("ok")
