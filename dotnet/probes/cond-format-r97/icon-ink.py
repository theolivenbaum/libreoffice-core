#!/usr/bin/env python3
"""How much ink an `iconSet` rule is actually worth, from the reference's own renderings.

An icon is a 16 x 16 bitmap `drawIconSets` scales to ten points square
(`sc/source/ui/view/output.cxx`:955-985), so it is the only conditional format in the file that
arrives in a PDF as an *image*. Counting the small images in 26.2.4.2's rendering of each
document that states such a rule is therefore an exact reach figure for the family, and it needs
no comparison against our side at all.

Reads the banked gate reference at /home/user/gate-orig-r83/ref/.

Usage: icon-ink.py <identity-without-__xlsx> ...
"""
import os
import sys

import pymupdf

total = 0
for name in sys.argv[1:]:
    path = f'/home/user/gate-orig-r83/ref/{name}__xlsx.pdf'
    if not os.path.exists(path):
        print(f'{name} MISSING')
        continue
    doc = pymupdf.open(path)
    icons = 0
    area = 0.0
    for page in doc:
        for info in page.get_image_info():
            # A conditional-format icon is 16 x 16 in the file; anything larger is a picture.
            if info['width'] <= 32 and info['height'] <= 32:
                icons += 1
                box = info['bbox']
                area += (box[2] - box[0]) * (box[3] - box[1])
    print(f'{name[:56]:56} pages {len(doc):4d}  icons {icons:3d}  area {area:8.1f} pt2')
    total += icons
print('total icons the reference draws:', total)
