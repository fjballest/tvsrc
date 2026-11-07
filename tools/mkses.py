# Builds ICTScalpSes.txt from ICTScalp.txt: ICTScalp plus the required sweep of a session or day high / low.
# usage: python3 mkses.py ICTScalp.txt ICTScalpSes.txt
import sys
src, dst = sys.argv[1], sys.argv[2]
s = open(src).read()

def rep(old, new, count=1):
    global s
    assert s.count(old) == count, (s.count(old), old)
    s = s.replace(old, new)

rep('indicator("ICTScalp", ', 'indicator("ICTScalpSes", ')

rep('// ICTScalp: enter-long / enter-short signals only, to experiment with scalping conditions.',
    '// ICTScalpSes: ICTScalp with one more required condition:\n'
    '//  - Long: a confirmed IFVG+ after a candle took the low of an ended session (Asia, London, NY AM,\n'
    '//    NY PM) or of a previous day, within the last N candles. Short: a confirmed IFVG- after a candle\n'
    '//    took the high of an ended session or of a previous day.\n'
    '//  - Sessions, days, time zone and the taken rule (price trades beyond the unbroken max / min) are\n'
    '//    those of ICTlevels.\n'
    '// Generated from ICTScalp.txt (mkses.py): everything else is ICTScalp, and changes to ICTScalp apply here.\n'
    '//\n'
    '// ICTScalp: enter-long / enter-short signals only, to experiment with scalping conditions.')

rep('''const string G_TM = "Time"''', '''const string G_SES = "Session / day high or low taken"
bool sesOn = input.bool(true, "Sessions, keep last", group = G_SES, inline = "ses",
     tooltip = "Asia, London, NY AM and NY PM, defined as in ICTlevels (time zone rule of the Time settings). "+
         "The max / min of an ended session counts until price takes it.")
int sesKeep = input.int(8, "", minval = 1, maxval = 40, group = G_SES, inline = "ses")
bool dayOn = input.bool(true, "Previous days, keep last", group = G_SES, inline = "day",
     tooltip = "Days as in ICTlevels (calendar day in the time zone). The high / low of a previous day "+
         "counts until price takes it.")
int dayKeep = input.int(5, "", minval = 1, maxval = 30, group = G_SES, inline = "day")
int sesLook = input.int(20, "Taken within the last candles", minval = 1, maxval = 500, group = G_SES,
     tooltip = "Long: a session or day low was taken within the last N candles (the entry candle included). "+
         "Short: a session or day high.")
bool showSes = input.bool(true, "Mark the candles that take them", group = G_SES, inline = "mk")
color sesCol = input.color(color.orange, "", group = G_SES, inline = "mk")

const string G_TM = "Time"''')

# the time zone and session strings are needed before the entry conditions: move them up
tzblock = '''string tkr = str.upper(syminfo.ticker)
string dsc = str.upper(syminfo.description)
bool autoEU = str.contains(dsc, "DAX") or str.contains(tkr, "DAX") or str.contains(dsc, "GERMANY")
     or str.contains(tkr, "FDX")
bool useUS = TZ == "US" or (TZ == "Auto" and not autoEU)
string tz = useUS ? "America/New_York" : "Europe/Madrid"
string asiastr = useUS ? "2000-0000" : "2300-0600"
string londonstr = useUS ? "0200-0500" : "0800-1300"
string nyamstr = useUS ? "0700-1130" : "1330-1800"
string nypmstr = useUS ? "1330-1600" : "1900-2200"
'''
rep(tzblock, '')

rep('''
bool condShort = barstate.isconfirmed and trigShort
''', '''
// session and day highs / lows (ICTlevels zones, state only): a session or day keeps its max / min
// while it runs; once ended they count until a candle trades beyond them
''' + tzblock + '''int tsec = hour(time, tz) * 3600 + minute(time, tz) * 60 + second(time, tz)
int tsecPrev = nz(tsec[1], tsec)
int dow = dayofweek(time, tz)
int dowPrev = nz(dow[1], dow)
bool newDay = dow != dowPrev

type SesInfo
    int ss      // start, seconds of day
    int es      // end

type SesZone
    float hi
    float lo
    SesInfo si = na

parseSecs(string v) =>
    60 * (60 * int(str.tonumber(str.substring(v, 0, 2))) + int(str.tonumber(str.substring(v, 2, 4))))

mkSesInfo(string ival) =>
    SesInfo.new(parseSecs(str.substring(ival, 0, 4)), parseSecs(str.substring(ival, 5, 9)))

method inside(SesInfo z, int t) =>
    z.ss < z.es ? (t >= z.ss and t < z.es) : (t >= z.ss or t < z.es)

// the bar contains the start instant
method starts(SesInfo z) =>
    (z.ss - tsec + 86400) % 86400 < tfsecs

// the bar contains the end instant, or the session was left (data gaps)
method ends(SesInfo z) =>
    (z.es - tsec + 86400) % 86400 < tfsecs or (z.inside(tsecPrev) and not z.inside(tsec))

var array<SesInfo> sinfos = array.from(mkSesInfo(asiastr), mkSesInfo(londonstr), mkSesInfo(nyamstr),
     mkSesInfo(nypmstr))
var array<SesZone> sesAct = array.new<SesZone>()   // sessions still running
var SesZone dayCur = na
// ended levels not taken yet: price, and whether it is a day level
var array<float> lvH = array.new<float>()
var array<bool> lvHd = array.new<bool>()
var array<float> lvL = array.new<float>()
var array<bool> lvLd = array.new<bool>()

addEnded(SesZone z, bool isDay) =>
    lvH.push(z.hi)
    lvHd.push(isDay)
    lvL.push(z.lo)
    lvLd.push(isDay)
    // keep the last dayKeep days and sesKeep sessions per side
    for side = 0 to 1
        array<float> ps = side == 0 ? lvH : lvL
        array<bool> ds = side == 0 ? lvHd : lvLd
        int cnt = 0
        int i = ps.size() - 1
        while i >= 0
            if ds.get(i) == isDay
                cnt += 1
                if cnt > (isDay ? dayKeep : sesKeep)
                    ps.remove(i)
                    ds.remove(i)
            i -= 1
    true

if newDay
    if not na(dayCur) and dayOn
        addEnded(dayCur, true)
    dayCur := SesZone.new(high, low)
if sesOn
    for zi in sinfos
        if zi.starts()
            sesAct.push(SesZone.new(high, low, zi))
if sesAct.size() > 0
    for i = sesAct.size() - 1 to 0
        SesZone z = sesAct.get(i)
        if z.si.ends()
            addEnded(z, false)
            sesAct.remove(i)
        else
            z.hi := math.max(z.hi, high)
            z.lo := math.min(z.lo, low)
if not na(dayCur)
    dayCur.hi := math.max(dayCur.hi, high)
    dayCur.lo := math.min(dayCur.lo, low)

var int lastSesH = na     // bar that took the last session / day high
var int lastSesL = na
bool tookH = false
bool tookL = false
if lvH.size() > 0
    for i = lvH.size() - 1 to 0
        if high > lvH.get(i)
            lvH.remove(i)
            lvHd.remove(i)
            tookH := true
if lvL.size() > 0
    for i = lvL.size() - 1 to 0
        if low < lvL.get(i)
            lvL.remove(i)
            lvLd.remove(i)
            tookL := true
if tookH
    lastSesH := bar_index
if tookL
    lastSesL := bar_index
plotshape(showSes and tookH, "Session / day high taken", shape.diamond, location.abovebar, sesCol,
     size = size.tiny)
plotshape(showSes and tookL, "Session / day low taken", shape.diamond, location.belowbar, sesCol,
     size = size.tiny)

bool condShort = barstate.isconfirmed and trigShort
''')

rep('''     and (not liqOn or (not na(lastSweepH) and bar_index - lastSweepH < liqLook))
''', '''     and (not liqOn or (not na(lastSweepH) and bar_index - lastSweepH < liqLook))
     and not na(lastSesH) and bar_index - lastSesH < sesLook
''')
rep('''     and (not liqOn or (not na(lastSweepL) and bar_index - lastSweepL < liqLook))
''', '''     and (not liqOn or (not na(lastSweepL) and bar_index - lastSweepL < liqLook))
     and not na(lastSesL) and bar_index - lastSesL < sesLook
''')

rep('"ICTScalp short"', '"ICTScalpSes short"')
rep('"ICTScalp long"', '"ICTScalpSes long"')
rep('"ICTScalp SHORT ', '"ICTScalpSes SHORT ', 2)
rep('"ICTScalp LONG ', '"ICTScalpSes LONG ', 2)

open(dst, 'w').write(s)
print("ok")
