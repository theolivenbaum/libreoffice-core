#!/usr/bin/env python3
"""Reach, direction and the gate's verdict, for all three banks in one pass.

    gate.py <before-dir> <after-dir> <reference-dir> > rows.tsv 2> summary.txt

`paperless analyze` reads every bank, so no column here can be an artefact of one extractor
reading one side. The verdict rule is `words-rebase-02/verdict.py` unchanged — this round moves
its *input*, never the rule.
"""
import os
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "..", "words-rebase-02"))
from verdict import verdict                                       # noqa: E402

CLI = os.environ["PAPERLESS_CLI"]


def read(path):
    """(pages, alnum words, unembedded fonts, ### tokens) from one PDF."""
    row = subprocess.run([CLI, "analyze", "--no-header", path],
                         capture_output=True, text=True).stdout.rstrip("\n").split("\t")
    text = subprocess.run([CLI, "analyze", "--text", path],
                          capture_output=True, text=True).stdout
    hashes = sum(1 for t in text.split() if t and set(t) == {"#"})
    return row[1], row[4], row[10], hashes


def main(before, after, ref):
    names = sorted(f for f in os.listdir(after) if f.endswith(".pdf"))
    print("stem\tpagesB\tpagesA\tpagesR\twordsB\twordsA\twordsR"
          "\thashB\thashA\thashR\tverdictB\tverdictA\tbytesChanged")

    moved = closer = further = unchanged = 0
    sums = {"B": 0, "A": 0, "R": 0}
    vb = va = 0
    flips = []

    for n in names:
        stem = n[:-4]
        cands = [f for f in os.listdir(ref)
                 if f.startswith(stem + "__") and f.endswith(".pdf")]
        if not cands:
            print("NO REFERENCE FOR %s" % stem, file=sys.stderr)
            continue

        bp, bw, bu, bh = read(os.path.join(before, n))
        ap, aw, au, ah = read(os.path.join(after, n))
        rp, rw, _, rh = read(os.path.join(ref, cands[0]))

        changed = (open(os.path.join(before, n), "rb").read()
                   != open(os.path.join(after, n), "rb").read())
        moved += changed
        if changed:
            if abs(ah - rh) < abs(bh - rh):
                closer += 1
            elif abs(ah - rh) > abs(bh - rh):
                further += 1
            else:
                unchanged += 1

        sums["B"] += bh
        sums["A"] += ah
        sums["R"] += rh

        b = verdict(bp, rp, bw, rw, bu)
        a = verdict(ap, rp, aw, rw, au)
        vb += b == "match"
        va += a == "match"
        if a != b:
            flips.append((stem, b, a, bw, aw, rw, bh, ah, rh))

        print("\t".join(str(x) for x in
                        (stem, bp, ap, rp, bw, aw, rw, bh, ah, rh, b, a, int(changed))))

    print("documents: %d" % len(names), file=sys.stderr)
    print("renderings byte-changed: %d" % moved, file=sys.stderr)
    print("of those, ### count closer / unchanged / further: %d / %d / %d"
          % (closer, unchanged, further), file=sys.stderr)
    print("total ###: before %d, after %d, reference %d"
          % (sums["B"], sums["A"], sums["R"]), file=sys.stderr)
    print("gate matches: before %d, after %d" % (vb, va), file=sys.stderr)
    for f in flips:
        print("VERDICT %s: %s -> %s (words %s -> %s vs %s; ### %s -> %s vs %s)"
              % f, file=sys.stderr)


if __name__ == "__main__":
    main(*sys.argv[1:4])
