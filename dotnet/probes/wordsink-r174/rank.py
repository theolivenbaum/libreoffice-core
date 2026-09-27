#!/usr/bin/env python3
"""Rank the words track by ink, which is the one measure the gate is blind to.

The gate is page count, alphanumerics within max(2 %, 15) and unembedded fonts, and a whole track
can be page-exact while its pages are visibly wrong -- a border, a fill, a bullet, a missing
arrowhead and a displaced block all add no character and no page.  So this scores every document
on summed unsigned per-page ink against 26.2.4.2 and prints the gate verdict beside it, because
the interesting rows are the ones that PASS and are still far out.

A MAJOR page is one whose |ink| exceeds one per cent of the page; a percentage summed over pages
is not comparable between a 5-page document and a 200-page one, so the page count and the per-page
mean are printed with it.
"""
import hashlib, pathlib, sys


def key(path):
    return hashlib.md5(str(path).encode()).hexdigest()[:16]


def load(pdf):
    import fitz
    with fitz.open(pdf) as doc:
        text = "".join(page.get_text() for page in doc)
        ink = []
        for page in doc:
            pixmap = page.get_pixmap(dpi=36, colorspace=fitz.csGRAY)
            data = pixmap.samples
            ink.append(sum(255 - b for b in data) / (255.0 * len(data)))
        return doc.page_count, sum(c.isalnum() for c in text), ink


def verdict(ours, reference):
    if ours[0] != reference[0]:
        return "pages"
    d = abs(ours[1] - reference[1])
    return "match" if not (d > reference[1] * 0.02 and d > 15) else "glyphs"


def main():
    root = pathlib.Path(sys.argv[1])
    rows = []
    for line in open(sys.argv[2]):
        path = pathlib.Path(line.strip())
        if not path.name:
            continue
        a = next((root / "ours" / key(path)).glob("*.pdf"), None)
        b = next((root / "ref" / key(path)).glob("*.pdf"), None)
        if a is None or b is None:
            print(f"# missing {'ours' if a is None else 'ref'}\t{path.name}", file=sys.stderr)
            continue
        ours, reference = load(a), load(b)
        pages = reference[0]
        pairs = list(zip(ours[2] + [0.0] * pages, reference[2]))[:pages]
        per = [abs(x - y) * 100 for x, y in pairs]
        rows.append((sum(per), sum(1 for v in per if v > 1.0), pages,
                     verdict(ours, reference), ours[1], reference[1], path))
    rows.sort(reverse=True)
    print("sum|ink|\tperPage\tMAJOR\tpages\tverdict\toursG\trefG\tdocument")
    for total, major, pages, v, og, rg, path in rows:
        print(f"{total:.2f}\t{total / max(pages, 1):.3f}\t{major}\t{pages}\t{v}\t{og}\t{rg}\t"
              f"{path.name}")
    passing = [r for r in rows if r[3] == "match"]
    print(f"\n{len(rows)} scored, {len(passing)} pass the gate", file=sys.stderr)
    print(f"the ten worst PASSING documents by summed |ink|:", file=sys.stderr)
    for total, major, pages, v, og, rg, path in passing[:10]:
        print(f"  {total:8.2f}  {total / max(pages,1):6.3f}/page  {major:>3} MAJOR  {pages:>4}p  "
              f"{path.name[:62]}", file=sys.stderr)


if __name__ == "__main__":
    main()
