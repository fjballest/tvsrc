# Builds ICTscalpHTFFVGs.txt from ICTScalp.txt: ICTScalp plus the required HTF FVG models (FVGs of 15m / 1H / 4H by
# default): CE2END (CE rejection, failure at the near edge, entry on the IFVG of an FVG formed on the way to that edge)
# TOP2NEXT (HTF candle wicking past the CE and closing at the far edge, entry on the next HTF candle) and END2END
# (CE, one edge, the other edge, liquidity on the target side only; entry on the IFVG leaving the second edge).
# usage: python3 mkhtf.py ICTScalp.txt ICTscalpHTFFVGs.txt
import sys
src, dst = sys.argv[1], sys.argv[2]
s = open(src).read()

def rep(old, new, count=1):
    global s
    assert s.count(old) == count, (s.count(old), old)
    s = s.replace(old, new)

rep('indicator("ICTScalp", ', 'indicator("ICTscalpHTFFVGs", ')

rep('// ICTScalp: enter-long / enter-short signals only, to experiment with scalping conditions.',
    '// ICTscalpHTFFVGs: ICTScalp with one more required condition, one of the higher timeframe FVG models, for\n'
    '// lower timeframe charts (e.g. 2m). The FVGs come from up to three selectable timeframes (15m, 1H, 4H by default).\n'
    '// CE2END:\n'
    '//  - Short, on a bullish HTF FVG: price comes down to its CE (low at or below the CE) without going below\n'
    '//    its lower quadrant (low under lo + 25% of the FVG), then goes up to its high. It may close a bit above\n'
    '//    the high, but a close beyond the high by more than the tolerance cancels the setup. The entry is the\n'
    '//    IFVG- of a chart timeframe bullish FVG formed on that way up (its candle 2 between the CE candle and\n'
    '//    the first candle that reached the high), within N candles of reaching the high. One entry per setup.\n'
    '//  - Long: the mirror on a bearish HTF FVG (up to its CE without going above its upper quadrant, down to\n'
    '//    its low, IFVG+ of a bearish FVG formed on that way down).\n'
    '// TOP2NEXT:\n'
    '//  - Long, on a bullish HTF FVG: an HTF candle of its timeframe wicks to or below the CE but closes at or near\n'
    '//    the high (within a fraction of the FVG), with no body in the lower half (body bottom at or above the CE).\n'
    '//    During the next HTF candle, the entry is an IFVG+ whose candle closes at or above the FVG high. One entry.\n'
    '//  - Short: the mirror on a bearish HTF FVG (wick to or above the CE, close at or near the low, body in the\n'
    '//    lower half only; IFVG- closing at or below the low during the next HTF candle).\n'
    '// END2END:\n'
    '//  - Long: price reaches the CE of an HTF FVG, then its high, then its low. Near the high there are relatively\n'
    '//    equal highs with the right one lower than the left (short-term highs of the "Took liquidity" pivots,\n'
    '//    since the CE), and near the low there are no relatively equal lows with the right one higher than the\n'
    '//    left. The entry is the IFVG+ of a bearish FVG formed after the high was reached, leaving the low (its\n'
    '//    candle closes at or above the low), before price is back at the high and within N candles of the low.\n'
    '//  - Short: the mirror (CE, low, then high; RELs with the right one higher near the low, no REHs with the\n'
    '//    right one lower near the high; IFVG- leaving the high).\n'
    '// HTF FVGs: completed candle triples of each selected timeframe (ICTcotas rule, with its VI extension when\n'
    '// "extend FVG range to include VIs" is on), removed once an HTF candle closes through them (inverts them).\n'
    '// The model and HTF of each entry are written in front of its trade label.\n'
    '// Generated from ICTScalp.txt (mkhtf.py): everything else is ICTScalp, and changes to ICTScalp apply here.\n'
    '//\n'
    '// ICTScalp: enter-long / enter-short signals only, to experiment with scalping conditions.')

rep('''const string G_TM = "Time"''', '''const string G_H1 = "HTF FVG models"
bool ce2On = input.bool(true, "CE2END", group = G_H1, inline = "md")
bool t2nOn = input.bool(true, "TOP2NEXT", group = G_H1, inline = "md")
bool e2On = input.bool(true, "END2END", group = G_H1, inline = "md",
     tooltip = "Models that can give an entry (see the description at the top of the script). The inputs below "+
         "are shared; the ones marked CE2END or TOP2NEXT apply to that model only.")
bool sOn1 = input.bool(true, "Timeframes", group = G_H1, inline = "tf",
     tooltip = "FVGs of these timeframes are used by the models (each one only when it is above the chart "+
         "timeframe).")
string sTf1 = input.timeframe("15", "", group = G_H1, inline = "tf")
bool sOn2 = input.bool(true, "", group = G_H1, inline = "tf")
string sTf2 = input.timeframe("60", "", group = G_H1, inline = "tf")
bool sOn3 = input.bool(true, "", group = G_H1, inline = "tf")
string sTf3 = input.timeframe("240", "", group = G_H1, inline = "tf")
int sKeep = input.int(6, "Keep per timeframe", minval = 1, maxval = 20, group = G_H1, inline = "k")
float sMin = input.float(0.0, "min size", minval = 0.0, group = G_H1, inline = "k",
     tooltip = "Most recent FVGs kept for each timeframe, and minimum gap size in price points.")
float h1Q = input.float(0.25, "CE2END: quadrant price must not pass (fraction of the FVG)", minval = 0.0, maxval = 0.5,
     step = 0.05, group = G_H1,
     tooltip = "Short (bullish HTF FVG): before reaching the high, no low below lo + this fraction of the FVG "+
         "(0.25 = the lower quadrant). Long (bearish HTF FVG): no high above hi - this fraction.")
float h1Tol = input.float(0.25, "CE2END / END2END: close beyond the edge allowed (fraction of the FVG)", minval = 0.0, step = 0.05,
     group = G_H1,
     tooltip = "After reaching the high (short) / low (long) of the HTF FVG, a close beyond it by more than this "+
         "fraction of the FVG cancels the setup (price did not refuse to go further).")
int h1Win = input.int(60, "CE2END / END2END: entry within candles after the edge", minval = 1, maxval = 1000,
     group = G_H1)
float t2nNear = input.float(0.2, "TOP2NEXT: close near the edge (fraction of the FVG)", minval = 0.0, maxval = 0.5,
     step = 0.05, group = G_H1,
     tooltip = "Long: the HTF candle closes at or above hi - this fraction of the FVG. Short: at or below "+
         "lo + this fraction.")
float e2Band = input.float(0.15, "END2END: liquidity near the edge (fraction of the FVG)", minval = 0.0, step = 0.05,
     group = G_H1, inline = "e2")
float e2Eq = input.float(0.1, "equal within", minval = 0.0, step = 0.05, group = G_H1, inline = "e2",
     tooltip = "Short-term highs / lows count near an edge when within this fraction of the FVG from it, and two "+
         "consecutive ones are relatively equal when they differ by at most the second fraction of the FVG.")
bool showH1 = input.bool(true, "Mark the model candles", group = G_H1, inline = "mk",
     tooltip = "CE2END: a small circle on the candle that reaches the CE and a diamond on the first candle that "+
         "reaches the edge. TOP2NEXT: a square on the first candle of the next HTF candle, when the model is armed. END2END: a cross on the candle that reaches the second edge.")
color h1Col = input.color(color.fuchsia, "", group = G_H1, inline = "mk")

const string G_TM = "Time"''')

rep('''
runMit()
''', '''
// ============================================================ HTF FVG setup
type SetupFvg
    int slot        // 0, 1, 2: timeframe input
    int dir         // 1 bullish (short setup), -1 bearish (long setup)
    float hi
    float lo
    float ce
    int hst = 0     // 0 waiting for the CE, 1 CE reached, 2 edge reached, 3 entered, -1 cancelled
    int hce = na    // bar that reached the CE
    int hhit = na   // first bar that reached the edge
    int e2Ls = 0    // END2END long: 0 waiting for the CE, 1 CE, 2 high reached, 3 low reached
    int e2Lc = na   // bars: CE, high, low
    int e2La = na
    int e2Lb = na
    int e2Ss = 0    // END2END short: 0 waiting for the CE, 1 CE, 2 low reached, 3 high reached
    int e2Sc = na   // bars: CE, low, high
    int e2Sa = na
    int e2Sb = na
    int tnArm = na  // TOP2NEXT: open time of the HTF candle where the entry may happen (na: not armed / used)

var array<SetupFvg> sfv = array.new<SetupFvg>()
var array<int> sLastT = array.new<int>(3, na)

sAdd(int slot, int dir, float lo, float hi) =>
    sfv.push(SetupFvg.new(slot, dir, hi, lo, (hi + lo) / 2))
    int cnt = 0
    for f in sfv
        if f.slot == slot
            cnt += 1
    if cnt > sKeep
        for i = 0 to sfv.size() - 1
            if sfv.get(i).slot == slot
                sfv.remove(i)
                break
    true

// FVGs of one completed candle triple (1 newest, 3 oldest), with the VI extension (addFvgs of ICTcotas)
sFvgs(int slot, float o1, float h1, float l1, float c1, float o2, float h2, float l2, float c2,
     float h3, float l3, float o3, float c3) =>
    float topP = math.max(o2, c2)
    float botP = math.min(o2, c2)
    float topC = math.max(o1, c1)
    float botC = math.min(o1, c1)
    float top3 = math.max(o3, c3)
    float bot3 = math.min(o3, c3)
    if l1 > h3 and l1 - h3 >= sMin
        float flo = h3
        float fhi = l1
        if fvgvi
            if botP > top3 and botP - top3 >= vimin
                flo := math.min(flo, top3)
                fhi := math.max(fhi, botP)
            if botC > topP and botC - topP >= vimin
                flo := math.min(flo, topP)
                fhi := math.max(fhi, botC)
        sAdd(slot, 1, flo, fhi)
    if h1 < l3 and l3 - h1 >= sMin
        float flo = h1
        float fhi = l3
        if fvgvi
            if topP < bot3 and bot3 - topP >= vimin
                flo := math.min(flo, topP)
                fhi := math.max(fhi, bot3)
            if topC < botP and botP - topC >= vimin
                flo := math.min(flo, topC)
                fhi := math.max(fhi, botP)
        sAdd(slot, -1, flo, fhi)
    true

// one completed HTF candle of slot (cO, cH, cL, cC), the next one opening at t0: the FVGs it closes through are
// removed, and TOP2NEXT is armed on those it qualifies for. Returns [armed long, armed short].
sHtf(int slot, float cO, float cH, float cL, float cC, int t0) =>
    bool aL = false
    bool aS = false
    if sfv.size() > 0
        for i = sfv.size() - 1 to 0
            SetupFvg f = sfv.get(i)
            if f.slot == slot
                if (f.dir == 1 and cC < f.lo) or (f.dir == -1 and cC > f.hi)
                    sfv.remove(i)
                else if t2nOn
                    float r = f.hi - f.lo
                    if f.dir == 1 and cL <= f.ce and math.min(cO, cC) >= f.ce and cC >= f.hi - t2nNear * r
                        f.tnArm := t0
                        aL := true
                    else if f.dir == -1 and cH >= f.ce and math.max(cO, cC) <= f.ce and cC <= f.lo + t2nNear * r
                        f.tnArm := t0
                        aS := true
    [aL, aS]

var array<string> sTfs = array.from(sTf1, sTf2, sTf3)
var array<bool> sOns = array.from(sOn1, sOn2, sOn3)
bool sAny = false
bool tnArmL = false
bool tnArmS = false
for i = 0 to 2
    string tf = sTfs.get(i)
    if sOns.get(i) and timeframe.in_seconds(tf) > tfsecs
        sAny := true
        [t0, p1, q1, r1, u1, t1, p2, q2, r2, u2, t2, h3, l3, p3, u3] = htf(tf)
        if not na(t0)
            int last = sLastT.get(i)
            if not na(last) and t0 != last
                [aL, aS] = sHtf(i, p1, q1, r1, u1, t0)
                tnArmL := tnArmL or aL
                tnArmS := tnArmS or aS
                sFvgs(i, p1, q1, r1, u1, p2, q2, r2, u2, h3, l3, p3, u3)
            sLastT.set(i, t0)

// CE2END state, on closed candles.
// Returns the marks [CE reached / edge reached] for shorts (bullish FVGs) and longs (bearish FVGs).
h1Step() =>
    bool ceS = false
    bool hitS = false
    bool ceL = false
    bool hitL = false
    if barstate.isconfirmed and ce2On
        for l in sfv
            if l.hst >= 0 and l.hst < 3
                float r = l.hi - l.lo
                if l.dir == 1
                    if l.hst < 2 and low < l.lo + h1Q * r
                        l.hst := -1
                    else if l.hst == 0 and low <= l.ce
                        l.hst := 1
                        l.hce := bar_index
                        ceS := true
                    else if l.hst == 1 and high >= l.hi
                        l.hst := 2
                        l.hhit := bar_index
                        hitS := true
                    if l.hst == 2 and (close > l.hi + h1Tol * r or bar_index - l.hhit > h1Win)
                        l.hst := -1
                else
                    if l.hst < 2 and high > l.hi - h1Q * r
                        l.hst := -1
                    else if l.hst == 0 and high >= l.ce
                        l.hst := 1
                        l.hce := bar_index
                        ceL := true
                    else if l.hst == 1 and low <= l.lo
                        l.hst := 2
                        l.hhit := bar_index
                        hitL := true
                    if l.hst == 2 and (close < l.lo - h1Tol * r or bar_index - l.hhit > h1Win)
                        l.hst := -1
    [ceS, hitS, ceL, hitL]

// CE2END: HTF FVG in setup (edge reached) for an entry in direction dir with the IFVG of f: f was formed on the
// way to the edge (its candle 2 between the CE candle and the first candle at the edge)
h1Match(int dir, FVG f) =>
    SetupFvg m = na
    if not na(f)
        for l in sfv
            if ce2On and na(m) and l.hst == 2 and l.dir == -dir and f.sbar >= l.hce and f.sbar + 1 <= l.hhit
                m := l
    m

// TOP2NEXT: HTF FVG armed for an entry in direction dir during the current HTF candle of its timeframe, with
// this candle closing at or beyond its edge (at or above the high for longs, at or below the low for shorts)
tnMatch(int dir) =>
    SetupFvg m = na
    if t2nOn
        for l in sfv
            if na(m) and l.dir == dir and not na(l.tnArm) and l.tnArm == sLastT.get(l.slot)
                 and (dir == 1 ? close >= l.hi : close <= l.lo)
                m := l
    m

// END2END state, on closed candles. Returns [second edge reached long, short].
e2Step() =>
    bool bL = false
    bool bS = false
    if barstate.isconfirmed and e2On
        for l in sfv
            float r = l.hi - l.lo
            // long: CE, high, low
            if l.e2Ls == 3 and (high >= l.hi or close < l.lo - h1Tol * r or bar_index - l.e2Lb > h1Win)
                l.e2Ls := 0
            if l.e2Ls == 0 and low <= l.ce and high >= l.ce
                l.e2Ls := 1
                l.e2Lc := bar_index
            else if l.e2Ls == 1 and high >= l.hi
                l.e2Ls := 2
                l.e2La := bar_index
            else if l.e2Ls == 2 and low <= l.lo
                l.e2Ls := 3
                l.e2Lb := bar_index
                bL := true
            // short: CE, low, high
            if l.e2Ss == 3 and (low <= l.lo or close > l.hi + h1Tol * r or bar_index - l.e2Sb > h1Win)
                l.e2Ss := 0
            if l.e2Ss == 0 and low <= l.ce and high >= l.ce
                l.e2Ss := 1
                l.e2Sc := bar_index
            else if l.e2Ss == 1 and low <= l.lo
                l.e2Ss := 2
                l.e2Sa := bar_index
            else if l.e2Ss == 2 and high >= l.hi
                l.e2Ss := 3
                l.e2Sb := bar_index
                bS := true
    [bL, bS]

[h1CeS, h1HitS, h1CeL, h1HitL] = h1Step()
[e2BotL, e2TopS] = e2Step()
plotshape(showH1 and e2BotL, "END2END long: low", shape.xcross, location.belowbar, h1Col, size = size.tiny)
plotshape(showH1 and e2TopS, "END2END short: high", shape.xcross, location.abovebar, h1Col, size = size.tiny)
plotshape(showH1 and h1CeS, "HTF setup short: CE", shape.circle, location.belowbar, h1Col, size = size.tiny)
plotshape(showH1 and h1HitS, "HTF setup short: high", shape.diamond, location.abovebar, h1Col, size = size.tiny)
plotshape(showH1 and h1CeL, "HTF setup long: CE", shape.circle, location.abovebar, h1Col, size = size.tiny)
plotshape(showH1 and h1HitL, "HTF setup long: low", shape.diamond, location.belowbar, h1Col, size = size.tiny)
plotshape(showH1 and tnArmL, "TOP2NEXT long armed", shape.square, location.belowbar, h1Col, size = size.tiny)
plotshape(showH1 and tnArmS, "TOP2NEXT short armed", shape.square, location.abovebar, h1Col, size = size.tiny)

runMit()
''')

rep('''
bool condShort = barstate.isconfirmed and trigShort
''', '''
// END2END: two consecutive short-term pivots (bars from 'from' on, within band of lvl) that are relatively
// equal (differ by at most eq) with the right one on side sgn of the left (-1: lower, 1: higher)
eqPair(array<int> bs, array<float> ps, int from, float lvl, float band, float eq, int sgn) =>
    bool found = false
    float prev = na
    if bs.size() > 0
        for i = 0 to bs.size() - 1
            if bs.get(i) >= from
                float p = ps.get(i)
                if math.abs(p - lvl) <= band
                    if not na(prev) and (p - prev) * sgn > 0 and math.abs(p - prev) <= eq
                        found := true
                    prev := p
    found

// END2END: HTF FVG with the second edge reached for an entry in direction dir with the IFVG of f, formed
// after the first edge was reached; the entry candle closes back inside (at or beyond the second edge), and
// the liquidity rule holds: equal pivots, right one closer to the inside, at the target edge; none at the
// second edge with the right one further out
e2Match(int dir, FVG f) =>
    SetupFvg m = na
    if e2On and not na(f)
        for l in sfv
            if na(m)
                float r = l.hi - l.lo
                if dir == 1 and l.e2Ls == 3 and f.sbar + 1 >= l.e2La and close >= l.lo
                    if eqPair(sthB, sthP, l.e2Lc, l.hi, e2Band * r, e2Eq * r, -1)
                         and not eqPair(stlB, stlP, l.e2Lc, l.lo, e2Band * r, e2Eq * r, 1)
                        m := l
                else if dir == -1 and l.e2Ss == 3 and f.sbar + 1 >= l.e2Sa and close <= l.hi
                    if eqPair(stlB, stlP, l.e2Sc, l.lo, e2Band * r, e2Eq * r, 1)
                         and not eqPair(sthB, sthP, l.e2Sc, l.hi, e2Band * r, e2Eq * r, -1)
                        m := l
    m

bool condShort = barstate.isconfirmed and trigShort
''')
rep('''     and (not liqOn or (not na(lastSweepH) and bar_index - lastSweepH < liqLook))
''', '''     and (not liqOn or (not na(lastSweepH) and bar_index - lastSweepH < liqLook))
     and (not na(h1Match(-1, bearifvg)) or not na(tnMatch(-1)) or not na(e2Match(-1, bearifvg)))
''')
rep('''     and (not liqOn or (not na(lastSweepL) and bar_index - lastSweepL < liqLook))
''', '''     and (not liqOn or (not na(lastSweepL) and bar_index - lastSweepL < liqLook))
     and (not na(h1Match(1, bullifvg)) or not na(tnMatch(1)) or not na(e2Match(1, bullifvg)))
''')

rep('''if not timeOk
    goShort := false
    goLong := false
''', '''if not timeOk
    goShort := false
    goLong := false
// a waiting entry needs its HTF setup to be still valid; one entry per setup
if goShort
    SetupFvg h1s = h1Match(-1, bearifvg)
    if na(h1s)
        goShort := false
    else
        h1s.hst := 3
if goLong
    SetupFvg h1l = h1Match(1, bullifvg)
    if na(h1l)
        goLong := false
    else
        h1l.hst := 3
''')

rep('"ICTScalp short"', '"ICTscalpHTFFVGs short"')
rep('"ICTScalp long"', '"ICTscalpHTFFVGs long"')
rep('"ICTScalp SHORT ', '"ICTscalpHTFFVGs SHORT ', 2)
rep('"ICTScalp LONG ', '"ICTscalpHTFFVGs LONG ', 2)

s += '''
var table warnH1 = table.new(position.bottom_right, 1, 1)
if barstate.islast and not sAny
    table.cell(warnH1, 0, 0, "ICTscalpHTFFVGs: no selected timeframe is above the chart timeframe",
         text_color = color.orange,
         text_size = size.small)
'''

# take profit at the HTF FVG of the setup: its far quadrant or its far edge
rep('''     "Next short-term high / low"], group = G_T,
     tooltip = "Risk ratio 1:N: at N times the risk.\\n"+''', '''     "Next short-term high / low", "HTF FVG far quadrant", "HTF FVG far edge"], group = G_T,
     tooltip = "Risk ratio 1:N: at N times the risk.\\n"+''')
rep('''         "pivot lengths.")
float rrN = input.float(2.0, "Risk ratio 1:N (minimum for short-term high / low), N", minval = 1.0, step = 0.5,''',
'''         "pivot lengths.\\n"+
         "HTF FVG far quadrant: at the lower quadrant (short, low + 25% of the FVG) / upper quadrant (long, "+
         "high - 25%) of the HTF FVG of the setup.\\n"+
         "HTF FVG far edge: at the low (short) / high (long) of the HTF FVG of the setup.\\n"+
         "For both HTF FVG modes, with price already past that level at the entry, the take profit is at 1:N "+
         "(always the case for TOP2NEXT, whose entries are beyond the FVG). For END2END the far edge is the "+
         "target edge (the high for longs, the low for shorts).")
float rrN = input.float(2.0, "Risk ratio 1:N (minimum for short-term high / low, fallback for HTF FVG), N",
     minval = 1.0, step = 0.5,''')
rep('''// a waiting entry needs its HTF setup to be still valid; one entry per setup
if goShort
    SetupFvg h1s = h1Match(-1, bearifvg)
    if na(h1s)
        goShort := false
    else
        h1s.hst := 3
if goLong
    SetupFvg h1l = h1Match(1, bullifvg)
    if na(h1l)
        goLong := false
    else
        h1l.hst := 3
''', '''// a waiting entry needs its HTF setup to be still valid; one entry per setup
var SetupFvg h1Cur = na     // HTF FVG of the last entry (take profit modes)
var string h1Model = ""     // model of the last entry (trade label)
if goShort
    SetupFvg h1s = h1Match(-1, bearifvg)
    SetupFvg tns = tnMatch(-1)
    SetupFvg e2s = e2Match(-1, bearifvg)
    if not na(h1s)
        h1s.hst := 3
        h1Cur := h1s
        h1Model := "CE2END " + tfName(sTfs.get(h1Cur.slot))
    else if not na(tns)
        tns.tnArm := na
        h1Cur := tns
        h1Model := "TOP2NEXT " + tfName(sTfs.get(h1Cur.slot))
    else if not na(e2s)
        e2s.e2Ss := 0
        h1Cur := e2s
        h1Model := "END2END " + tfName(sTfs.get(h1Cur.slot))
    else
        goShort := false
if goLong
    SetupFvg h1l = h1Match(1, bullifvg)
    SetupFvg tnl = tnMatch(1)
    SetupFvg e2l = e2Match(1, bullifvg)
    if not na(h1l)
        h1l.hst := 3
        h1Cur := h1l
        h1Model := "CE2END " + tfName(sTfs.get(h1Cur.slot))
    else if not na(tnl)
        tnl.tnArm := na
        h1Cur := tnl
        h1Model := "TOP2NEXT " + tfName(sTfs.get(h1Cur.slot))
    else if not na(e2l)
        e2l.e2Ls := 0
        h1Cur := e2l
        h1Model := "END2END " + tfName(sTfs.get(h1Cur.slot))
    else
        goLong := false
''')
rep('''        if not na(nx)
            tp := nx
    [stp, tp, risk > 0]
''', '''        if not na(nx)
            tp := nx
    // at the far quadrant / edge of the HTF FVG of the setup, while price has not passed it
    if (tpMode == "HTF FVG far quadrant" or tpMode == "HTF FVG far edge") and not na(h1Cur)
        float r = h1Cur.hi - h1Cur.lo
        float q = tpMode == "HTF FVG far edge" ? 0.0 : 0.25
        float lv = dir == -1 ? h1Cur.lo + q * r : h1Cur.hi - q * r
        if (lv - close) * dir > 0
            tp := lv
    [stp, tp, risk > 0]
''')

rep('''string lt = "SL " + ''', '''string lt = h1Model + "   SL " + ''')

# defaults chosen by Francisco for this script (set whatever the default in ICTScalp is)
import re
for name, val in [('retMode', 'true'), ('oteOnly', 'true'), ('liqOn', 'true'), ('showTrades', 'true'),
                  ('simulate', 'true'), ('beOn', 'false'),
                  ('trOn', 'false')]:
    s, n = re.subn(r'^bool %s = input\.bool\((true|false), ' % name, 'bool %s = input.bool(%s, ' % (name, val), s,
                   flags=re.M)
    assert n == 1, name

# CRLF, as TradingView saves it
open(dst, 'w', newline='\r\n').write(s)
print("ok")
