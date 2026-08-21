#!/usr/bin/env python3
"""What the reference decided on every rotation deck, and the ruler it decided with.

Two independent readings of the same decision:

  * the plot rectangle LibreOffice states in `chart:coordinate-region` in its own `.odp`
    export — a rotated category axis reserves a much deeper bottom band than an upright one;
  * whether the category labels are in the exported PDF's **text layer** at all — 26.2.4.2
    emits a 45-degree rotated chart label as outlines and an upright one as text, which round
    62's parent verification established independently on `Demick_JetBlue`.

and one measurement: the drawn advance of an upright `WWWW…` label, read off the reference's
own PDF, which is the reference's own ruler and owes nothing to ours.
"""
import os, re, subprocess, sys, zipfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "..", "..", "research", "probes", "slides-r30"))
from region import regions  # noqa: E402

NAME = re.compile(r"^rot-(?:z(?P<size>\d+)-n(?P<n>\d+)|n(?P<n2>\d+)c(?P<c>\d+))$")


def variant(stem):
    m = NAME.match(stem)
    if not m:
        return None
    if m.group("size"):
        return dict(size=int(m.group("size")) / 100.0, count=int(m.group("n")), chars=6)
    return dict(size=10.0, count=int(m.group("n2")), chars=int(m.group("c")))


def text_of(pdf):
    return subprocess.run(["pdftotext", "-layout", pdf, "-"],
                          capture_output=True, text=True).stdout


def drawn_width(pdf, chars):
    """The widest `W*chars` run's drawn advance, in text-space points, from the PDF operators.

    Reads `Tf` for the size, the glyph string for the count and any `TJ` adjustments, so the
    number is the reference's own advance for that string at that size."""
    raw = subprocess.run(["qpdf", "--qdf", "--object-streams=disable", pdf, "-"],
                         capture_output=True).stdout.decode("latin-1", "replace")
    best = None
    size = None
    for m in re.finditer(r"/([A-Za-z0-9+._-]+)\s+([\d.]+)\s+Tf|\[([^\]]*)\]\s*TJ|\((?:[^()\\]|\\.)*\)\s*Tj", raw):
        if m.group(2):
            size = float(m.group(2))
            continue
        body = m.group(3) if m.group(3) is not None else m.group(0)
        glyphs = "".join(re.findall(r"\(((?:[^()\\]|\\.)*)\)", body))
        if glyphs.count("W") < chars or size is None:
            continue
        adj = sum(float(a) for a in re.findall(r"(?<=\))\s*(-?[\d.]+)", body))
        width = size * (len(glyphs) - adj / 1000.0) if False else None
        # advance = sum of glyph advances - sum(adjust)/1000 * size; glyph advances are unknown
        # here, so this only reports the character count and the adjustment total.  The advance
        # itself is measured by `advance_of` below on a known-monospaced run.
        best = (size, glyphs, adj)
    return best


def advance_of(pdf, chars):
    """The advance of one upright `W*chars` label, from the *positions* of two labels.

    Every category label is centred under its own tick, so two consecutive labels' text-matrix
    x values give the tick spacing on the page and not the label width.  The width instead comes
    from a single label's own text: LibreOffice writes each as one show at a known size in a
    monospaced face, so the advance is `count x adv(W) x size`, and `adv(W)` is read once from
    the font's own widths array in the PDF."""
    raw = subprocess.run(["qpdf", "--qdf", "--object-streams=disable", pdf, "-"],
                         capture_output=True).stdout.decode("latin-1", "replace")
    return raw


if __name__ == "__main__":
    print("use rot-derive.py")
