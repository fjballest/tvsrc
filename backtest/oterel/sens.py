import oterel as o, numpy as np
from rep import ses
from multiprocessing import Pool
base = dict(stop='79', maxage=1440, minrr=0, wick=True)
var = [{}, dict(rejwin=3), dict(rejwin=10), dict(maxage=360), dict(maxage=None), dict(eqtol=.05), dict(eqtol=.2),
       dict(minfvg=.5), dict(minfvg=3), dict(wick=False), dict(maxover=0), dict(maxover=1.0), dict(swL=5, swR=2), dict(swL=15,swR=3), dict(cost=2.0)]
def job(a):
    tf, v = a
    tr = o.run(tf, **{**base, **v})
    am = [t for t in tr if ses(t['t']).startswith('NY AM')]
    p = np.sort([t['pts'] for t in am])
    return tf, v, o.stats(tr), o.stats(am), round(float(p[:-5].mean()),2)
if __name__ == '__main__':
    with Pool(4) as P:
        for tf, v, s, a, x in P.imap(job, [(tf, v) for tf in (2, 3) for v in var]):
            print(tf, v, f"all n={s['n']} avg={s['avg']} pf={s['pf']} | NYAM n={a['n']} avg={a['avg']} pf={a['pf']} dd={a['dd']} avg-w/o-top5={x}", flush=True)
