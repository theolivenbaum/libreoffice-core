#!/usr/bin/env python3
"""When does Calc draw nothing at all for a wrapped cell in a fixed-height row?

Why this exists
---------------

`sheets/unstable-001/xlsx/fse_identification_form.xlsx` is 440 words against 427,
and the whole difference is one sentence we draw and LibreOffice does not:
cell B16, "The serial number of the FSE assigned by the Original Equipment
Manufacturer (OEM)." Rasterised at 150 dpi its row band is 0.06 % dark in the
reference against 6.12 % in ours, so the reference draws no glyphs there at all —
this is not white text and not something covering it.

B16 is the odd one out among its neighbours in exactly one way:

    cell  chars  style           row height
    B12      66  wrap            auto
    B13     507  wrap            61.5   fixed
    B14     347  wrap + vcentre  44.1   fixed
    B15     237  wrap + vcentre  39.95  fixed
    B16      87  wrap            14.45  fixed   <- drawn by us, not by the reference
    B17      31  wrap            auto
    B18      43  wrap            15.75  fixed

Every neighbour either has room for its text or is free to grow. B16 has 87
characters in a row pinned to a single line's height. So the question is what
Calc does when wrapped text needs more lines than a `customHeight` row allows —
and specifically whether it clips to the visible band, which would still show
*some* text, or suppresses the cell entirely, which is what the reference does.

This is worth measuring rather than reasoning about, because both behaviours are
plausible and they differ in what a reader sees.

What it builds
--------------

One sheet per case, each a merged `B:E` cell with `wrapText`, sweeping two
variables independently:

- how many characters the cell holds, so the wrapped text needs 1, 2, 3 or 5
  lines at the merged width;
- the row's height: `auto` (no `ht`), and fixed at one, two and three lines.

The control is the auto-height row, which must always draw everything.

What it found, 2026-08-16 — and what the ABLATION found instead
---------------------------------------------------------------

**This synthetic does not reproduce the corpus case, and that is worth knowing
before it is trusted.** Across all sixteen cases the reference draws essentially
every word, including 300 characters in a 14.45 pt row, so a synthetic alone
would have refuted the suppression rule. The `auto` control agrees throughout
once each case's words are made unique — the first cut of this probe counted
consecutive filler words after a marker and ran into the neighbouring row, which
made the control disagree and looked like a renderer difference.

The rule is real, and it was established by ablating the corpus file itself
rather than by building one. Mutating `fse_identification_form.xlsx` one property
at a time and re-rendering through LibreOffice:

    case          does the reference draw B16?
    baseline      no
    no-height     YES     (drop ht="14.45" customHeight="1" from row 16)
    tall-row      YES     (raise that height to 40)
    no-merge16    no      (drop the B16:G16 merge)
    no-wrap       YES     (give B16 a style without wrapText)

So the suppression needs *both* `wrapText` and a `customHeight` row too short for
the wrapped result, and it is total — not a clip that leaves a partial line.

The threshold is tight, which is why the synthetic misses it: the workbook
declares `defaultRowHeight="15"` and pins row 16 to **14.45**, 0.55 pt *below* one
line of its 11 pt Calibri. The 87 characters fit one line across the ~760 pt
merged band, so even that single line does not fit the row, and nothing is drawn.
Its neighbours all have room — B13 is 507 characters in a 61.5 pt row, B15 is 237
in 39.95 — and all of them are drawn.

Reading the output
------------------

`ref` and `ours` are how many of the cell's words each rendering puts on the
page. The interesting rows are the ones where the text needs more lines than the
height allows:

- both drawing everything means the row height does not clip at all;
- the reference drawing *some* means it clips to the band, and the rule is a
  partial one;
- the reference drawing **none** while we draw everything is the corpus case, and
  the rule is "wrapped text that does not fit a fixed-height row is not drawn".

Usage
-----

    PAPERLESS_CLI=... python3 merged-wrap-fixed-height.py /abs/workdir
"""

import os
import subprocess
import sys
import zipfile

# One line of 11 pt Calibri is about 14.4 pt; the merged B:E band is wide enough
# that these lengths wrap to roughly one, two, three and five lines.
LENGTHS = [30, 90, 170, 300]
HEIGHTS = [None, 14.45, 29.0, 43.5]

CONTENT_TYPES = (
    '<?xml version="1.0"?>'
    '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
    '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.'
    'relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>'
    '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-'
    'officedocument.spreadsheetml.sheet.main+xml"/>'
    '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.'
    'openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
    '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-'
    'officedocument.spreadsheetml.styles+xml"/></Types>')

ROOT_RELS = (
    '<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/'
    'package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.'
    'openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
    'Target="xl/workbook.xml"/></Relationships>')

WORKBOOK_RELS = (
    '<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/'
    'package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.'
    'openxmlformats.org/officeDocument/2006/relationships/worksheet" '
    'Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.'
    'openxmlformats.org/officeDocument/2006/relationships/styles" '
    'Target="styles.xml"/></Relationships>')

WORKBOOK = (
    '<?xml version="1.0"?><workbook xmlns="http://schemas.openxmlformats.org/'
    'spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/'
    'officeDocument/2006/relationships"><sheets><sheet name="S" sheetId="1" '
    'r:id="rId1"/></sheets></workbook>')

# Style 1 is the one under test: wrapped and left-aligned, like the corpus cell's.
STYLES = (
    '<?xml version="1.0"?><styleSheet xmlns="http://schemas.openxmlformats.org/'
    'spreadsheetml/2006/main">'
    '<fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>'
    '<fills count="1"><fill><patternFill patternType="none"/></fill></fills>'
    '<borders count="1"><border/></borders>'
    '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
    '<cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>'
    '<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1">'
    '<alignment horizontal="left" wrapText="1"/></xf></cellXfs>'
    '</styleSheet>')


def words(length, tag):
    """Words unique to this case, so the count cannot run into a neighbouring row."""
    out = []
    index = 0
    while len(' '.join(out)) < length:
        out.append(f'{tag}x{index}')
        index += 1
    return ' '.join(out)


def build(path, cases):
    rows, merges = '', ''

    for index, (length, height, tag) in enumerate(cases):
        r = index + 1
        ht = f' ht="{height}" customHeight="1"' if height is not None else ''
        rows += (
            f'<row r="{r}" spans="1:5"{ht}>'
            f'<c r="A{r}" t="inlineStr"><is><t>row{r}</t></is></c>'
            f'<c r="B{r}" s="1" t="inlineStr"><is><t>{words(length, tag)}</t></is></c>'
            '</row>')
        merges += f'<mergeCell ref="B{r}:E{r}"/>'

    sheet = (
        '<?xml version="1.0"?><worksheet xmlns="http://schemas.openxmlformats.org/'
        'spreadsheetml/2006/main">'
        f'<dimension ref="A1:E{len(cases)}"/>'
        '<cols><col min="1" max="1" width="12" customWidth="1"/>'
        '<col min="2" max="5" width="20" customWidth="1"/></cols>'
        f'<sheetData>{rows}</sheetData><mergeCells count="{len(cases)}">{merges}</mergeCells>'
        '</worksheet>')

    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as package:
        package.writestr('[Content_Types].xml', CONTENT_TYPES)
        package.writestr('_rels/.rels', ROOT_RELS)
        package.writestr('xl/_rels/workbook.xml.rels', WORKBOOK_RELS)
        package.writestr('xl/workbook.xml', WORKBOOK)
        package.writestr('xl/styles.xml', STYLES)
        package.writestr('xl/worksheets/sheet1.xml', sheet)


def drawn(pdf, tag):
    """How many of this case's own words reached the page — they appear nowhere else."""
    text = subprocess.run(['pdftotext', pdf, '-'], capture_output=True, text=True,
                          check=True).stdout
    return len({w for w in text.split() if w.startswith(tag + 'x')})


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else '/tmp/merged-wrap'
    cli = os.environ.get('PAPERLESS_CLI')
    if not cli:
        raise SystemExit('set PAPERLESS_CLI to the tree you mean to measure')

    os.makedirs(os.path.join(out, 'ours'), exist_ok=True)

    cases = []
    for length in LENGTHS:
        for height in HEIGHTS:
            tag = f'C{length}H{"A" if height is None else int(height)}'
            cases.append((length, height, tag))

    xlsx = os.path.join(out, 'wrap.xlsx')
    build(xlsx, cases)

    subprocess.run(['soffice', '--headless', '--convert-to', 'pdf', '--outdir', out, xlsx],
                   capture_output=True, check=True)
    subprocess.run([cli, 'render', '--quiet', '--outdir', os.path.join(out, 'ours'), xlsx],
                   check=True)

    reference = os.path.join(out, 'wrap.pdf')
    ours = os.path.join(out, 'ours', 'wrap.pdf')
    for produced in (reference, ours):
        if not os.path.isfile(produced):
            raise SystemExit(f'{produced} was not written — nothing to compare')

    print(f"{'chars':>6s} {'height':>7s} {'total':>6s} {'ref':>6s} {'ours':>6s}  agree?")

    for length, height, tag in cases:
        r, o = drawn(reference, tag), drawn(ours, tag)
        total = len(words(length, tag).split())
        name = 'auto' if height is None else f'{height:g}'
        print(f'{length:6d} {name:>7s} {total:6d} {r:6d} {o:6d}  {"yes" if r == o else "NO"}')

    print('\nthe auto-height rows are the control and must always agree.')
    print('a reference count of 0 where ours is not is the corpus case.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
