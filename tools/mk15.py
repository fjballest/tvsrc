# Builds ICTScalp15.txt from ICTScalp.txt: ICTScalp plus the required 15m FVG / IFVG rejection.
# usage: python3 mk15.py ICTScalp.txt ICTScalp15.txt
import sys
src, dst = sys.argv[1], sys.argv[2]
s = open(src).read()

def rep(old, new, count=1):
    global s
    assert s.count(old) == count, (s.count(old), old)
    s = s.replace(old, new)

rep('indicator("ICTScalp", ', 'indicator("ICTScalp15", ')

rep('// ICTScalp: enter-long / enter-short signals only, to experiment with scalping conditions.',
    '// ICTScalp15: ICTScalp with one more required condition, for charts below 15m:\n'
    '//  - Long: a candle rejected a bullish 15m FVG or IFVG (its low reached the FVG high or CE and it closed\n'
    '//    above that level) and no later candle reached that level again; then an IFVG+ is confirmed on the\n'
    '//    chart timeframe. Short: the same in the other direction (bearish 15m FVG / IFVG, its low or CE).\n'
    '//  - The 15m FVGs / IFVGs are the 15m FVGs by timeframe of ICTcotas (always on in this script).\n'
    '// Generated from ICTScalp.txt (mk15.py): everything else is ICTScalp, and changes to ICTScalp apply here.\n'
    '//\n'
    '// ICTScalp: enter-long / enter-short signals only, to experiment with scalping conditions.')

rep('''const string G_TM = "Time"''', '''const string G_15 = "15m FVG / IFVG rejection"
bool show15 = input.bool(true, "Mark the rejection candles", group = G_15,
     tooltip = "A small circle below (long) or above (short) each candle that rejects a 15m FVG / IFVG.")
color rej15Col = input.color(color.purple, "color", group = G_15)

const string G_TM = "Time"''')

# the 15m FVGs of ICTcotas are always computed (charts below 15m)
rep('    bool on = domf and mfOk(s) and mfSel.get(i)',
    '    bool on = (domf and mfOk(s) and mfSel.get(i)) or (i == 0 and mfOk(s))')

rep('''
runMit()
''', '''
// 15m FVG / IFVG rejection, tested like rejCheck against the levels alive before the mitigation of this bar.
// Long: a bullish 15m FVG / IFVG whose CE (or else its high) is reached by the low of the candle, with the
// close above that level. Short: a bearish one whose CE (or else its low) is reached by the high, with the
// close below it. When several levels are rejected, the one closest to the close is kept.
rej15() =>
    float pL = na
    float pS = na
    for l in lvls
        if not l.dead and l.kind == 3 and l.tfi == 3
            if l.dir == 1
                float p = low <= l.ce ? l.ce : low <= l.hi ? l.hi : na
                if not na(p) and close > p
                    pL := na(pL) ? p : math.max(pL, p)
            else
                float p = high >= l.ce ? l.ce : high >= l.lo ? l.lo : na
                if not na(p) and close < p
                    pS := na(pS) ? p : math.min(pS, p)
    [pL, pS]

[rej15L, rej15S] = rej15()
var float r15L = na     // level of the last 15m rejection for longs, while not reached again
var float r15S = na
if barstate.isconfirmed
    if not na(rej15L)
        r15L := rej15L
    else if not na(r15L) and low <= r15L
        r15L := na
    if not na(rej15S)
        r15S := rej15S
    else if not na(r15S) and high >= r15S
        r15S := na
plotshape(show15 and barstate.isconfirmed and not na(rej15L), "15m rejection long", shape.circle,
     location.belowbar, rej15Col, size = size.tiny)
plotshape(show15 and barstate.isconfirmed and not na(rej15S), "15m rejection short", shape.circle,
     location.abovebar, rej15Col, size = size.tiny)

runMit()
''')

rep('''     and (not liqOn or (not na(lastSweepH) and bar_index - lastSweepH < liqLook))
''', '''     and (not liqOn or (not na(lastSweepH) and bar_index - lastSweepH < liqLook))
     and not na(r15S)
''')
rep('''     and (not liqOn or (not na(lastSweepL) and bar_index - lastSweepL < liqLook))
''', '''     and (not liqOn or (not na(lastSweepL) and bar_index - lastSweepL < liqLook))
     and not na(r15L)
''')

rep('''if not timeOk
    goShort := false
    goLong := false
''', '''if not timeOk
    goShort := false
    goLong := false
// a waiting entry needs the 15m rejection to be still valid
if na(r15S)
    goShort := false
if na(r15L)
    goLong := false
''')

rep('"ICTScalp short"', '"ICTScalp15 short"')
rep('"ICTScalp long"', '"ICTScalp15 long"')
rep('"ICTScalp SHORT ', '"ICTScalp15 SHORT ', 2)
rep('"ICTScalp LONG ', '"ICTScalp15 LONG ', 2)

s += '''
var table warn15 = table.new(position.bottom_right, 1, 1)
if barstate.islast and tfsecs >= 900
    table.cell(warn15, 0, 0, "ICTScalp15: use a chart timeframe below 15m", text_color = color.orange,
         text_size = size.small)
'''

open(dst, 'w').write(s)
print("ok")
