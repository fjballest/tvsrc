/*
 * ICTcotas for Tradovate (Code Explorer custom indicator, JavaScript)
 * ----------------------------------------------------------------------------
 * Daily, weekly and monthly Fair Value Gaps (FVG / IFVG), Volume Imbalances (VI)
 * and wick CE levels, plus FVG boxes (and optional wick CEs) of the 15m / 1H /
 * 4H / D / W / M candles. Each FVG / VI is drawn with its high, low and CE
 * (midpoint). Wicks are grouped (consecutive candles with a qualifying wick on
 * the same side) and the CE of the longest wick of the group is drawn. FVGs
 * closed through become IFVGs. Mitigated levels are removed.
 *
 * Port of the TradingView Pine Script v6 indicator ICTcotas.txt
 * (md5 3636809354c6e1a528daa558a99d3b6c), (c) franjballest, Mozilla Public
 * License 2.0 (https://mozilla.org/MPL/2.0/). Same logic, same inputs (as
 * camelCase parameters) and the same defaults.
 *
 * Install (Tradovate web / desktop / NinjaTrader web platform):
 *   1. Open Code Explorer (application menu > Code Explorer).
 *   2. Create a new indicator, delete the template code, paste this whole file.
 *   3. Save. "ICTcotas" then appears in the chart's indicator list (tag "ICT").
 *   Use it on an intraday time-based chart (1m .. 4H) of a CME future.
 *
 * Differences from the Pine version that could not be avoided:
 *   - There is no request.security. Higher timeframe candles are built from
 *     the chart bars by timestamp: trading days start at sessionStartHour
 *     (default 18:00) America/New_York, weeks on Sunday at that hour, months
 *     by the calendar month of the trading day, 4H candles aligned to the
 *     session start, 1H / 15m to the clock. They therefore only exist inside
 *     the loaded chart history, the first (usually partial) candle of each
 *     timeframe is discarded, and a candle's OHLC is that of the chart bars it
 *     contains (missing bars or a different exchange session change it).
 *     As in Pine (lookahead_on with [1],[2],[3]) a candle triple is processed
 *     when the first chart bar of the next candle arrives.
 *   - Chart objects are placed by bar index instead of xloc.bar_time; the
 *     right edge is "offset" bars after the last bar, as in Pine.
 *   - Line styles map to Tradovate dash patterns (dashed = 5-2, dotted = 2-2),
 *     label sizes to font sizes (tiny 7 .. huge 18), label styles to text
 *     alignments. Boxes are drawn as a filled polygon plus outline segments.
 *   - The bottom-right warning table is a text drawn next to the last bar.
 *   - Option lists are enums with short keys; integer maxima (keep <= 20,
 *     widths 1..3, transparency <= 100, ...) are enforced by clamping, because
 *     Tradovate parameter specs only carry a minimum.
 *   - The chart timeframe comes from chartDescription (minute / daily bars);
 *     on tick, volume or range charts it is taken as below every level
 *     timeframe. Without chartDescription it is the smallest bar spacing seen.
 *   - Graphics are returned only for the last bar (global items). A forming
 *     bar is evaluated on a copy of the state, so inversion / mitigation on
 *     the live bar is provisional and final when the bar closes, as in Pine.
 *   - No 500 object limit; nothing is deleted by the platform.
 *   - sessionStartHour is an extra parameter (no Pine equivalent).
 */

const predef = require("./tools/predef");
const meta = require("./tools/meta");
const { px, du, op } = require("./tools/graphics");

// ============================================================ constants
const HOUR = 3600000;
const DAY = 86400000;
const MF_SECS = [900, 3600, 14400, 86400, 604800, 2628003];

// only timeframes above the chart are used, never the chart's own
function mfOk(s, tfs) {
    return s > tfs;
}
const MF_NAMES = ["15m", "1H", "4H", "D", "W", "M"];
const FONT = { tiny: 7, small: 9, normal: 11, large: 14, huge: 18 };
const DASH = { solid: 1, dashed: 4, dotted: 3 };
const STYLES = { solid: "solid", dashed: "dashed", dotted: "dotted" };
const SIZES = { tiny: "tiny", small: "small", normal: "normal", large: "large", huge: "huge" };

// ============================================================ time helpers
// America/New_York UTC offset (US DST rules since 2007), in ms
function nthSundayUtc(year, month, n) {
    const first = new Date(Date.UTC(year, month, 1)).getUTCDay();
    const day = 1 + ((7 - first) % 7) + (n - 1) * 7;
    return Date.UTC(year, month, day);
}
function nyOffset(utc) {
    const y = new Date(utc).getUTCFullYear();
    const start = nthSundayUtc(y, 2, 2) + 7 * HOUR;   // 2:00 EST
    const end = nthSundayUtc(y, 10, 1) + 6 * HOUR;    // 2:00 EDT
    return utc >= start && utc < end ? -4 * HOUR : -5 * HOUR;
}

// Key functions of the higher timeframe candles. Two timestamps belong to the
// same candle when their keys are equal.
function makeKeyFns(sessHour) {
    // session start -> midnight of the trading day (an evening start belongs to the next date)
    const shift = (sessHour >= 12 ? 24 - sessHour : -sessHour) * HOUR;
    const loc = t => t + nyOffset(t);
    const tday = t => Math.floor((loc(t) + shift) / DAY);
    return {
        m15: t => Math.floor(loc(t) / (15 * 60000)),
        h1: t => Math.floor(loc(t) / HOUR),
        h4: t => Math.floor((loc(t) + shift) / (4 * HOUR)),
        d: tday,
        w: t => Math.floor((tday(t) + 3) / 7),   // Monday-based weeks of trading days
        m: t => {
            const dt = new Date(tday(t) * DAY);
            return dt.getUTCFullYear() * 12 + dt.getUTCMonth();
        }
    };
}

// ============================================================ HTF aggregation
// Builds higher timeframe candles from chart bars. push() returns true when the
// bar opens a new candle, i.e. the previous candle has just completed. done[0]
// is the newest completed candle (Pine [1]), done[1] is [2], done[2] is [3].
// Each candle carries the bar index of its first chart bar as t.
class HtfAgg {
    constructor(keyFn) {
        this.keyFn = keyFn;
        this.cur = null;
        this.done = [];
    }
    push(idx, b, barMs) {
        const k = this.keyFn(b.t);
        if (this.cur === null) {
            // the first candle is complete only if the first bar opens it
            const partial = !(barMs > 0 && this.keyFn(b.t - barMs) !== k);
            this.cur = { k, o: b.o, h: b.h, l: b.l, c: b.c, t: idx, partial };
            return false;
        }
        if (k !== this.cur.k) {
            if (!this.cur.partial) {
                this.done.unshift(this.cur);
                if (this.done.length > 3) this.done.pop();
            }
            this.cur = { k, o: b.o, h: b.h, l: b.l, c: b.c, t: idx, partial: false };
            return true;
        }
        const c = this.cur;
        if (b.h > c.h) c.h = b.h;
        if (b.l < c.l) c.l = b.l;
        c.c = b.c;
        return false;
    }
    // completed candle n (1 newest .. 3), or an all-NaN candle (Pine na)
    get(n) {
        return this.done[n - 1] || { o: NaN, h: NaN, l: NaN, c: NaN, t: NaN };
    }
    clone() {
        const a = new HtfAgg(this.keyFn);
        a.cur = this.cur ? Object.assign({}, this.cur) : null;
        a.done = this.done.slice();
        return a;
    }
}

// ============================================================ state
function newState(keys) {
    return {
        lvls: [],
        wcur: new Array(16).fill(null),   // open wick group level: tfi * 2 + side
        uid: 0,
        tfsecs: 0,
        aggs: {
            d: new HtfAgg(keys.d), w: new HtfAgg(keys.w), m: new HtfAgg(keys.m),
            m15: new HtfAgg(keys.m15), h1: new HtfAgg(keys.h1), h4: new HtfAgg(keys.h4)
        }
    };
}

function cloneState(S) {
    const map = new Map();
    const lvls = S.lvls.map(l => {
        const c = Object.assign({}, l);
        map.set(l, c);
        return c;
    });
    const wcur = S.wcur.map(l => (l ? map.get(l) || Object.assign({}, l) : null));
    const aggs = {};
    for (const k in S.aggs) aggs[k] = S.aggs[k].clone();
    return { lvls, wcur, uid: S.uid, tfsecs: S.tfsecs, aggs };
}

// ============================================================ engine
// Detection and maintenance on one state (Pine: addLvl, fvgOk, wickStep,
// addFvgs, addWicks, processTf, runMit and the per-bar pipeline).
class Engine {
    constructor(cfg, S) {
        this.cfg = cfg;
        this.S = S;
    }

    tfsecs() {
        return this.cfg.fixedTfSecs > 0 ? this.cfg.fixedTfSecs : this.S.tfsecs;
    }

    mfEnabled(i) {
        const c = this.cfg;
        const tfs = this.tfsecs();
        const s = MF_SECS[i];
        let on = c.doMf && mfOk(s, tfs) && c.mfSel[i];
        if (c.doMf && s > tfs && c.mfNext > 0) {
            let rank = 0;
            for (let j = 0; j < i; j++) if (MF_SECS[j] > tfs) rank++;
            if (rank < c.mfNext) on = true;
        }
        return on;
    }

    wkEnabled(i) {
        const c = this.cfg;
        return c.doWk && mfOk(MF_SECS[i], this.tfsecs()) && ((i === 1 && c.wk1h) || (i === 2 && c.wk4h));
    }

    kill(l) {
        l.dead = true;
    }

    // overlapping FVGs of the same kind, timeframe and direction
    fvgOk(kind, tfi, dir, lo, hi) {
        const c = this.cfg;
        const lv = this.S.lvls;
        let ok = true;
        if (c.fvgOvl !== "all") {
            if (c.fvgOvl === "enclosing") {
                for (const o of lv) {
                    if (o.kind === kind && o.tfi === tfi && o.dir === dir && !o.inv && !o.dead) {
                        if (lo <= o.hi && hi >= o.lo && o.lo <= lo && o.hi >= hi) ok = false;
                    }
                }
            }
            if (ok) {
                for (let i = lv.length - 1; i >= 0; i--) {
                    const o = lv[i];
                    if (o.kind === kind && o.tfi === tfi && o.dir === dir && !o.inv && !o.dead) {
                        if (lo <= o.hi && hi >= o.lo) {
                            this.kill(o);
                            lv.splice(i, 1);
                        }
                    }
                }
            }
        }
        return ok;
    }

    // returns null when an FVG is discarded
    addLvl(kind, tfi, dir, lo, hi, t) {
        const c = this.cfg;
        const lv = this.S.lvls;
        if (!((kind !== 0 && kind !== 3) || this.fvgOk(kind, tfi, dir, lo, hi))) return null;
        const l = {
            id: ++this.S.uid, kind, tfi, dir, hi, lo, ce: (hi + lo) / 2,
            t: Math.max(t, 0), wsize: NaN, inv: false, dead: false, tfm: null
        };
        lv.push(l);
        const keep = kind === 0 ? c.nFvg : kind === 1 ? c.nVi : kind === 3 ? c.nMf : c.nWk;
        let cnt = 0;
        for (const v of lv) if (v.kind === kind && v.tfi === tfi && !v.dead) cnt++;
        if (cnt > keep) {
            for (let i = 0; i < lv.length; i++) {
                const o = lv[i];
                if (o.kind === kind && o.tfi === tfi && !o.dead) {
                    this.kill(o);
                    lv.splice(i, 1);
                    break;
                }
            }
        }
        return l;
    }

    // one step of the wick group state machine (side 0 upper wick, 1 lower wick)
    wickStep(tfi, side, q, sz, lo, hi, t) {
        const W = this.S.wcur;
        const idx = tfi * 2 + side;
        const cur = W[idx];
        if (!q) {
            W[idx] = null;
        } else if (cur === null || (cur.dead && sz > cur.wsize)) {
            const n = this.addLvl(2, tfi, side === 0 ? -1 : 1, lo, hi, t);
            n.wsize = sz;
            W[idx] = n;
        } else if (sz > cur.wsize) {
            cur.lo = lo;
            cur.hi = hi;
            cur.ce = (lo + hi) / 2;
            cur.t = Math.max(t, 0);
            cur.wsize = sz;
        }
    }

    // FVGs of one completed candle triple (1 newest, 3 oldest), with the VI
    // extension: any body gap between candle 2 and candle 1 or 3 widens the range
    addFvgs(kind, tfi, minsz, k1, k2, k3, tform) {
        const c = this.cfg;
        const topP = Math.max(k2.o, k2.c), botP = Math.min(k2.o, k2.c);
        const topC = Math.max(k1.o, k1.c), botC = Math.min(k1.o, k1.c);
        const top3 = Math.max(k3.o, k3.c), bot3 = Math.min(k3.o, k3.c);
        if (k1.l > k3.h && k1.l - k3.h >= minsz) {
            let flo = k3.h, fhi = k1.l;
            if (c.fvgVi) {
                if (botP > top3 && botP - top3 >= c.viMin) {
                    flo = Math.min(flo, top3);
                    fhi = Math.max(fhi, botP);
                }
                if (botC > topP && botC - topP >= c.viMin) {
                    flo = Math.min(flo, topP);
                    fhi = Math.max(fhi, botC);
                }
            }
            const nb = this.addLvl(kind, tfi, 1, flo, fhi, k2.t);
            if (nb) nb.tfm = tform;
        }
        if (k1.h < k3.l && k3.l - k1.h >= minsz) {
            let flo = k1.h, fhi = k3.l;
            if (c.fvgVi) {
                if (topP < bot3 && bot3 - topP >= c.viMin) {
                    flo = Math.min(flo, topP);
                    fhi = Math.max(fhi, bot3);
                }
                if (topC < botP && botP - topC >= c.viMin) {
                    flo = Math.min(flo, topC);
                    fhi = Math.max(fhi, botP);
                }
            }
            const ns = this.addLvl(kind, tfi, -1, flo, fhi, k2.t);
            if (ns) ns.tfm = tform;
        }
    }

    // wick groups of one completed candle
    addWicks(tfi, k1) {
        const wkmin = this.cfg.wkMin;
        const topC = Math.max(k1.o, k1.c), botC = Math.min(k1.o, k1.c);
        const rng = k1.h - k1.l;
        const uw = k1.h - topC;
        const lw = botC - k1.l;
        const uq = rng > 0 && uw >= (wkmin / 100) * rng;
        const lq = rng > 0 && lw >= (wkmin / 100) * rng;
        this.wickStep(tfi, 0, uq, uw, topC, k1.h, k1.t);
        this.wickStep(tfi, 1, lq, lw, k1.l, botC, k1.t);
    }

    // one completed daily / weekly / monthly candle
    processTf(tfi, agg, barIdx) {
        const c = this.cfg;
        const k1 = agg.get(1), k2 = agg.get(2), k3 = agg.get(3);
        const topP = Math.max(k2.o, k2.c), botP = Math.min(k2.o, k2.c);
        const topC = Math.max(k1.o, k1.c), botC = Math.min(k1.o, k1.c);
        // no FVG / IFVG lines for D, W or M when the same timeframe is drawn as boxes (redundant)
        if (c.doFvg && !this.mfEnabled(tfi + 3)) this.addFvgs(0, tfi, c.fvgMin, k1, k2, k3, barIdx);
        if (c.doVi) {
            if (botC > topP && k1.l <= k2.h && botC - topP >= c.viMin) this.addLvl(1, tfi, 1, topP, botC, k1.t);
            if (topC < botP && k1.h >= k2.l && botP - topC >= c.viMin) this.addLvl(1, tfi, -1, topC, botP, k1.t);
        }
        if (c.doWk) this.addWicks(tfi, k1);
    }

    // FVG -> IFVG inversion and mitigation, every chart bar
    runMit(b) {
        const c = this.cfg;
        const lv = this.S.lvls;
        for (let i = lv.length - 1; i >= 0; i--) {
            const l = lv[i];
            if (l.dead) {
                lv.splice(i, 1);
                continue;
            }
            const fvgLive = (l.kind === 0 || l.kind === 3) && c.doIfvg && !l.inv;
            let flipped = false;
            if (fvgLive) {
                if ((l.dir === 1 && b.c < l.lo) || (l.dir === -1 && b.c > l.hi)) {
                    l.inv = true;
                    l.dir = -l.dir;
                    flipped = true;
                }
            }
            if (c.mmode > 0 && !fvgLive && !flipped) {
                let m;
                if (l.dir === 1) m = c.mmode === 1 ? b.l <= l.ce : c.mmode === 2 ? b.l <= l.lo : b.c < l.lo;
                else m = c.mmode === 1 ? b.h >= l.ce : c.mmode === 2 ? b.h >= l.hi : b.c > l.hi;
                if (m) {
                    this.kill(l);
                    lv.splice(i, 1);
                }
            }
        }
    }

    // full pipeline of one chart bar (index j, values b)
    applyBar(j, b) {
        const c = this.cfg;
        const A = this.S.aggs;
        const tfs = this.tfsecs();
        const barMs = tfs * 1000;
        const ev = {};
        for (const k of ["d", "w", "m", "m15", "h1", "h4"]) ev[k] = A[k].push(j, b, barMs);
        // D, W and M levels only on lower chart timeframes, never the chart's own
        const dayOk = c.doDaily && tfs < 86400;
        const wkOk = c.doWeekly && tfs < 604800;
        const monOk = c.doMonthly && tfs < 2592000;
        if (dayOk && ev.d) this.processTf(0, A.d, j);
        if (wkOk && ev.w) this.processTf(1, A.w, j);
        if (monOk && ev.m) this.processTf(2, A.m, j);
        const mfAgg = ["m15", "h1", "h4", "d", "w", "m"];
        for (let i = 0; i < 6; i++) {
            const fvgOn = this.mfEnabled(i);
            const wkOn = this.wkEnabled(i);
            const name = mfAgg[i];
            if ((fvgOn || wkOn) && ev[name]) {
                const g = A[name];
                if (fvgOn) this.addFvgs(3, 3 + i, c.mfMin, g.get(1), g.get(2), g.get(3), j);
                if (wkOn) this.addWicks(3 + i, g.get(1));
            }
        }
        this.runMit(b);
    }
}

// ============================================================ config
function num(v, def) {
    const n = Number(v);
    return v === undefined || v === null || v === "" || isNaN(n) ? def : n;
}
function clamp(v, lo, hi) {
    return Math.min(hi, Math.max(lo, v));
}
function bool(v, def) {
    return v === undefined || v === null ? def : !!v;
}
function str(v, def) {
    return v === undefined || v === null ? def : String(v);
}

function buildConfig(p, chart) {
    p = p || {};
    const sty = (v, d) => DASH[str(v, d)] || 1;
    const font = (v, d) => FONT[str(v, d)] || 11;
    const mit = str(p.mitig, "wick");
    const c = {
        doDaily: bool(p.doDaily, true),
        doWeekly: bool(p.doWeekly, true),
        doMonthly: bool(p.doMonthly, true),
        wExtra: clamp(Math.round(num(p.wExtra, 1)), 0, 2),
        mmode: mit === "ce" ? 1 : mit === "wick" ? 2 : mit === "close" ? 3 : 0,
        sessHour: clamp(num(p.sessionStartHour, 18), 0, 23),

        dBull: str(p.dBull, "#4CAF50"), dBear: str(p.dBear, "#F23645"),
        wBull: str(p.wBull, "#089981"), wBear: str(p.wBear, "#FF9800"),
        mBull: str(p.mBull, "#2196F3"), mBear: str(p.mBear, "#9C27B0"),

        doLbl: bool(p.doLbl, true),
        lblBold: bool(p.lblBold, true),
        lblDir: bool(p.lblDir, false),
        lblFont: font(p.lblSize, "normal"),
        xOff: Math.max(1, Math.round(num(p.xOff, 2))),
        txtD: str(p.txtD, "D"), txtW: str(p.txtW, "W"), txtM: str(p.txtM, "M"),
        pTxtHi: str(p.pTxtHi, "hi"), pTxtLo: str(p.pTxtLo, "lo"), pTxtCe: str(p.pTxtCe, "CE"),

        doFvg: bool(p.doFvg, true),
        nFvg: clamp(Math.round(num(p.nFvg, 8)), 1, 20),
        fvgMin: Math.max(0, num(p.fvgMin, 0)),
        fvgSty: sty(p.fvgSty, "solid"),
        fvgWid: clamp(Math.round(num(p.fvgWid, 1)), 1, 3),
        fvgCeSty: sty(p.fvgCeSty, "dotted"),
        fvgShowHi: bool(p.fvgShowHi, true), fvgShowLo: bool(p.fvgShowLo, true), fvgShowCe: bool(p.fvgShowCe, true),
        fvgNoFill: bool(p.fvgNoFill, false),
        fvgTr: clamp(num(p.fvgTr, 88), 0, 100),
        txtFvg: str(p.txtFvg, "FVG"),
        doIfvg: bool(p.doIfvg, true),
        txtIfvg: str(p.txtIfvg, "IFVG"),
        fvgOvl: str(p.fvgOvl, "recent"),
        fvgVi: bool(p.fvgVi, true),

        doVi: bool(p.doVi, true),
        nVi: clamp(Math.round(num(p.nVi, 8)), 1, 20),
        viMin: Math.max(0, num(p.viMin, 0)),
        viSty: sty(p.viSty, "solid"),
        viWid: clamp(Math.round(num(p.viWid, 1)), 1, 3),
        viCeSty: sty(p.viCeSty, "dotted"),
        viShowHi: bool(p.viShowHi, true), viShowLo: bool(p.viShowLo, true), viShowCe: bool(p.viShowCe, true),
        viNoFill: bool(p.viNoFill, false),
        viTr: clamp(num(p.viTr, 88), 0, 100),
        txtVi: str(p.txtVi, "VI"),

        doWk: bool(p.doWk, true),
        nWk: clamp(Math.round(num(p.nWk, 8)), 1, 20),
        wkMin: clamp(num(p.wkMin, 30), 1, 100),
        wkSty: sty(p.wkSty, "dashed"),
        wkWid: clamp(Math.round(num(p.wkWid, 1)), 1, 3),
        txtWkU: str(p.txtWkU, "UW"), txtWkL: str(p.txtWkL, "LW"),

        doMf: bool(p.doMf, true),
        mfNext: clamp(Math.round(num(p.mfNext, 0)), 0, 6),
        mfSel: [bool(p.mf15, false), bool(p.mf1h, true), bool(p.mf4h, true), bool(p.mfD, false), bool(p.mfW, false), bool(p.mfM, false)],
        nMf: clamp(Math.round(num(p.nMf, 6)), 1, 20),
        mfMin: Math.max(0, num(p.mfMin, 0)),
        mfCol: [
            [str(p.mf15Bull, "#00E676"), str(p.mf15Bear, "#880E4F")],
            [str(p.mf1hBull, "#4CAF50"), str(p.mf1hBear, "#F23645")],
            [str(p.mf4hBull, "#089981"), str(p.mf4hBear, "#FF9800")],
            [str(p.mfDBull, "#2196F3"), str(p.mfDBear, "#E040FB")],
            [str(p.mfWBull, "#311B92"), str(p.mfWBear, "#9C27B0")],
            [str(p.mfMBull, "#808000"), str(p.mfMBear, "#787B86")]
        ],
        mfSty: sty(p.mfSty, "solid"),
        mfWid: clamp(Math.round(num(p.mfWid, 1)), 1, 3),
        mfFill: ["never", "margin", "always"].includes(p.mfFill) ? p.mfFill : "margin",
        mfTr: clamp(num(p.mfTr, 88), 0, 100),
        mfShowForm: bool(p.mfShowForm, true),
        mfShowCe: bool(p.mfShowCe, true),
        mfCeSty: sty(p.mfCeSty, "dotted"),
        mfLbl: bool(p.mfLbl, true),
        mfLShowTf: bool(p.mfLShowTf, true),
        mfLShowType: bool(p.mfLShowType, true),
        mfLShowDir: bool(p.mfLShowDir, false),
        mfLFont: font(p.mfLSize, "normal"),
        mfLPos: str(p.mfLPos, "insideRight"),
        mfLVert: str(p.mfLVert, "ce"),
        wk1h: bool(p.wk1h, false),
        wk4h: bool(p.wk4h, false),
        mfMargin: bool(p.mfMargin, false),
        mfMOff: clamp(Math.round(num(p.mfMOff, 4)), 0, 200),
        mfMW: clamp(Math.round(num(p.mfMW, 4)), 1, 50),
        mfExt: bool(p.mfExt, false),
        mfExtN: clamp(Math.round(num(p.mfExtN, 20)), 1, 500),
        mfExtTo: ["for", "start", "creation"].includes(p.mfExtTo) ? p.mfExtTo : "for"
    };
    // chart timeframe in seconds from the chart description, when time based
    c.fixedTfSecs = 0;
    if (chart && chart.elementSizeUnit === "UnderlyingUnits") {
        const n = num(chart.elementSize, 1);
        if (chart.underlyingType === "MinuteBar") c.fixedTfSecs = n * 60;
        else if (chart.underlyingType === "DailyBar") c.fixedTfSecs = n * 86400;
        else if (chart.underlyingType === "Tick") c.fixedTfSecs = 1;
    } else if (chart && chart.elementSizeUnit) {
        c.fixedTfSecs = 1;   // volume / range / renko ...: below every level timeframe
    }
    c.keys = makeKeyFns(c.sessHour);
    return c;
}

// ============================================================ drawing
function tcol(c, tfi, d) {
    if (tfi >= 3) return c.mfCol[tfi - 3][d === 1 ? 0 : 1];
    if (tfi === 0) return d === 1 ? c.dBull : c.dBear;
    if (tfi === 1) return d === 1 ? c.wBull : c.wBear;
    return d === 1 ? c.mBull : c.mBear;
}
function ksty(c, k) {
    return k === 0 ? c.fvgSty : k === 1 ? c.viSty : c.wkSty;
}
function kcesty(c, k) {
    return k === 0 ? c.fvgCeSty : k === 1 ? c.viCeSty : c.wkSty;
}
function kwid(c, k, tfi) {
    return (k === 0 ? c.fvgWid : k === 1 ? c.viWid : c.wkWid) + (tfi < 3 ? tfi * c.wExtra : 0);
}
// part: 0 high, 1 low, 2 CE. Wick levels only show the CE
function kshow(c, k, part) {
    if (k === 0) return part === 0 ? c.fvgShowHi : part === 1 ? c.fvgShowLo : c.fvgShowCe;
    if (k === 1) return part === 0 ? c.viShowHi : part === 1 ? c.viShowLo : c.viShowCe;
    return part === 2;
}
function kfill(c, k) {
    return k === 0 ? !c.fvgNoFill : k === 1 ? !c.viNoFill : false;
}
function lbltxt(c, l, part) {
    const nm = l.kind === 0 ? (l.inv ? c.txtIfvg : c.txtFvg) : l.kind === 1 ? c.txtVi : l.dir === -1 ? c.txtWkU : c.txtWkL;
    const sg = c.lblDir && l.kind < 2 ? (l.dir === 1 ? "+" : "-") : "";
    const tf = l.tfi >= 3 ? MF_NAMES[l.tfi - 3] : l.tfi === 0 ? c.txtD : l.tfi === 1 ? c.txtW : c.txtM;
    return tf + " " + nm + sg + " " + part;
}

function pt(x, y) {
    return { x: du(x), y: du(y) };
}
function seg(x1, y1, x2, y2) {
    return { tag: "Line", a: pt(x1, y1), b: pt(x2, y2), infiniteStart: false, infiniteEnd: false };
}
function linesItem(key, lines, color, width, dash) {
    return { tag: "LineSegments", key, lines, lineStyle: { lineWidth: width, color, lineStyle: dash }, global: true };
}
function rectItem(key, x1, y1, x2, y2, color, transp) {
    return {
        tag: "Shapes", key, global: true,
        primitives: [{ tag: "Polygon", points: [pt(x1, y1), pt(x2, y1), pt(x2, y2), pt(x1, y2)] }],
        fillStyle: fillStyleOf(color, (100 - transp) / 100)
    };
}
// align: "left" text extends to the right of x, "right" to the left, "center"
// (Tradovate's alignment names the side of the point the text goes to, so "left" is "rightMiddle")
function textItem(key, x, y, text, color, size, bold, align) {
    const xb = align === "left" ? op(du(x), "+", px(4)) : align === "right" ? op(du(x), "-", px(4)) : du(x);
    const style = { fontSize: size, fill: color };
    if (bold) style.fontWeight = "bold";
    return {
        tag: "Text", key, global: true, point: { x: xb, y: du(y) }, text, style,
        textAlignment: align === "left" ? "rightMiddle" : align === "right" ? "leftMiddle" : "centerMiddle"
    };
}

// level of kind 0, 1 or 2 (Pine draw)
function drawLvl(c, l, x, items) {
    const col = tcol(c, l.tfi, l.dir);
    const w = kwid(c, l.kind, l.tfi);
    const s1 = ksty(c, l.kind);
    const s2 = kcesty(c, l.kind);
    const k = "ictc" + l.id;
    if (kfill(c, l.kind)) items.push(rectItem(k + "bx", l.t, l.hi, x, l.lo, col, l.kind === 0 ? c.fvgTr : c.viTr));
    const parts = [[0, l.hi, s1, c.pTxtHi, "hi"], [1, l.lo, s1, c.pTxtLo, "lo"], [2, l.ce, s2, c.pTxtCe, "ce"]];
    for (const [part, y, s, txt, nm] of parts) {
        if (!kshow(c, l.kind, part)) continue;
        items.push(linesItem(k + nm, [seg(l.t, y, x, y)], col, w, s));
        if (c.doLbl) items.push(textItem(k + nm + "lb", x, y, lbltxt(c, l, txt), col, c.lblFont, c.lblBold, "left"));
    }
}

// FVG by timeframe (Pine drawM)
function drawMf(c, l, x, items) {
    const col = c.mfCol[l.tfi - 3][l.dir === 1 ? 0 : 1];
    const k = "ictc" + l.id;
    if (c.mfFill === "always") items.push(rectItem(k + "fill", l.t, l.hi, x, l.lo, col, c.mfTr));
    items.push(linesItem(k + "bx", [
        seg(l.t, l.hi, x, l.hi), seg(x, l.hi, x, l.lo), seg(x, l.lo, l.t, l.lo), seg(l.t, l.lo, l.t, l.hi)
    ], col, c.mfWid, c.mfSty));
    if (c.mfShowCe) items.push(linesItem(k + "ce", [seg(l.t, l.ce, x, l.ce)], col, c.mfWid, c.mfCeSty));
    if (c.mfShowForm && l.tfm !== null && l.tfm >= l.t) {
        items.push(linesItem(k + "form", [seg(l.tfm, l.hi, l.tfm, l.lo)], col, c.mfWid > 1 ? c.mfWid - 1 : c.mfWid, DASH.solid));
    }
    if (c.mfLbl && (c.mfLShowTf || c.mfLShowType || c.mfLShowDir)) {
        const y = c.mfLVert === "high" ? l.hi : c.mfLVert === "low" ? l.lo : l.ce;
        const pos = c.mfLPos;
        const lx = pos === "insideLeft" || pos === "leftOfBox" ? l.t : pos === "center" ? Math.floor((l.t + x) / 2) : x;
        const align = pos === "rightMargin" || pos === "insideLeft" ? "left" : pos === "center" ? "center" : "right";
        const sg = c.mfLShowDir ? (l.dir === 1 ? "+" : "-") : "";
        const ty = (c.mfLShowType ? (l.inv ? c.txtIfvg : c.txtFvg) : "") + sg;
        const tfn = c.mfLShowTf ? MF_NAMES[l.tfi - 3] : "";
        const txt = tfn + (tfn !== "" && ty !== "" ? " " : "") + ty;
        items.push(textItem(k + "lb", lx, y, txt, col, c.mfLFont, c.lblBold, align));
    }
}

// FVG by timeframe in the right margin (Pine drawMM): box in its timeframe column, CE line, arrow label,
// extended levels
function drawMfMargin(c, l, eng, lastIdx, items) {
    const col = c.mfCol[l.tfi - 3][l.dir === 1 ? 0 : 1];
    const k = "ictc" + l.id;
    let cn = 0;
    for (let j = 0; j < l.tfi - 3; j++) if (eng.mfEnabled(j)) cn++;
    const x0 = lastIdx + c.mfMOff + cn * (c.mfMW + 1);
    const x1 = x0 + c.mfMW;
    if (c.mfFill !== "never") items.push(rectItem(k + "fill", x0, l.hi, x1, l.lo, col, c.mfTr));
    items.push(linesItem(k + "bx", [
        seg(x0, l.hi, x1, l.hi), seg(x1, l.hi, x1, l.lo), seg(x1, l.lo, x0, l.lo), seg(x0, l.lo, x0, l.hi)
    ], col, c.mfWid, c.mfSty));
    if (c.mfShowCe) items.push(linesItem(k + "ce", [seg(x0, l.ce, x1, l.ce)], col, c.mfWid, c.mfCeSty));
    if (c.mfExt) {
        // "for" N candles, "start" of the FVG, or "creation": end of its third candle
        const xs = c.mfExtTo === "start" ? l.t : c.mfExtTo === "creation" ? (l.tfm !== null ? l.tfm : l.t)
            : lastIdx - c.mfExtN;
        items.push(linesItem(k + "ext", [seg(xs, l.hi, x0, l.hi), seg(xs, l.lo, x0, l.lo)], col, c.mfWid, c.mfSty));
        if (c.mfShowCe) items.push(linesItem(k + "extce", [seg(xs, l.ce, x0, l.ce)], col, c.mfWid, c.mfCeSty));
    }
    if (c.mfLbl) {
        const y = c.mfLVert === "high" ? l.hi : c.mfLVert === "low" ? l.lo : l.ce;
        // TF and arrow on one line, centered on the box: "1Hi \u2193"
        // (Tradovate does not break text at "\n" and misplaces centerBottom / centerTop text)
        const xm = Math.floor((x0 + x1) / 2);
        const tf = c.mfLShowTf ? MF_NAMES[l.tfi - 3] + (l.inv ? "i" : "") + " " : "";
        items.push(textItem(k + "lb", xm, y, tf + (l.dir === 1 ? "\u2191" : "\u2193"), col, c.mfLFont, c.lblBold, "center"));
    }
}

function drawAll(c, eng, lastIdx, lastBar) {
    const x = lastIdx + c.xOff;
    const items = [];
    for (const l of eng.S.lvls) {
        if (l.dead) continue;
        if (l.kind === 3) {
            if (c.mfMargin) drawMfMargin(c, l, eng, lastIdx, items);
            else drawMf(c, l, x, items);
        }
        else drawLvl(c, l, x, items);
    }
    const tfs = eng.tfsecs();
    const any = (c.doDaily && tfs < 86400) || (c.doWeekly && tfs < 604800) || (c.doMonthly && tfs < 2592000);
    if (!any) {
        items.push(textItem("ictcnote", x, lastBar.c, "ICTcotas: use a chart timeframe lower than 1M",
            "#FF9800", FONT.small, false, "left"));
    }
    return items;
}

// ============================================================ calculator
function barOf(d) {
    const ts = d.timestamp();
    return {
        o: Number(d.open()), h: Number(d.high()), l: Number(d.low()), c: Number(d.close()),
        t: ts instanceof Date ? ts.getTime() : Number(ts)
    };
}

// fill style with its opacity. Tradovate reads fillStyle.opacity as a percentage (0 to 100), although its
// documentation says 0 to 1: 0 to 1 values left the fills invisible, and an alpha in the color is ignored
function fillStyleOf(color, opacity) {
    let c = String(color || "").trim();
    if (/^#[0-9a-fA-F]{8}$/.test(c)) c = c.slice(0, 7);
    return { color: c, opacity: Math.round(Math.max(0, Math.min(1, Number(opacity))) * 100) };
}

// Index of the bar on the chart. The index argument of map() is not always the bar's position: with the
// market closed, after the chart reloads its bars, it no longer matched the chart and everything was drawn
// shifted to the right. The bar's own index() is used when there is one.
function barIndexOf(d, i) {
    try {
        if (d && typeof d.index === "function") {
            const k = Number(d.index());
            if (Number.isInteger(k) && k >= 0) return k;
        }
    } catch (e) { /* ignore */ }
    return i;
}

// Last bar of the chart. d.isLast() alone is not enough: with the market closed it can stay false
// when the chart reloads its bars (for example after loading older history), which left the drawing of
// the previous pass on the chart, at the old bar indices. So the history length is checked too.
function chartLastBar(d, i, history) {
    let hasFn = false;
    try {
        if (d && typeof d.isLast === "function") {
            hasFn = true;
            if (d.isLast()) return true;
        }
    } catch (e) { /* ignore */ }
    try {
        if (history && typeof history.size === "function") {
            const n = history.size();
            if (n > 0) return i >= n - 1;
        }
        if (history && history.data && typeof history.data.length === "number" && history.data.length > 0) {
            return i >= history.data.length - 1;
        }
    } catch (e) { /* ignore */ }
    return !hasFn;
}

class ICTcotasClass {
    init() {
        this.cfg = null;
        this.bars = [];
        this.minDelta = 0;
        this.S = null;
        this.committed = -1;
        this.lastOut = null;
        this.lastOutIdx = -1;
    }

    setup() {
        if (!this.cfg) this.cfg = buildConfig(this.props, this.chartDescription);
        if (!this.S) {
            this.S = newState(this.cfg.keys);
            this.committed = -1;
        }
    }

    // advances the committed state through bar j (final values only)
    commit(j, history) {
        let b = this.bars[j];
        if (!b && history && typeof history.get === "function") {
            const e = history.get(j);
            if (e) b = this.bars[j] = barOf(e);
        }
        if (b) {
            this.S.tfsecs = this.minDelta > 0 ? this.minDelta / 1000 : 0;
            try {
                new Engine(this.cfg, this.S).applyBar(j, b);
            } catch (e) {
                // an error on one bar must not stop the indicator: the bar is skipped (it stopped drawing
                // on a thin contract, NNQ)
            }
        }
        this.committed = j;
    }

    map(d, i, history) {
        i = barIndexOf(d, i);
        if (this.bars === undefined) this.init();
        this.setup();
        const b = barOf(d);
        // a recalculation from an earlier bar without init() (the chart reloaded its bars): rebuild, and
        // drop the previous drawing, whose bar indices may no longer match
        if (i <= this.committed) {
            this.S = newState(this.cfg.keys);
            this.committed = -1;
            this.bars = [];
            this.bars[i] = b;
            this.minDelta = 0;
            if (this.lastOut) {
                try {
                    this.lastOut.graphics.items = [];
                } catch (e) {
                    // frozen by the host: nothing to do
                }
                this.lastOut = null;
                this.lastOutIdx = -1;
            }
        }
        this.bars[i] = b;
        const prev = this.bars[i - 1];
        if (prev && b.t > prev.t) {
            const dlt = b.t - prev.t;
            if (this.minDelta === 0 || dlt < this.minDelta) this.minDelta = dlt;
        }
        // bars before i are complete: commit them (bar i may still be forming)
        while (this.committed < i - 1) this.commit(this.committed + 1, history);

        if (!chartLastBar(d, i, history)) return undefined;

        // outputs of earlier bars must not keep stale global graphics
        if (this.lastOut && this.lastOutIdx !== i) {
            try {
                this.lastOut.graphics.items = [];
            } catch (e) {
                // frozen by the host: nothing to do
            }
        }
        // provisional evaluation of bar i on a copy of the committed state
        const S2 = cloneState(this.S);
        S2.tfsecs = this.minDelta > 0 ? this.minDelta / 1000 : 0;
        const eng = new Engine(this.cfg, S2);
        let items = [];
        try {
            eng.applyBar(i, b);
            items = drawAll(this.cfg, eng, i, b);
        } catch (e) {
            // skip the forming bar's update rather than stop the indicator
        }
        const out = { graphics: { items } };
        this.lastOut = out;
        this.lastOutIdx = i;
        return out;
    }
}

// ============================================================ definition
const styleEnum = def => predef.paramSpecs.enum(STYLES, def);
const sizeEnum = def => predef.paramSpecs.enum(SIZES, def);
const num0 = def => predef.paramSpecs.number(def, 0.25, 0);
const int = (def, min) => predef.paramSpecs.number(def, 1, min);

module.exports = {
    name: "ictcotas",
    description: "ICTcotas",
    calculator: ICTcotasClass,
    params: {
        // General
        doDaily: predef.paramSpecs.bool(true),
        doWeekly: predef.paramSpecs.bool(true),
        doMonthly: predef.paramSpecs.bool(true),
        wExtra: int(1, 0),
        mitig: predef.paramSpecs.enum({
            never: "Never", ce: "CE touched", wick: "Fully filled (wick)", close: "Fully filled (close)"
        }, "wick"),
        sessionStartHour: int(18, 0),
        // Colors (by timeframe)
        dBull: predef.paramSpecs.color("#4CAF50"),
        dBear: predef.paramSpecs.color("#F23645"),
        wBull: predef.paramSpecs.color("#089981"),
        wBear: predef.paramSpecs.color("#FF9800"),
        mBull: predef.paramSpecs.color("#2196F3"),
        mBear: predef.paramSpecs.color("#9C27B0"),
        // Labels
        doLbl: predef.paramSpecs.bool(true),
        lblBold: predef.paramSpecs.bool(true),
        lblDir: predef.paramSpecs.bool(false),
        lblSize: sizeEnum("normal"),
        xOff: int(2, 1),
        txtD: predef.paramSpecs.text("D"),
        txtW: predef.paramSpecs.text("W"),
        txtM: predef.paramSpecs.text("M"),
        pTxtHi: predef.paramSpecs.text("hi"),
        pTxtLo: predef.paramSpecs.text("lo"),
        pTxtCe: predef.paramSpecs.text("CE"),
        // Fair Value Gaps
        doFvg: predef.paramSpecs.bool(true),
        nFvg: int(8, 1),
        fvgMin: num0(0),
        fvgSty: styleEnum("solid"),
        fvgWid: int(1, 1),
        fvgCeSty: styleEnum("dotted"),
        fvgShowHi: predef.paramSpecs.bool(true),
        fvgShowLo: predef.paramSpecs.bool(true),
        fvgShowCe: predef.paramSpecs.bool(true),
        fvgNoFill: predef.paramSpecs.bool(false),
        fvgTr: int(88, 0),
        txtFvg: predef.paramSpecs.text("FVG"),
        doIfvg: predef.paramSpecs.bool(true),
        txtIfvg: predef.paramSpecs.text("IFVG"),
        fvgOvl: predef.paramSpecs.enum({ recent: "Most recent", enclosing: "Enclosing", all: "Keep all" }, "recent"),
        fvgVi: predef.paramSpecs.bool(true),
        // Volume Imbalances
        doVi: predef.paramSpecs.bool(true),
        nVi: int(8, 1),
        viMin: num0(0),
        viSty: styleEnum("solid"),
        viWid: int(1, 1),
        viCeSty: styleEnum("dotted"),
        viShowHi: predef.paramSpecs.bool(true),
        viShowLo: predef.paramSpecs.bool(true),
        viShowCe: predef.paramSpecs.bool(true),
        viNoFill: predef.paramSpecs.bool(false),
        viTr: int(88, 0),
        txtVi: predef.paramSpecs.text("VI"),
        // Wick CE
        doWk: predef.paramSpecs.bool(true),
        nWk: int(8, 1),
        wkMin: predef.paramSpecs.number(30, 1, 1),
        wkSty: styleEnum("dashed"),
        wkWid: int(1, 1),
        txtWkU: predef.paramSpecs.text("UW"),
        txtWkL: predef.paramSpecs.text("LW"),
        // FVGs and wick CE by timeframe (15m / 1H / 4H / D / W / M)
        doMf: predef.paramSpecs.bool(true),
        mfNext: int(0, 0),
        mf15: predef.paramSpecs.bool(false),
        mf1h: predef.paramSpecs.bool(true),
        mf4h: predef.paramSpecs.bool(true),
        mfD: predef.paramSpecs.bool(false),
        mfW: predef.paramSpecs.bool(false),
        mfM: predef.paramSpecs.bool(false),
        nMf: int(6, 1),
        mfMin: num0(0),
        mf15Bull: predef.paramSpecs.color("#00E676"),
        mf15Bear: predef.paramSpecs.color("#880E4F"),
        mf1hBull: predef.paramSpecs.color("#4CAF50"),
        mf1hBear: predef.paramSpecs.color("#F23645"),
        mf4hBull: predef.paramSpecs.color("#089981"),
        mf4hBear: predef.paramSpecs.color("#FF9800"),
        mfDBull: predef.paramSpecs.color("#2196F3"),
        mfDBear: predef.paramSpecs.color("#E040FB"),
        mfWBull: predef.paramSpecs.color("#311B92"),
        mfWBear: predef.paramSpecs.color("#9C27B0"),
        mfMBull: predef.paramSpecs.color("#808000"),
        mfMBear: predef.paramSpecs.color("#787B86"),
        mfSty: styleEnum("solid"),
        mfWid: int(1, 1),
        mfFill: predef.paramSpecs.enum({ never: "never", margin: "at margin", always: "always" }, "margin"),
        mfTr: int(88, 0),
        mfShowForm: predef.paramSpecs.bool(true),
        mfShowCe: predef.paramSpecs.bool(true),
        mfCeSty: styleEnum("dotted"),
        mfLbl: predef.paramSpecs.bool(true),
        mfLShowTf: predef.paramSpecs.bool(true),
        mfLShowType: predef.paramSpecs.bool(true),
        mfLShowDir: predef.paramSpecs.bool(false),
        mfLSize: sizeEnum("normal"),
        mfLPos: predef.paramSpecs.enum({
            rightMargin: "Right margin", insideLeft: "Inside left", insideRight: "Inside right",
            center: "Center", leftOfBox: "Left of box"
        }, "insideRight"),
        mfLVert: predef.paramSpecs.enum({ high: "High", ce: "CE", low: "Low" }, "ce"),
        wk1h: predef.paramSpecs.bool(false),
        wk4h: predef.paramSpecs.bool(false),
        // right margin: one column per timeframe
        mfMargin: predef.paramSpecs.bool(false),
        mfMOff: int(4, 0),
        mfMW: int(4, 1),
        mfExt: predef.paramSpecs.bool(false),
        mfExtTo: predef.paramSpecs.enum({ for: "for", start: "to start", creation: "to creation" }, "for"),
        mfExtN: int(20, 1)
    },
    inputType: meta.InputType.BARS,
    areaChoice: meta.AreaChoice.OVERLAY,
    tags: ["ICT"]
};
