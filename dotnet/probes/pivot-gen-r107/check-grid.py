#!/usr/bin/env python3
"""Score pivot-grid.py's prediction against 26.2.4.2's own resolved view.

For each pivot table in a workbook, predicts the generated border grid from the
pivot part alone and compares it, edge by edge, against the same cells in the
`.fods` the reference writes for that workbook.

Usage: check-grid.py <workbook.xlsx> <workbook.fods>
"""
import sys, os, re, zipfile
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib.util
spec = importlib.util.spec_from_file_location(
    'pg', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'pivot-grid.py'))
pg = importlib.util.module_from_spec(spec); spec.loader.exec_module(pg)

MAIN = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
REL = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
PKR = 'http://schemas.openxmlformats.org/package/2006/relationships'
def m(t): return '{%s}%s' % (MAIN, t)

FODNS = {'office': 'urn:oasis:names:tc:opendocument:xmlns:office:1.0',
         'table': 'urn:oasis:names:tc:opendocument:xmlns:table:1.0',
         'style': 'urn:oasis:names:tc:opendocument:xmlns:style:1.0',
         'fo': 'urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0'}
def fq(n):
    p, l = n.split(':'); return '{%s}%s' % (FODNS[p], l)


def sheet_pivots(z):
    """[(sheet name, sheet order, [pivot roots])] for the workbook."""
    wb = ET.fromstring(z.read('xl/workbook.xml'))
    rels = ET.fromstring(z.read('xl/_rels/workbook.xml.rels'))
    target = {r.get('Id'): r.get('Target') for r in rels}
    out = []
    for i, sh in enumerate(wb.find(m('sheets'))):
        t = target[sh.get('{%s}id' % REL)]
        part = t.lstrip('/') if t.startswith('/') else ('xl/' + t if not t.startswith('xl/') else t)
        rp = os.path.join(os.path.dirname(part), '_rels', os.path.basename(part) + '.rels')
        pivots = []
        if rp in z.namelist():
            for r in ET.fromstring(z.read(rp)):
                if r.get('Type', '').endswith('/pivotTable'):
                    raw = r.get('Target')
                    tgt = (raw.lstrip('/') if raw.startswith('/')
                           else os.path.normpath(os.path.join(os.path.dirname(part), raw)))
                    pivots.append(ET.fromstring(z.read(tgt.replace('\\', '/'))))
        out.append((sh.get('name'), i, pivots))
    return out


def fods_borders(path, sheet):
    """(row, col) -> [left, right, top, bottom] in twips, for one sheet."""
    root = ET.parse(path).getroot()
    styles = {}
    for holder in root:
        if holder.tag not in (fq('office:automatic-styles'), fq('office:styles')): continue
        for st in holder.findall(fq('style:style')):
            props = st.find(fq('style:table-cell-properties'))
            b = {}
            if props is not None:
                for side, attr in (('all', 'fo:border'), ('left', 'fo:border-left'),
                                   ('right', 'fo:border-right'), ('top', 'fo:border-top'),
                                   ('bottom', 'fo:border-bottom')):
                    v = props.get(fq(attr))
                    if v is not None: b[side] = v
            styles[st.get(fq('style:name'))] = (b, st.get(fq('style:parent-style-name')))

    cache = {}
    def resolve(name):
        if name in cache: return cache[name]
        chain, seen, n = [], set(), name
        while n and n in styles and n not in seen:
            seen.add(n); chain.append(n); n = styles[n][1]
        sides = {'left': None, 'right': None, 'top': None, 'bottom': None}
        for n in reversed(chain):
            b = styles[n][0]
            if 'all' in b:
                for s in sides: sides[s] = b['all']
            for s in sides:
                if s in b: sides[s] = b[s]
        v = [twips(sides['left']), twips(sides['right']), twips(sides['top']), twips(sides['bottom'])]
        cache[name] = v
        return v

    body = root.find(fq('office:body')).find(fq('office:spreadsheet'))
    for table in body.findall(fq('table:table')):
        if table.get(fq('table:name')) != sheet: continue
        coldef = []
        for col in table.findall(fq('table:table-column')):
            crep = int(col.get(fq('table:number-columns-repeated'), 1))
            coldef += [col.get(fq('table:default-cell-style-name'))] * min(crep, 1024)
        grid = {}
        r = -1
        for row in table.findall(fq('table:table-row')):
            rep = int(row.get(fq('table:number-rows-repeated'), 1))
            rowdef = row.get(fq('table:default-cell-style-name'))
            cells = [c for c in row if c.tag in (fq('table:table-cell'), fq('table:covered-table-cell'))]
            if rep > 4096: rep = 4096
            for _ in range(rep):
                r += 1
                c = -1
                for cell in cells:
                    crep = min(int(cell.get(fq('table:number-columns-repeated'), 1)), 1024)
                    sn0 = cell.get(fq('table:style-name'))
                    for _ in range(crep):
                        c += 1
                        eff = sn0 or rowdef or (coldef[c] if c < len(coldef) else None)
                        if eff is None: continue
                        v = resolve(eff)
                        if any(v): grid[(r, c)] = v
        return grid
    return {}


def twips(v):
    if not v or v.startswith('none') or v == '-': return 0
    mm = re.match(r'([0-9.]+)(pt|in|cm|mm)', v)
    if not mm: return -1
    n = float(mm.group(1)); u = mm.group(2)
    pt = n if u == 'pt' else n * 72 if u == 'in' else n * 72 / 2.54 if u == 'cm' else n * 72 / 25.4
    return int(round(pt * 20))


def main():
    xlsx, fods = sys.argv[1], sys.argv[2]
    z = zipfile.ZipFile(xlsx)
    total = agree = 0
    for name, _, pivots in sheet_pivots(z):
        if not pivots: continue
        oracle = fods_borders(fods, name)
        for pi, pivot in enumerate(pivots):
            try:
                o = pg.generate(pivot)
            except Exception as e:
                print('%s\t%s#%d\tFAILED\t%s' % (os.path.basename(xlsx), name, pi, e)); continue
            t = a = 0
            bad = []
            for (r, c), e in o.g.cells.items():
                got = oracle.get((r, c), [0, 0, 0, 0])
                for k in range(4):
                    if e[k] == 0: continue
                    t += 1
                    # 0.99pt/2.01pt round-trips; allow one twip either way
                    if abs(got[k] - e[k]) <= 1: a += 1
                    elif len(bad) < 6: bad.append((pg.colname(c) + str(r + 1), 'lrtb'[k], e[k], got[k]))
            total += t; agree += a
            print('%s\t%s#%d\tedges=%d\tagree=%d\t%.1f%%\t%s' % (
                os.path.basename(xlsx), name, pi, t, a, 100.0 * a / t if t else 0, bad))
    if total:
        print('TOTAL\t%s\tedges=%d\tagree=%d\t%.2f%%' % (os.path.basename(xlsx), total, agree, 100.0 * agree / total))

main()
