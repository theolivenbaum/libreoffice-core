#!/usr/bin/env python3
"""Predict the border grid ScDPOutput generates for an OOXML pivot table.

A direct transcription of sc/source/core/data/dpoutput.cxx's OutputBlockFrame,
outputColumnHeaders, outputRowHeader and OutputDataArea, fed from the pivot table
part's own laid-out axis (rowItems/colItems) instead of from a DP source.

Usage: pivot-grid.py <workbook.xlsx> [sheetIndexOrName]
Prints one line per cell that gains a border:
    <A1>  <left> <right> <top> <bottom>   widths in twips, 0 for none
"""
import sys, zipfile, re
import xml.etree.ElementTree as ET

MAIN = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
PR = 'http://schemas.openxmlformats.org/package/2006/relationships'
def m(t): return '{%s}%s' % (MAIN, t)

INNER, OUTER = 20, 40

def colnum(s):
    n = 0
    for ch in s: n = n * 26 + ord(ch) - 64
    return n - 1
def colname(c):
    s = ''; c += 1
    while c:
        c, r = divmod(c - 1, 26); s = chr(65 + r) + s
    return s
def parse_ref(ref):
    a, _, b = ref.partition(':')
    b = b or a
    ma = re.match(r'\$?([A-Z]+)\$?(\d+)', a); mb = re.match(r'\$?([A-Z]+)\$?(\d+)', b)
    return colnum(ma.group(1)), int(ma.group(2)) - 1, colnum(mb.group(1)), int(mb.group(2)) - 1

HASMEMBER, SUBTOTAL, CONTINUE = 1, 2, 4

class Grid:
    """The four edges of every cell a frame call touches."""
    def __init__(self): self.cells = {}
    def set(self, r, c, side, w):
        e = self.cells.setdefault((r, c), [0, 0, 0, 0])
        e['lrtb'.index(side)] = w

class Out:
    def __init__(self, tsc, tsr, dsc, dsr, tec, ter):
        self.tsc, self.tsr, self.dsc, self.dsr, self.tec, self.ter = tsc, tsr, dsc, dsr, tec, ter
        self.needCol = {}; self.needRow = {}
        self.cols = []; self.rows = []
        self.g = Grid()

    def AddRow(self, r):
        if not self.needRow.get(r): self.needRow[r] = True; self.rows.append(r)
    def AddCol(self, c):
        if not self.needCol.get(c): self.needCol[c] = True; self.cols.append(c)

    def block(self, c0, r0, c1, r1, hori=False):
        """OutputBlockFrame, dpoutput.cxx:217."""
        if c1 < c0 or r1 < r0: return
        L = OUTER if c0 == self.tsc else INNER
        T = OUTER if r0 == self.tsr else INNER
        R = OUTER if c1 == self.tec else INNER
        B = OUTER if r1 == self.ter else INNER
        for r in range(r0, r1 + 1):
            self.g.set(r, c0, 'l', L)
            self.g.set(r, c1, 'r', R)
        for c in range(c0, c1 + 1):
            self.g.set(r0, c, 't', T)
            self.g.set(r1, c, 'b', B)
        if hori:
            for r in range(r0, r1 + 1):
                for c in range(c0, c1 + 1):
                    if r != r0: self.g.set(r, c, 't', INNER)
                    if r != r1: self.g.set(r, c, 'b', INNER)

    def box(self, c, r, w):
        """lcl_SetFrame, dpoutput.cxx:297 — all four edges of one cell."""
        for s in 'lrtb': self.g.set(r, c, s, w)

    def data_area(self):
        """OutputDataArea, dpoutput.cxx:127."""
        self.AddRow(self.dsr); self.AddCol(self.dsc)
        cols = sorted(self.cols + [self.tec + 1]); rows = sorted(self.rows + [self.ter + 1])
        allRows = (self.ter - self.dsr + 2) == len(rows)
        for i in range(len(cols) - 1):
            if not allRows:
                if i < len(cols) - 2:
                    for k in range(i % 2, len(rows) - 2, 2):
                        self.block(cols[i], rows[k], cols[i + 1] - 1, rows[k + 1] - 1)
                    if len(rows) >= 2:
                        self.block(cols[i], rows[-2], cols[i + 1] - 1, rows[-1] - 1)
                else:
                    for k in range(len(rows) - 1):
                        self.block(cols[i], rows[k], cols[i + 1] - 1, rows[k + 1] - 1)
            else:
                self.block(cols[i], rows[0], cols[i + 1] - 1, rows[-1] - 1, True)
        if self.tsc != self.dsc:
            if self.tsr != self.dsr:
                self.block(self.tsc, self.tsr, self.dsc - 1, self.dsr - 1)
            self.block(self.tsc, self.dsr, self.dsc - 1, self.ter)
        self.block(self.dsc, self.tsr, self.tec, self.dsr - 1)


def axis_flags(items, nfields, count):
    """MemberResult flags per (field, position) out of rowItems/colItems."""
    flags = [[0] * count for _ in range(nfields)]
    for n, it in enumerate(items):
        if n >= count: break
        rep = int(it.get('r', 0))
        xs = it.findall(m('x'))
        t = it.get('t', 'data')
        depth = rep + max(len(xs), 1)
        if t == 'data':
            for f in range(min(rep, nfields)): flags[f][n] |= CONTINUE
            for f in range(rep, min(depth, nfields)): flags[f][n] |= HASMEMBER
        else:
            lvl = min(depth - 1, nfields - 1)
            for f in range(min(rep, lvl)): flags[f][n] |= CONTINUE
            if lvl >= 0: flags[lvl][n] |= HASMEMBER | SUBTOTAL
    return flags


def compact_flags(pivot):
    """maRowCompactFlags: pivottablebuffer.cxx:296 — subtotalTop && outline && compact."""
    fields = pivot.find(m('pivotFields'))
    fields = list(fields) if fields is not None else []
    rf = pivot.find(m('rowFields'))
    out = []
    for fld in (rf.findall(m('field')) if rf is not None else []):
        x = int(fld.get('x', '-1'))
        if x < 0 or x >= len(fields):
            out.append(False)       # the data-layout dimension is never compact
            continue
        pf = fields[x]
        def b(a, d='1'): return pf.get(a, d) not in ('0', 'false')
        out.append(b('subtotalTop') and b('outline') and b('compact'))
    return out


def generate(pivot, compact_row=None):
    loc = pivot.find(m('location'))
    tsc, tsr, tec, ter = parse_ref(loc.get('ref'))
    firstHeaderRow = int(loc.get('firstHeaderRow', 1))
    firstDataRow = int(loc.get('firstDataRow', 1))
    firstDataCol = int(loc.get('firstDataCol', 0))
    msr = tsr + firstHeaderRow
    dsr = tsr + firstDataRow
    dsc = tsc + firstDataCol
    rowFields = pivot.find(m('rowFields'))
    colFields = pivot.find(m('colFields'))
    R = len(rowFields.findall(m('field'))) if rowFields is not None else 0
    cf = colFields.findall(m('field')) if colFields is not None else []
    C = len(cf)
    # bColumnFieldIsDataOnly, dpoutput.cxx:1205 — one data field behind a bare data-layout
    # placeholder leaves Calc with no column field at all.
    df = pivot.find(m('dataFields'))
    if C == 1 and int(cf[0].get('x', '0')) < 0 and (len(df) if df is not None else 0) <= 1:
        C = 0
    rowItems = pivot.find(m('rowItems'))
    colItems = pivot.find(m('colItems'))
    if compact_row is None: compact_row = compact_flags(pivot)
    # CalcSizes, dpoutput.cxx:912-921 — the table's last row and column come from the
    # result's own extent, not from the stated ref, and the two disagree on
    # DynamicBubbleChart, whose ref stops one column short of its data.
    rowCount = len(rowItems) if rowItems is not None else 0
    colCount = len(colItems) if colItems is not None else 0
    ter = dsr + rowCount - 1 if rowCount else dsr
    tec = dsc + colCount - 1 if colCount else dsc

    o = Out(tsc, tsr, dsc, dsr, tec, ter)
    rflags = axis_flags(list(rowItems) if rowItems is not None else [], R, rowCount)
    cflags = axis_flags(list(colItems) if colItems is not None else [], C, colCount)

    # page fields — outputPageFields, dpoutput.cxx:969
    pf = pivot.find(m('pageFields'))
    P = len(pf.findall(m('pageField'))) if pf is not None else 0
    for n in range(P):
        o.box(tsc + 1, tsr - P - 1 + n, INNER)

    # outputColumnHeaders, dpoutput.cxx:1002
    hasCompactRow = bool(compact_row and any(compact_row))
    for f in range(C):
        headerCol = dsc + f
        if msr > tsr and (not hasCompactRow or C == 1):
            o.box(headerCol, tsr, INNER)
        rowPos = msr + f
        for n in range(colCount):
            colPos = dsc + n
            fl = cflags[f][n]
            if (fl & HASMEMBER) and not (fl & SUBTOTAL):
                end = n
                while end + 1 < colCount and (cflags[f][end + 1] & CONTINUE): end += 1
                endColPos = dsc + end
                if f + 1 < C:
                    if f + 2 == C:
                        o.AddCol(colPos)
                        if colPos + 1 == endColPos:
                            o.block(colPos, rowPos, endColPos, rowPos + 1, True)
                    else:
                        o.block(colPos, rowPos, endColPos, rowPos)
            elif fl & SUBTOTAL:
                # HeaderCell, dpoutput.cxx:748 — a subtotal member frames its own strip
                o.block(colPos, msr + f, colPos, dsr - 1)
                o.AddCol(colPos)
        if f == 0 and C == 1 and msr > tsr:
            o.block(dsc, tsr, tec, rowPos - 1)

    # outputRowHeader, dpoutput.cxx:1075
    setBorder = [False] * rowCount
    offset = 0
    for f in range(R):
        compact = bool(compact_row[f]) if compact_row else False
        if not hasCompactRow or R == 1:
            o.box(tsc + f, dsr - 1, INNER)
        colPos = tsc + offset
        for n in range(rowCount):
            rowPos = dsr + n
            fl = rflags[f][n]
            if (fl & HASMEMBER) and not (fl & SUBTOTAL):
                if f + 1 < R:
                    end = n
                    while end + 1 < rowCount and (rflags[f][end + 1] & CONTINUE): end += 1
                    endRowPos = dsr + end
                    o.AddRow(rowPos)
                    if not setBorder[n]:
                        o.block(colPos, rowPos, tec, endRowPos); setBorder[n] = True
                    o.block(colPos, rowPos, colPos, endRowPos)
                    if f == R - 2:
                        o.block(colPos + 1, rowPos, colPos + 1, endRowPos)
            elif fl & SUBTOTAL:
                # HeaderCell, dpoutput.cxx:748 — the subtotal's own row-header strip
                o.block(tsc + offset, rowPos, dsc - 1, rowPos)
                o.AddRow(rowPos)
        if not compact: offset += 1

    o.data_area()
    return o


def main():
    z = zipfile.ZipFile(sys.argv[1])
    for name in z.namelist():
        if re.match(r'xl/pivotTables/pivotTable\d+\.xml$', name):
            pivot = ET.fromstring(z.read(name))
            o = generate(pivot)
            print('## %s' % name)
            for (r, c), e in sorted(o.g.cells.items()):
                if any(e):
                    print('%s%d\t%d\t%d\t%d\t%d' % (colname(c), r + 1, e[0], e[1], e[2], e[3]))

if __name__ == '__main__':
    main()
