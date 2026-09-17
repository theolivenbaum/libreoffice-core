#!/usr/bin/env python3
"""Count horizontal grid lines carrying more than one band width, in any rendering.

**UNRECONCILED — do not quote this script's numbers.** It answers 0 mixed boundaries for
26.2.4.2's own rendering of `150_5300_13_chg8.doc` and `150-5370-10H.docx`, which round 146's
`census-pdf.py` scored at 210 and 206. One of the two readings is wrong and this round did not
establish which: `census-pdf.py` matches two shapes with a tolerance, including a narrower band
contained inside a wider one, where this groups on an exactly rounded top edge. It is kept
because the question it asks -- how many mixed boundaries are in OUR ink, not the reference's --
is the one that bounds this change, and because a discarded instrument is re-written by the next
round that wants it.

The point of running it on OUR output as well as the reference's: round 146's census read the
reference's ink, and that is an upper bound on what a change to our painter can move. Where our
own border resolution gives a boundary one width, the boundary is not mixed for us and nothing
moves however mixed the reference draws it.

**A stroke-only reader answers zero for the reference and means nothing by it.** 26.2.4.2 draws
most table borders as filled rectangles rather than as stroked lines, so a script matching only
`l` segments reports 0 mixed boundaries on a document round 146 scored at 210. Both operators
are read here, and the first cut of this file did not — which is the same shape of error as
counting `sh` against `Do` when comparing gradients.
"""
import collections
import sys

import pymupdf


def mixed(path):
    document = pymupdf.open(path)
    lines = collections.defaultdict(set)
    for page in document:
        for item in page.get_drawings():
            kind = item.get('type', '')
            for segment in item['items']:
                if segment[0] == 'l':
                    a, b = segment[1], segment[2]
                    if abs(a.y - b.y) > 0.01 or abs(a.x - b.x) < 4:
                        continue
                    width = round(item.get('width') or 0.0, 3)
                    lines[(page.number, round(a.y - (width / 2), 2))].add(width)
                elif segment[0] == 're' and kind in ('f', 'fs', 's'):
                    box = segment[1]
                    if box.height > 6.0 or box.width < 4.0:
                        continue
                    lines[(page.number, round(box.y0, 2))].add(round(box.height, 3))
    return sum(1 for widths in lines.values() if len(widths) > 1), len(lines)


def main():
    for path in sys.argv[1:]:
        count, total = mixed(path)
        print('%5d mixed of %5d horizontal grid lines   %s' % (count, total, path))


if __name__ == '__main__':
    main()
