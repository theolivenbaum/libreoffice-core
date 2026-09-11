#!/usr/bin/env python3
r"""Resolve the HEAD paragraph's properties out of a flat ODF file.

`soffice --convert-to fodt` prints the reference's own resolved view of a document in ~0.15 s per
file when a whole directory is handed to one process, against ~8 s a document for a PDF render —
so this is the instrument for "what did the style resolve to", and rendering is kept for "did the
page move".

The chain walked is `style:parent-style-name`, nearest first: a paragraph names one style, that
style may name a parent, and Writer writes only what each level states. That is exactly the pool
chain the question is about, so an inherited value shows up as a property found at an *ancestor*
and the level it was found at is reported.

  readfodt.py <dir of .fodt> [<second dir>]
"""
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

NS = {
    'office': 'urn:oasis:names:tc:opendocument:xmlns:office:1.0',
    'style': 'urn:oasis:names:tc:opendocument:xmlns:style:1.0',
    'text': 'urn:oasis:names:tc:opendocument:xmlns:text:1.0',
    'fo': 'urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0',
}
Q = {k: '{%s}' % v for k, v in NS.items()}

WANT = [
    ('size', Q['fo'] + 'font-size', 'text'),
    ('italic', Q['fo'] + 'font-style', 'text'),
    ('weight', Q['fo'] + 'font-weight', 'text'),
    ('above', Q['fo'] + 'margin-top', 'para'),
    ('below', Q['fo'] + 'margin-bottom', 'para'),
    ('left', Q['fo'] + 'margin-left', 'para'),
    ('keep', Q['fo'] + 'keep-with-next', 'para'),
]


def styles_of(root):
    out = {}
    for holder in ('styles', 'automatic-styles'):
        node = root.find(Q['office'] + holder)
        if node is None:
            continue
        for st in node.findall(Q['style'] + 'style'):
            if st.get(Q['style'] + 'family') != 'paragraph':
                continue
            name = st.get(Q['style'] + 'name')
            para = st.find(Q['style'] + 'paragraph-properties')
            text = st.find(Q['style'] + 'text-properties')
            tabs = []
            if para is not None:
                stops = para.find(Q['style'] + 'tab-stops')
                if stops is not None:
                    tabs = [(s.get(Q['style'] + 'position'), s.get(Q['style'] + 'type') or 'left')
                            for s in stops.findall(Q['style'] + 'tab-stop')]
            out[name] = {
                'parent': st.get(Q['style'] + 'parent-style-name'),
                'para': para, 'text': text, 'tabs': tabs,
            }
    return out


def resolve(styles, start):
    chain, seen, cur = [], set(), start
    while cur and cur in styles and cur not in seen:
        seen.add(cur)
        chain.append(cur)
        cur = styles[cur]['parent']
    row = {'chain': '>'.join(chain)}
    for key, attr, where in WANT:
        for depth, name in enumerate(chain):
            node = styles[name][where]
            if node is not None and node.get(attr) is not None:
                row[key] = node.get(attr) + ('' if depth == 0 else '@' + chain[depth])
                break
        else:
            row[key] = '-'
    for depth, name in enumerate(chain):
        if styles[name]['tabs']:
            row['tabs'] = ','.join(f'{p}:{t}' for p, t in styles[name]['tabs'])
            row['tabs'] += '' if depth == 0 else '@' + chain[depth]
            break
    else:
        row['tabs'] = '-'
    return row


def read(path):
    root = ET.parse(path).getroot()
    styles = styles_of(root)
    body = root.find(Q['office'] + 'body/' + Q['office'] + 'text')
    for p in body.iter(Q['text'] + 'p'):
        if ''.join(p.itertext()).strip().startswith('HEAD') or \
           ''.join(p.itertext()).strip().endswith('HEAD'):
            return resolve(styles, p.get(Q['text'] + 'style-name'))
        if 'HEAD' in ''.join(p.itertext()):
            return resolve(styles, p.get(Q['text'] + 'style-name'))
    return None


dirs = [pathlib.Path(d) for d in sys.argv[1:]] or [pathlib.Path('.')]
names = sorted({p.stem for d in dirs for p in d.glob('p_*.fodt')})
width = max((len(n) for n in names), default=10)
print(f'{"probe":<{width}}  {"style chain":<34} {"size":<12} {"ital":<8} {"above":<10} '
      f'{"below":<10} tabs')
for name in names:
    for d in dirs:
        path = d / f'{name}.fodt'
        row = read(path) if path.exists() else None
        if row is None:
            print(f'{name:<{width}}  -')
            continue
        print(f'{name:<{width}}  {row["chain"]:<34} {row["size"]:<12} {row["italic"]:<8} '
              f'{row["above"]:<10} {row["below"]:<10} {row["tabs"]}')
