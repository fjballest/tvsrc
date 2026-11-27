# usage: BT=bt python3 prep.py MNQ.m1.csv  (1m csv: time_utc_close;open;high;low;close;volume)
import pandas as pd, numpy as np, sys, os
BT = os.environ.get('BT', 'bt')
os.makedirs(BT, exist_ok=True)
df = pd.read_csv(sys.argv[1], sep=';')
t = pd.to_datetime(df.time_utc_close, utc=True) - pd.Timedelta(minutes=1)
ny = t.dt.tz_convert('America/New_York')
df['ny'] = ny
print(len(df), df.ny.iloc[0], df.ny.iloc[-1])
# check gaps / duplicates
print('dups', t.duplicated().sum())
for n in (1,2,3,5):
    # bucket by NY wall clock minutes
    wall = ny.dt.tz_localize(None)
    key = wall.dt.floor(f'{n}min')
    g = df.groupby(key, sort=True)
    o = g.open.first(); h = g.high.max(); l = g.low.min(); c = g.close.last(); v = g.volume.sum()
    ts = pd.DatetimeIndex(o.index)
    np.savez(os.path.join(BT, f'tf{n}.npz'), o=o.values, h=h.values, l=l.values, c=c.values, v=v.values,
             ts=ts.values.astype('datetime64[m]').astype(np.int64))
    print(n, len(o))
