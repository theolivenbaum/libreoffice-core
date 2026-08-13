import re,sys,zlib
def pages(path):
    data=open(path,'rb').read()
    # crude: find all stream objects, inflate
    out=[]
    for m in re.finditer(rb'stream\r?\n',data):
        s=m.end()
        e=data.find(b'endstream',s)
        if e<0: continue
        raw=data[s:e]
        try: out.append(zlib.decompress(raw))
        except Exception: pass
    return out
path=sys.argv[1]
pat=re.compile(rb'([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) Tm')
import math, collections
c=collections.Counter()
tot=0
streams=pages(path)
for st in streams:
    for m in pat.finditer(st):
        a,b=float(m.group(1)),float(m.group(2))
        ang=round(math.degrees(math.atan2(b,a)),1)
        c[ang]+=1; tot+=1
print(path.split('/')[-1], 'streams',len(streams),'Tm total',tot)
for k,v in sorted(c.items()): print(f"  {k:8.1f} deg : {v}")
