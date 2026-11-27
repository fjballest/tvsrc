"""Usage: BT=bt python3 rep.py (all runs, trades.pkl), then brk.py (breakdowns), sens.py (sensitivity).

OTE rejection to RELs / REHs backtest (MNQ / NQ, 1m-based data).

Short (long is the mirror):
  * Leg: OTEFibWick fib on swings (pivots 10 / 3, the 0 / 100 extended by new extremes, one update per bar as in
    OTEFibWick): last swing high before the last swing low = a down leg; levels lvl(x) = low + x * (high - low).
  * Retracement: the highest high since the leg low reaches the 0.62 level and never goes above the 0.705 level by
    more than one tick (0.25).
  * Support: a live bearish FVG or bearish IFVG (inverted bullish FVG) overlapping the OTE band 0.62 - 0.79, that
    the retracement traded into.
  * Rejection: within `rejwin` bars of the last tap of the 0.62 level, a bearish candle closing below 0.62: enter
    at its close.
  * Target: the nearest live relative equal lows (two 3/3 pivot lows within 0.10 x ATR(14), unswept, as in
    ICTlevels EQH / EQL) below the entry, at the lower of the two lows; filled when price trades one tick through.
  * Stop: one tick past the 0.79 level (stop='79') or past the leg high (stop='100').
  * Flat at 16:59 New York if neither is hit. Same bar hitting both: stop. One trade per leg and per timeframe at
    a time. Cost: `cost` points per trade.
"""
import numpy as np, os
BT = os.environ.get('BT', 'bt')   # folder with tf1/2/3/5.npz written by prep.py
from numpy.lib.stride_tricks import sliding_window_view as swv

TICK = 0.25


def pivots(x, L, R, high=True):
    n = len(x)
    out = np.full(n, np.nan)
    w = swv(x, L + R + 1)
    c = w[:, L]
    if high:
        ok = (c > w[:, :L].max(1)) & (c >= w[:, L + 1:].max(1))
    else:
        ok = (c < w[:, :L].min(1)) & (c <= w[:, L + 1:].min(1))
    idx = np.arange(len(w)) + L + R
    out[idx[ok]] = c[ok]
    return out


def atr(h, l, c, n=14):
    pc = np.r_[c[0], c[:-1]]
    tr = np.maximum(h - l, np.maximum(np.abs(h - pc), np.abs(l - pc)))
    out = np.empty(len(tr))
    a = tr[:n].mean()
    out[:n] = a
    k = 1.0 / n
    for i in range(n, len(tr)):
        a += (tr[i] - a) * k
        out[i] = a
    return out


def load(tf):
    z = np.load(os.path.join(BT, f'tf{tf}.npz'))
    return z['o'], z['h'], z['l'], z['c'], z['ts']


def run(tf, stop='79', rejwin=5, minfvg=1.0, eqtol=0.10, cost=1.0, minleg=0.0, minrr=0.0, maxover=TICK,
        swL=10, swR=3, maxage=None, maxrr=None, wick=False, data=None):
    o, h, l, c, m = data if data is not None else load(tf)
    n = len(o)
    ph = pivots(h, swL, swR, True).tolist()
    pl = pivots(l, swL, swR, False).tolist()
    eph = pivots(h, 3, 3, True).tolist()
    epl = pivots(l, 3, 3, False).tolist()
    at = atr(h, l, c).tolist()
    mod = (m % 1440).tolist()
    day = ((m - 1020) // 1440).tolist()          # trading day starts 17:00 NY
    o_, h_, l_, c_ = o.tolist(), h.tolist(), l.tolist(), c.tolist()
    ts = m.tolist()
    nan = float('nan')

    swhi = swlo = nan
    swhit = swlot = -1
    fv = []          # [lo, hi, dir(+1 bull FVG / -1 bear FVG), inverted(bool)]; dir after inversion = zone side
    pvH, pvL = [], []    # live 3/3 pivots [price]
    eqH, eqL = [], []    # live REH / REL levels
    legkey = None
    rh = rl = nan        # retracement extreme since the leg end
    tapbar = -10**9
    used = set()
    pos = None
    trades = []

    for i in range(n):
        hi, lo, op, cl = h_[i], l_[i], o_[i], c_[i]
        # ---------- manage the open trade
        if pos is not None:
            d = pos['d']
            ex = None
            if (d < 0 and hi >= pos['sl']) or (d > 0 and lo <= pos['sl']):
                ex = (pos['sl'], 'SL')
            elif (d < 0 and lo <= pos['tp'] - TICK) or (d > 0 and hi >= pos['tp'] + TICK):
                ex = (pos['tp'], 'TP')
            elif day[i + 1] != day[i] if i + 1 < n else True:
                ex = (cl, 'EOD')
            if ex:
                pts = (ex[0] - pos['e']) * d - cost
                pos.update(x=ex[0], why=ex[1], pts=pts, xt=ts[i], bars=i - pos['i'])
                trades.append(pos)
                pos = None

        # ---------- leg state before this bar (levels known at the open)
        down = swhit >= 0 and swlot >= 0 and swhit < swlot
        up = swhit >= 0 and swlot >= 0 and swlot < swhit
        if down or up:
            key = (swhit, swlot, swhi, swlo)
            if key != legkey:
                legkey = key
                rh, rl = -1e18, 1e18
                tapbar = -10**9
            r = swhi - swlo
            if down:
                rh = max(rh, hi)
                if cl >= swlo + .62 * r and wick:
                    rh = 1e18
                l62, l705, l79 = swlo + .62 * r, swlo + .705 * r, swlo + .79 * r
                if hi >= l62:
                    tapbar = i
                if (pos is None and r >= minleg and (swhit, swlo) not in used and rh >= l62 and rh <= l705 + maxover
                        and i - tapbar <= rejwin and cl < l62 and cl < op):
                    sup = None
                    for f in fv:
                        if f[2] < 0 and f[1] >= l62 and f[0] <= l79 and rh >= f[0]:
                            sup = f
                            break
                    if sup is not None:
                        cands = [e[0] for e in eqL if e[0] < cl and (maxage is None or ts[i] - e[1] <= maxage)]
                        if cands:
                            tp = max(cands)
                            sl = np.ceil(((l79 if stop == '79' else swhi) + TICK) / TICK) * TICK
                            risk = sl - cl
                            if risk > 0 and (cl - tp) / risk >= minrr and (maxrr is None or (cl - tp) / risk <= maxrr):
                                used.add((swhit, swlo))
                                pos = dict(d=-1, i=i, t=ts[i], e=cl, sl=sl, tp=tp, risk=risk, rew=cl - tp,
                                           leg=r, ifvg=sup[3], tf=tf)
            else:
                rl = min(rl, lo)
                if cl <= swhi - .62 * r and wick:
                    rl = -1e18
                l62, l705, l79 = swhi - .62 * r, swhi - .705 * r, swhi - .79 * r
                if lo <= l62:
                    tapbar = i
                if (pos is None and r >= minleg and (swlot, swhi) not in used and rl <= l62 and rl >= l705 - maxover
                        and i - tapbar <= rejwin and cl > l62 and cl > op):
                    sup = None
                    for f in fv:
                        if f[2] > 0 and f[0] <= l62 and f[1] >= l79 and rl <= f[1]:
                            sup = f
                            break
                    if sup is not None:
                        cands = [e[0] for e in eqH if e[0] > cl and (maxage is None or ts[i] - e[1] <= maxage)]
                        if cands:
                            tp = min(cands)
                            sl = np.floor(((l79 if stop == '79' else swlo) - TICK) / TICK) * TICK
                            risk = cl - sl
                            if risk > 0 and (tp - cl) / risk >= minrr and (maxrr is None or (tp - cl) / risk <= maxrr):
                                used.add((swlot, swhi))
                                pos = dict(d=1, i=i, t=ts[i], e=cl, sl=sl, tp=tp, risk=risk, rew=tp - cl,
                                           leg=r, ifvg=sup[3], tf=tf)

        # ---------- update FVGs / IFVGs at the close of this bar
        nf = []
        for f in fv:
            if f[2] > 0:       # bullish zone: dies on a close below its low
                if cl < f[0]:
                    if not f[3]:
                        nf.append([f[0], f[1], -1, True])
                    continue
            else:
                if cl > f[1]:
                    if not f[3]:
                        nf.append([f[0], f[1], 1, True])
                    continue
            nf.append(f)
        if i >= 2:
            if lo > h_[i - 2] and lo - h_[i - 2] >= minfvg:
                nf.insert(0, [h_[i - 2], lo, 1, False])
            elif hi < l_[i - 2] and l_[i - 2] - hi >= minfvg:
                nf.insert(0, [hi, l_[i - 2], -1, False])
        fv = nf[:60]

        # ---------- REH / REL (ICTlevels EQH / EQL)
        eqL = [e for e in eqL if lo >= e[0]]
        eqH = [e for e in eqH if hi <= e[0]]
        pvL = [p for p in pvL if lo >= p]
        pvH = [p for p in pvH if hi <= p]
        tol = eqtol * at[i]
        x = epl[i]
        if x == x:
            for p in pvL:
                if abs(p - x) <= tol:
                    eqL.append((min(p, x), ts[i]))
                    break
            pvL.insert(0, x)
            pvL = pvL[:20]
            eqL = eqL[-20:]
        x = eph[i]
        if x == x:
            for p in pvH:
                if abs(p - x) <= tol:
                    eqH.append((max(p, x), ts[i]))
                    break
            pvH.insert(0, x)
            pvH = pvH[:20]
            eqH = eqH[-20:]

        # ---------- OTEFibWick swing update (one per bar)
        if pl[i] == pl[i]:
            swlo, swlot = pl[i], i - swR
        elif ph[i] == ph[i]:
            swhi, swhit = ph[i], i - swR
        elif swhi == swhi and hi > swhi:
            swhi, swhit = hi, i
        elif swlo == swlo and lo < swlo:
            swlo, swlot = lo, i
    return trades


def stats(tr):
    if not tr:
        return dict(n=0)
    p = np.array([t['pts'] for t in tr])
    w = p[p > 0].sum(); ls = -p[p < 0].sum()
    eq = np.cumsum(p); dd = (np.maximum.accumulate(eq) - eq).max()
    return dict(n=len(p), win=round(float((p > 0).mean()) * 100, 1), avg=round(float(p.mean()), 2),
                tot=round(float(p.sum()), 1), pf=round(float(w / ls), 2) if ls else None, dd=round(float(dd), 1),
                tp=sum(t['why'] == 'TP' for t in tr), rr=round(float(np.median([t['rew'] / t['risk'] for t in tr])), 2),
                risk=round(float(np.median([t['risk'] for t in tr])), 1))
