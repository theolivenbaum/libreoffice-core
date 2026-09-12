#!/usr/bin/env python3
"""Census of every pivot table in a workbook, and whether XlsxPivotGrid.IsGeneratable accepts it."""
import sys, os, zipfile, re
import xml.etree.ElementTree as ET
M='http://schemas.openxmlformats.org/spreadsheetml/2006/main'
R='http://schemas.openxmlformats.org/officeDocument/2006/relationships'
def m(t): return '{%s}%s'%(M,t)
def flag(e,a,d=True):
    v=e.get(a)
    if v is None: return d
    return v not in ('0','false')

def sheets(z):
    wb=ET.fromstring(z.read('xl/workbook.xml'))
    rels=ET.fromstring(z.read('xl/_rels/workbook.xml.rels'))
    tgt={r.get('Id'):r.get('Target') for r in rels}
    out=[]
    for sh in wb.find(m('sheets')):
        t=tgt[sh.get('{%s}id'%R)]
        part=t.lstrip('/') if t.startswith('/') else ('xl/'+t if not t.startswith('xl/') else t)
        out.append((sh.get('name'),part))
    return out

def rel_targets(z, part, typ):
    rp=os.path.join(os.path.dirname(part),'_rels',os.path.basename(part)+'.rels')
    if rp not in z.namelist(): return []
    res=[]
    for r in ET.fromstring(z.read(rp)):
        if not r.get('Type','').endswith('/'+typ): continue
        raw=r.get('Target')
        if r.get('TargetMode')=='External': continue
        res.append((raw.lstrip('/') if raw.startswith('/') else os.path.normpath(os.path.join(os.path.dirname(part),raw))).replace('\\','/'))
    return res

def main():
    for path in sys.argv[1:]:
        z=zipfile.ZipFile(path)
        stem=os.path.basename(path)
        n=0
        for name,part in sheets(z):
            for pt in rel_targets(z,part,'pivotTable'):
                n+=1
                root=ET.fromstring(z.read(pt))
                loc=root.find(m('location'))
                ref=loc.get('ref'); fhr=int(loc.get('firstHeaderRow',1)); fdr=int(loc.get('firstDataRow',1)); fdc=int(loc.get('firstDataCol',0))
                pfs=list(root.find(m('pivotFields')) or [])
                rf=[int(f.get('x','-1')) for f in (root.find(m('rowFields')) or [])]
                cf=[int(f.get('x','-1')) for f in (root.find(m('colFields')) or [])]
                dfe=root.find(m('dataFields')); dfn=len(dfe) if dfe is not None else 0
                ri=root.find(m('rowItems')); ci=root.find(m('colItems'))
                nri=len(ri) if ri is not None else 0; nci=len(ci) if ci is not None else 0
                showDrill=flag(root,'showDrill',True)
                C=len(cf)
                if C==1 and cf[0]<0 and dfn<=1: C=0
                # cache
                cache='none'
                for cd in rel_targets(z,pt,'pivotCacheDefinition'):
                    try:
                        cr=ET.fromstring(z.read(cd))
                        cs=cr.find(m('cacheSource'))
                        cache=cs.get('type') if cs is not None else '?'
                    except Exception as e: cache='err'
                compacts=[]
                for x in rf:
                    if x<0 or x>=len(pfs): compacts.append(False); continue
                    pf=pfs[x]
                    compacts.append(flag(pf,'subtotalTop') and flag(pf,'outline') and flag(pf,'compact'))
                ok = (cache=='worksheet') and nri>0 and nci>0 and fhr==1 and fdr==1+C and fdc==len(rf) and fdc!=0 and not any(compacts)
                why=[]
                if cache!='worksheet': why.append('cache=%s'%cache)
                if nri==0: why.append('no rowItems')
                if nci==0: why.append('no colItems')
                if fhr!=1: why.append('fhr=%d'%fhr)
                if fdr!=1+C: why.append('fdr=%d vs %d'%(fdr,1+C))
                if fdc!=len(rf): why.append('fdc=%d vs rowFields=%d'%(fdc,len(rf)))
                if fdc==0: why.append('fdc=0')
                if any(compacts): why.append('compact')
                rowdata = any(x<0 for x in rf)
                print('%s\t%s\t%s\tref=%s fhr=%d fdr=%d fdc=%d rf=%s cf=%s df=%d ri=%d ci=%d drill=%s cache=%s rowDataPH=%s\t%s\t%s'%(
                    stem,name,os.path.basename(pt),ref,fhr,fdr,fdc,rf,cf,dfn,nri,nci,showDrill,cache,rowdata,
                    'ACCEPT' if ok else 'decline','; '.join(why)))
        if n==0: print('%s\t-\t-\tno pivot parts'%stem)
main()
