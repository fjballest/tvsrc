import oterel as o, numpy as np, itertools, sys, json
from multiprocessing import Pool
SES = [('Asia 18-02', 1080, 120), ('London 02-07', 120, 420), ('NY AM 07-12', 420, 720), ('NY lunch 12-13:30', 720, 810), ('NY PM 13:30-17', 810, 1020)]
def ses(t):
    m = t % 1440
    for nm, a, b in SES:
        if (a <= m < b) if a < b else (m >= a or m < b):
            return nm
def job(a):
    tf, st, mn, wk = a
    return a, o.run(tf, stop=st, maxage=1440, minrr=mn, wick=wk)
if __name__ == '__main__':
    args = list(itertools.product((1, 2, 3, 5), ('79', '100'), (0, 1), (False, True)))
    allt = {}
    with Pool(4) as p:
        for a, t in p.imap(job, args):
            allt[a] = t
            print(a, o.stats(t), flush=True)
    import pickle; pickle.dump(allt, open('trades.pkl', 'wb'))
