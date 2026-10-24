/*
 * ICTlevels for Tradovate (also the NinjaTrader web platform), custom indicator in JavaScript.
 *
 * What it is
 *   An overlay that draws ICT time based reference levels on intraday charts of NQ (New York time)
 *   and DAX (Madrid time): day / week / month high, low and close, the midnight open, the day
 *   equilibrium, REHs / RELs, the Asia, London, NY AM and NY PM sessions (killzones), the premarket
 *   range with quadrants and stddevs, NDOG / NWOG / RTHOG opening gaps with quadrants and stddevs
 *   (including the "auto" stddevs and the combined "RTHOG +2 PMKT +3" labels), EQH / EQL, TGIF
 *   levels, macros (optional background), silver bullet and the active session / macro table.
 *   It draws levels only: no signals, no alerts, no plots.
 *
 *   It is a port of the TradingView Pine Script v6 indicator ICTlevels.txt
 *   (md5 89f7ef371984d7f2d7205c710f05a38e). Parameter names are the Pine variable names and the
 *   defaults are the Pine defaults; see "ICTlevels user manual.md" for what every level means.
 *
 * How to install
 *   1. In Tradovate open Code Explorer (the "{ }" / Code Explorer module from the app menu).
 *   2. Create a new indicator (New > Indicator), delete the template code and paste this file.
 *   3. Save it (Ctrl+S / Save). It appears in the chart's indicator list as "ICTlevels" (tag ICT).
 *   4. Add it to an NQ / MNQ chart of 1 hour or lower (5m or lower for the premarket range,
 *      15m or lower for RTHOG). Load enough history: at least 15 days for the daily ATR and the
 *      daily EQH / EQL, and about 4 months for the three month levels.
 *
 * Differences with the Pine version that could not be avoided
 *   - No request.security: day bars (daily ATR(14), daily pivots for EQH D / EQL D) and month bars
 *     are built here from the chart bars. A day is the futures trading day (18:00-17:00 New York
 *     for US, the Madrid calendar day for EU). Months whose start is not in the loaded history are
 *     skipped, so the month lines need about 4 months of chart history; the ATR needs 15 days.
 *   - Time zones come from Intl.DateTimeFormat (America/New_York or Europe/Madrid), independent of
 *     the platform time zone. The bar timestamp is taken as the bar open time (as in Pine).
 *   - "market" (Auto / US / EU) replaces the Pine "Zone" input; Auto picks EU when the contract or
 *     product name contains DAX, FDX or GERMANY, otherwise US.
 *   - The 500 object budget does not exist here, so it was dropped: ndays is used as given (Pine cuts
 *     it to 46 days on 5m and lower charts with the defaults), the "object cap" table row and the
 *     showcap input are gone. The keep counts and their caps (40 sessions, 15 gaps) are kept.
 *   - The Pine processing window (history limited to about 1.5 x the days kept) and the RTHOG limit
 *     of the last 5000 bars are dropped: all loaded bars are processed.
 *   - "compute for historic data use" (visible) is not available: there is no visible range in the
 *     calculator API. Everything is computed up to the last bar.
 *   - Levels are drawn as global graphics returned from the last bar (rebuilt from the state on
 *     every update of the last bar). Labels are plain text items (no background), left aligned at
 *     the line end. Line styles map to Tradovate dash styles (dashed = FiveTwo, dotted = TwoTwo).
 *     Pine label sizes map to font sizes tiny 9, small 10, normal 12, large 16, huge 22.
 *   - The status table is drawn as global text anchored to the top right of the chart
 *     (origin grid / right / top). Colors with transparency take a separate transparency input
 *     (macrobgtransp) because color inputs have no alpha here.
 *   - The rectangle timeframe input (rectmintf) is a number of minutes.
 *   - Pivots (EQH / EQL) follow ta.pivothigh / ta.pivotlow: the center must be higher (lower) than
 *     the bars on its right and not lower (higher) than the bars on its left.
 *   - The RTHOG hint (15 minutes before the open) is drawn only on the forming last bar, as in Pine.
 *     State is committed only when a new bar index arrives; repeated calls for the forming bar
 *     restore the state saved before that bar and recompute it.
 */

const predef = require("./tools/predef");
const meta = require("./tools/meta");
const { px, du } = require("./tools/graphics");

// ============================================================ params
const STYLES = { solid: "solid", dashed: "dashed", dotted: "dotted" };
const SIZES = { tiny: "tiny", small: "small", normal: "normal", large: "large", huge: "huge" };
const ps = predef.paramSpecs;

// Pine v6 palette
const C_GREEN = "#4CAF50", C_RED = "#F23645", C_BLUE = "#2962FF", C_BLACK = "#363A45",
    C_ORANGE = "#FF9800", C_MAROON = "#880E4F";

const PARAMS = {
    // Time & Settings
    market: ps.enum({ Auto: "Auto", US: "US", EU: "EU" }, "Auto"),
    xoff: ps.number(2, 1, 1),
    // Month Settings
    domonth: ps.bool(true),
    monmaxcol: ps.color(C_GREEN),
    monmincol: ps.color(C_RED),
    monccol: ps.color(C_BLACK),
    monsty: ps.enum(STYLES, "solid"),
    monwid: ps.number(3, 1, 1),
    // Day Settings
    ndays: ps.number(60, 1, 1),
    dolines: ps.bool(true),
    daylnsty: ps.enum(STYLES, "dotted"),
    daywid: ps.number(2, 1, 1),
    daycol: ps.color(C_BLUE),
    wdaylnsty: ps.enum(STYLES, "dashed"),
    wdaywid: ps.number(2, 1, 1),
    dodmaxmin: ps.bool(true),
    daymmaxcol: ps.color(C_GREEN),
    daymmincol: ps.color(C_RED),
    daymlnsty: ps.enum(STYLES, "solid"),
    daymwid: ps.number(2, 1, 1),
    dodaylbl: ps.bool(true),
    dlblsize: ps.enum(SIZES, "tiny"),
    doreh: ps.bool(true),
    reltol: ps.number(0.05, 0.01, 0),
    doeq: ps.bool(true),
    eqlnsty: ps.enum(STYLES, "dashed"),
    eqwid: ps.number(1, 1, 1),
    eqcolor: ps.color(C_BLUE),
    doopen: ps.bool(true),
    oplnsty: ps.enum(STYLES, "dashed"),
    opwid: ps.number(1, 1, 1),
    opcolor: ps.color(C_BLACK),
    // TGIF
    dotgif: ps.bool(true),
    tgp1: ps.number(0.2, 0.05, 0.01),
    tgp2: ps.number(0.3, 0.05, 0.01),
    tgnear: ps.number(0.4, 0.05, 0.05),
    tgupcol: ps.color(C_GREEN),
    tgdncol: ps.color(C_RED),
    tgsty: ps.enum(STYLES, "dashed"),
    tgwid: ps.number(2, 1, 1),
    tglblsize: ps.enum(SIZES, "normal"),
    // Session Settings
    doses: ps.bool(true),
    nses: ps.number(20, 1, 3),
    dorect: ps.bool(true),
    rtransp: ps.number(75, 1, 0),
    nolowrects: ps.bool(true),
    rectmintf: ps.number(5, 1, 1),
    dosmaxmin: ps.bool(true),
    sesmmaxcol: ps.color(C_GREEN),
    sesmmincol: ps.color(C_RED),
    sesmlnsty: ps.enum(STYLES, "solid"),
    sesmwid: ps.number(1, 1, 1),
    doseslbl: ps.bool(true),
    slblsize: ps.enum(SIZES, "tiny"),
    asiacolor: ps.color(C_RED),
    londoncolor: ps.color(C_GREEN),
    uscolor: ps.color(C_BLUE),
    // Gaps and Ranges
    dogaps: ps.bool(true),
    ngaps: ps.number(10, 1, 1),
    dondog: ps.bool(true),
    donwog: ps.bool(true),
    dorthog: ps.bool(true),
    dorthogq: ps.bool(true),
    rthogqmin: ps.number(25.0, 0.25, 0),
    dorthogoct: ps.bool(true),
    dorthogpo3: ps.bool(true),
    rthogpo3min: ps.number(25.0, 0.25, 0),
    rthogpo3num: ps.number(4, 1, 0),
    rthogauto: ps.bool(false),
    dogrect: ps.bool(false),
    grtransp: ps.number(90, 1, 0),
    combgaps: ps.bool(true),
    combtol: ps.number(0.05, 0.01, 0),
    gaplnsty: ps.enum(STYLES, "solid"),
    gapwid: ps.number(1, 1, 1),
    gapcolor: ps.color("#C32E7D"),
    wgaplnsty: ps.enum(STYLES, "solid"),
    wgapwid: ps.number(2, 1, 1),
    wgapcolor: ps.color(C_MAROON),
    rgaplnsty: ps.enum(STYLES, "solid"),
    rgapwid: ps.number(2, 1, 1),
    rgapcolor: ps.color("#17E1DA"),
    lrgapcolor: ps.color("#6B17E1"),
    rgapqlnsty: ps.enum(STYLES, "solid"),
    rgappo3lnsty: ps.enum(STYLES, "dotted"),
    extendgaps: ps.bool(false),
    // Premarket Range
    dopremarket: ps.bool(true),
    pmkeep: ps.number(1, 1, 1),
    pmmode: ps.enum({ Auto: "Auto", Manual: "Manual" }, "Auto"),
    pmsession: ps.text("0700-0900"),
    dopmq: ps.bool(true),
    pmqmin: ps.number(20.0, 0.25, 0),
    dopmpo3: ps.bool(true),
    pmpo3min: ps.number(20.0, 0.25, 0),
    pmpo3num: ps.number(4, 1, 1),
    pmauto: ps.bool(false),
    pmcolor: ps.color(C_RED),
    pmwid: ps.number(2, 1, 1),
    pmqsty: ps.enum(STYLES, "solid"),
    pmcesty: ps.enum(STYLES, "solid"),
    pmpo3sty: ps.enum(STYLES, "dotted"),
    pmlblsize: ps.enum(SIZES, "normal"),
    pmctol: ps.number(0.05, 0.01, 0),
    // Equal Highs / Lows
    doeqhl: ps.bool(true),
    doeqd: ps.bool(true),
    eqlen: ps.number(3, 1, 1),
    eqlenD: ps.number(3, 1, 1),
    eqtol: ps.number(0.10, 0.01, 0),
    eqkeep: ps.number(5, 1, 1),
    doeq3: ps.bool(true),
    eq3pts: ps.number(3.0, 0.25, 0),
    eqhcol: ps.color(C_GREEN),
    eqlcol: ps.color(C_RED),
    eqhlsty: ps.enum(STYLES, "solid"),
    eqhlwid: ps.number(2, 1, 1),
    eqlblsize: ps.enum(SIZES, "normal"),
    // Macros and Events
    dozones: ps.bool(true),
    zonecolor: ps.color(C_BLUE),
    domacros: ps.bool(true),
    macrocolor: ps.color(C_ORANGE),
    domacrobg: ps.bool(false),
    macrobgcolor: ps.color("#000000"),
    macrobgtransp: ps.number(90, 1, 0),
    dosilver: ps.bool(true),
    silvercolor: ps.color(C_RED)
};

const DEFAULTS = {};
for (const k of Object.keys(PARAMS)) {
    DEFAULTS[k] = PARAMS[k].def;
}

// ============================================================ helpers (pure)
const DAY = 86400;
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
const WDAYS = { Sun: 0, Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6 };
const SUN = 0, MON = 1, FRI = 5;

const FMT = {};
function fmtFor(tz) {
    if (!FMT[tz]) {
        FMT[tz] = new Intl.DateTimeFormat("en-US", {
            timeZone: tz, hourCycle: "h23", year: "numeric", month: "2-digit", day: "2-digit",
            hour: "2-digit", minute: "2-digit", second: "2-digit", weekday: "short"
        });
    }
    return FMT[tz];
}

// local calendar fields of an instant in tz (cached for the last few instants)
const PCACHE = new Map();
function tzParts(ms, tz) {
    const ck = tz + ms;
    const hit = PCACHE.get(ck);
    if (hit) {
        return hit;
    }
    const r = { y: 0, mo: 0, d: 0, h: 0, mi: 0, s: 0, dow: 0 };
    for (const p of fmtFor(tz).formatToParts(new Date(ms))) {
        switch (p.type) {
            case "year": r.y = +p.value; break;
            case "month": r.mo = +p.value; break;
            case "day": r.d = +p.value; break;
            case "hour": r.h = +p.value % 24; break;
            case "minute": r.mi = +p.value; break;
            case "second": r.s = +p.value; break;
            case "weekday": r.dow = WDAYS[p.value]; break;
        }
    }
    if (PCACHE.size > 4000) {
        PCACHE.clear();
    }
    PCACHE.set(ck, r);
    return r;
}

function dmy(ms, tz) {
    const p = tzParts(ms, tz);
    return p.d + " " + MONTHS[p.mo - 1];
}

function parseSecs(s) {
    return 60 * (60 * parseInt(s.substring(0, 2), 10) + parseInt(s.substring(2, 4), 10));
}

function mkZoneInfo(name, ival, sty) {
    if (!name || !ival || ival.length < 9) {
        return null;
    }
    return { name, ss: parseSecs(ival.substring(0, 4)), es: parseSecs(ival.substring(5, 9)), sty: sty || null };
}

function inside(z, t) {
    return z.ss < z.es ? (t >= z.ss && t < z.es) : (t >= z.ss || t < z.es);
}

// the bar contains the start instant
function starts(z, tsec, tfsecs) {
    return (z.ss - tsec + DAY) % DAY < tfsecs;
}

// the bar contains the end instant, or the session was left (data gaps)
function ends(z, tsec, tsecPrev, tfsecs) {
    return (z.es - tsec + DAY) % DAY < tfsecs || (inside(z, tsecPrev) && !inside(z, tsec));
}

const nz = (v, r) => (v === null || v === undefined || Number.isNaN(v) ? r : v);
const isna = v => v === null || v === undefined || Number.isNaN(v);

// stddevs to draw on one side of a range: at least n; with auto, also every stddev reached by the extreme
// ext (base: the range edge on that side, u: the step, sgn 1 above, -1 below) plus the next one
function sdCount(n, auto, base, u, ext, sgn) {
    let r = n;
    if (auto && !isna(ext) && u > 0) {
        const reached = Math.max(0, Math.floor((ext - base) * sgn / u));
        r = Math.max(n, Math.min(reached + 1, 40));
    }
    return r;
}

// stddev step: range / 8, 4, 2 or 1, the first larger than the minimum, else 0
// stddev name: distance from the range edge in whole ranges (step k of u, range r), for example "+0.25", "-1.5"
function sdName(k, u, r, sgn) {
    return (sgn > 0 ? "+" : "-") + String(Math.round(k * u / r * 1000) / 1000);
}

function sdStep(r, mn) {
    if (r / 8 > mn) return r / 8;
    if (r / 4 > mn) return r / 4;
    if (r / 2 > mn) return r / 2;
    if (r > mn) return r;
    return 0;
}

// Pine str.tostring(x, "#.#")
function fmt1(x) {
    const v = Math.round(x * 10) / 10;
    return String(v);
}

// deep copy of the plain-data state, keeping shared references shared
function cloneState(v, memo) {
    if (v === null || typeof v !== "object") {
        return v;
    }
    const m = memo.get(v);
    if (m) {
        return m;
    }
    if (Array.isArray(v)) {
        const a = new Array(v.length);
        memo.set(v, a);
        for (let k = 0; k < v.length; k++) {
            a[k] = cloneState(v[k], memo);
        }
        return a;
    }
    const o = {};
    memo.set(v, o);
    for (const k of Object.keys(v)) {
        o[k] = cloneState(v[k], memo);
    }
    return o;
}

// Wilder ATR(14), as ta.atr (seeded with the SMA of the first 14 true ranges)
function atrNew() {
    return { cnt: 0, sum: 0, val: null, pc: null };
}
function atrPush(a, h, l, c) {
    const tr = a.pc === null ? h - l : Math.max(h - l, Math.abs(h - a.pc), Math.abs(l - a.pc));
    if (a.val === null) {
        a.sum += tr;
        a.cnt += 1;
        if (a.cnt >= 14) {
            a.val = a.sum / 14;
        }
    } else {
        a.val = (a.val * 13 + tr) / 14;
    }
    a.pc = c;
}
function atrPeek(a, h, l) {
    // ATR including a bar that is not committed into a (for the current chart bar)
    const tr = a.pc === null ? h - l : Math.max(h - l, Math.abs(h - a.pc), Math.abs(l - a.pc));
    if (a.val === null) {
        return a.cnt + 1 >= 14 ? (a.sum + tr) / 14 : null;
    }
    return (a.val * 13 + tr) / 14;
}

// ta.pivothigh / ta.pivotlow on an array of values (last element = current bar):
// returns the value of the bar len bars back when it is a pivot, else null
function pivotAt(vals, len, isHigh) {
    const n = vals.length;
    if (n < 2 * len + 1) {
        return null;
    }
    const c = n - 1 - len;
    const v = vals[c];
    for (let k = 1; k <= len; k++) {
        const lv = vals[c - k];
        const rv = vals[c + k];
        if (isHigh ? (lv > v || rv >= v) : (lv < v || rv <= v)) {
            return null;
        }
    }
    return v;
}

// ============================================================ calculator
class ICTlevels {
    init() {
        const P = this.P = Object.assign({}, DEFAULTS, this.props || {});
        const ci = this.contractInfo || {};
        const nm = String((ci.contract || "") + " " + (ci.product || "")).toUpperCase();
        const autoEU = nm.indexOf("DAX") >= 0 || nm.indexOf("FDX") >= 0 || nm.indexOf("GERMANY") >= 0;
        const useUS = P.market === "US" || (P.market !== "EU" && !autoEU);
        const C = this.C = {};
        C.useUS = useUS;
        C.tz = useUS ? "America/New_York" : "Europe/Madrid";
        C.tick = ci.tickSize || 0.25;
        const mktstr = useUS ? "0930-1615" : "0900-1730";
        const gapstr = useUS ? "1700-1800" : "2300-0000";
        const asiastr = useUS ? "2000-0000" : "2300-0600";
        const londonstr = useUS ? "0200-0500" : "0800-1300";
        const nyamstr = useUS ? "0700-1130" : "1330-1800";
        const nypmstr = useUS ? "1330-1600" : "1900-2200";
        const premarketstr = P.pmmode === "Manual" ? String(P.pmsession || "0700-0900").replace(/[^0-9-]/g, "")
            : (useUS ? "0700-0900" : "0630-0830");
        const mNames = useUS ? ["NYpre", "NYAM.1", "NYAM.2", "NYAM.3", "NY.lunch", "NYPM", "NYLast", "MOC",
            "LDN.1", "LDN.2"]
            : ["Dax7", "Dax8", "DaxOpen", "Dax10", "Dax11", "DaxTapas", "Dax17", "NYopen", "Dax19", "DaxLunch"];
        const mTimes = useUS ? ["0740-0810", "0850-0910", "0950-1010", "1050-1110", "1150-1210", "1310-1340",
            "1515-1530", "1545-1600", "0230-0300", "0403-0430"]
            : ["0700-0710", "0750-0810", "0850-0910", "0955-1010", "1050-1110", "1245-1310", "1650-1710",
                "1515-1540", "1850-1910", "1350-1410"];
        const sbTimes = useUS ? ["1000-1100", "1400-1500"] : ["1000-1100"];

        // styles: { sty, wid, col, op } (op: fill opacity for rectangles)
        const rop = (100 - P.rtransp) / 100;
        C.daysty = { sty: P.daylnsty, wid: P.daywid, col: P.daycol };
        C.daymaxsty = { sty: P.daymlnsty, wid: P.daymwid, col: P.daymmaxcol };
        C.dayminsty = { sty: P.daymlnsty, wid: P.daymwid, col: P.daymmincol };
        C.sesmaxsty = { sty: P.sesmlnsty, wid: P.sesmwid, col: P.sesmmaxcol };
        C.sesminsty = { sty: P.sesmlnsty, wid: P.sesmwid, col: P.sesmmincol };
        C.gapsty = { sty: P.gaplnsty, wid: P.gapwid, col: P.gapcolor };
        C.wgapsty = { sty: P.wgaplnsty, wid: P.wgapwid, col: P.wgapcolor };
        const nysty = { sty: P.daylnsty, wid: 1, col: P.uscolor, op: rop };
        const londonsty = { sty: P.daylnsty, wid: 1, col: P.londoncolor, op: rop };
        const asiasty = { sty: P.daylnsty, wid: 1, col: P.asiacolor, op: rop };

        C.market = mkZoneInfo("market", mktstr);
        C.asia = mkZoneInfo("Asia", asiastr, asiasty);
        C.london = mkZoneInfo("London", londonstr, londonsty);
        C.nyam = mkZoneInfo("NY AM", nyamstr, nysty);
        C.nypm = mkZoneInfo("NY PM", nypmstr, nysty);
        C.premarket = mkZoneInfo("Pre-mkt", premarketstr, nysty);
        C.zinfos = [C.asia, C.london, C.nyam, C.nypm];
        C.zi = {};
        for (const z of C.zinfos.concat([C.premarket])) {
            if (z) {
                C.zi[z.name] = z;
            }
        }
        C.macros = [];
        for (let k = 0; k < mNames.length; k++) {
            const m = mkZoneInfo(mNames[k], mTimes[k]);
            if (m) {
                C.macros.push(m);
            }
        }
        C.sbs = sbTimes.map(s => mkZoneInfo("S.Bullet", s));
        C.gapStartSec = parseSecs(gapstr.substring(0, 4));
        C.gapEndSec = parseSecs(gapstr.substring(5, 9));
        C.nsesEff = Math.min(P.nses, 40);
        C.ngapsEff = Math.min(P.ngaps, 15);
        C.ndays = Math.max(1, P.ndays);

        // chart timeframe in seconds, when the chart says so; otherwise inferred from the bars
        C.tfFixed = null;
        const cd = this.chartDescription;
        if (cd && cd.elementSizeUnit === "UnderlyingUnits" && cd.elementSize > 0) {
            if (cd.underlyingType === "MinuteBar") {
                C.tfFixed = cd.elementSize * 60;
            } else if (cd.underlyingType === "DailyBar") {
                C.tfFixed = cd.elementSize * DAY;
            }
        }

        this.S = this.newState();
        this.snap = null;
        this.snapIdx = -1;
        this.lastIdx = -1;
        this.lastOut = null;
        this.lastLocal = null;
    }

    newState() {
        return {
            prev: null,       // previous bar: { t, o, h, l, c, tsec, dow, mktEnds, dkey, i }
            tfInf: null,
            days: [], zones: [], premarkets: [], actz: [], monz: [],
            dgaps: [], wgaps: [], ndog: null, rthogs: [],
            hpv: [], lpv: [], hpvD: [], lpvD: [],
            sh3p: [], sh3t: [], sl3p: [], sl3t: [],
            hist: [],         // last bars: { h, l }
            atr: atrNew(),    // chart ATR(14), committed bars
            // TGIF
            twO: null, twH: null, twL: null, tgDir: 0, tgMon: false, tgHit: false, tgX: null, tgR: null,
            tgT0: null, tgV1: null, tgV2: null,
            // day bars (trading days) and month bars, built from the chart
            dcur: null,       // { key, o, h, l, c, i0 }
            dbars: [],        // completed day bars { h, l, i0 }
            datrS: atrNew(),
            datr: null,
            mcur: null,       // { key, h, l, c, i0, full }
            mbars: []         // completed months { key, h, l, c, i0, full }
        };
    }

    isLast(d, i, history) {
        if (d && typeof d.isLast === "function") {
            return !!d.isLast();
        }
        if (history && typeof history.size === "function") {
            return i >= history.size() - 1;
        }
        return true;
    }

    map(d, i, history) {
        const last = this.isLast(d, i, history);
        if (i < this.lastIdx) {
            return undefined;
        }
        if (i === this.lastIdx) {
            if (this.snapIdx !== i) {
                return this.lastOut || undefined;
            }
            // the forming bar again: back to the state before it
            this.S = cloneState(this.snap, new Map());
        } else {
            // a new bar: the previous result keeps only its own per-bar items
            if (this.lastOut && this.lastOut.graphics) {
                this.lastOut.graphics.items = this.lastLocal || [];
            }
            if (last) {
                this.snap = cloneState(this.S, new Map());
                this.snapIdx = i;
            } else {
                this.snap = null;
                this.snapIdx = -1;
            }
            this.lastIdx = i;
        }
        const local = this.step(d, i, last);
        let items = local;
        if (last && this.ctx.tfok) {
            items = local.concat(this.render(i));
        }
        const out = items.length > 0 ? { graphics: { items } } : {};
        this.lastOut = out;
        this.lastLocal = local;
        return out;
    }

    // ------------------------------------------------------------ one bar (state only)
    step(d, i, last) {
        const S = this.S, C = this.C, P = this.P;
        const t = d.timestamp().getTime();
        const open = d.open(), high = d.high(), low = d.low(), close = d.close();
        const pv = S.prev;
        if (pv && t > pv.t) {
            const dt = (t - pv.t) / 1000;
            S.tfInf = S.tfInf === null ? dt : Math.min(S.tfInf, dt);
        }
        const tfsecs = C.tfFixed || S.tfInf || 60;
        const tfok = tfsecs <= 3600;
        const rectOn = P.dorect && !(P.nolowrects && tfsecs < 900 && tfsecs <= P.rectmintf * 60);
        const preOn = P.dopremarket && tfsecs <= 300;
        const rdx = tfsecs <= 60 ? 4 : 2;
        const p = tzParts(t, C.tz);
        const tsec = p.h * 3600 + p.mi * 60 + p.s;
        const tsecPrev = pv ? pv.tsec : tsec;
        const dow = p.dow;
        const dowPrev = pv ? pv.dow : dow;
        const newDay = dow !== dowPrev;
        const newWeek = (dow === SUN && dowPrev !== SUN) || (dow === MON && dowPrev === FRI);
        const ctx = this.ctx = { i, t, open, high, low, close, tfsecs, tfms: tfsecs * 1000, tfok, rectOn,
            preOn, rdx, tsec, dow, p };

        // ---------------- day and month bars built from the chart (request.security replacement)
        let dkey;
        if (C.useUS && p.h >= 18) {
            const nd = new Date(Date.UTC(p.y, p.mo - 1, p.d + 1));
            dkey = nd.getUTCFullYear() * 10000 + (nd.getUTCMonth() + 1) * 100 + nd.getUTCDate();
        } else {
            dkey = p.y * 10000 + p.mo * 100 + p.d;
        }
        let newDBar = false;
        if (!S.dcur || S.dcur.key !== dkey) {
            if (S.dcur) {
                newDBar = true;
                const db = S.dcur;
                S.dbars.push({ h: db.h, l: db.l, i0: db.i0 });
                if (S.dbars.length > 200) {
                    S.dbars.shift();
                }
                atrPush(S.datrS, db.h, db.l, db.c);
                S.datr = S.datrS.val;
            }
            S.dcur = { key: dkey, h: high, l: low, c: close, i0: i };
        } else {
            S.dcur.h = Math.max(S.dcur.h, high);
            S.dcur.l = Math.min(S.dcur.l, low);
            S.dcur.c = close;
        }
        const mkey = Math.floor(dkey / 100);
        if (!S.mcur || S.mcur.key !== mkey) {
            if (S.mcur) {
                S.mbars.push(S.mcur);
                if (S.mbars.length > 4) {
                    S.mbars.shift();
                }
            }
            S.mcur = { key: mkey, h: high, l: low, c: close, i0: i, full: !!S.mcur };
        } else {
            S.mcur.h = Math.max(S.mcur.h, high);
            S.mcur.l = Math.min(S.mcur.l, low);
            S.mcur.c = close;
        }
        const datr = nz(S.datr, 0);

        // premarket lines stop at the market close: index of that bar once reached
        for (const z of S.premarkets) {
            if (z.tcloseIdx === null && t >= z.tclose) {
                z.tcloseIdx = i;
            }
        }

        // ---------------- zones
        if (tfok) {
            if (newDay) {
                const dz = this.mkZone("D", null, "day");
                dz.isday = true;
                dz.isweek = newWeek;
                dz.drawmaxmin = P.dodmaxmin;
                dz.drawlbl = P.dodaylbl;
                if (S.days.length > 0) {
                    const ld = S.days[S.days.length - 1];
                    if (!dz.isweek) {
                        dz.pwhi = isna(ld.pwhi) ? ld.hi : Math.max(ld.pwhi, ld.hi);
                        dz.pwlo = isna(ld.pwlo) ? ld.lo : Math.min(ld.pwlo, ld.lo);
                    }
                    this.endZone(ld);
                    if (S.days.length > 1) {
                        S.days[S.days.length - 2].extendt = ld.tend;
                    }
                }
                S.days.push(dz);
                S.actz.push(dz);
                if (S.days.length > C.ndays) {
                    S.days.shift().dead = true;
                }
            }
            if (P.doses) {
                for (const zi of C.zinfos) {
                    if (starts(zi, tsec, tfsecs)) {
                        const s = this.mkZone(zi.name, zi.name, "ses");
                        s.drawmaxmin = P.dosmaxmin;
                        s.drawrect = rectOn;
                        s.drawlbl = P.doseslbl;
                        S.zones.push(s);
                        S.actz.push(s);
                        if (S.zones.length > C.nsesEff) {
                            S.zones.shift().dead = true;
                        }
                    }
                }
            }
            if (preOn && C.premarket && starts(C.premarket, tsec, tfsecs)) {
                const pz = this.mkZone(C.premarket.name, C.premarket.name, "pm");
                pz.drawmaxmin = P.dosmaxmin;
                pz.drawvlines = true;
                pz.tclose = t + ((C.market.es - tsec + DAY) % DAY) * 1000;
                pz.tcloseIdx = pz.tclose <= t ? i : null;
                pz.drawlbl = P.doseslbl;
                S.premarkets.push(pz);
                S.actz.push(pz);
                if (S.premarkets.length > C.nsesEff) {
                    S.premarkets.shift().dead = true;
                }
            }
            // zones still growing
            for (let k = S.actz.length - 1; k >= 0; k--) {
                const z = S.actz[k];
                if (z.dead || z.tend !== null) {
                    S.actz.splice(k, 1);
                } else if (z.zi && ends(C.zi[z.zi], tsec, tsecPrev, tfsecs)) {
                    this.endZone(z);
                    S.actz.splice(k, 1);
                } else {
                    if (high >= z.hi) {
                        z.hit = i;
                        z.hi = high;
                    }
                    if (low <= z.lo) {
                        z.lot = i;
                        z.lo = low;
                    }
                }
            }
            // ended zones: only watch for the max/min being taken
            for (let k = S.monz.length - 1; k >= 0; k--) {
                const z = S.monz[k];
                if (z.dead) {
                    S.monz.splice(k, 1);
                    continue;
                }
                if (z.hidonet === null && high > z.hi) {
                    z.hidonet = i;
                }
                if (z.lodonet === null && low < z.lo) {
                    z.lodonet = i;
                }
                if (z.hidonet !== null && z.lodonet !== null) {
                    S.monz.splice(k, 1);
                }
            }
        }

        // ---------------- TGIF
        if (tfok && P.dotgif) {
            const prevClose = pv ? pv.c : close;
            if (newWeek) {
                const dd = S.twO === null ? 0 : this.tgEval(S.twH, S.twL, S.twO, prevClose);
                const carry = dd !== 0 && !(S.tgDir === dd && S.tgHit);
                S.tgDir = 0;
                S.tgMon = false;
                S.tgHit = false;
                if (carry) {
                    S.tgDir = dd;
                    S.tgMon = true;
                    S.tgX = dd === 1 ? S.twH : S.twL;
                    S.tgR = S.twH - S.twL;
                    S.tgT0 = i;
                }
                S.twO = open;
                S.twH = high;
                S.twL = low;
            } else if (S.twO !== null) {
                if (newDay && dow === FRI) {
                    S.tgDir = this.tgEval(S.twH, S.twL, S.twO, prevClose);
                    S.tgMon = false;
                    S.tgHit = false;
                    S.tgT0 = i;
                } else if (newDay && S.tgDir !== 0 && !(S.tgMon && dow === MON)) {
                    S.tgDir = 0;
                    S.tgMon = false;
                }
                S.twH = Math.max(S.twH, high);
                S.twL = Math.min(S.twL, low);
            }
            if (S.tgDir !== 0) {
                if (!S.tgMon && !S.tgHit) {
                    S.tgX = S.tgDir === 1 ? S.twH : S.twL;
                    S.tgR = S.twH - S.twL;
                }
                S.tgV1 = S.tgX - S.tgDir * P.tgp1 * S.tgR;
                S.tgV2 = S.tgX - S.tgDir * P.tgp2 * S.tgR;
                if (!S.tgHit && (S.tgDir === 1 ? low <= S.tgV1 : high >= S.tgV1)) {
                    S.tgHit = true;
                }
            }
        }

        // ---------------- NDOG / NWOG
        if (P.dogaps && tfok && pv) {
            const gapOpenBar = tsec === C.gapStartSec;
            const gapCloseBar = (tsec + tfsecs) % DAY === C.gapStartSec;
            const gapEndBar = tsec === C.gapEndSec;
            const isWeekGap = dow === FRI || dow === SUN;
            if (gapOpenBar || gapCloseBar) {
                if ((isWeekGap && P.donwog) || (!isWeekGap && P.dondog)) {
                    S.ndog = gapOpenBar
                        ? this.mkGap((isWeekGap ? "NWOG " : "NDOG ") + dmy(t, C.tz), i - 1, pv.c, isWeekGap)
                        : this.mkGap((isWeekGap ? "NWOG " : "NDOG ") + dmy(t, C.tz), i, close, isWeekGap);
                }
            } else if (dow === SUN && dowPrev === FRI && P.donwog) {
                S.ndog = this.mkGap("NWOG " + dmy(t, C.tz), i - 1, pv.c, true);
            }
            if (S.ndog && gapEndBar) {
                const g = S.ndog;
                const s0 = g.hi;
                g.hi = Math.max(s0, open);
                g.lo = Math.min(s0, open);
                g.name = (g.isw ? "NWOG " : "NDOG ") + dmy(t, C.tz);
                const lst = g.isw ? S.wgaps : S.dgaps;
                lst.push(g);
                if (lst.length > C.ngapsEff) {
                    lst.shift();
                }
                S.ndog = null;
            }
        }

        // ---------------- RTHOG
        const mktEnds = ends(C.market, tsec, tsecPrev, tfsecs);
        const mktClose = mktEnds && !(pv ? pv.mktEnds : false);
        const dOpen = (C.market.ss - tsec + DAY) % DAY;
        const rthStarting = dOpen < tfsecs + 900;
        const rthOpenBar = dOpen < tfsecs;
        const rthOk = P.dogaps && P.dorthog && tfsecs <= 900 && pv && dow !== SUN;
        if (rthOk) {
            const R = S.rthogs;
            if (mktClose) {
                const rt = { tstart: i - 1, cl: pv.c, tend: null, op: null, xhi: null, xlo: null, isold: false,
                    nm: "", clText: "new RTHOG settlement", noonT: null, noonIdx: null };
                if (R.length > 1) {
                    R[R.length - 2].isold = true;
                }
                R.push(rt);
                if (R.length > C.ngapsEff) {
                    R.shift();
                }
            } else if (rthStarting && R.length > 0) {
                const cg = R[R.length - 1];
                if (!isna(cg.cl) && (last || rthOpenBar)) {
                    // opening gap: hint (current close) on the forming last bar, final on the open bar
                    cg.op = rthOpenBar ? open : close;
                    cg.tend = i;
                    cg.xhi = high;
                    cg.xlo = low;
                    cg.nm = dmy(t, C.tz);
                    cg.noonT = t + (12 * 3600 - tsec) * 1000; // noon of the RTHOG day (octants)
                    cg.noonIdx = null;
                    cg.clText = (cg.op > cg.cl ? "RTHOG lo " : "RTHOG hi ") + cg.nm;
                }
            }
            // octants stop at noon: first bar at or after it
            for (const og of R) {
                if (og.noonT !== null && og.noonIdx === null && i > og.tend && t >= og.noonT) {
                    og.noonIdx = i;
                }
            }
            if (P.rthogauto && !mktClose && R.length > 0) {
                const ag = R[R.length - 1];
                if (ag.tend !== null && i > ag.tend) {
                    ag.xhi = Math.max(ag.xhi, high);
                    ag.xlo = Math.min(ag.xlo, low);
                }
            }
        }
        if (preOn && P.pmauto && S.premarkets.length > 0) {
            const az = S.premarkets[S.premarkets.length - 1];
            if (az.tend !== null && i > az.tend && (az.tclose === null || t < az.tclose)) {
                az.xhi = isna(az.xhi) ? high : Math.max(az.xhi, high);
                az.xlo = isna(az.xlo) ? low : Math.min(az.xlo, low);
            }
        }

        // ---------------- EQH / EQL
        S.hist.push({ h: high, l: low });
        const keepH = Math.max(2 * P.eqlen + 1, 3);
        if (S.hist.length > keepH) {
            S.hist.splice(0, S.hist.length - keepH);
        }
        const atrv = atrPeek(S.atr, high, low);
        atrPush(S.atr, high, low, close);
        if (tfok && P.doeqhl) {
            const hs = S.hist.map(b => b.h), ls = S.hist.map(b => b.l);
            const ph = pivotAt(hs, P.eqlen, true);
            const pl = pivotAt(ls, P.eqlen, false);
            const ph3 = pivotAt(hs, 1, true);
            const pl3 = pivotAt(ls, 1, false);
            const pivT = i - P.eqlen;
            const tol = P.eqtol * nz(atrv, 0);
            this.sweepPivs(S.hpv, true, tol, high, low);
            this.sweepPivs(S.lpv, false, tol, high, low);
            this.sweep3(S.sh3p, S.sh3t, true, high, low);
            this.sweep3(S.sl3p, S.sl3t, false, high, low);
            if (ph !== null) {
                this.addPiv(S.hpv, ph, pivT, true, tol);
                if (P.doeq3) {
                    this.eq3(S.hpv, S.sh3p, S.sh3t, ph, pivT, true);
                }
            }
            if (pl !== null) {
                this.addPiv(S.lpv, pl, pivT, false, tol);
                if (P.doeq3) {
                    this.eq3(S.lpv, S.sl3p, S.sl3t, pl, pivT, false);
                }
            }
            if (ph3 !== null) {
                this.add3(S.sh3p, S.sh3t, ph3, i - 1);
            }
            if (pl3 !== null) {
                this.add3(S.sl3p, S.sl3t, pl3, i - 1);
            }
        }
        // daily swings: completed day bars only
        if (tfok && P.doeqhl && P.doeqd) {
            const tolD = P.eqtol * datr;
            this.sweepPivs(S.hpvD, true, tolD, high, low);
            this.sweepPivs(S.lpvD, false, tolD, high, low);
            if (newDBar) {
                const L = P.eqlenD;
                const db = S.dbars;
                if (db.length >= 2 * L + 1) {
                    const win = db.slice(db.length - (2 * L + 1));
                    const dph = pivotAt(win.map(b => b.h), L, true);
                    const dpl = pivotAt(win.map(b => b.l), L, false);
                    const dpt = win[L].i0;
                    if (dph !== null) {
                        this.addPiv(S.hpvD, dph, dpt, true, tolD);
                    }
                    if (dpl !== null) {
                        this.addPiv(S.lpvD, dpl, dpt, false, tolD);
                    }
                }
            }
        }

        S.prev = { t, o: open, h: high, l: low, c: close, tsec, dow, mktEnds, i };

        // ---------------- per-bar output: macro background
        const local = [];
        if (P.domacrobg && tfok) {
            let inMacro = false;
            for (const m of C.macros) {
                if (inside(m, tsec)) {
                    inMacro = true;
                    break;
                }
            }
            if (inMacro) {
                local.push({
                    tag: "Shapes",
                    key: "macrobg",
                    primitives: [{
                        tag: "Rectangle",
                        position: { x: du(i - 0.5), y: px(0) },
                        size: { width: du(1), height: px(10000) }
                    }],
                    fillStyle: { color: P.macrobgcolor, opacity: (100 - P.macrobgtransp) / 100 }
                });
            }
        }
        return local;
    }

    // ------------------------------------------------------------ state helpers
    mkZone(name, zi, kind) {
        const c = this.ctx;
        return {
            name, kind, zi, tstart: c.i, t0: c.t, op: c.open, hi: c.high, lo: c.low, hit: c.i, lot: c.i,
            tend: null, hidonet: null, lodonet: null, extendt: null, tclose: null, tcloseIdx: null,
            pwhi: null, pwlo: null, hmode: 0, lmode: 0, isday: false, isweek: false, dead: false,
            drawmaxmin: true, drawrect: false, drawvlines: false, drawlbl: false, xhi: null, xlo: null
        };
    }

    endZone(z) {
        if (z.tend === null) {
            z.tend = this.ctx.i - 1;
            if (z.drawmaxmin) {
                this.S.monz.push(z);
            }
        }
    }

    mkGap(name, tstart, price, isw) {
        return { name, tstart, hi: price, lo: null, isw, n: 1, nd: 0, show: true, uhi: null, ulo: null, ut: null };
    }

    tgEval(h, l, o, c) {
        const P = this.P;
        const r = h - l;
        let d = 0;
        if (r > 0) {
            if (h - c <= P.tgnear * r) {
                d = 1;
            } else if (c - l <= P.tgnear * r) {
                d = -1;
            }
        }
        return d;
    }

    dropFirst(arr, wantEq) {
        for (let k = 0; k < arr.length; k++) {
            if (arr[k].eq === wantEq) {
                arr.splice(k, 1);
                break;
            }
        }
    }

    trimPivs(arr) {
        let ne = 0;
        for (const v of arr) {
            if (v.eq) {
                ne += 1;
            }
        }
        while (ne > this.P.eqkeep) {
            this.dropFirst(arr, true);
            ne -= 1;
        }
        while (arr.length - ne > 30) {
            this.dropFirst(arr, false);
        }
    }

    sweepPivs(arr, isHigh, tol, high, low) {
        for (let k = arr.length - 1; k >= 0; k--) {
            const v = arr[k];
            const slack = v.eq ? 0 : tol;
            if (isHigh ? high > v.p + slack : low < v.p - slack) {
                arr.splice(k, 1);
            }
        }
    }

    addPiv(arr, p, t, isHigh, tol) {
        let m = null;
        for (const v of arr) {
            if (Math.abs(v.p - p) <= tol) {
                m = v;
                break;
            }
        }
        if (m) {
            m.eq = true;
            if (isHigh ? p > m.p : p < m.p) {
                m.p = p;
            }
        } else {
            arr.push({ p, t, eq: false });
        }
        this.trimPivs(arr);
    }

    sweep3(ps3, ts3, isHigh, high, low) {
        for (let k = ps3.length - 1; k >= 0; k--) {
            if (isHigh ? high > ps3[k] : low < ps3[k]) {
                ps3.splice(k, 1);
                ts3.splice(k, 1);
            }
        }
    }

    add3(ps3, ts3, p, t) {
        ps3.push(p);
        ts3.push(t);
        if (ps3.length > 60) {
            ps3.shift();
            ts3.shift();
        }
    }

    // the pivot just added at t (price p) becomes an EQH / EQL when an earlier unswept 3-candle swing has the
    // same price or is at most eq3pts beyond it. The level is the swing price, drawn from the swing.
    eq3(arr, ps3, ts3, p, t, isHigh) {
        if (arr.length > 0) {
            const v = arr[arr.length - 1];
            if (v.t === t && !v.eq) {
                for (let k = ps3.length - 1; k >= 0; k--) {
                    const q = ps3[k];
                    const qt = ts3[k];
                    const dd = isHigh ? q - p : p - q;
                    if (qt < t && dd >= 0 && dd <= this.P.eq3pts) {
                        v.eq = true;
                        v.p = q;
                        v.t = qt;
                        this.trimPivs(arr);
                        break;
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------ drawing (last bar, from the state)
    render(i) {
        const S = this.S, C = this.C, P = this.P, c = this.ctx;
        const G = new Gfx();
        const xr = i + P.xoff;      // tright(xoff)
        const tclX = z => (z.tcloseIdx !== null ? z.tcloseIdx : i + (z.tclose - c.t) / c.tfms);
        const datr = nz(S.datr, 0);
        const fs = s => FONT[s] || 12;

        // ---- REHs / RELs
        if (P.doreh && P.dodaylbl && P.dodmaxmin) {
            markRel(S.days, P.reltol * datr);
        }

        // ---- zones
        const drawZone = z => {
            let re = i + 2;
            if (z.tclose !== null) {
                re = Math.min(re, tclX(z));
            }
            const xe = z.tend === null ? re : z.tend;
            const lnsty = z.kind === "ses" ? C.zi[z.zi].sty : C.daysty;
            const maxsty = z.isday ? C.daymaxsty : C.sesmaxsty;
            const minsty = z.isday ? C.dayminsty : C.sesminsty;
            if (z.drawvlines || (z.isday && P.dolines)) {
                G.vline(z.tstart, lnsty.col, z.isweek ? P.wdaylnsty : lnsty.sty, z.isweek ? P.wdaywid : lnsty.wid);
                if (z.drawvlines && z.tend !== null) {
                    G.vline(z.tend + 1, lnsty.col, lnsty.sty, lnsty.wid);
                }
            }
            if (z.drawmaxmin) {
                const hx = z.hidonet === null ? re : Math.min(z.hidonet, re);
                const lx = z.lodonet === null ? re : Math.min(z.lodonet, re);
                G.line(z.hit, hx, z.hi, maxsty.col, maxsty.sty, maxsty.wid);
                G.line(z.lot, lx, z.lo, minsty.col, minsty.sty, minsty.wid);
                if (z.drawlbl) {
                    const lsz = fs(z.isday ? P.dlblsize : P.slblsize);
                    let hn = z.name + " hi";
                    let ln = z.name + " lo";
                    if (z.isday) {
                        hn = isna(z.pwhi) || z.hi >= z.pwhi ? "week high" : "day high";
                        ln = isna(z.pwlo) || z.lo <= z.pwlo ? "week low" : "day low";
                        if (z.hmode === 1) {
                            hn = "day REHs";
                        }
                        if (z.lmode === 1) {
                            ln = "day RELs";
                        }
                    }
                    if (z.hidonet === null && z.hmode !== 2) {
                        G.text(re, z.hi, hn, maxsty.col, lsz);
                    }
                    if (z.lodonet === null && z.lmode !== 2) {
                        G.text(re, z.lo, ln, minsty.col, lsz);
                    }
                }
            }
            if (z.isday && P.doeq) {
                const eq = (z.hi + z.lo) / 2;
                const ex = z.extendt === null ? re : z.extendt;
                G.line(Math.min(z.hit, z.lot), ex, eq, P.eqcolor, P.eqlnsty, P.eqwid);
                G.text(ex, eq, "eq", P.eqcolor, 12);
            }
            if (z.isday && P.doopen) {
                G.line(z.tstart, xe, z.op, P.opcolor, P.oplnsty, P.opwid);
                G.text(xe, z.op, "M.O.", P.opcolor, 12);
            }
            if (z.drawrect) {
                G.rect(z.tstart, xe, z.hi, z.lo, lnsty.col, lnsty.op);
            }
        };
        for (const z of S.days) drawZone(z);
        for (const z of S.zones) drawZone(z);
        for (const z of S.premarkets) drawZone(z);

        // ---- premarket quadrants / stddevs (sd entries kept for the label merging)
        let pmSd = [];
        if (c.preOn) {
            const npm = S.premarkets.length;
            S.premarkets.forEach((z, k) => {
                if (k >= npm - P.pmkeep) {
                    const sd = this.pmdraw(G, z, i, P.pmauto && k === npm - 1, xr, tclX);
                    if (k === npm - 1) {
                        pmSd = sd;
                    }
                }
            });
        }

        // ---- NDOG / NWOG
        const gtol = P.combtol * datr;
        if (P.combgaps) {
            combineGaps(S.dgaps, S.wgaps, gtol);
            for (const g of S.dgaps.concat(S.wgaps)) {
                if (g.show) {
                    this.drawGap(G, g, g.ut, g.uhi, g.ulo, gtol, xr);
                }
            }
        } else {
            for (const g of S.dgaps.concat(S.wgaps)) {
                this.drawGap(G, g, g.tstart, g.hi, g.lo, gtol, xr);
            }
        }

        // ---- RTHOG
        let rSd = [];
        const R = S.rthogs;
        R.forEach((g, k) => {
            if (g.isold) {
                this.drawOldRthog(G, g, c.rdx);
            } else if (k === R.length - 1) {
                rSd = this.drawRthog(G, g, P.lrgapcolor, true, xr, c.rdx);
            } else if (k === R.length - 2) {
                this.drawRthog(G, g, P.rgapcolor, false, xr, c.rdx);
            }
        });

        // ---- RTHOG / premarket stddev labels: one label for both when they are close
        if (c.preOn && R.length > 0 && S.premarkets.length > 0) {
            const tol = P.pmctol * datr;
            const used = pmSd.map(() => false);
            for (const a of rSd) {
                for (let m = 0; m < pmSd.length; m++) {
                    if (!used[m] && Math.abs(a.y - pmSd[m].y) <= tol) {
                        a.lbl.text = "RTHOG" + a.name + " PMKT" + pmSd[m].name;
                        pmSd[m].lbl.text = "";
                        used[m] = true;
                        break;
                    }
                }
            }
        }

        // ---- TGIF
        if (P.dotgif && S.tgDir !== 0 && !isna(S.tgV1)) {
            const col = S.tgDir === 1 ? P.tgdncol : P.tgupcol;
            const sz = fs(P.tglblsize);
            G.line(S.tgT0, xr, S.tgV1, col, P.tgsty, P.tgwid);
            G.text(xr, S.tgV1, "TGIF-" + fmt1(P.tgp1 * 100) + "%", col, sz);
            G.line(S.tgT0, xr, S.tgV2, col, P.tgsty, P.tgwid);
            G.text(xr, S.tgV2, "TGIF-" + fmt1(P.tgp2 * 100) + "%", col, sz);
        }

        // ---- EQH / EQL
        if (P.doeqhl) {
            const sz = fs(P.eqlblsize);
            const drawEq = (arr, txt, col) => {
                for (const v of arr) {
                    if (v.eq) {
                        G.line(v.t, xr, v.p, col, P.eqhlsty, P.eqhlwid);
                        G.text(xr, v.p, txt, col, sz, true);
                    }
                }
            };
            drawEq(S.hpv, "EQH", P.eqhcol);
            drawEq(S.lpv, "EQL", P.eqlcol);
            if (P.doeqd) {
                drawEq(S.hpvD, "EQH D", P.eqhcol);
                drawEq(S.lpvD, "EQL D", P.eqlcol);
            }
        }

        // ---- months: the three months before the current one, complete months only
        if (P.domonth && S.mcur) {
            const ms = S.mbars.slice(-3).reverse();       // [previous, 2 back, 3 back]
            ms.forEach((m, k) => {
                if (!m.full) {
                    return;
                }
                const tc = k === 0 ? S.mcur.i0 : ms[k - 1].i0;
                const suf = " " + MONTHS[(m.key % 100) - 1];
                G.line(m.i0, m.i0 + 1, m.h, P.monmaxcol, P.monsty, P.monwid, true);
                G.text(xr, m.h, "max" + suf, P.monmaxcol, 12);
                G.line(m.i0, m.i0 + 1, m.l, P.monmincol, P.monsty, P.monwid, true);
                G.text(xr, m.l, "min" + suf, P.monmincol, 12);
                G.line(tc, tc + 1, m.c, P.monccol, P.monsty, P.monwid, true);
                G.text(xr, m.c, "close" + suf, P.monccol, 12);
            });
        }

        // ---- table (top right of the chart)
        let sname = "", mname = "", bname = "";
        if (P.dozones) {
            for (const zi of C.zinfos) {
                if (inside(zi, c.tsec)) {
                    sname = zi.name;
                    break;
                }
            }
        }
        if (P.domacros) {
            for (const m of C.macros) {
                if (inside(m, c.tsec)) {
                    mname = m.name;
                    break;
                }
            }
        }
        if (P.dosilver) {
            for (const m of C.sbs) {
                if (m && inside(m, c.tsec)) {
                    bname = m.name;
                    break;
                }
            }
        }
        G.cell(0, sname, P.zonecolor);
        G.cell(1, mname, P.macrocolor);
        G.cell(2, bname, P.silvercolor);

        return G.items();
    }

    // premarket quadrants (25%, CE, 75%), stddevs and PMKT hi / lo
    pmdraw(G, z, i, auto, xr, tclX) {
        const P = this.P, C = this.C, c = this.ctx;
        const sd = [];
        const r = z.hi - z.lo;
        const x1 = z.tclose === null ? xr : Math.min(xr, tclX(z));
        const x0 = (z.tend === null ? i : z.tend) - c.rdx;
        const nm = dmy(z.t0, C.tz);
        const sz = FONT[P.pmlblsize] || 12;
        let any = false;
        if (r > 0) {
            if (P.dopmq && r / 4 > P.pmqmin) {
                for (let k = 1; k <= 3; k++) {
                    const pt = k === 1 ? "dq" : k === 2 ? "ce" : "uq";
                    const y = z.lo + k * r / 4;
                    G.line(x0, x1, y, P.pmcolor, k === 2 ? P.pmcesty : P.pmqsty, P.pmwid);
                    G.text(x1, y, "PMKT " + pt + " " + nm, P.pmcolor, sz);
                    any = true;
                }
            }
            if (P.dopmpo3) {
                const u = sdStep(r, P.pmpo3min);
                if (u > 0) {
                    const nu = sdCount(P.pmpo3num, auto, z.hi, u, z.xhi, 1);
                    const nd = sdCount(P.pmpo3num, auto, z.lo, u, z.xlo, -1);
                    for (let k = 1; k <= Math.max(nu, nd); k++) {
                        if (k <= nu) {
                            const y = z.hi + k * u;
                            G.line(x0, x1, y, P.pmcolor, P.pmpo3sty, P.pmwid);
                            sd.push({ y, name: sdName(k, u, r, 1), lbl: G.text(x1, y, "PMKT" + sdName(k, u, r, 1), P.pmcolor, sz) });
                            any = true;
                        }
                        if (k <= nd) {
                            const y = z.lo - k * u;
                            G.line(x0, x1, y, P.pmcolor, P.pmpo3sty, P.pmwid);
                            sd.push({ y, name: sdName(k, u, r, -1), lbl: G.text(x1, y, "PMKT" + sdName(k, u, r, -1), P.pmcolor, sz) });
                            any = true;
                        }
                    }
                }
            }
            if (any) {
                G.line(x0, x1, z.hi, P.pmcolor, "solid", P.pmwid);
                G.text(x1, z.hi, "PMKT hi " + nm, P.pmcolor, sz);
                G.line(x0, x1, z.lo, P.pmcolor, "solid", P.pmwid);
                G.text(x1, z.lo, "PMKT lo " + nm, P.pmcolor, sz);
            }
        }
        return sd;
    }

    drawGap(G, g, x0, h, l, tol, xr) {
        const P = this.P;
        const st = g.isw ? this.C.wgapsty : this.C.gapsty;
        G.line(x0, xr, h, st.col, st.sty, st.wid);
        G.line(x0, xr, l, st.col, st.sty, st.wid);
        const small = Math.abs(h - l) <= tol;
        const b = gname(g);
        if (small) {
            G.text(xr, (h + l) / 2, b, st.col, 12);
        } else {
            G.text(xr, h, b + ".hi", st.col, 12);
            G.text(xr, l, b + ".lo", st.col, 12);
        }
        if (P.dogrect) {
            G.rect(x0, xr, h, l, st.col, (100 - P.grtransp) / 100);
        }
    }

    // current (with quadrants, octants and stddevs) or previous RTHOG; returns the stddev entries
    drawRthog(G, g, col, current, xr, rdx) {
        const P = this.P;
        const sd = [];
        G.line(g.tstart, xr, g.cl, col, P.rgaplnsty, P.rgapwid);
        G.text(xr, g.cl, g.clText, col, 12);
        if (g.tend === null || isna(g.op)) {
            return sd;
        }
        const x0 = g.tend - rdx;
        const nm = g.nm;
        const mid = (g.cl + g.op) / 2;
        const up = g.op > g.cl;
        G.line(x0, xr, mid, col, P.rgaplnsty, P.rgapwid);
        G.text(xr, mid, "RTHOG ce " + nm, col, 12);
        G.line(g.tend, xr, g.op, col, P.rgaplnsty, P.rgapwid);
        G.text(xr, g.op, (up ? "RTHOG hi " : "RTHOG lo ") + nm, col, 12);
        if (P.dogrect) {
            G.rect(x0, xr, g.op, g.cl, P.lrgapcolor, (100 - P.grtransp) / 100);
        }
        if (!current) {
            return sd;
        }
        // octants: 1/8, 3/8, 5/8, 7/8 of the gap, from the open to noon (or the right edge before noon)
        if (P.dorthogoct) {
            const ody = Math.abs(g.op - g.cl) / 8;
            const x1 = g.noonIdx !== null ? Math.min(xr, g.noonIdx) : xr;
            if (ody > P.rthogqmin && x1 > x0) {
                const pl = Math.min(g.cl, g.op);
                for (const k of [1, 3, 5, 7]) {
                    G.line(x0, x1, pl + k * ody, col, P.rgapqlnsty, 1);
                    G.text(x1, pl + k * ody, "RTHOG " + k + "/8 " + nm, col, 12);
                }
            }
        }
        if (P.dorthogq) {
            const qdy = Math.abs(g.op - g.cl) / 4;
            if (qdy > P.rthogqmin) {
                const pl = Math.min(g.cl, g.op);
                G.line(x0, xr, pl + qdy, col, P.rgapqlnsty, P.rgapwid);
                G.text(xr, pl + qdy, "RTHOG dq " + nm, col, 12);
                G.line(x0, xr, pl + 3 * qdy, col, P.rgapqlnsty, P.rgapwid);
                G.text(xr, pl + 3 * qdy, "RTHOG uq " + nm, col, 12);
            }
        }
        if (P.dorthogpo3) {
            const gap = Math.abs(g.op - g.cl);
            const u = sdStep(gap, P.rthogpo3min);
            if (u > 0) {
                const pu0 = Math.max(g.cl, g.op);
                const pd0 = Math.min(g.cl, g.op);
                const nu = sdCount(P.rthogpo3num, P.rthogauto, pu0, u, g.xhi, 1);
                const nd = sdCount(P.rthogpo3num, P.rthogauto, pd0, u, g.xlo, -1);
                for (let k = 1; k <= Math.max(nu, nd); k++) {
                    if (k <= nu) {
                        const y = pu0 + k * u;
                        G.line(x0, xr, y, col, P.rgappo3lnsty, P.rgapwid);
                        sd.push({ y, name: sdName(k, u, gap, 1), lbl: G.text(xr, y, "RTHOG" + sdName(k, u, gap, 1), col, 12) });
                    }
                    if (k <= nd) {
                        const y = pd0 - k * u;
                        G.line(x0, xr, y, col, P.rgappo3lnsty, P.rgapwid);
                        sd.push({ y, name: sdName(k, u, gap, -1), lbl: G.text(xr, y, "RTHOG" + sdName(k, u, gap, -1), col, 12) });
                    }
                }
            }
        }
        return sd;
    }

    // older gaps: short ticks around the settlement and the open
    drawOldRthog(G, g, rdx) {
        const P = this.P;
        if (g.tend === null || isna(g.op) || isna(g.cl)) {
            return;
        }
        const col = P.rgapcolor;
        const mid = (g.cl + g.op) / 2;
        const ext = P.extendgaps;
        G.line(g.tstart - rdx, g.tstart + rdx, g.cl, col, P.rgaplnsty, P.rgapwid);
        G.text(g.tstart + rdx, g.cl, "rthog.cl", C_BLACK, 12);
        G.line(g.tend - rdx, g.tend + rdx, g.cl, col, P.rgaplnsty, P.rgapwid, ext);
        G.text(g.tend + rdx, g.cl, "rthog.cl", C_BLACK, 12);
        G.line(g.tend - rdx, g.tend + rdx, mid, col, P.rgaplnsty, P.rgapwid, ext);
        G.text(g.tend + rdx, mid, "rthog.ce", C_BLACK, 12);
        G.line(g.tend - rdx, g.tend + rdx, g.op, col, P.rgaplnsty, P.rgapwid, ext);
        G.text(g.tend + rdx, g.op, "rthog.op", C_BLACK, 12);
    }
}

const FONT = { tiny: 9, small: 10, normal: 12, large: 16, huge: 22 };

// unbroken day highs (lows) close to each other: the lines stay, but only the higher high
// (lower low) keeps a label, which reads REHs (RELs)
function markRel(arr, tol) {
    const n = arr.length;
    for (let a = 0; a < n; a++) {
        const A = arr[a];
        A.hmode = 0;
        A.lmode = 0;
        for (let b = 0; b < n; b++) {
            if (b === a) {
                continue;
            }
            const B = arr[b];
            if (A.hidonet === null && B.hidonet === null && Math.abs(A.hi - B.hi) <= tol) {
                if (B.hi > A.hi || (B.hi === A.hi && b > a)) {
                    A.hmode = 2;
                } else if (A.hmode === 0) {
                    A.hmode = 1;
                }
            }
            if (A.lodonet === null && B.lodonet === null && Math.abs(A.lo - B.lo) <= tol) {
                if (B.lo < A.lo || (B.lo === A.lo && b > a)) {
                    A.lmode = 2;
                } else if (A.lmode === 0) {
                    A.lmode = 1;
                }
            }
        }
    }
}

// text of the gap: plain, or combined (NDOGx3, NWOGx2, NWOG+NDOG) keeping the date
function gname(g) {
    let b = g.name;
    const dt = " " + g.name.substring(5);
    if (g.isw && g.nd > 0) {
        b = (g.n > 1 ? "NWOGx" + g.n : "NWOG") + "+NDOG" + (g.nd > 1 ? "x" + g.nd : "") + dt;
    } else if (g.n > 1) {
        b = (g.isw ? "NWOGx" : "NDOGx") + g.n + dt;
    }
    return b;
}

// gaps whose ranges overlap or are within tol are one group: the oldest draws the outer levels of the
// group, the others are hidden
function gcluster(arr, tol) {
    const n = arr.length;
    const cid = [];
    for (let k = 0; k < n; k++) {
        const g = arr[k];
        g.n = 1;
        g.nd = 0;
        g.show = true;
        g.uhi = g.hi;
        g.ulo = g.lo;
        g.ut = g.tstart;
        cid.push(k);
    }
    let ch = n > 1;
    while (ch) {
        ch = false;
        for (let a = 0; a < n - 1; a++) {
            for (let b = a + 1; b < n; b++) {
                if (cid[a] !== cid[b]) {
                    const A = arr[a], B = arr[b];
                    if (A.hi + tol >= B.lo && B.hi + tol >= A.lo) {
                        const from = cid[b], to = cid[a];
                        for (let k = 0; k < n; k++) {
                            if (cid[k] === from) {
                                cid[k] = to;
                            }
                        }
                        ch = true;
                    }
                }
            }
        }
    }
    const first = new Array(n).fill(-1);
    for (let k = 0; k < n; k++) {
        const cc = cid[k];
        const r = first[cc];
        if (r < 0) {
            first[cc] = k;
        } else {
            const rep = arr[r], g = arr[k];
            rep.n += 1;
            rep.uhi = Math.max(rep.uhi, g.hi);
            rep.ulo = Math.min(rep.ulo, g.lo);
            g.show = false;
        }
    }
}

function combineGaps(dg, wg, tol) {
    gcluster(dg, tol);
    gcluster(wg, tol);
    for (const w of wg) {
        if (w.show) {
            for (const d of dg) {
                if (d.show && Math.abs(d.uhi - w.uhi) <= tol && Math.abs(d.ulo - w.ulo) <= tol) {
                    w.nd += d.n;
                    d.show = false;
                }
            }
        }
    }
}

// ============================================================ graphics builder
const DASH = { solid: 1, dashed: 4, dotted: 3 };

class Gfx {
    constructor() {
        this.segs = new Map();     // style key -> LineSegments
        this.texts = [];
        this.polys = new Map();    // color|opacity -> Shapes
        this.cells = [];
    }

    seg(col, sty, wid) {
        const k = col + "|" + sty + "|" + wid;
        let s = this.segs.get(k);
        if (!s) {
            s = {
                tag: "LineSegments",
                key: "ls" + this.segs.size,
                lines: [],
                lineStyle: { lineWidth: wid, color: col, lineStyle: DASH[sty] || 1 },
                global: true
            };
            this.segs.set(k, s);
        }
        return s;
    }

    // horizontal line from bar x1 to bar x2 at price y (infEnd: extended to the right)
    line(x1, x2, y, col, sty, wid, infEnd) {
        if (isna(y) || isna(x1) || isna(x2)) {
            return;
        }
        this.seg(col, sty, wid).lines.push({
            tag: "Line",
            a: { x: du(x1), y: du(y) },
            b: { x: du(x2), y: du(y) },
            infiniteStart: false,
            infiniteEnd: !!infEnd
        });
    }

    // vertical line over the whole chart height at bar x
    vline(x, col, sty, wid) {
        this.seg(col, sty, wid).lines.push({
            tag: "Line",
            a: { x: du(x), y: px(0) },
            b: { x: du(x), y: px(10000) },
            infiniteStart: false,
            infiniteEnd: false
        });
    }

    // label: left aligned text at the line end; returns the entry so its text can be changed later
    text(x, y, txt, col, size, bold) {
        const style = { fontSize: size || 12, fill: col };
        if (bold) {
            style.fontWeight = "bold";
        }
        const t = { x, y, text: txt, style };
        this.texts.push(t);
        return t;
    }

    rect(x1, x2, top, bot, col, opacity) {
        if (isna(top) || isna(bot)) {
            return;
        }
        const k = col + "|" + opacity;
        let s = this.polys.get(k);
        if (!s) {
            s = { tag: "Shapes", key: "sh" + this.polys.size, primitives: [],
                fillStyle: { color: col, opacity: opacity }, global: true };
            this.polys.set(k, s);
        }
        s.primitives.push({
            tag: "Polygon",
            points: [
                { x: du(x1), y: du(top) }, { x: du(x2), y: du(top) },
                { x: du(x2), y: du(bot) }, { x: du(x1), y: du(bot) }
            ]
        });
    }

    // status table row, top right of the chart
    cell(row, txt, col) {
        if (txt) {
            this.cells.push({
                tag: "Text",
                key: "tbl" + row,
                point: { x: px(10), y: px(8 + row * 22) },
                text: txt,
                style: { fontSize: 16, fill: col },
                textAlignment: "rightBelow",
                global: true,
                origin: { cs: "grid", h: "right", v: "top" }
            });
        }
    }

    items() {
        const out = [];
        for (const s of this.polys.values()) {
            out.push(s);
        }
        for (const s of this.segs.values()) {
            if (s.lines.length > 0) {
                out.push(s);
            }
        }
        let n = 0;
        for (const t of this.texts) {
            if (!t.text || isna(t.y)) {
                continue;
            }
            out.push({
                tag: "Text",
                key: "tx" + (n++),
                point: { x: du(t.x), y: du(t.y) },
                text: t.text,
                style: t.style,
                textAlignment: "leftMiddle",
                global: true
            });
        }
        return out.concat(this.cells);
    }
}

module.exports = {
    name: "ictlevels",
    description: "ICTlevels",
    calculator: ICTlevels,
    params: PARAMS,
    inputType: meta.InputType.BARS,
    areaChoice: meta.AreaChoice.OVERLAY,
    tags: ["ICT"]
};
