/*
 * FVGs+IFVG+OTE  -  Tradovate custom indicator (JavaScript, Code Explorer format)
 * ============================================================================
 *
 * What it is
 *   Fair value gaps (FVGs, with suspension blocks), 1st presented FVGs (and the inverse 1PFVG), wick C.E.
 *   segments, inverse FVGs (IFVGs, close mode or swing mode, kept IFVGs), ICT OTE detection for IFVGs on the
 *   chart timeframe and on the next two timeframes of the ladder 3m > 5m > 15m > 1h > 4h > D (prior-leg
 *   rejection and retrace checks, OTE bands drawn in the right margin with up / down arrows), the Delivery
 *   Engine features (displacement filter / flag / candle color, full-body confirmation, inversion rules,
 *   A+ setups with killzones and HTF structure), breakers and mitigation blocks (zone, break, swing,
 *   pullback, rectangle / shadow rectangle / flagged candles, bold IFVG labels, arrows, b+/b-, M+/M-,
 *   Mb+/Mb-, inheritance of the tags by IFVGs) and LONG / SHORT / OTE signal markers.
 *
 *   Port of the TradingView Pine Script v6 indicator "FVGS+IFVG+OTE.txt"
 *   (md5 d5c4461b0cfb5612a8ea566d3e950448, "FVGs+IFVG+OTE5"). Parameter names and defaults are the Pine
 *   input variable names (camelCase). See the design document (d.md) and user manual (m.md) for the logic.
 *
 * How to install
 *   Tradovate (web / desktop) > Code Explorer (the "{ }" / Code Explorer tool) > New > Indicator,
 *   name it "fvgsifvgote", replace the template with this whole file, Save. Then add it to a chart from the
 *   indicators list ("FVGs+IFVG+OTE"). Use a time based chart (seconds / minutes / hours / daily).
 *
 * Differences with the Pine version that could not be avoided
 *   - Evaluation on closed bars only. Tradovate calls map() again for the forming bar; this port commits a
 *     bar when the next one starts (as the Pine barstate.isconfirmed logic). Things that Pine moves on every
 *     tick (right edges of live boxes, the OTE bands) are drawn up to the forming bar, but no state changes
 *     before the bar closes. On a historical-only chart the very last bar is treated as forming.
 *   - No alert() from indicators: the alert conditions are exposed as plots (not drawn):
 *       signal    +1 LONG IFVG / -1 SHORT IFVG    (doifvgalert on, suppressed by useOTE + oteOnly)
 *       oteSignal +1 LONG IFVG+OTE / -1 SHORT     (doifvgalert and useOTE on)
 *       bounce    1 on an FVG hi/lo/C.E. bounce (dofvgalert) or a wick C.E. bounce (dowickalert)
 *     A value describes the bar that has just closed and is reported on the following (forming) bar, the bar
 *     on which it can be acted upon.
 *   - No request.security: the higher timeframe candles of the OTE ladder and of the A+ HTF structure are
 *     built by aggregating the chart bars by timestamp in New York time (intraday buckets aligned to the
 *     futures day that starts at 18:00 New York, D = that futures day); only completed HTF candles are used.
 *     Bar timestamps are taken as bar open times (as Pine "time").
 *     The chart timeframe is inferred from the bar timestamps (smallest step seen), so tick / range / renko
 *     / volume charts are not supported by the HTF ladder, the 1PFVG and the killzones.
 *   - Symbol: Pine "TZ = Auto" looks for DAX in the symbol. Tradovate does not reliably expose the symbol to
 *     a calculator, so the parameter "market" (Auto / US / EU) replaces "tz": Auto tries to read the
 *     contract name (FDAX / FDXM / DAX -> EU 09:00 Madrid) and falls back to US (New York). EU = Madrid
 *     with the "fpfvgtime" given. "fpfvgtime" is a number HHMM (930 = 09:30).
 *   - Colors: Tradovate color parameters have no transparency; each color parameter is the Pine color and the
 *     Pine default transparency is applied on top (e.g. FVG fill 90% transparent). ifvgTrans / borderTrans
 *     still work as in Pine. Line styles and opacity use the constants LS_* and OPACITY_SCALE below; adjust
 *     them if your Tradovate version expects other values.
 *   - "visible" (Historic compute) has no effect: the chart visible range is not available, the port always
 *     computes the whole history (and keeps "maxfvgs" FVGs per bias as Pine does).
 *   - barcolor() (displacement candles) is replaced by drawing the candle (wick + body lines) in the color,
 *     like the breaker "Flag candles" option. Pine text sizes / box text alignment are approximated
 *     (Text items, bold = fontWeight bold). x positions are bar indexes instead of bar times (same result
 *     except across missing bars). The Pine object limits (500 boxes, 500 lines, 50 labels) are emulated:
 *     the oldest object is removed first.
 *   - Graphics are returned as one "global" set from the last bar only (d.isLast() or history.data.length),
 *     with stable keys. If neither is available the set is returned on every bar (slower on long histories).
 */

"use strict";

const predef = require("./tools/predef");
const meta = require("./tools/meta");
const { du } = require("./tools/graphics");

// ---- rendering constants (platform dependent) ---------------------------------------------------------
const OPACITY_SCALE = 1;            // fillStyle / lineStyle opacity: 1 -> 0..1 scale, 100 -> 0..100 scale
const LS_SOLID = 1;                 // lineStyle.lineStyle values
const LS_DOTTED = 2;
const LS_DASHED = 3;
const EXT_BARS = 300;               // "extend right": bars drawn beyond the last bar
const MAX_BOXES = 500;              // Pine max_boxes_count
const MAX_LINES = 500;              // Pine max_lines_count
const MAX_LABELS = 50;              // Pine default max_labels_count
const MAX_MARKERS = 500;

// TradingView built-in colors
const TV = {
    orange: "#FF9800", aqua: "#00BCD4", yellow: "#FFEB3B", fuchsia: "#E040FB", gray: "#787B86",
    black: "#363A45", blue: "#2196F3", white: "#FFFFFF"
};

// ---- parameters ----------------------------------------------------------------------------------------
// [name, kind, default, extra]  kind: b bool, n number(step,min), c color, e enum(options)
const STYLE_OPTS = { solid: "solid", dashed: "dashed", dotted: "dotted" };
const PARAMS = [
    // Enable / Draw / Filter
    ["dofp", "b", true], ["doifvg", "b", true], ["dowick", "b", true],
    ["drawMode", "e", "all", { all: "all", PFVG: "1PFVGs", IFVG: "IFVGs", PFVG_IFVG: "1PFVGs+IFVGs" }],
    ["fltMode", "e", "none", { none: "none", sweeps: "sweeps", sweepsMid: "sweeps&mid" }],
    // FVG Settings
    ["visible", "b", false], ["dofvgalert", "b", false],
    ["minfvg", "n", 3, [0.25, 1]], ["maxfvg", "n", 10000, [1, 100]], ["maxfvgs", "n", 200, [1, 2]],
    ["dupTol", "n", 10, [1, 0]], ["flL", "n", 3, [1, 1]], ["flR", "n", 3, [1, 1]],
    ["drawStyle", "e", "box", { box: "Box", boxNoFill: "Box, no fill", segments: "Segments (hi / lo / C.E.)" }],
    ["mince", "n", 20, [1, 10]], ["celnwid", "n", 2, [1, 1]], ["cesty", "e", "dotted", STYLE_OPTS],
    ["lcol", "c", "#FF6D00"], ["lnlcol", "c", "#FF6D00"], ["scol", "c", "#f7525f"], ["lnscol", "c", "#f7525f"],
    // 1st Presented FVGs
    ["fpfvgtime", "n", 930, [1, 0]],
    ["market", "e", "Auto", { Auto: "Auto (DAX: EU, else US)", US: "US (New York)", EU: "EU (Madrid)" }],
    ["nfp", "n", 6, [1, 1]],
    ["fpcol", "c", "#00fbff"], ["lnfpcol", "c", "#0097a7"], ["wfpcol", "c", "#2962ff"], ["lnwfpcol", "c", "#2962ff"],
    ["doifp", "b", false], ["ifpcol", "c", "#880e4f"], ["ilnfpcol", "c", "#880e4f"],
    // Wick Settings
    ["nwicks", "n", 30, [1, 0]], ["minwick", "n", 20, [1, 0]], ["wickdx", "n", 0, [1, 0]],
    ["wicksty", "e", "solid", STYLE_OPTS], ["wickcol", "c", TV.black], ["dowickalert", "b", false],
    // IFVG Settings
    ["doifvgalert", "b", true], ["showSignals", "b", false], ["showIfvgLbl", "b", true],
    ["lblTf", "b", true], ["lblSign", "b", true], ["lblAp", "b", true],
    ["keepIFVG", "b", true], ["keepMax", "n", 20, [1, 1]],
    ["deConf", "e", "close", { close: "Close", fullBody: "Full body" }],
    ["deInv", "b", false], ["deMaxInv", "n", 1, [1, 1]],
    ["deDead", "e", "fade", { fade: "Fade", backToFVG: "Back to FVG" }], ["deDeadCol", "c", TV.gray],
    ["ifvgswings", "b", false], ["useSwingFilter", "b", true], ["showSwing", "b", false],
    ["pivL", "n", 10, [1, 1]], ["pivR", "n", 3, [1, 1]], ["atrLen", "n", 14, [1, 1]], ["minATR", "n", 4.0, [0.1, 0]],
    ["bullIFVGColor", "c", TV.orange], ["bearIFVGColor", "c", TV.aqua],
    ["ifvgTrans", "n", 80, [1, 0]], ["borderTrans", "n", 20, [1, 0]],
    ["ifvgtsz", "e", "small", { auto: "auto", tiny: "tiny", small: "small", normal: "normal", huge: "huge" }],
    ["ifvgha", "e", "right", { left: "left", center: "center", right: "right" }],
    ["ifvgva", "e", "center", { top: "top", center: "center", bottom: "bottom" }],
    // IFVG + OTE
    ["useOTE", "b", true], ["oteOnly", "b", false], ["showOTEsig", "b", false],
    ["oteMin", "n", 0.62, [0.01, 0]], ["oteMax", "n", 0.79, [0.01, 0]],
    ["oteMode", "e", "overlap", { overlap: "Overlap", fullyInside: "Fully inside", ceInside: "C.E. inside" }],
    ["oteRej", "b", true], ["oteRejAny", "b", false], ["oteRejCap", "b", false],
    ["oteHtf1", "b", true], ["oteHtf2", "b", true], ["oteHtfL", "n", 5, [1, 1]], ["oteHtfR", "n", 2, [1, 1]],
    ["showOTEband", "b", true], ["oteOffset", "n", 5, [1, 0]], ["oteWidth", "n", 8, [1, 1]],
    ["oteBandCol", "c", TV.yellow], ["oteHighlight", "c", TV.yellow], ["oteTextCol", "c", TV.black],
    // Breakers
    ["brkOn", "b", true], ["brkMode", "e", "last", { last: "Last candle", sequence: "Whole sequence" }],
    ["brkLen", "n", 3, [1, 1]], ["brkKeep", "n", 3, [1, 1]], ["brkBox", "b", false], ["brkCol", "c", TV.fuchsia],
    ["brkSty", "e", "dashed", STYLE_OPTS], ["brkW", "n", 1, [1, 1]], ["brkFlag", "b", false], ["brkShd", "b", false],
    // Mitigation blocks
    ["mbOn", "b", true], ["mbMode", "e", "last", { last: "Last candle", sequence: "Whole sequence" }],
    ["mbLen", "n", 3, [1, 1]], ["mbKeep", "n", 3, [1, 1]], ["mbBox", "b", false], ["mbCol", "c", TV.orange],
    ["mbSty", "e", "dashed", STYLE_OPTS], ["mbW", "n", 1, [1, 1]], ["mbFlag", "b", false], ["mbShd", "b", false],
    // Delivery Engine
    ["deDisp", "b", false], ["deDispType", "e", "oc", { oc: "Open to Close", hl: "High to Low" }],
    ["deStdLen", "n", 100, [1, 1]], ["deStdX", "n", 4.0, [0.5, 0]],
    ["deDispBar", "b", false], ["deDispCol", "c", TV.yellow], ["deFlag", "b", false], ["deFlagW", "n", 3, [1, 1]],
    ["deAplus", "b", false], ["deLiqLen", "n", 5, [1, 1]], ["deSeqWin", "n", 50, [1, 1]], ["deMssLen", "n", 3, [1, 1]],
    ["deMssX", "n", 2.0, [0.25, 0.5]], ["deMssWin", "n", 2, [1, 0]], ["deKz", "b", true],
    ["deHtfOn", "b", false],
    ["deHtf", "e", "60", { "5": "5m", "15": "15m", "30": "30m", "60": "1h", "120": "2h", "240": "4h", "D": "D" }]
];

// integer parameters (Pine input.int)
const INT_PARAMS = new Set(["maxfvgs", "flL", "flR", "mince", "celnwid", "fpfvgtime", "nfp", "nwicks", "wickdx",
    "keepMax", "deMaxInv", "pivL", "pivR", "atrLen", "ifvgTrans", "borderTrans", "oteHtfL", "oteHtfR", "oteOffset",
    "oteWidth", "brkLen", "brkKeep", "brkW", "mbLen", "mbKeep", "mbW", "deStdLen", "deFlagW", "deLiqLen", "deSeqWin",
    "deMssLen", "deMssWin"]);

function buildParamSpecs() {
    const out = {};
    for (const [name, kind, def, extra] of PARAMS) {
        if (kind === "b") out[name] = predef.paramSpecs.bool(def);
        else if (kind === "n") out[name] = predef.paramSpecs.number(def, extra[0], extra[1]);
        else if (kind === "c") out[name] = predef.paramSpecs.color(def);
        else out[name] = predef.paramSpecs.enum(extra, def);
    }
    return out;
}

// ---- small helpers -------------------------------------------------------------------------------------
const isNa = v => v === null || v === undefined || (typeof v === "number" && Number.isNaN(v));
// Pine color.new(hex, transp) -> { c, a } with a = opacity 0..1
const col = (hex, transp) => ({ c: hex, a: Math.max(0, Math.min(1, (100 - (transp || 0)) / 100)) });
const avg = (a, b) => (a + b) / 2;
const lsOf = s => (s === "dashed" ? LS_DASHED : s === "dotted" ? LS_DOTTED : LS_SOLID);
const FONT = { auto: 11, tiny: 9, small: 11, normal: 13, large: 16, huge: 20 };

// time zone helpers: offset cached per UTC hour, formatter cached per zone
const FMT_CACHE = {};
const OFF_CACHE = {};
function tzOffsetMs(ts, tz) {
    const hk = Math.floor(ts / 3600000);
    let cache = OFF_CACHE[tz];
    if (!cache) cache = OFF_CACHE[tz] = new Map();
    let off = cache.get(hk);
    if (off === undefined) {
        let fmt = FMT_CACHE[tz];
        if (!fmt) {
            fmt = FMT_CACHE[tz] = new Intl.DateTimeFormat("en-US", {
                timeZone: tz, hourCycle: "h23", year: "numeric", month: "2-digit", day: "2-digit",
                hour: "2-digit", minute: "2-digit", second: "2-digit"
            });
        }
        const base = hk * 3600000;
        const p = {};
        for (const x of fmt.formatToParts(new Date(base))) p[x.type] = x.value;
        const hh = parseInt(p.hour, 10) % 24;
        const asUtc = Date.UTC(parseInt(p.year, 10), parseInt(p.month, 10) - 1, parseInt(p.day, 10), hh,
            parseInt(p.minute, 10), parseInt(p.second, 10));
        off = asUtc - base;
        cache.set(hk, off);
        if (cache.size > 20000) cache.clear();
    }
    return off;
}
// { mod: minute of day, dow: 0 Sunday .. 6, day: local day number }
function localInfo(ts, tz) {
    const lt = ts + tzOffsetMs(ts, tz);
    const day = Math.floor(lt / 86400000);
    const mod = Math.floor((lt - day * 86400000) / 60000);
    const dow = ((day % 7) + 7 + 4) % 7; // 1970-01-01 was a Thursday
    return { mod, dow, day };
}
// minutes of a session string "HHMM-HHMM" (end exclusive, may cross midnight)
function inSession(mod, startHHMM, endHHMM) {
    const s = Math.floor(startHHMM / 100) * 60 + (startHHMM % 100);
    const e = Math.floor(endHHMM / 100) * 60 + (endHHMM % 100);
    return s < e ? (mod >= s && mod < e) : (mod >= s || mod < e);
}

// pivots on arrays of closed bars (value of bar j-R, or null)
function pivotHigh(a, j, L, R) {
    const p = j - R;
    if (p - L < 0 || p < 0) return null;
    const v = a[p];
    for (let k = p - L; k < p; k++) if (a[k] > v) return null;
    for (let k = p + 1; k <= j; k++) if (a[k] >= v) return null;
    return v;
}
function pivotLow(a, j, L, R) {
    const p = j - R;
    if (p - L < 0 || p < 0) return null;
    const v = a[p];
    for (let k = p - L; k < p; k++) if (a[k] < v) return null;
    for (let k = p + 1; k <= j; k++) if (a[k] <= v) return null;
    return v;
}

// rolling population standard deviation (Pine ta.stdev, biased)
class RollStd {
    constructor(n) { this.n = n; this.buf = []; this.s = 0; this.s2 = 0; }
    push(x) {
        this.buf.push(x); this.s += x; this.s2 += x * x;
        if (this.buf.length > this.n) { const y = this.buf.shift(); this.s -= y; this.s2 -= y * y; }
        if (this.buf.length < this.n) return null;
        const m = this.s / this.n;
        let v = this.s2 / this.n - m * m;
        if (v < 1e-12 * Math.max(1, m * m)) {   // precision guard: recompute exactly
            v = 0;
            for (const y of this.buf) v += (y - m) * (y - m);
            v /= this.n;
        }
        return Math.sqrt(Math.max(0, v));
    }
}

// higher timeframe candles built from chart bars (bucket key computed by the caller)
class HtfAgg {
    constructor(minutes) { this.n = minutes; this.key = null; this.cur = null; this.bars = []; }
    // returns the HTF candle completed by the arrival of this chart bar (in a new bucket), or null
    feed(key, o, h, l, c, idx) {
        let done = null;
        if (this.cur !== null && key !== this.key) {
            done = this.cur;
            this.bars.push(done);
            if (this.bars.length > 1000) this.bars.splice(0, 200);
            this.cur = null;
        }
        if (this.cur === null) {
            this.cur = { o, h, l, c, idx };
            this.key = key;
        } else {
            if (h > this.cur.h) this.cur.h = h;
            if (l < this.cur.l) this.cur.l = l;
            this.cur.c = c;
        }
        return done;
    }
    // pivot of the completed candles: [price, chartIdx] of candle (last - R), or null
    pivot(L, R, high) {
        const b = this.bars, m = b.length - 1, p = m - R;
        if (p - L < 0) return null;
        const v = high ? b[p].h : b[p].l;
        for (let k = p - L; k <= m; k++) {
            if (k === p) continue;
            const x = high ? b[k].h : b[k].l;
            if (k < p ? (high ? x > v : x < v) : (high ? x >= v : x <= v)) return null;
        }
        return [v, b[p].idx];
    }
}
function htfKey(n, ny) {
    // futures day starts 18:00 New York
    const dayKey = ny.mod >= 1080 ? ny.day + 1 : ny.day;
    if (n >= 1440) return dayKey;
    const fm = (ny.mod - 1080 + 1440) % 1440;
    return dayKey * 1440 + Math.floor(fm / n) * n;
}

// ---- drawing objects (emulate Pine box / line / label handles) -------------------------------------
class Gfx {
    constructor() { this.boxes = new Map(); this.lines = new Map(); this.labels = new Map(); this.nid = 1; }
    _add(map, o, max) {
        o.id = this.nid++;
        o.dead = false;
        o._map = map;
        map.set(o.id, o);
        while (map.size > max) {
            const first = map.values().next().value;
            first.dead = true;
            map.delete(first.id);
        }
        return o;
    }
    box(o) {
        return this._add(this.boxes, Object.assign({
            left: 0, right: 0, top: 0, bottom: 0, bg: col(TV.blue, 90), border: col(TV.blue, 0), bw: 1,
            bstyle: "solid", extend: "none", text: "", textCol: col("#000000", 0), tsize: "normal",
            halign: "center", valign: "center", bold: false, rel: false
        }, o), MAX_BOXES);
    }
    line(o) {
        return this._add(this.lines, Object.assign({ x1: 0, y1: 0, x2: 0, y2: 0, color: col(TV.blue, 0), width: 1,
            style: "solid", extend: "none" }, o), MAX_LINES);
    }
    label(o) { return this._add(this.labels, o, MAX_LABELS); }
    del(o) { if (o && !o.dead) { o.dead = true; o._map.delete(o.id); } }
}

// ============================================================================================================
class FVGSIFVGOTE {
    init() {
        // parameters
        const pr = this.props || {};
        const P = {};
        for (const [name, kind, def] of PARAMS) {
            let v = pr[name];
            if (v === undefined || v === null) v = def;
            if (kind === "n") { v = Number(v); if (!isFinite(v)) v = def; if (INT_PARAMS.has(name)) v = Math.round(v); }
            if (kind === "b") v = !!v;
            P[name] = v;
        }
        this.P = P;
        this.fponly = P.drawMode === "PFVG";
        this.ifvgOnly = P.drawMode === "IFVG";
        this.hideReg = P.drawMode === "IFVG" || P.drawMode === "PFVG_IFVG";
        this.fltOff = P.fltMode === "none";
        this.fltMid = P.fltMode === "sweepsMid";
        this.flN = 1;
        this.flWin = 25;
        this.styNoFill = P.drawStyle === "boxNoFill";
        this.stySeg = P.drawStyle === "segments";
        this.ifvgTxtCol = P.showIfvgLbl ? col(TV.black, 0) : col(TV.black, 100);

        this.g = new Gfx();
        // closed bars
        this.O = []; this.H = []; this.L = []; this.C = []; this.T = [];
        this.nextJ = 0;          // next bar index to commit
        this.curI = -1;          // last index map() was called with
        this.out = [];           // plot values per map index
        this.tfsecs = null;
        this.tfSamples = 0;
        this.ladderTf = null;
        this.tzResolved = false;
        this.lastJ = -1;

        // wicks
        this.wicks = [];
        // FVGs
        this.lfvgs = []; this.sfvgs = []; this.fpfvgs = []; this.keptIf = []; this.pendF = [];
        this.fs = { post: 1000000, stH: null, stL: null, stHbar: null, stLbar: null };
        this.hasfp = false; this.hasifp = false;
        // DE
        this.deRange = []; this.deStd = []; this.deStdMss = [];
        this.rsStd = new RollStd(Math.max(1, P.deStdLen));
        this.lastDispUp = null; this.lastDispDn = null;
        this.deBsl = []; this.deSsl = []; this.deLastBsl = null; this.deLastSsl = null;
        this.deShP = null; this.deSlP = null; this.deDir = 0;
        this.deMssUpBar = null; this.deMssDnBar = null; this.deChochUp = null; this.deChochDn = null;
        this.deHtfDir = 0;
        this.deHtfAgg = null;
        this.deHtfState = { sh: null, sl: null, d: 0 };
        this.dispBars = [];
        // ATR
        this.atrArr = []; this.trSum = 0; this.trCnt = 0; this.atrPrev = null;
        // OTE pivots
        this.pv0 = this.newPivSet(false);
        this.pv1 = this.newPivSet(true);
        this.pv2 = this.newPivSet(true);
        this.hasH1 = false; this.hasH2 = false; this.agg1 = null; this.agg2 = null;
        this.gBear = this.newGroup(); this.gBull = this.newGroup(); this.gSw = this.newGroup();
        // IFVG state
        this.ifvgBox = null; this.ifvgBoxb = null;
        this.bullifvg = null; this.bearifvg = null; this.bullconfirm = null; this.bearconfirm = null;
        this.bullInv = 0; this.bearInv = 0; this.bullAp = false; this.bearAp = false; this.swAp = false;
        this.findRf = { l: null, s: null };
        this.swingTime = null; this.swingBar = null;
        this.lastHi = null; this.lastHiBar = null; this.lastLo = null; this.lastLoBar = null;
        this.swFvg = null; this.ifstime = null; this.ifshi = null; this.ifslo = null; this.ifsdir = 0;
        this.ifsconfirm = null;
        this.prevSig = 0;
        // breakers / mitigation blocks
        this.brks = []; this.bz = { lastH: null, lastHb: null, prevH: null, prevHb: null, lastL: null, lastLb: null, prevL: null, prevLb: null };
        this.mbs = []; this.mz = { lastH: null, lastHb: null, prevH: null, prevHb: null, lastL: null, lastLb: null, prevL: null, prevLb: null };
        // markers
        this.markers = [];
    }

    // ---------------------------------------------------------------------------------------------- map
    map(d, i, history) {
        if (i < this.curI) {
            // recalculation from an earlier bar: start again
            this.init();
        }
        this.curI = i;
        // commit every closed bar before i
        while (this.nextJ < i) {
            const j = this.nextJ;
            const bar = this.histGet(history, j, i);
            const nxt = j + 1 === i ? d : this.histGet(history, j + 1, i);
            if (!bar) break;
            this.commit(j, bar, nxt);
            this.nextJ = j + 1;
        }
        if (!this.out[i]) this.out[i] = { signal: 0, oteSignal: 0, bounce: 0 };
        const o = this.out[i];
        const res = { signal: o.signal, oteSignal: o.oteSignal, bounce: o.bounce };
        if (this.wantGraphics(d, i, history)) res.graphics = { items: this.render(i) };
        return res;
    }

    histGet(history, k, i) {
        try { if (history && typeof history.get === "function") { const b = history.get(k); if (b) return b; } } catch (e) { /* ignore */ }
        try { if (history && typeof history.back === "function") return history.back(i - k); } catch (e) { /* ignore */ }
        return null;
    }

    // graphics only from the last bar; fallback: every bar (stable keys, the newest set wins)
    wantGraphics(d, i, history) {
        try { if (d && typeof d.isLast === "function") return !!d.isLast(); } catch (e) { /* ignore */ }
        try {
            if (history && history.data && typeof history.data.length === "number" && history.data.length > 0)
                return i >= history.data.length - 1;
        } catch (e) { /* ignore */ }
        return true;
    }

    // --------------------------------------------------------------------------------------- commit bar
    commit(j, bar, nxt) {
        const o = +bar.open(), h = +bar.high(), l = +bar.low(), c = +bar.close();
        const ts = +bar.timestamp();
        this.O[j] = o; this.H[j] = h; this.L[j] = l; this.C[j] = c; this.T[j] = ts;
        // chart timeframe from the timestamps
        if (nxt && this.tfSamples < 400) {
            try {
                const dt = Math.round((+nxt.timestamp() - ts) / 1000);
                if (dt > 0 && (this.tfsecs === null || dt < this.tfsecs)) this.tfsecs = dt;
                this.tfSamples++;
            } catch (e) { /* ignore */ }
        }
        if (this.tfsecs === null && j > 0) {
            const dt = Math.round((ts - this.T[j - 1]) / 1000);
            if (dt > 0) this.tfsecs = dt;
        }
        if (this.tfsecs === null) this.tfsecs = 60;
        this.setupTf();
        if (!this.tzResolved) this.resolveTz();
        this.lastJ = j;
        this.j = j; this.o = o; this.h = h; this.l = l; this.c = c; this.ts = ts;
        this.ny = localInfo(ts, "America/New_York");
        this.loc = this.tz === "America/New_York" ? this.ny : localInfo(ts, this.tz);
        this.bounceNow = 0;
        this.step();
    }

    // ladder and HTF aggregators (re-done if the inferred timeframe changes)
    setupTf() {
        if (this.ladderTf === this.tfsecs) return;
        this.ladderTf = this.tfsecs;
        const P = this.P;
        const tfmin = this.tfsecs / 60;
        const ladMin = [3, 5, 15, 60, 240, 1440];
        const ladTf = ["3", "5", "15", "60", "240", "D"];
        let ladIdx = 0;
        for (let k = 0; k <= 5; k++) if (ladMin[k] <= tfmin) ladIdx = k;
        const htf1 = ladIdx + 1 <= 5 ? ladTf[ladIdx + 1] : "";
        const htf2 = ladIdx + 2 <= 5 ? ladTf[ladIdx + 2] : "";
        this.hasH1 = P.oteHtf1 && htf1 !== "";
        this.hasH2 = P.oteHtf2 && htf2 !== "";
        this.pv0.tag = this.tfNameSecs(this.tfsecs);
        this.pv1.tag = this.hasH1 ? this.tfName(htf1) : "";
        this.pv2.tag = this.hasH2 ? this.tfName(htf2) : "";
        this.agg1 = this.hasH1 ? new HtfAgg(htf1 === "D" ? 1440 : +htf1) : null;
        this.agg2 = this.hasH2 ? new HtfAgg(htf2 === "D" ? 1440 : +htf2) : null;
        this.pv1.hb = []; this.pv1.hp = []; this.pv1.lb = []; this.pv1.lp = [];
        this.pv2.hb = []; this.pv2.hp = []; this.pv2.lb = []; this.pv2.lp = [];
        const dm = P.deHtf === "D" ? 1440 : +P.deHtf;
        this.deHtfUse = P.deHtfOn && dm * 60 > this.tfsecs;
        this.deHtfAgg = this.deHtfUse ? new HtfAgg(dm) : null;
        this.deHtfState = { sh: null, sl: null, d: 0 };
        this.deHtfDir = 0;
        this.isMinutes = this.tfsecs >= 60 && this.tfsecs < 86400 && this.tfsecs % 60 === 0;
        this.isIntraday = this.tfsecs < 86400;
    }
    tfName(s) {
        if (s === "D") return "D";
        const m = +s;
        return m >= 60 && m % 60 === 0 ? (m / 60) + "h" : m + "m";
    }
    tfNameSecs(secs) {
        if (secs >= 86400) return secs === 86400 ? "D" : Math.round(secs / 86400) + "D";
        if (secs < 60 || secs % 60 !== 0) return secs + "S";
        return this.tfName(String(secs / 60));
    }

    symbolText() {
        const cands = [];
        const push = v => { if (typeof v === "string") cands.push(v); };
        try {
            const ci = this.contractInfo || this.contract || {};
            push(ci.name); push(ci.symbol); push(ci.description);
            if (ci.product) { push(ci.product.name); push(ci.product.description); }
            if (ci.contractMaturity) push(ci.contractMaturity.productName);
        } catch (e) { /* ignore */ }
        try { push(this.props && this.props.symbol); push(this.symbol); } catch (e) { /* ignore */ }
        return cands.join(" ").toUpperCase();
    }
    resolveTz() {
        this.tzResolved = true;
        const P = this.P;
        let fpt = P.fpfvgtime;
        let tz = "America/New_York";
        if (P.market === "EU") tz = "Europe/Madrid";
        else if (P.market === "Auto") {
            const s = this.symbolText();
            if (s.indexOf("DAX") >= 0 || s.indexOf("FDX") >= 0 || s.indexOf("GERMANY") >= 0) {
                fpt = 900;
                tz = "Europe/Madrid";
            }
        }
        this.tz = tz;
        this.fph = Math.floor(fpt / 100);
        this.fpm = fpt % 100;
    }

    // ------------------------------------------------------------------------------------- bar series
    hiB(k) { return this.H[this.j - k]; }
    loB(k) { return this.L[this.j - k]; }
    opB(k) { return this.O[this.j - k]; }
    clB(k) { return this.C[this.j - k]; }

    bounces(p) {
        const { l, h, c } = this;
        return (Math.abs(p - l) < 2 && c > l + 10) || (Math.abs(p - h) < 2 && c < h - 10);
    }

    // ===================================================================================== per closed bar
    step() {
        const P = this.P, j = this.j;
        const { o, h, l, c } = this;

        // ---- wicks
        if (P.dowick && j >= 1) {
            const bodyh = Math.max(this.opB(1), this.clB(1));
            const bodyl = Math.min(this.opB(1), this.clB(1));
            const wh = this.hiB(1) - bodyh;
            const wl = bodyl - this.loB(1);
            const wcol = this.hideReg ? col(P.wickcol, 100) : col(P.wickcol, 0);
            const wdx = this.wdx();
            if (wh > P.minwick) {
                const ce = avg(this.hiB(1), bodyh);
                this.wicks.push(this.g.line({ x1: j - 1, y1: ce, x2: j - 1 + wdx, y2: ce, style: P.wicksty, color: wcol }));
            }
            if (wl > P.minwick) {
                const ce = avg(this.loB(1), bodyl);
                this.wicks.push(this.g.line({ x1: j - 1, y1: ce, x2: j - 1 + wdx, y2: ce, style: P.wicksty, color: wcol }));
            }
            if (this.wicks.length > P.nwicks) this.g.del(this.wicks.shift());
            if (P.dowickalert) {
                for (const w of this.wicks) if (this.bounces(w.y1)) this.bounceNow = 1;
            }
        }

        // ---- [DE] Delivery Engine (before the FVGs are created)
        const rng = P.deDispType === "oc" ? Math.abs(o - c) : h - l;
        this.deRange[j] = rng;
        const sd = this.rsStd.push(rng);
        this.deStd[j] = sd === null ? null : sd * P.deStdX;
        this.deStdMss[j] = sd === null ? null : sd * P.deMssX;
        const gt = (a, b) => !isNa(a) && !isNa(b) && a > b;
        const deDispOK = j >= 2 && gt(this.deRange[j - 2], this.deStd[j - 2]);
        if (j >= 2) {
            const dispFvg = gt(this.deRange[j - 1], this.deStd[j - 1]) &&
                (this.clB(1) > this.opB(1) ? this.hiB(2) < l : this.loB(2) > h);
            if (P.deDispBar && dispFvg) {
                this.dispBars.push(j - 1);
                if (this.dispBars.length > MAX_MARKERS) this.dispBars.shift();
            }
        }
        if (gt(rng, this.deStdMss[j]) && c > o) this.lastDispUp = j;
        if (gt(rng, this.deStdMss[j]) && c < o) this.lastDispDn = j;
        const deDispUp = this.lastDispUp !== null && j - this.lastDispUp <= P.deMssWin;
        const deDispDn = this.lastDispDn !== null && j - this.lastDispDn <= P.deMssWin;

        // killzones (ICTlevels sessions) in the 1PFVG time zone
        const deUS = this.tz !== "Europe/Madrid";
        const m = this.loc.mod;
        const deInKz = inSession(m, deUS ? 2000 : 2300, deUS ? 0 : 600) || inSession(m, deUS ? 200 : 800, deUS ? 500 : 1300)
            || inSession(m, deUS ? 700 : 1330, deUS ? 1130 : 1800) || inSession(m, deUS ? 1330 : 1900, deUS ? 1600 : 2200);
        this.deKzOK = !P.deKz || !this.isIntraday || this.tfsecs > 900 || deInKz;

        // HTF structure from completed HTF candles
        if (this.deHtfAgg) {
            const done = this.deHtfAgg.feed(htfKey(this.deHtfAgg.n, this.ny), o, h, l, c, j);
            if (done) {
                const st = this.deHtfState;
                if (st.sh !== null && done.c > st.sh) { st.d = 1; st.sh = null; }
                if (st.sl !== null && done.c < st.sl) { st.d = -1; st.sl = null; }
                const ph = this.deHtfAgg.pivot(P.deLiqLen, P.deLiqLen, true);
                const pl = this.deHtfAgg.pivot(P.deLiqLen, P.deLiqLen, false);
                if (ph) st.sh = ph[0];
                if (pl) st.sl = pl[0];
                this.deHtfDir = st.d;
            }
        }
        this.deHtfUp = !this.deHtfUse || this.deHtfDir === 1;
        this.deHtfDn = !this.deHtfUse || this.deHtfDir === -1;

        // swing liquidity and its sweeps
        const dePH = pivotHigh(this.H, j, P.deLiqLen, P.deLiqLen);
        const dePL = pivotLow(this.L, j, P.deLiqLen, P.deLiqLen);
        for (let k = this.deBsl.length - 1; k >= 0; k--) {
            if (h > this.deBsl[k]) { this.deBsl.splice(k, 1); this.deLastBsl = j; }
        }
        for (let k = this.deSsl.length - 1; k >= 0; k--) {
            if (l < this.deSsl[k]) { this.deSsl.splice(k, 1); this.deLastSsl = j; }
        }
        this.deSslRecent = this.deLastSsl !== null && j - this.deLastSsl <= P.deSeqWin;
        this.deBslRecent = this.deLastBsl !== null && j - this.deLastBsl <= P.deSeqWin;

        // structure / MSS
        const deMPH = pivotHigh(this.H, j, P.deMssLen, P.deMssLen);
        const deMPL = pivotLow(this.L, j, P.deMssLen, P.deMssLen);
        if (this.deShP !== null && c > this.deShP) {
            const isRev = this.deDir !== 1;
            const upg = !isRev && this.deChochUp !== null && j - this.deChochUp <= P.deSeqWin && deDispUp;
            const isMss = (isRev || upg) && deDispUp;
            if (isMss) this.deChochUp = null;
            else if (isRev) this.deChochUp = j;
            this.deChochDn = null;
            if (isMss && this.deAp(1)) {
                this.deMssUpBar = j;
                for (const f of this.lfvgs) {
                    if (f.sbar !== null && this.deLastSsl !== null && f.sbar >= this.deLastSsl && !f.aplus) {
                        f.aplus = true;
                        if (f.rect && !f.kept && P.lblAp && !this.hideReg) f.rect.text = "A+";
                    }
                }
            }
            this.deDir = 1;
            this.deShP = null;
        }
        if (this.deSlP !== null && c < this.deSlP) {
            const isRev = this.deDir !== -1;
            const upg = !isRev && this.deChochDn !== null && j - this.deChochDn <= P.deSeqWin && deDispDn;
            const isMss = (isRev || upg) && deDispDn;
            if (isMss) this.deChochDn = null;
            else if (isRev) this.deChochDn = j;
            this.deChochUp = null;
            if (isMss && this.deAp(-1)) {
                this.deMssDnBar = j;
                for (const f of this.sfvgs) {
                    if (f.sbar !== null && this.deLastBsl !== null && f.sbar >= this.deLastBsl && !f.aplus) {
                        f.aplus = true;
                        if (f.rect && !f.kept && P.lblAp && !this.hideReg) f.rect.text = "A+";
                    }
                }
            }
            this.deDir = -1;
            this.deSlP = null;
        }
        if (deMPH !== null) this.deShP = deMPH;
        if (deMPL !== null) this.deSlP = deMPL;
        if (dePH !== null) { this.deBsl.push(dePH); if (this.deBsl.length > 50) this.deBsl.shift(); }
        if (dePL !== null) { this.deSsl.push(dePL); if (this.deSsl.length > 50) this.deSsl.shift(); }

        // ---- FVGs
        if (Math.floor(this.loc.mod / 60) !== this.fph) { this.hasfp = false; this.hasifp = false; }
        let lastlongfvg = null, lastshortfvg = null;
        if (j >= 3) {
            const isfpNow = this.isFPFVG();
            for (const islong of [true, false]) {
                const z = islong ? this.longFVGHL() : this.shortFVGHL();
                const [hi, lo, fhi, flo] = z;
                if (fhi - flo > P.minfvg && fhi - flo < P.maxfvg && (!P.deDisp || deDispOK || isfpNow)) {
                    const f = this.newFVG(hi, lo, avg(hi, lo), islong, j - 3);
                    f.sbar = j - 3;
                    const mssBar = islong ? this.deMssUpBar : this.deMssDnBar;
                    f.aplus = P.deAplus && mssBar !== null && j - mssBar <= 3;
                    f.disp = deDispOK;
                    if (islong) lastlongfvg = f; else lastshortfvg = f;
                    if (!this.hasifp && this.hasfp && P.doifp && this.fpfvgs.length > 0) {
                        const fp = this.fpfvgs[this.fpfvgs.length - 1];
                        if (fp.islong !== f.islong) {
                            f.isifp = true;
                            this.hasifp = true;
                            this.fpfvgs.push(f);
                        }
                    }
                    if (!this.hasfp && isfpNow && !f.isifp) {
                        f.isfp = true;
                        f.iswfp = this.loc.dow === 1;
                        this.hasfp = f.isfp;
                        this.fpfvgs.push(f);
                        this.clearlastfp();
                    } else if (!this.fponly && !f.isifp) {
                        this.route(f);
                    } else {
                        this.kill(f);
                    }
                }
            }
        }

        // ---- FVG filter: liquidity sweeps
        const fPH = pivotHigh(this.H, j, P.flL, P.flR);
        const fPL = pivotLow(this.L, j, P.flL, P.flR);
        if (!this.fltOff) {
            const fs = this.fs;
            if (fs.stH !== null && h > fs.stH) { this.onSweep(fs.stLbar); fs.post = 0; fs.stH = null; }
            if (fs.stL !== null && l < fs.stL) { this.onSweep(fs.stHbar); fs.post = 0; fs.stL = null; }
            if (fPH !== null) { fs.stH = fPH; fs.stHbar = j - P.flR; }
            if (fPL !== null) { fs.stL = fPL; fs.stLbar = j - P.flR; }
        }

        // ---- FVG update
        this.updfvgs(this.lfvgs);
        this.updfvgs(this.sfvgs);
        this.updfvgs(this.fpfvgs);

        // ---- [OTE] pivots: chart and higher timeframes
        const oph = pivotHigh(this.H, j, P.pivL, P.pivR);
        const opl = pivotLow(this.L, j, P.pivL, P.pivR);
        if (oph !== null) this.addHigh(this.pv0, j - P.pivR, oph);
        if (opl !== null) this.addLow(this.pv0, j - P.pivR, opl);
        for (const [agg, ps] of [[this.agg1, this.pv1], [this.agg2, this.pv2]]) {
            if (!agg) continue;
            const done = agg.feed(htfKey(agg.n, this.ny), o, h, l, c, j);
            if (done && P.doifvg && P.useOTE) {
                const ph = agg.pivot(P.oteHtfL, P.oteHtfR, true);
                const pl = agg.pivot(P.oteHtfL, P.oteHtfR, false);
                if (ph) this.addHigh(ps, ph[1], ph[0]);
                if (pl) this.addLow(ps, pl[1], pl[0]);
            }
        }

        // ---- close mode candidates
        const h4 = Math.max(this.hiB(0), j >= 1 ? this.hiB(1) : -Infinity, j >= 2 ? this.hiB(2) : -Infinity, j >= 3 ? this.hiB(3) : -Infinity);
        const l4 = Math.min(this.loB(0), j >= 1 ? this.loB(1) : Infinity, j >= 2 ? this.loB(2) : Infinity, j >= 3 ? this.loB(3) : Infinity);
        if (P.doifvg && !P.ifvgswings && lastlongfvg) {
            if (this.bearifvg) this.release(this.bearifvg, this.bearconfirm !== null, this.bearconfirm);
            this.bearifvg = lastlongfvg;
            this.bearconfirm = null;
            this.bearInv = 0;
            this.bearAp = false;
            this.gStart(this.gBear, -1, this.bearifvg.lo, this.bearifvg.hi, h4, this.bearifvg.sbar);
            this.ifvgBoxb = this.adopt(this.bearifvg, -1);
        }
        if (P.doifvg && !P.ifvgswings && lastshortfvg) {
            if (this.bullifvg) this.release(this.bullifvg, this.bullconfirm !== null, this.bullconfirm);
            this.bullifvg = lastshortfvg;
            this.bullconfirm = null;
            this.bullInv = 0;
            this.bullAp = false;
            this.gStart(this.gBull, 1, this.bullifvg.lo, this.bullifvg.hi, l4, this.bullifvg.sbar);
            this.ifvgBox = this.adopt(this.bullifvg, 1);
        }
        if (this.ifvgBox) this.ifvgBox.right = j;
        if (this.ifvgBoxb) this.ifvgBoxb.right = j;

        // ---- swing mode
        this.updAtr();
        let pivHi = null, pivLo = null;
        if (P.doifvg && P.ifvgswings) { pivHi = oph; pivLo = opl; }
        const hiBar = j - P.pivR, loBar = j - P.pivR;
        if (P.doifvg && P.ifvgswings) {
            if (pivHi !== null) { this.lastHi = pivHi; this.lastHiBar = hiBar; }
            if (pivLo !== null) { this.lastLo = pivLo; this.lastLoBar = loBar; }
        }
        const atrR = j - P.pivR >= 0 ? this.atrArr[j - P.pivR] : null;
        if (pivHi !== null && P.doifvg && P.ifvgswings) {
            const swingPrice = pivHi, refLo = this.lastLo;
            const sig = !P.useSwingFilter || refLo === null || (atrR !== null && swingPrice - refLo >= atrR * P.minATR);
            if (sig) {
                this.swingTime = hiBar; this.swingBar = hiBar;
                if (P.showSwing && !this.hideReg)
                    this.g.label({ x: hiBar, y: swingPrice, text: "SWING HIGH", up: false });
                const ifvg = this.findFVG(this.swingTime, this.lfvgs, "l");
                if (ifvg) {
                    if (this.swFvg && this.swFvg.start !== ifvg.start) this.release(this.swFvg, this.ifsconfirm !== null, this.ifsconfirm);
                    this.swFvg = ifvg;
                    this.ifstime = ifvg.start; this.ifshi = ifvg.hi; this.ifslo = ifvg.lo; this.ifsdir = -1;
                    this.ifsconfirm = null;
                    this.gStart(this.gSw, -1, ifvg.lo, ifvg.hi, swingPrice, ifvg.sbar);
                    this.ifvgBox = this.adopt(ifvg, -1);
                }
            }
        }
        if (pivLo !== null && P.doifvg && P.ifvgswings) {
            const swingPrice = pivLo, refHi = this.lastHi;
            const sig = !P.useSwingFilter || refHi === null || (atrR !== null && refHi - swingPrice >= atrR * P.minATR);
            if (sig) {
                this.swingTime = loBar; this.swingBar = loBar;
                if (P.showSwing && !this.hideReg)
                    this.g.label({ x: loBar, y: swingPrice, text: "SWING LOW", up: true });
                const ifvg = this.findFVG(this.swingTime, this.sfvgs, "s");
                if (ifvg) {
                    if (this.swFvg && this.swFvg.start !== ifvg.start) this.release(this.swFvg, this.ifsconfirm !== null, this.ifsconfirm);
                    this.swFvg = ifvg;
                    this.ifstime = ifvg.start; this.ifshi = ifvg.hi; this.ifslo = ifvg.lo; this.ifsdir = 1;
                    this.ifsconfirm = null;
                    this.gStart(this.gSw, 1, ifvg.lo, ifvg.hi, swingPrice, ifvg.sbar);
                    this.ifvgBox = this.adopt(ifvg, 1);
                }
            }
        }
        if (this.ifvgBox) this.ifvgBox.right = j;
        if (this.ifstime !== null) {
            if (this.ifsdir === -1 && this.dnBreak(this.ifslo) && this.ifsconfirm === null) {
                this.ifsconfirm = j;
                this.gConfirm(this.gSw);
                this.swAp = this.deAp(-1);
                this.setText(this.ifvgBox, this.ifvgTxt(-1, 1, false, "", this.swAp, this.mbOf(this.swFvg)));
            } else if (this.ifsdir === 1 && this.upBreak(this.ifshi) && this.ifsconfirm === null) {
                this.ifsconfirm = j;
                this.gConfirm(this.gSw);
                this.swAp = this.deAp(1);
                this.setText(this.ifvgBox, this.ifvgTxt(1, 1, false, "", this.swAp, this.mbOf(this.swFvg)));
            }
            if (this.ifsdir === -1 && this.ifsconfirm !== null && c > this.ifshi) {
                this.ifsconfirm = null;
                this.gCancel(this.gSw);
                this.setText(this.ifvgBox, "");
            } else if (this.ifsdir === 1 && this.ifsconfirm !== null && c < this.ifslo) {
                this.ifsconfirm = null;
                this.gCancel(this.gSw);
                this.setText(this.ifvgBox, "");
            }
        }

        // ---- IFVG signals
        let sig = 0;
        if (P.doifvg && P.ifvgswings) {
            if (this.ifsconfirm !== null && j !== this.ifsconfirm && this.prevSig === 0) {
                if (this.ifsdir === -1 && h >= this.ifslo && h < this.ifshi) sig = -1;
                else if (this.ifsdir === 1 && l <= this.ifshi && l >= this.ifslo) sig = 1;
            }
        }
        if (P.doifvg && !P.ifvgswings && !P.deInv) {
            if (this.bullifvg && this.bullconfirm !== null && c < this.bullifvg.lo) {
                this.bullconfirm = null;
                this.gCancel(this.gBull);
                this.setText(this.ifvgBox, "");
            }
            if (this.bullifvg && this.upBreak(this.bullifvg.hi) && this.bullconfirm === null) {
                this.bullconfirm = j;
                this.gConfirm(this.gBull);
                this.bullAp = this.deAp(1);
                this.setText(this.ifvgBox, this.ifvgTxt(1, 1, false, "", this.bullAp, this.mbOf(this.bullifvg)));
                sig = 1;
            }
            if (this.bearifvg && this.bearconfirm !== null && c > this.bearifvg.hi) {
                this.bearconfirm = null;
                this.gCancel(this.gBear);
                this.setText(this.ifvgBoxb, "");
            }
            if (this.bearifvg && this.dnBreak(this.bearifvg.lo) && this.bearconfirm === null) {
                this.bearAp = this.deAp(-1);
                this.setText(this.ifvgBoxb, this.ifvgTxt(-1, 1, false, "", this.bearAp, this.mbOf(this.bearifvg)));
                this.bearconfirm = j;
                this.gConfirm(this.gBear);
                sig = -1;
            }
        }
        if (P.doifvg && !P.ifvgswings && P.deInv) {
            if (this.bullifvg && this.bullInv >= 0) {
                const brk = this.bullInv % 2 === 0 ? this.upBreak(this.bullifvg.hi) : this.dnBreak(this.bullifvg.lo);
                if (brk) {
                    if (this.bullInv >= P.deMaxInv) {
                        this.bullInv = -1;
                        this.bullconfirm = null;
                        this.gCancel(this.gBull);
                        this.deKill(this.bullifvg);
                        this.ifvgBox = null;
                    } else {
                        this.bullInv += 1;
                        const dd = this.bullInv % 2 === 1 ? 1 : -1;
                        if (this.bullInv === 1) {
                            this.bullconfirm = j;
                            this.gConfirm(this.gBull);
                        } else {
                            this.bullconfirm = null;
                            this.gCancel(this.gBull);
                            this.deRecolor(this.ifvgBox, dd);
                        }
                        this.bullAp = this.deAp(dd);
                        this.setText(this.ifvgBox, this.ifvgTxt(dd, this.bullInv, false, "", this.bullAp, this.mbOf(this.bullifvg)));
                        sig = dd;
                    }
                }
            }
            if (this.bearifvg && this.bearInv >= 0) {
                const brk = this.bearInv % 2 === 0 ? this.dnBreak(this.bearifvg.lo) : this.upBreak(this.bearifvg.hi);
                if (brk) {
                    if (this.bearInv >= P.deMaxInv) {
                        this.bearInv = -1;
                        this.bearconfirm = null;
                        this.gCancel(this.gBear);
                        this.deKill(this.bearifvg);
                        this.ifvgBoxb = null;
                    } else {
                        this.bearInv += 1;
                        const dd = this.bearInv % 2 === 1 ? -1 : 1;
                        if (this.bearInv === 1) {
                            this.bearconfirm = j;
                            this.gConfirm(this.gBear);
                        } else {
                            this.bearconfirm = null;
                            this.gCancel(this.gBear);
                            this.deRecolor(this.ifvgBoxb, dd);
                        }
                        this.bearAp = this.deAp(dd);
                        this.setText(this.ifvgBoxb, this.ifvgTxt(dd, this.bearInv, false, "", this.bearAp, this.mbOf(this.bearifvg)));
                        sig = sig === 0 ? dd : sig;
                    }
                }
            }
        }

        this.updKept();

        // ---- [BRK] breakers and [MB] mitigation blocks
        const bPH = pivotHigh(this.H, j, P.brkLen, P.brkLen);
        const bPL = pivotLow(this.L, j, P.brkLen, P.brkLen);
        if (P.brkOn) this.zoneBlock(false, bPH, bPL);
        const mPH = pivotHigh(this.H, j, P.mbLen, P.mbLen);
        const mPL = pivotLow(this.L, j, P.mbLen, P.mbLen);
        if (P.mbOn) this.zoneBlock(true, mPH, mPL);

        // ---- [MB] inheritance by the confirmed IFVGs
        if (P.brkOn || P.mbOn) {
            if (!P.ifvgswings) {
                if (this.bullconfirm !== null) this.ifvgInherit(this.bullifvg, this.ifvgBox, 1, 1);
                if (this.bearconfirm !== null) this.ifvgInherit(this.bearifvg, this.ifvgBoxb, -1, -1);
            } else if (this.ifsconfirm !== null) {
                this.ifvgInherit(this.swFvg, this.ifvgBox, 2, this.ifsdir);
            }
        }

        // ---- [OTE] signals
        const bearOteSig = this.gStep(this.gBear);
        const bullOteSig = this.gStep(this.gBull);
        const swOteSig = this.gStep(this.gSw);
        let oteSig = 0;
        if (bearOteSig !== 0) oteSig = -1;
        else if (bullOteSig !== 0) oteSig = 1;
        else if (swOteSig !== 0) oteSig = swOteSig;
        if (P.doifvg && P.useOTE) {
            const hl = col(P.oteHighlight, 0);
            if (bearOteSig !== 0 && this.ifvgBoxb) {
                this.setText(this.ifvgBoxb, this.ifvgTxt(-1, 1, true, this.gBear.tags, this.bearAp, this.mbOf(this.bearifvg)));
                this.ifvgBoxb.border = hl;
            }
            if (bullOteSig !== 0 && this.ifvgBox) {
                this.setText(this.ifvgBox, this.ifvgTxt(1, 1, true, this.gBull.tags, this.bullAp, this.mbOf(this.bullifvg)));
                this.ifvgBox.border = hl;
            }
            if (swOteSig !== 0 && this.ifvgBox) {
                this.setText(this.ifvgBox, this.ifvgTxt(swOteSig, 1, true, this.gSw.tags, this.swAp, this.mbOf(this.swFvg)));
                this.ifvgBox.border = hl;
            }
        }

        // ---- markers
        if (P.showSignals && !this.hideReg && sig !== 0 && this.prevSig === 0) this.addMarker(j, sig, false);
        if (P.showOTEsig && !this.hideReg && oteSig !== 0) this.addMarker(j, oteSig, true);

        // ---- "alerts" -> plots of the next bar
        this.out[j + 1] = {
            signal: P.doifvgalert && sig !== 0 && !(P.useOTE && P.oteOnly) ? sig : 0,
            oteSignal: P.doifvgalert && P.useOTE && oteSig !== 0 ? oteSig : 0,
            bounce: this.bounceNow
        };
        this.lastSig = sig;
        this.lastOteSig = oteSig;
        this.prevSig = sig;
    }

    addMarker(j, dir, ote) {
        this.markers.push({ x: j, dir, ote, y: dir > 0 ? this.l : this.h });
        if (this.markers.length > MAX_MARKERS) this.markers.shift();
    }

    wdx() {
        let r = this.P.wickdx;
        if (r === 0 && this.tfsecs / 60 < 15) r = 4;
        else if (r === 0) r = 1;
        return r;
    }

    updAtr() {
        const j = this.j;
        const tr = j === 0 ? this.h - this.l : Math.max(this.h - this.l, Math.abs(this.h - this.C[j - 1]), Math.abs(this.l - this.C[j - 1]));
        const n = this.P.atrLen;
        let v = null;
        if (this.atrPrev === null) {
            this.trSum += tr; this.trCnt++;
            if (this.trCnt >= n) v = this.trSum / n;
        } else {
            v = (this.atrPrev * (n - 1) + tr) / n;
        }
        this.atrPrev = v;
        this.atrArr[j] = v;
    }

    // ---------------------------------------------------------------------------------- FVG helpers
    newFVG(hi, lo, ce, islong, start) {
        return {
            hi, lo, ce, islong, start, end: null, rect: null, celine: null, isfp: false, iswfp: false, isifp: false,
            sbar: null, hiln: null, loln: null, kept: false, kdir: 0, dup: null, cand: false, own: false,
            shown: false, aplus: false, disp: false, brk: false, brkD: 0, mbD: 0
        };
    }
    longFVGHL() {
        const o1 = this.opB(1), c1 = this.clB(1), l1 = this.loB(1);
        const o2 = this.opB(2), c2 = this.clB(2);
        const o3 = this.opB(3), c3 = this.clB(3), h3 = this.hiB(3);
        let hi = l1, lo = h3;
        const blow1 = Math.min(o1, c1), bhi2 = Math.max(o2, c2), blow2 = Math.min(o2, c2), bhi3 = Math.max(o3, c3);
        const fhi = hi, flo = lo;
        if (bhi3 < blow2) lo = bhi3;
        if (blow1 > bhi2) hi = blow1;
        return [hi, lo, fhi, flo];
    }
    shortFVGHL() {
        const o1 = this.opB(1), c1 = this.clB(1), h1 = this.hiB(1);
        const o2 = this.opB(2), c2 = this.clB(2);
        const o3 = this.opB(3), c3 = this.clB(3), l3 = this.loB(3);
        let hi = l3, lo = h1;
        const blow3 = Math.min(o3, c3), bhi2 = Math.max(o2, c2), blow2 = Math.min(o2, c2), bhi1 = Math.max(o1, c1);
        const fhi = hi, flo = lo;
        if (blow3 > bhi2) hi = blow3;
        if (bhi1 < blow2) lo = bhi1;
        return [hi, lo, fhi, flo];
    }
    isFPFVG() {
        const P = this.P;
        let r = P.dofp;
        if (r) {
            const hh = Math.floor(this.loc.mod / 60), mm = this.loc.mod % 60;
            let nmin = 2;
            if (this.isMinutes) nmin = this.tfsecs / 60 * 2;
            r = hh === this.fph && mm > this.fpm + nmin && this.isMinutes && this.tfsecs / 60 <= 3;
        }
        return r;
    }
    bw(f) { return this.P.deFlag && f.disp ? this.P.deFlagW : 1; }
    kill(f) {
        if (!f) return;
        this.g.del(f.rect); this.g.del(f.celine); this.g.del(f.hiln); this.g.del(f.loln);
        f.rect = null; f.celine = null; f.hiln = null; f.loln = null;
    }
    endsAt(f) { return f.end === null ? this.j : f.end; }
    isDone(f, o, c) { return f.islong ? (c < f.lo || o < f.lo) : (c > f.hi || o > f.hi); }
    bgColor(f) {
        const P = this.P;
        let c = f.islong ? P.lcol : P.scol, t = 90;
        if (f.isfp) { c = P.fpcol; if (f.iswfp) c = P.wfpcol; }
        if (f.isifp) { c = P.ifpcol; t = 80; }
        const hid = this.hideReg && (this.ifvgOnly || (!f.isfp && !f.isifp));
        return this.styNoFill ? null : (hid ? col(c, 100) : col(c, t));
    }
    lnColor(f) {
        const P = this.P;
        let c = f.islong ? P.lnlcol : P.lnscol;
        if (f.isfp) { c = P.lnfpcol; if (f.iswfp) c = P.lnwfpcol; }
        if (f.isifp) c = P.ilnfpcol;
        const hid = this.hideReg && (this.ifvgOnly || (!f.isfp && !f.isifp));
        return hid ? col(c, 100) : col(c, 20);
    }
    clearlastfp() {
        while (this.fpfvgs.length > this.P.nfp) {
            const f = this.fpfvgs.shift();
            if (f && f.isfp) {
                if (f.rect) f.rect.extend = "none";
                if (f.celine) f.celine.extend = "none";
                if (f.hiln) f.hiln.extend = "none";
                if (f.loln) f.loln.extend = "none";
                f.isfp = false;
                this.kill(f);
            }
        }
    }
    setText(b, t) { if (b) b.text = t || ""; }
    textFmt(b) { const P = this.P; b.tsize = P.ifvgtsz; b.halign = P.ifvgha; b.valign = P.ifvgva; }

    restore(f) {
        f.kept = false;
        f.cand = false;
        if (f.rect) {
            if (f.own || this.stySeg) {
                this.g.del(f.rect);
                f.rect = null;
                f.own = false;
            } else {
                f.rect.bg = this.bgColor(f);
                f.rect.border = this.lnColor(f);
                f.rect.bw = this.bw(f);
                f.rect.text = f.aplus && this.P.lblAp && !this.hideReg ? "A+" : "";
                f.rect.right = this.endsAt(f);
                f.rect.extend = (f.isifp || f.isfp) ? "right" : "none";
            }
        }
    }
    adopt(fv, dir) {
        const P = this.P;
        const f = fv.dup ? fv.dup : fv;
        const cc = dir < 0 ? P.bearIFVGColor : P.bullIFVGColor;
        if (f.rect && f.rect.dead) f.rect = null;   // removed by the object limit
        if (!f.rect) {
            f.rect = this.g.box({ left: f.start, top: f.hi, right: this.j, bottom: f.lo });
            f.own = !(f.shown || f.isfp || f.isifp);
        }
        if (f.isfp || f.isifp) {
            const fc = f.isifp ? col(P.ifpcol, 80) : (f.iswfp ? col(P.wfpcol, 90) : col(P.fpcol, 90));
            const lc = f.isifp ? col(P.ilnfpcol, 20) : (f.iswfp ? col(P.lnwfpcol, 20) : col(P.lnfpcol, 20));
            f.rect.bg = this.styNoFill ? null : fc;
            f.rect.border = lc;
            f.rect.bw = 1;
            f.rect.extend = "right";
        } else {
            f.rect.bg = col(cc, P.ifvgTrans);
            f.rect.border = col(cc, P.borderTrans);
            f.rect.bw = 2;
            f.rect.extend = "none";
        }
        f.rect.right = this.j;
        f.rect.text = "";
        f.rect.textCol = this.ifvgTxtCol;
        this.textFmt(f.rect);
        if (!f.kept) this.keptIf.push(f);
        f.kept = true;
        f.cand = true;
        f.kdir = dir;
        return f.rect;
    }
    release(fv, confirmed, ctime) {
        const f = fv.dup ? fv.dup : fv;
        f.cand = false;
        if (f.kept) {
            if (this.P.keepIFVG && confirmed) {
                if (f.end === null) f.end = ctime;
                if (this.keptIf.length > this.P.keepMax + 3) {
                    const o = this.keptIf.shift();
                    if (o.cand) this.keptIf.push(o);
                    else this.restore(o);
                }
            } else {
                this.restore(f);
            }
        }
    }
    updKept() {
        for (let k = this.keptIf.length - 1; k >= 0; k--) {
            const f = this.keptIf[k];
            if (!f.rect || f.rect.dead || !f.kept) {
                if (f.rect && f.rect.dead) f.rect = null;
                f.kept = false;
                f.cand = false;
                this.keptIf.splice(k, 1);
            } else if (!f.cand && (f.kdir < 0 ? this.c > f.hi : this.c < f.lo)) {
                this.restore(f);
                this.keptIf.splice(k, 1);
            } else {
                f.rect.right = this.j;
            }
        }
    }
    mergeZone(g, f) {
        if (!g.kept) {
            const nh = Math.max(g.hi, f.hi), nl = Math.min(g.lo, f.lo);
            g.hi = nh; g.lo = nl; g.ce = avg(nh, nl);
            f.hi = nh; f.lo = nl; f.ce = g.ce;
            if (g.rect) { g.rect.top = nh; g.rect.bottom = nl; }
            if (g.hiln) { g.hiln.y1 = nh; g.hiln.y2 = nh; g.loln.y1 = nl; g.loln.y2 = nl; }
            if (g.celine) { g.celine.y1 = g.ce; g.celine.y2 = g.ce; }
            g.end = null;
        }
    }
    insertSorted(a, f) {
        let pos = a.length;
        while (pos > 0 && a[pos - 1].start > f.start) pos--;
        f.shown = true;
        a.splice(pos, 0, f);
    }
    findDup(f) {
        let r = null;
        const P = this.P;
        const tol = (f.hi - f.lo) * P.dupTol / 100.0;
        if (P.dupTol > 0) {
            for (const arr of [this.lfvgs, this.sfvgs, this.fpfvgs, this.keptIf]) {
                for (let k = arr.length - 1; k >= 0; k--) {
                    const g = arr[k];
                    if (Math.abs(g.hi - f.hi) <= tol && Math.abs(g.lo - f.lo) <= tol && (r === null || g.start > r.start)) {
                        r = g;
                        break;
                    }
                }
            }
        }
        return r;
    }
    route(f) {
        const s = this.fs;
        const d = this.findDup(f);
        let shown = false;
        if (d) {
            f.dup = d;
            this.mergeZone(d, f);
        } else if (this.fltOff) {
            shown = true;
        } else if (s.post < this.flN) {
            shown = true;
            s.post += 1;
        }
        if (shown) this.insertSorted(f.islong ? this.lfvgs : this.sfvgs, f);
        else if (!d) {
            this.pendF.push(f);
            if (this.pendF.length > 60) this.pendF.shift();
        }
    }
    reveal(f) {
        const d = f.kept ? null : this.findDup(f);
        if (!d) {
            this.insertSorted(f.islong ? this.lfvgs : this.sfvgs, f);
        } else {
            f.dup = d;
            if (f.start < d.start && !d.kept) {
                d.start = f.start;
                if (d.rect) d.rect.left = d.start;
                if (d.hiln) { d.hiln.x1 = d.start; d.loln.x1 = d.start; }
                if (d.celine) d.celine.x1 = d.start;
            }
            this.mergeZone(d, f);
        }
    }
    onSweep(armStart) {
        const n = this.pendF.length, j = this.j;
        if (n > 0) {
            const lastF = this.pendF[n - 1];
            let midF = null;
            if (this.fltMid && n > 1 && armStart !== null && armStart < j) {
                const mb = Math.round((armStart + j) / 2.0);
                const w = Math.max(1, Math.round((j - armStart) * this.flWin / 100.0));
                let best = 0.0;
                for (let k = 0; k <= n - 2; k++) {
                    const g = this.pendF[k];
                    const sz = g.hi - g.lo;
                    if (g.sbar >= mb - w && g.sbar <= mb + w && sz > best) { best = sz; midF = g; }
                }
            }
            this.reveal(lastF);
            if (midF) this.reveal(midF);
            this.pendF = [];
        }
    }
    updfvgs(fvgs) {
        const P = this.P;
        if (fvgs.length > 0) {
            for (const f of fvgs) {
                const isdone = f.end !== null;
                if (!isdone && this.isDone(f, this.o, this.c)) f.end = this.endsAt(f);
                const et = this.endsAt(f);
                const ext = (f.isifp || f.isfp) ? "right" : "none";
                if (this.stySeg) {
                    if (!f.hiln) {
                        f.hiln = this.g.line({ x1: f.start, y1: f.hi, x2: et, y2: f.hi, color: this.lnColor(f), extend: ext, width: this.bw(f) });
                        f.loln = this.g.line({ x1: f.start, y1: f.lo, x2: et, y2: f.lo, color: this.lnColor(f), extend: ext, width: this.bw(f) });
                    }
                } else if (!f.rect) {
                    f.rect = this.g.box({ left: f.start, top: f.hi, right: et, bottom: f.lo, bg: this.bgColor(f),
                        border: this.lnColor(f), bw: this.bw(f), extend: ext, bstyle: "solid" });
                    if (f.aplus && P.lblAp && !this.hideReg) {
                        f.rect.text = "A+";
                        this.textFmt(f.rect);
                    }
                }
                if (!f.celine && f.hi - f.lo > P.mince) {
                    f.celine = this.g.line({ x1: f.start, y1: f.ce, x2: et, y2: f.ce, color: this.lnColor(f), style: P.cesty, width: P.celnwid });
                    if (f.isfp) f.celine.extend = "right";
                }
                if (f.rect && !f.kept) f.rect.right = et;
                if (f.hiln) { f.hiln.x2 = et; f.loln.x2 = et; }
                if (f.celine) f.celine.x2 = et;
                if (P.dofvgalert && f.end === null) {
                    if (this.bounces(f.hi) || this.bounces(f.lo) || this.bounces(f.ce)) this.bounceNow = 1;
                }
            }
            if (fvgs.length >= P.maxfvgs && fvgs.length > 0) {
                const df = fvgs.shift();
                this.kill(df);
            }
        }
    }

    // ---------------------------------------------------------------------------------- DE helpers
    deAp(d) {
        return this.P.deAplus && this.deKzOK && (d === 1 ? this.deSslRecent && this.deHtfUp : this.deBslRecent && this.deHtfDn);
    }
    upBreak(hi) { return this.P.deConf === "fullBody" ? Math.min(this.o, this.c) > hi : this.c > hi; }
    dnBreak(lo) { return this.P.deConf === "fullBody" ? Math.max(this.o, this.c) < lo : this.c < lo; }
    ifvgTxt(d, inv, ote, tags, ap, mbd) {
        const P = this.P;
        let t = "IFVG" + (P.lblSign ? (d < 0 ? "-" : "+") : "");
        if (inv > 1) t += " ×" + inv;
        if (ote) t += " OTE" + (P.lblTf && tags !== "" ? " " + tags : "");
        if (ap && P.lblAp) t += " A+";
        if (mbd !== 0 && mbd === d) t += mbd > 0 ? " ↑" : " ↓";
        return t;
    }
    mbOf(f) { return f ? f.mbD : 0; }
    deKill(fv) {
        const f = fv.dup ? fv.dup : fv;
        if (this.P.deDead === "backToFVG") {
            this.restore(f);
        } else {
            f.kept = false;
            f.cand = false;
            if (f.rect) {
                const dc = col(this.P.deDeadCol, 85);
                f.rect.bg = dc;
                f.rect.border = dc;
                f.rect.bw = 1;
                f.rect.text = "";
                f.rect.right = this.j;
                f.rect.frozen = true;
            }
        }
    }
    deRecolor(b, d) {
        if (b) {
            const cc = d < 0 ? this.P.bearIFVGColor : this.P.bullIFVGColor;
            b.bg = col(cc, this.P.ifvgTrans);
            b.border = col(cc, this.P.borderTrans);
        }
    }

    findFVG(sTime, fvgs, site) {
        const sz = fvgs.length;
        if (sz > 0) {
            for (let k = sz - 1; k >= 0; k--) {
                this.findRf[site] = fvgs[k];
                if (this.findRf[site].start < sTime) break;
            }
        }
        return this.findRf[site];
    }

    // ---------------------------------------------------------------------------------- OTE
    newPivSet(htf) { return { hb: [], hp: [], lb: [], lp: [], tag: "", htf }; }
    addHigh(ps, b, p) { ps.hb.push(b); ps.hp.push(p); if (ps.hb.length > 60) { ps.hb.shift(); ps.hp.shift(); } }
    addLow(ps, b, p) { ps.lb.push(b); ps.lp.push(p); if (ps.lb.length > 60) { ps.lb.shift(); ps.lp.shift(); } }
    lastHigh(ps) { return ps.hp.length > 0 ? ps.hp[ps.hp.length - 1] : null; }
    lastLow(ps) { return ps.lp.length > 0 ? ps.lp[ps.lp.length - 1] : null; }
    priorLeg(dir, sbar, ps) {
        const j = this.j;
        let h = null, l = null;
        if (dir === -1) {
            for (let k = ps.hb.length - 1; k >= 0; k--) {
                const hb = ps.hb[k];
                if (hb < sbar && j - hb < 4000) {
                    let lo = this.L[hb];
                    for (let q = hb; q <= sbar; q++) lo = Math.min(lo, this.L[q]);
                    h = ps.hp[k]; l = lo;
                    break;
                }
            }
        } else {
            for (let k = ps.lb.length - 1; k >= 0; k--) {
                const lb = ps.lb[k];
                if (lb < sbar && j - lb < 4000) {
                    let hi = this.H[lb];
                    for (let q = lb; q <= sbar; q++) hi = Math.max(hi, this.H[q]);
                    l = ps.lp[k]; h = hi;
                    break;
                }
            }
        }
        return [h, l];
    }
    newTrack() {
        return { dir: 0, zlo: null, zhi: null, legHi: null, legLo: null, rext: null, armed: false, fired: false,
            ctime: null, cbar: null, band: null, pHi: null, pLo: null, sbar: null, rejHit: false, rej: null, tag: "" };
    }
    newGroup() { return { t0: this.newTrack(), t1: this.newTrack(), t2: this.newTrack(), flagged: false, tags: "" }; }
    oteStart(t, dir, zlo, zhi, origin, sbar, ps) {
        this.g.del(t.band); t.band = null;
        this.g.del(t.rej); t.rej = null;
        t.rejHit = false;
        t.sbar = sbar;
        t.tag = ps.tag;
        const [pgh, pgl] = this.priorLeg(dir, sbar, ps);
        t.pHi = pgh; t.pLo = pgl;
        let o = origin;
        if (ps.htf) {
            const adj = dir === -1 ? this.lastHigh(ps) : this.lastLow(ps);
            if (adj !== null) o = dir === -1 ? Math.max(origin, adj) : Math.min(origin, adj);
        }
        t.dir = dir; t.zlo = zlo; t.zhi = zhi;
        t.armed = false; t.fired = false; t.ctime = null;
        t.rext = origin;
        t.legHi = dir === -1 ? o : null;
        t.legLo = dir === 1 ? o : null;
    }
    // Pine math.max / math.min with na -> na
    nmax(a, b) { return a === null || b === null ? null : Math.max(a, b); }
    nmin(a, b) { return a === null || b === null ? null : Math.min(a, b); }
    oteConfirm(t) {
        const P = this.P, j = this.j;
        if (t.dir !== 0 && !t.armed) {
            if (P.useOTE && P.oteRej && t.pHi !== null && t.pLo !== null && t.pHi > t.pLo) {
                const pr = t.pHi - t.pLo;
                let ext, rLo, rHi, hit = false;
                if (t.dir === -1) {
                    ext = P.oteRejAny ? this.nmax(t.rext, this.h) : this.h;
                    rLo = t.pLo + P.oteMin * pr;
                    rHi = t.pLo + P.oteMax * pr;
                    hit = ext !== null && ext >= rLo && ext <= (P.oteRejCap ? rHi : t.pHi) && this.c <= rHi;
                } else {
                    ext = P.oteRejAny ? this.nmin(t.rext, this.l) : this.l;
                    rLo = t.pHi - P.oteMax * pr;
                    rHi = t.pHi - P.oteMin * pr;
                    hit = ext !== null && ext <= rHi && ext >= (P.oteRejCap ? rLo : t.pLo) && this.c >= rLo;
                }
                if (hit) {
                    t.rejHit = true;
                    if (P.showOTEband) {
                        t.rej = this.g.box({ rel: true, relL: P.oteOffset, relR: P.oteOffset + P.oteWidth,
                            left: j + P.oteOffset, right: j + P.oteOffset + P.oteWidth, top: rHi, bottom: rLo,
                            bg: col(P.oteBandCol, 85), border: col(P.oteHighlight, 0), bstyle: "dashed", bw: 1,
                            text: t.tag + " OTE" + (t.dir === 1 ? " ↑" : " ↓"), tsize: "small",
                            textCol: col(P.oteTextCol, 0), halign: "center" });
                    }
                }
            }
            t.armed = true;
            t.fired = t.rejHit;
            t.ctime = j;
            t.cbar = j;
            if (t.dir === -1) { t.legHi = this.nmax(t.legHi, this.h); t.legLo = this.l; }
            else { t.legLo = this.nmin(t.legLo, this.l); t.legHi = this.h; }
        }
    }
    oteCancel(t) {
        this.g.del(t.band); t.band = null;
        this.g.del(t.rej); t.rej = null;
        t.rejHit = false;
        t.armed = false;
        if (t.dir === -1) t.legLo = null; else t.legHi = null;
    }
    oteStep(t) {
        const P = this.P, j = this.j;
        let s = 0;
        if (P.useOTE && t.dir !== 0) {
            if (!t.armed) {
                if (t.dir === -1) { t.legHi = this.nmax(t.legHi, this.h); t.rext = this.nmax(t.rext, this.h); }
                else { t.legLo = this.nmin(t.legLo, this.l); t.rext = this.nmin(t.rext, this.l); }
            } else {
                if (t.rej) { t.rej.left = j + P.oteOffset; t.rej.right = j + P.oteOffset + P.oteWidth; }
                if (t.rejHit) { s = t.dir; t.rejHit = false; }
                if (t.legHi !== null && t.legLo !== null) {
                    const r = t.legHi - t.legLo;
                    if (r > 0) {
                        let bLo, bHi;
                        if (t.dir === -1) { bLo = t.legLo + P.oteMin * r; bHi = t.legLo + P.oteMax * r; }
                        else { bLo = t.legHi - P.oteMax * r; bHi = t.legHi - P.oteMin * r; }
                        const mid = avg(t.zlo, t.zhi);
                        let m;
                        if (P.oteMode === "overlap") m = t.zhi >= bLo && t.zlo <= bHi;
                        else if (P.oteMode === "fullyInside") m = t.zlo >= bLo && t.zhi <= bHi;
                        else m = mid >= bLo && mid <= bHi;
                        if (P.showOTEband) {
                            const bg = m ? col(P.oteBandCol, 85) : col(TV.gray, 92);
                            const bd = m ? col(P.oteHighlight, 0) : col(TV.gray, 60);
                            const txt = t.tag + (t.dir === 1 ? " ↑" : " ↓");
                            if (!t.band) {
                                t.band = this.g.box({ rel: true, relL: P.oteOffset, relR: P.oteOffset + P.oteWidth,
                                    left: j + P.oteOffset, right: j + P.oteOffset + P.oteWidth, top: bHi, bottom: bLo,
                                    bg, border: bd, bstyle: "dashed", bw: 1, text: txt, tsize: "tiny",
                                    textCol: col(P.oteTextCol, 0), halign: "left" });
                            } else {
                                Object.assign(t.band, { top: bHi, bottom: bLo, left: j + P.oteOffset,
                                    right: j + P.oteOffset + P.oteWidth, bg, border: bd, text: txt });
                            }
                        }
                        const tap = t.dir === -1 ? this.h >= t.zlo : this.l <= t.zhi;
                        if (m && tap && !t.fired && j !== t.ctime) { t.fired = true; s = t.dir; }
                    }
                }
                if (t.dir === -1) t.legLo = this.nmin(t.legLo, this.l);
                else t.legHi = this.nmax(t.legHi, this.h);
            }
        }
        return s;
    }
    gStart(g, dir, zlo, zhi, origin, sbar) {
        g.flagged = false;
        g.tags = "";
        this.oteStart(g.t0, dir, zlo, zhi, origin, sbar, this.pv0);
        if (this.hasH1) this.oteStart(g.t1, dir, zlo, zhi, origin, sbar, this.pv1);
        if (this.hasH2) this.oteStart(g.t2, dir, zlo, zhi, origin, sbar, this.pv2);
    }
    gConfirm(g) { this.oteConfirm(g.t0); this.oteConfirm(g.t1); this.oteConfirm(g.t2); }
    gCancel(g) {
        g.flagged = false;
        g.tags = "";
        this.oteCancel(g.t0); this.oteCancel(g.t1); this.oteCancel(g.t2);
    }
    gStep(g) {
        const s0 = this.oteStep(g.t0), s1 = this.oteStep(g.t1), s2 = this.oteStep(g.t2);
        let s = 0;
        if (!g.flagged && (s0 !== 0 || s1 !== 0 || s2 !== 0)) {
            g.flagged = true;
            s = s0 !== 0 ? s0 : (s1 !== 0 ? s1 : s2);
            let tg = "";
            if (s0 !== 0) tg += g.t0.tag;
            if (s1 !== 0) tg += (tg === "" ? "" : "+") + g.t1.tag;
            if (s2 !== 0) tg += (tg === "" ? "" : "+") + g.t2.tag;
            g.tags = tg;
        }
        return s;
    }

    // ---------------------------------------------------------------------- breakers / mitigation blocks
    // mb = false: breakers ([BRK]), true: mitigation blocks ([MB]); the only difference is the formation
    zoneBlock(mb, PH, PL) {
        const P = this.P, j = this.j;
        const z = mb ? this.mz : this.bz;
        const arr = mb ? this.mbs : this.brks;
        const len = mb ? P.mbLen : P.brkLen;
        const keep = mb ? P.mbKeep : P.brkKeep;
        const seq = (mb ? P.mbMode : P.brkMode) === "sequence";
        const { h, l, c } = this;
        if (PL !== null) {
            z.prevL = z.lastL; z.prevLb = z.lastLb;
            z.lastL = PL; z.lastLb = j - len;
            // breaker: lower low / mitigation block: higher low, with the last pivot high between them
            if (z.prevL !== null && (mb ? z.lastL > z.prevL : z.lastL < z.prevL) && z.lastHb !== null &&
                z.lastHb > z.prevLb && z.lastHb < z.lastLb) {
                const r = this.zoneOf(1, j - z.lastHb, z.prevLb, seq);
                if (r) this.zoneAdd(arr, keep, 1, r);
            }
        }
        if (PH !== null) {
            z.prevH = z.lastH; z.prevHb = z.lastHb;
            z.lastH = PH; z.lastHb = j - len;
            if (z.prevH !== null && (mb ? z.lastH < z.prevH : z.lastH > z.prevH) && z.lastLb !== null &&
                z.lastLb > z.prevHb && z.lastLb < z.lastHb) {
                const r = this.zoneOf(-1, j - z.lastLb, z.prevHb, seq);
                if (r) this.zoneAdd(arr, keep, -1, r);
            }
        }
        for (let k = arr.length - 1; k >= 0; k--) {
            const b = arr[k];
            if (b.tagged) {
                if ((b.dir === 1 && l < b.lo) || (b.dir === -1 && h > b.hi)) {
                    this.zoneDelDraw(b);
                    arr.splice(k, 1);
                } else if ((b.dir === 1 && c > b.hi) || (b.dir === -1 && c < b.lo)) {
                    arr.splice(k, 1);
                }
            } else if (!b.broken) {
                if ((b.dir === 1 && c > b.hi) || (b.dir === -1 && c < b.lo)) { b.broken = true; b.bkBar = j; }
            } else if (!b.swung) {
                if (b.dir === 1 && PH !== null && j - len >= b.bkBar && PH > b.hi) b.swung = true;
                else if (b.dir === -1 && PL !== null && j - len >= b.bkBar && PL < b.lo) b.swung = true;
                else if ((b.dir === 1 && c < b.lo) || (b.dir === -1 && c > b.hi)) arr.splice(k, 1);
            } else if ((b.dir === 1 && c < b.lo) || (b.dir === -1 && c > b.hi)) {
                arr.splice(k, 1);
            } else if ((b.dir === 1 && l <= b.hi) || (b.dir === -1 && h >= b.lo)) {
                const tag = mb ? (f, bb, ib, which) => this.mbTag(f, bb, ib, which) : (f, bb, ib) => this.brkTag(f, bb, ib);
                const fs0 = b.dir === 1 ? this.lfvgs : this.sfvgs;
                for (const f of fs0) if (f.end === null) tag(f, b, null, 0);
                if (b.dir === 1) {
                    if (!P.ifvgswings && this.bullconfirm !== null) tag(this.bullifvg, b, this.ifvgBox, 1);
                    if (P.ifvgswings && this.ifsdir === 1 && this.ifsconfirm !== null) tag(this.swFvg, b, this.ifvgBox, 2);
                } else {
                    if (!P.ifvgswings && this.bearconfirm !== null) tag(this.bearifvg, b, this.ifvgBoxb, -1);
                    if (P.ifvgswings && this.ifsdir === -1 && this.ifsconfirm !== null) tag(this.swFvg, b, this.ifvgBox, 2);
                }
                if (b.tagged && ((b.dir === 1 && l < b.lo) || (b.dir === -1 && h > b.hi))) {
                    this.zoneDelDraw(b);
                    arr.splice(k, 1);
                }
            }
        }
    }
    zoneOf(dir, k0, from, seq) {
        const j = this.j;
        let zh = null, zl = null, zt = null, zt1 = null;
        const kmax = Math.min(j - from, 300);
        let k = k0;
        while (k <= kmax && zt === null) {
            if (j - k >= 0 && (this.C[j - k] - this.O[j - k]) * dir > 0) {
                zh = this.H[j - k]; zl = this.L[j - k]; zt = j - k; zt1 = j - k;
            }
            k += 1;
        }
        if (zt !== null && seq) {
            while (k <= kmax && j - k >= 0 && (this.C[j - k] - this.O[j - k]) * dir > 0) {
                zh = Math.max(zh, this.H[j - k]); zl = Math.min(zl, this.L[j - k]); zt = j - k;
                k += 1;
            }
        }
        return zt === null ? null : { hi: zh, lo: zl, t: zt, t1: zt1 };
    }
    zoneAdd(arr, keep, dir, r) {
        arr.push({ dir, hi: r.hi, lo: r.lo, t: r.t, t1: r.t1, broken: false, bkBar: null, swung: false, tagged: false, bx: null, ml: null, fl: null });
        let cnt = 0;
        for (const b of arr) if (b.dir === dir) cnt++;
        if (cnt > keep) {
            for (let k = 0; k < arr.length; k++) if (arr[k].dir === dir) { arr.splice(k, 1); break; }
        }
    }
    zoneDelDraw(b) {
        if (b.bx) { this.g.del(b.bx); this.g.del(b.ml); }
        if (b.fl) for (const ln of b.fl) this.g.del(ln);
    }
    // draw the zone once (rectangle / shadow rectangle, mid line, flagged candles)
    zoneDraw(b, mb) {
        const P = this.P, j = this.j;
        const colr = mb ? P.mbCol : P.brkCol;
        const sty = mb ? P.mbSty : P.brkSty;
        const w = mb ? P.mbW : P.brkW;
        const shd = mb ? P.mbShd : P.brkShd;
        const mid = (b.hi + b.lo) / 2;
        b.tagged = true;
        if (mb ? P.mbFlag : P.brkFlag) {
            b.fl = [];
            let kk = 0;
            while (kk <= 300 && j - kk >= 0 && j - kk >= b.t) {
                const x = j - kk;
                if (x <= b.t1) {
                    b.fl.push(this.g.line({ x1: x, y1: this.H[x], x2: x, y2: this.L[x], color: col(colr, 0), width: 1 }));
                    b.fl.push(this.g.line({ x1: x, y1: this.O[x], x2: x, y2: this.C[x], color: col(colr, 0), width: 5 }));
                }
                kk += 1;
            }
        }
        if ((mb ? P.mbBox : P.brkBox) || shd) {
            b.bx = this.g.box({ left: b.t, top: b.hi, right: j, bottom: b.lo, border: shd ? col(TV.gray, 100) : col(colr, 0),
                bstyle: sty, bw: shd ? 0 : w, bg: col(shd ? TV.gray : colr, shd ? 80 : 100), fixed: true });
            b.ml = this.g.line({ x1: b.t, y1: mid, x2: j, y2: mid, color: col(colr, 0), style: sty, width: w, fixed: true });
        }
    }
    brkTag(f, b, ib) {
        const P = this.P;
        if (f && f.hi >= b.lo && f.lo <= b.hi) {
            f.brk = true;
            f.brkD = b.dir;
            if (ib) ib.bold = true;
            else if (f.end === null && !f.cand && !f.kept && !(f.aplus && P.lblAp) && f.rect) {
                f.rect.text = f.mbD !== 0 ? (b.dir === 1 ? "Mb+" : "Mb-") : (b.dir === 1 ? "b+" : "b-");
                this.textFmt(f.rect);
            }
            if (!b.tagged) this.zoneDraw(b, false);
        }
    }
    mbTxt(which, mbd) {
        const P = this.P;
        return which === 1 ? this.ifvgTxt(1, P.deInv ? this.bullInv : 1, P.useOTE && this.gBull.flagged, this.gBull.tags, this.bullAp, mbd)
            : which === -1 ? this.ifvgTxt(-1, P.deInv ? this.bearInv : 1, P.useOTE && this.gBear.flagged, this.gBear.tags, this.bearAp, mbd)
                : this.ifvgTxt(this.ifsdir, 1, P.useOTE && this.gSw.flagged, this.gSw.tags, this.swAp, mbd);
    }
    mbTag(f, b, ib, which) {
        const P = this.P;
        if (f && f.hi >= b.lo && f.lo <= b.hi) {
            if (ib) {
                if (f.mbD !== b.dir) {
                    f.mbD = b.dir;
                    ib.text = this.mbTxt(which, f.mbD);
                }
                ib.bold = true;
            } else {
                f.mbD = b.dir;
                if (f.end === null && !f.cand && !f.kept && !(f.aplus && P.lblAp) && f.rect) {
                    f.rect.text = f.brk ? (b.dir === 1 ? "Mb+" : "Mb-") : (b.dir === 1 ? "M+" : "M-");
                    this.textFmt(f.rect);
                }
            }
            if (!b.tagged) this.zoneDraw(b, true);
        }
    }
    ifvgInherit(iv, ib, which, dir) {
        const P = this.P;
        if (iv && ib) {
            const fs = dir === 1 ? this.lfvgs : this.sfvgs;
            let gb = false, gm = false;
            for (const f of fs) {
                if ((f.end === null || f.end >= iv.start) && f.hi >= iv.lo && f.lo <= iv.hi) {
                    if (f.brkD === dir && P.brkOn) gb = true;
                    if (f.mbD === dir && P.mbOn) gm = true;
                }
            }
            if (gb && !iv.brk) { iv.brk = true; ib.bold = true; }
            if (gm && iv.mbD !== dir) {
                iv.mbD = dir;
                ib.text = this.mbTxt(which, dir);
                ib.bold = true;
            }
        }
    }

    // ================================================================================= rendering
    render(now) {
        const P = this.P;
        const fills = [], lines = [], texts = [];
        const last = this.lastJ;
        const lineItem = (key, x1, y1, x2, y2, c, width, style, infEnd) => {
            if (!c || c.a <= 0) return;
            lines.push({
                tag: "LineSegments", key, global: true,
                lines: [{ tag: "Line", a: { x: du(x1), y: du(y1) }, b: { x: du(x2), y: du(y2) }, infiniteStart: false, infiniteEnd: !!infEnd }],
                lineStyle: { lineWidth: width, color: c.c, opacity: c.a * OPACITY_SCALE, lineStyle: lsOf(style) }
            });
        };
        for (const b of this.g.boxes.values()) {
            let left = b.left, right = b.right;
            if (b.rel) { left = now + b.relL; right = now + b.relR; }
            else if (!b.fixed && !b.frozen && right === last) right = now;
            const ext = b.extend === "right";
            const rr = ext ? Math.max(right, now + EXT_BARS) : right;
            const top = Math.max(b.top, b.bottom), bot = Math.min(b.top, b.bottom);
            if (b.bg && b.bg.a > 0) {
                fills.push({
                    tag: "Shapes", key: "bf" + b.id, global: true,
                    primitives: [{ tag: "Rectangle", position: { x: du(left), y: du(top) }, size: { width: du(Math.max(rr - left, 0.3)), height: du(top - bot) } }],
                    fillStyle: { color: b.bg.c, opacity: b.bg.a * OPACITY_SCALE }
                });
            }
            if (b.border && b.border.a > 0 && b.bw > 0) {
                const c = b.border, ls = { lineWidth: b.bw, color: c.c, opacity: c.a * OPACITY_SCALE, lineStyle: lsOf(b.bstyle) };
                const segs = [
                    { tag: "Line", a: { x: du(left), y: du(top) }, b: { x: du(right), y: du(top) }, infiniteStart: false, infiniteEnd: ext },
                    { tag: "Line", a: { x: du(left), y: du(bot) }, b: { x: du(right), y: du(bot) }, infiniteStart: false, infiniteEnd: ext },
                    { tag: "Line", a: { x: du(left), y: du(top) }, b: { x: du(left), y: du(bot) }, infiniteStart: false, infiniteEnd: false }
                ];
                if (!ext) segs.push({ tag: "Line", a: { x: du(right), y: du(top) }, b: { x: du(right), y: du(bot) }, infiniteStart: false, infiniteEnd: false });
                lines.push({ tag: "LineSegments", key: "bb" + b.id, global: true, lines: segs, lineStyle: ls });
            }
            if (b.text && b.textCol && b.textCol.a > 0) {
                const xr = ext ? now : right;
                const x = b.halign === "left" ? left : b.halign === "right" ? xr : (left + xr) / 2;
                const y = b.valign === "top" ? top : b.valign === "bottom" ? bot : (top + bot) / 2;
                const ha = b.halign === "left" ? "left" : b.halign === "right" ? "right" : "center";
                const va = b.valign === "top" ? "Top" : b.valign === "bottom" ? "Bottom" : "Middle";
                texts.push({
                    tag: "Text", key: "bt" + b.id, global: true, point: { x: du(x), y: du(y) }, text: b.text,
                    style: { fontSize: FONT[b.tsize] || 11, fontWeight: b.bold ? "bold" : "normal", fill: b.textCol.c },
                    textAlignment: ha + va
                });
            }
        }
        for (const ln of this.g.lines.values()) {
            let x2 = ln.x2;
            if (!ln.fixed && x2 === last && ln.x1 !== x2) x2 = now;
            lineItem("ln" + ln.id, ln.x1, ln.y1, x2, ln.y2, ln.color, ln.width, ln.style, ln.extend === "right");
        }
        for (const lb of this.g.labels.values()) {
            texts.push({
                tag: "Text", key: "lb" + lb.id, global: true, point: { x: du(lb.x), y: du(lb.y) }, text: lb.text,
                style: { fontSize: 9, fontWeight: "bold", fill: TV.blue }, textAlignment: lb.up ? "centerTop" : "centerBottom"
            });
        }
        if (P.deDispBar) {
            const c = col(P.deDispCol, 0);
            for (const x of this.dispBars) {
                lineItem("dw" + x, x, this.H[x], x, this.L[x], c, 1, "solid", false);
                lineItem("db" + x, x, this.O[x], x, this.C[x], c, 5, "solid", false);
            }
        }
        for (const mk of this.markers) {
            const long = mk.dir > 0;
            const c = mk.ote ? P.oteHighlight : (long ? P.bullIFVGColor : P.bearIFVGColor);
            texts.push({
                tag: "Text", key: (mk.ote ? "mo" : "ms") + mk.x, global: true, point: { x: du(mk.x), y: du(mk.y) },
                text: mk.ote ? (long ? "◆\nOTE" : "OTE\n◆") : (long ? "▲\nLONG" : "SHORT\n▼"),
                style: { fontSize: 11, fontWeight: "bold", fill: c }, textAlignment: long ? "centerTop" : "centerBottom"
            });
        }
        return fills.concat(lines, texts);
    }
}

// ---- plotter: the plots carry the alert conditions; they are not drawn (overlay price scale)
function noDraw() { /* signals are exposed as values only */ }
const plotter = (predef.plotters && typeof predef.plotters.custom === "function") ? [predef.plotters.custom(noDraw)] : [];

module.exports = {
    name: "fvgsifvgote",
    description: "FVGs+IFVG+OTE",
    calculator: FVGSIFVGOTE,
    params: buildParamSpecs(),
    inputType: meta.InputType.BARS,
    areaChoice: meta.AreaChoice.OVERLAY,
    plots: {
        signal: { title: "IFVG signal (+1 long / -1 short)" },
        oteSignal: { title: "IFVG+OTE signal (+1 long / -1 short)" },
        bounce: { title: "FVG / wick C.E. bounce" }
    },
    plotter,
    tags: ["ICT"]
};
