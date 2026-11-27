import pickle, numpy as np, oterel as o
from rep import ses
T = pickle.load(open('trades.pkl', 'rb'))
def yr(t): return int(str(np.datetime64(int(t['t']), 'm'))[:4])
def tab(tr, key):
    g = {}
    for t in tr: g.setdefault(key(t), []).append(t)
    for k in sorted(g): 
        s = o.stats(g[k]); print(f'   {str(k):18s} n={s["n"]:5d} win={s["win"]:5.1f}% avg={s["avg"]:6.2f} pf={s["pf"]} tot={s["tot"]}')
for st in ('79','100'):
  for tf in (1,2,3,5):
    tr = T[(tf, st, 0, True)]
    print(f'== {tf}m stop {st}: ', o.stats(tr))
    tab(tr, yr)
    tab(tr, lambda t: ses(t['t']))
    tab(tr, lambda t: 'short' if t['d']<0 else 'long')
    tab(tr, lambda t: 'IFVG' if t['ifvg'] else 'FVG')
    tab(tr, lambda t: t['why'])
