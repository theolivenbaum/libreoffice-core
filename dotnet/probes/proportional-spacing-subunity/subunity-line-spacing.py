#!/usr/bin/env python3
"""Proportional line spacing *below* 100%, which an earlier probe never tested.

Why this exists
---------------

An earlier probe on this project measured `w:line` with `w:lineRule="auto"` at
240, 264, 276, 288, 300 and 360 twentieths and found our line pitch agreeing
with the reference to **0.000 pt on every one**. That result was used to rule
proportional line spacing out as a cause on `OM template`, and it was sound as
far as it went.

Every value it tested was **at or above 100%**. The regime below was never
looked at, and it is wrong.

What this measures, 2026-08-15, Liberation Serif 11 pt, six lines per group so
the pitch cannot be contaminated by first-line rules. Both columns are whole
twips on both sides, so every disagreement is a rounding decision:

    w:line   percent   ref tw   our tw   delta pt
       120     50.00      126      127     +0.05
       150     62.50      159      157     -0.10
       168     70.00      177      178     +0.05
       180     75.00      189      190     +0.05
       192     80.00      202      203     +0.05
       200     83.33      209      210     +0.05
       210     87.50      220      223     +0.15
       220     91.67      232      233     +0.05
       233     97.08      245      246     +0.05
       239     99.58      253      253      0.00
       240    100.00      253      253      0.00
       260    108.33      275      273     -0.10

**A first, narrower sweep of this probe tested only 180, 200, 220, 233, 239,
240 and 260, and every sub-unity value came out at exactly +1 twip.** That
looked like a clean off-by-one and would have been implemented as one. Widening
it breaks that: 62.50% is **two twips short** and 87.50% is **three twips
long**. There is no single-twip correction to make.

The two outliers are exactly the two half-percent values, which is a lead
rather than an answer.

A candidate rule — take the percentage as `round(w:line × 100 / 240)`, then the
height as `floor(base × pct / 100)` with base 253 twips — reproduces **6 of the
8** measured points, and the two it misses are 62.50 and 87.50. It also
explains why 99.58% returns the 100% answer, since it rounds to 100. It is not
good enough to build on, and the half-percent behaviour has to be settled
first; whether LibreOffice rounds half away from zero, to even, or does not
round the percentage at all is exactly what those two points disagree about.

**Note the probe currently reports only the first eight groups** — the page is
not tall enough for nineteen and the later ones are silently dropped by the
grouping. Raise `w:h` before trusting a longer sweep. The four rows below 220
in the table above came from this run; the last four came from the narrow one.

Where it matters
----------------

`SPA-02_mcar_part-2_and_IS_v2.9.docx` and `02_mcar_part-2_and_IS_v2.10.docx`
set their table body paragraphs at `w:line="233" w:lineRule="auto"` — 97.08% —
and only 12 of 305 rows declare a `w:trHeight`, so for the rest the line height
*is* the row height. The +0.05 pt here is the +0.0525 pt per row measured
directly on those tables by `probes/row-boundary-drift/`, and it accumulates to
+1.08 pt over 24 rows, which is what decides where a long table breaks across a
page. Both documents fail on page count alone with words inside the band.

The rule is **not derived**, and the widening above is why that matters: the
narrow sweep gave a clean, wrong answer that a round would have shipped.

Usage
-----

    PAPERLESS_CLI=... python3 subunity-line-spacing.py /abs/workdir
"""

import os
import re
import subprocess
import sys
import zipfile
from collections import defaultdict

LINES = [120, 150, 168, 180, 192, 200, 210, 220, 228, 233, 236, 239, 240, 242, 250, 260, 264, 270, 288]
FACE = 'Liberation Serif'
HALF_POINTS = 22

CT = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
      '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
      '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
      '<Default Extension="xml" ContentType="application/xml"/>'
      '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-'
      'officedocument.wordprocessingml.document.main+xml"/></Types>')
RELS = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/'
        'relationships/officeDocument" Target="word/document.xml"/></Relationships>')
W = 'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"'


def build(path: str) -> list[str]:
    body, labels = '', []
    run = f'<w:rFonts w:ascii="{FACE}" w:hAnsi="{FACE}"/><w:sz w:val="{HALF_POINTS}"/>'

    for line in LINES:
        body += ''.join(
            f'<w:p><w:pPr><w:spacing w:line="{line}" w:lineRule="auto" w:before="0" w:after="0"/>'
            f'<w:rPr>{run}</w:rPr></w:pPr><w:r><w:rPr>{run}</w:rPr>'
            f'<w:t>L{i} line{line}</w:t></w:r></w:p>'
            for i in range(6))
        labels.append(f'w:line={line} ({line / 240 * 100:.2f}%)')
        # A tall blank between groups so the reader can separate them by gap alone.
        body += ('<w:p><w:pPr><w:spacing w:line="240" w:lineRule="auto" '
                 'w:before="300" w:after="300"/></w:pPr></w:p>')

    document = (
        f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document {W}><w:body>{body}'
        '<w:sectPr><w:pgSz w:w="11906" w:h="33000"/>'
        '<w:pgMar w:top="567" w:right="567" w:bottom="567" w:left="567"/></w:sectPr>'
        '</w:body></w:document>')

    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('[Content_Types].xml', CT)
        z.writestr('_rels/.rels', RELS)
        z.writestr('word/document.xml', document)
    return labels


def baselines(pdf: str) -> list[float]:
    text = subprocess.run(['pdftotext', '-bbox', '-f', '1', '-l', '1', pdf, '-'],
                          capture_output=True, text=True, check=True).stdout
    found = sorted({round(float(m.group(1)), 3)
                    for m in re.finditer(r'yMin="([\d.]+)"', text)})
    if not found:
        raise SystemExit(f'no text in {pdf} — did it render?')
    return found


def groups(ys: list[float]) -> list[list[float]]:
    out = [[ys[0]]]
    for a, b in zip(ys, ys[1:]):
        if b - a > 25:
            out.append([b])
        else:
            out[-1].append(b)
    return [g for g in out if len(g) >= 5]


def main() -> int:
    out = sys.argv[1] if len(sys.argv) > 1 else '/tmp/subunity-line-spacing'
    cli = os.environ.get('PAPERLESS_CLI')
    if not cli:
        raise SystemExit('set PAPERLESS_CLI to the tree you mean to measure')

    os.makedirs(os.path.join(out, 'ours'), exist_ok=True)
    docx = os.path.join(out, 'sub.docx')
    labels = build(docx)

    subprocess.run(['soffice', '--headless', '--convert-to', 'pdf', '--outdir', out, docx],
                   capture_output=True, check=True)
    subprocess.run([cli, 'render', '--quiet', '--outdir', os.path.join(out, 'ours'), docx],
                   check=True)

    reference, ours = os.path.join(out, 'sub.pdf'), os.path.join(out, 'ours', 'sub.pdf')
    for path in (reference, ours):
        if not os.path.isfile(path):
            raise SystemExit(f'{path} was not written — nothing to compare')

    r, o = groups(baselines(reference)), groups(baselines(ours))
    print(f"{'case':22s} {'REF pitch':>10s} {'OUR pitch':>10s} {'delta':>9s} {'ref tw':>7s} {'our tw':>7s}")

    for i in range(min(len(r), len(o), len(labels))):
        ref = (r[i][-1] - r[i][0]) / (len(r[i]) - 1)
        our = (o[i][-1] - o[i][0]) / (len(o[i]) - 1)
        print(f'{labels[i]:22s} {ref:10.4f} {our:10.4f} {our - ref:+9.4f} '
              f'{ref * 20:7.1f} {our * 20:7.1f}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
