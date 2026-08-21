#!/usr/bin/env python3
"""Glyphs drawn in the pure-fallback faces, ours and reference, leaning and flat.

A "pure-fallback face" here is one that only ever arrives through FontItemiser on this machine
because no corpus document names the family: WenQuanYi Zen Hei, OpenSymbol and IPA Gothic.
DejaVu Sans/Serif are deliberately NOT in the list -- they are also the *substitution* answer for
unrecognised families, and a PDF cannot tell the two routes apart.  So this is the part of the
reach that can be bounded from outside; the DejaVu part cannot.
"""
import glob, os, sys, importlib.util, collections
HERE='/c/sandbox/workdir/wt-words-r50/dotnet/probes/words-r56'
_s=importlib.util.spec_from_file_location("sf", os.path.join(HERE,"shear-faces.py"))
sf=importlib.util.module_from_spec(_s); _s.loader.exec_module(sf)
FB={'WenQuanYiZenHei','OpenSymbol','IPAGothic','IPAPGothic','TakaoPGothic','OpenSymbol-Regular'}
ours_dir, ref_dir, label = sys.argv[1], sys.argv[2], sys.argv[3]
refs=sorted(glob.glob(os.path.join(ref_dir,'*.pdf')))
missing=[p for p in refs if not os.path.exists(os.path.join(ours_dir,os.path.basename(p)))]
if missing:
    print('REFUSING (%s): %d of %d have no ours' % (label,len(missing),len(refs))); sys.exit(2)
ourl=ourf=refl=reff=0; docs=collections.Counter()
for p in refs:
    ident=os.path.basename(p)[:-4]
    ol,of=sf.census(os.path.join(ours_dir,ident+'.pdf')); rl,rf=sf.census(p)
    a=sum(v for k,v in ol.items() if sf.strip(k) in FB); b=sum(v for k,v in of.items() if sf.strip(k) in FB)
    c=sum(v for k,v in rl.items() if sf.strip(k) in FB); d=sum(v for k,v in rf.items() if sf.strip(k) in FB)
    ourl+=a; ourf+=b; refl+=c; reff+=d
    if a or b or c or d: docs[ident]=(a,b,c,d)
print('%s: %d documents draw a pure-fallback face' % (label,len(docs)))
print('  ours      lean %6d   flat %6d   total %6d' % (ourl,ourf,ourl+ourf))
print('  reference lean %6d   flat %6d   total %6d' % (refl,reff,refl+reff))
print('  documents where the reference leans some of it and we lean none:')
for d,(a,b,c,e) in sorted(docs.items(), key=lambda t:-t[1][2]):
    if c and not a: print('    ref-lean %5d  our-flat %5d  %s' % (c,b,d))
