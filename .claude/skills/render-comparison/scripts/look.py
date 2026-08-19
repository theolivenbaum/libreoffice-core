#!/usr/bin/env python3
"""Render one page both ways as PNGs, so you can *look* at it.

    look.py <document> [--page N | --worst] [--dpi 150] [--out DIR]

`<document>` is either a path under the corpus, or a per-format identity like
`Thailand17__ppt` which is resolved against the corpus and the banked reference.

Prints the two paths and the ink figures. Open them and read the page.

WHY THIS EXISTS
───────────────
Every other instrument here answers "how much differs" or "which element differs". None of
them answers **what is wrong**, and for a long stretch this project chased differences purely
through numbers — which is how a corner-focus radial gradient drawn as a diagonal linear ramp
sat unnoticed behind a 33% ink figure that nobody had a reason to open.

The gate cannot see most real defects: it is page count, extractable words in a 2%+3 band, and
unembedded fonts. A whole track can be 163 of 163 page-exact while a reviewer opens three pages
at random and finds a missing custom bullet, a hanging indent we invent, and glyphs a few
percent narrow. **Looking is not a fallback for when the metrics run out. It is the instrument
that tells you what the metrics are a proxy for.**

Two things looking gives you that no number here does:

  * **Direction.** "Text sizes are different" is a report; "every line breaks earlier in the
    reference, so our glyphs are narrower" is a lead. The metric that says 19.47% says nothing
    about which side is bigger.
  * **Kind.** A missing bullet, an inverted axis and a flattened gradient all read as "ink
    differs" and are three unrelated defects with three unrelated fixes.

And one thing it does **not** give you: **cause**. An image cannot tell a picture bullet from a
character bullet in a substituted symbol font from an autonumber. Say which of those the image
cannot decide, then go and measure it. A reading that quietly upgrades itself into a diagnosis
is worse than no reading.

HOW TO USE IT WELL
──────────────────
  * **Run it on documents that PASS.** The failing set is already picked over. Rank the passing
    documents by `|ink|%` — `first-divergence.py --corpus` or a per-page scan — and open the
    worst. Three of the first three tried this way produced findings, two of them new.
  * **Describe before you check the record.** Reading the page blind and only then looking up
    what is known is a control on the reading. It is how a gradient description was confirmed
    against a diagnosis made a week earlier from source, with no chance of having been led.
  * **Stack the pair, do not put it side by side.** The same region has to land under itself or
    you will compare the wrong things.
  * **Beware your own instrument.** A page-size difference of 0.08 pt rounds to a different
    pixel count, and a naive comparator reports that as 100% different. Check a "total"
    disagreement before believing it.
"""
from __future__ import annotations

import argparse
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile

CORPUS = pathlib.Path(os.environ.get("PAPERLESS_CORPUS", "/c/sandbox/workdir/sample-files"))
BANK = pathlib.Path(os.environ.get("PAPERLESS_REFBANK",
                                   "/c/sandbox/workdir/refpdfs-26.2.4.2-fonts"))
INK = 200  # below this is ink; keeps antialiasing out of the count


def run(cmd: list[str]) -> None:
    subprocess.run(cmd, check=False, capture_output=True, timeout=900)


def cli() -> pathlib.Path:
    p = os.environ.get("PAPERLESS_CLI")
    if p:
        return pathlib.Path(p)
    root = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True,
                          text=True).stdout.strip() or "."
    return pathlib.Path(root) / "dotnet/tools/Paperless.Cli/bin/Debug/net10.0/linux-x64/Paperless.Cli"


def resolve(doc: str) -> tuple[pathlib.Path, str]:
    """A corpus path, or a `stem__ext` identity, to (source path, identity)."""
    p = pathlib.Path(doc)
    if p.is_file():
        return p, f"{p.stem}__{p.suffix.lstrip('.').lower()}"
    m = re.match(r"^(.*)__([a-z0-9]+)$", doc)
    if not m:
        sys.exit(f"not a file and not a stem__ext identity: {doc}")
    stem, ext = m.groups()
    hits = [f for f in CORPUS.rglob(f"{stem}.{ext}")] + \
           [f for f in CORPUS.rglob(f"{stem}.{ext.upper()}")]
    if not hits:
        sys.exit(f"no corpus file for {doc} under {CORPUS}")
    return hits[0], doc


def reference(ident: str, src: pathlib.Path, work: pathlib.Path) -> pathlib.Path:
    """The banked reference if there is one; otherwise render it now."""
    for track in ("words", "slides", "sheets"):
        cand = BANK / track / f"{ident}.pdf"
        if cand.is_file():
            return cand
    out = work / "refpdf"
    out.mkdir(parents=True, exist_ok=True)
    prof = work / "prof"
    run(["soffice", f"-env:UserInstallation=file://{prof}", "--headless",
         "--convert-to", "pdf", "--outdir", str(out), str(src)])
    got = list(out.glob("*.pdf"))
    if not got:
        sys.exit(f"soffice produced no reference for {ident}")
    return got[0]


def ours(src: pathlib.Path, work: pathlib.Path) -> pathlib.Path:
    out = work / "ourpdf"
    out.mkdir(parents=True, exist_ok=True)
    exe = cli()
    if not exe.exists():
        sys.exit(f"no CLI at {exe}; set PAPERLESS_CLI to the tree you mean to measure")
    run([str(exe), "render", str(src), "--format", "pdf", "--outdir", str(out)])
    got = list(out.glob("*.pdf"))
    if not got:
        sys.exit(f"Paperless produced no rendering for {src}")
    return got[0]


def read_ppm(path: pathlib.Path) -> tuple[int, int, bytes]:
    """Width, height and raw RGB. P6 only, which is what pdftoppm writes."""
    data = path.read_bytes()
    fields, pos = [], 2
    while len(fields) < 3:
        while pos < len(data) and data[pos:pos + 1].isspace():
            pos += 1
        if data[pos:pos + 1] == b"#":
            while data[pos:pos + 1] not in (b"\n", b""):
                pos += 1
            continue
        start = pos
        while pos < len(data) and not data[pos:pos + 1].isspace():
            pos += 1
        fields.append(int(data[start:pos]))
    return fields[0], fields[1], data[pos + 1:]


def ink_mask(path: pathlib.Path) -> tuple[int, int, bytearray]:
    """Ink is dark *luminance*, not a dark average.

    This was wrong first time round and a known-answer check caught it, which is the whole
    argument for having one. Averaging the channels makes saturated yellow — (255,255,0),
    average 170 — count as ink, so a page whose background goes from periwinkle to yellow
    reads as ink on both sides and scores 0.03% different when it is 33%. Rec. 601 luma puts
    that yellow at 226 and leaves it as paper, which is what an eye sees.
    """
    w, h, rgb = read_ppm(path)
    m = bytearray(w * h)
    for i in range(w * h):
        y = (299 * rgb[3 * i] + 587 * rgb[3 * i + 1] + 114 * rgb[3 * i + 2]) // 1000
        if y < INK:
            m[i] = 1
    return w, h, m


def compare(a: pathlib.Path, b: pathlib.Path) -> tuple[float, float, bool]:
    wa, ha, ma = ink_mask(a)
    wb, hb, mb = ink_mask(b)
    if (wa, ha) != (wb, hb):
        # NOT 100% different. A 0.08 pt page-size difference lands here and means almost
        # nothing; report it as its own thing so nobody reads it as total disagreement.
        return (float("nan"), float("nan"), True)
    only_a = only_b = 0
    for i in range(len(ma)):
        if ma[i] and not mb[i]:
            only_a += 1
        elif mb[i] and not ma[i]:
            only_b += 1
    n = len(ma)
    return (100.0 * (only_a + only_b) / n, 100.0 * (only_a - only_b) / n, False)


def worst_page(o: pathlib.Path, r: pathlib.Path, work: pathlib.Path) -> int:
    scan = work / "scan"
    scan.mkdir(parents=True, exist_ok=True)
    run(["pdftoppm", "-r", "60", str(o), str(scan / "o")])
    run(["pdftoppm", "-r", "60", str(r), str(scan / "r")])
    op, rp = sorted(scan.glob("o-*.ppm")), sorted(scan.glob("r-*.ppm"))
    best, best_i = -1.0, 1
    for i in range(min(len(op), len(rp))):
        u, _s, sized = compare(op[i], rp[i])
        score = -1.0 if sized else u
        if score > best:
            best, best_i = score, i + 1
    return best_i


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("document")
    ap.add_argument("--page", type=int)
    ap.add_argument("--worst", action="store_true", help="pick the most divergent page")
    ap.add_argument("--dpi", type=int, default=150)
    ap.add_argument("--out", type=pathlib.Path, default=pathlib.Path("./look"))
    a = ap.parse_args()

    src, ident = resolve(a.document)
    work = pathlib.Path(tempfile.mkdtemp(prefix="look-"))
    try:
        o, r = ours(src, work), reference(ident, src, work)
        page = a.page or (worst_page(o, r, work) if a.worst else 1)

        a.out.mkdir(parents=True, exist_ok=True)
        stem_o, stem_r = a.out / f"{ident}-p{page}-ours", a.out / f"{ident}-p{page}-ref"
        for pdf, stem in ((o, stem_o), (r, stem_r)):
            run(["pdftoppm", "-r", str(a.dpi), "-f", str(page), "-l", str(page),
                 "-png", str(pdf), str(stem)])

        made = sorted(a.out.glob(f"{ident}-p{page}-*.png"))
        if len(made) < 2:
            sys.exit(f"pdftoppm produced {len(made)} images for page {page}")

        cmp_dir = work / "cmp"
        cmp_dir.mkdir(exist_ok=True)
        for pdf, tag in ((o, "o"), (r, "r")):
            run(["pdftoppm", "-r", "110", "-f", str(page), "-l", str(page),
                 str(pdf), str(cmp_dir / tag)])
        oc, rc = sorted(cmp_dir.glob("o-*.ppm")), sorted(cmp_dir.glob("r-*.ppm"))
        if oc and rc:
            u, s, sized = compare(oc[0], rc[0])
            if sized:
                print(f"page size differs between the two renderings — compare with care")
            else:
                print(f"|ink| {u:.2f}%   signed {s:+.2f}%   "
                      f"({'we draw less' if s < -0.5 else 'we draw more' if s > 0.5 else 'balanced'})")

        print(f"page {page} of {ident}")
        for p in made:
            print(f"  {p}")
        print("\nOpen both. Stack them so the same region lands under itself, describe what you\n"
              "see before looking up what is known, and say which causes the image cannot decide.")
        return 0
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
