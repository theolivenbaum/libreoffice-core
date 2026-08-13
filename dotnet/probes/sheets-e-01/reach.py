#!/usr/bin/env python3
"""Reach, direction and verdict movement for the ### round, over the whole sheets track.

    reach.py <before-dir> <after-dir> <reference-dir>

Direction is measured as the **number of `###` tokens** each side draws, because that is what
the change is: a cell that used to draw `1E+00` now draws `###`. It is the one sheets defect the
gate can see, so the word counts are reported beside it rather than instead of it.

Every count comes from `paperless analyze`, one binary reading all three banks in the same pass,
so nothing here can be an artefact of one extractor reading one side.
"""
import os
import subprocess
import sys
from collections import Counter

CLI = os.environ["PAPERLESS_CLI"]


def analyse(path):
    text = subprocess.run([CLI, "analyze", "--text", path],
                          capture_output=True, text=True).stdout
    tokens = text.split()
    hashes = sum(1 for t in tokens if set(t) == {"#"})
    alnum = sum(1 for t in tokens if any(c.isalnum() for c in t))
    return hashes, alnum, len(tokens)


def pages(path):
    row = subprocess.run([CLI, "analyze", "--no-header", path],
                         capture_output=True, text=True).stdout.split("\t")
    return int(row[1])


def main(before, after, ref):
    names = sorted(f for f in os.listdir(after) if f.endswith(".pdf"))
    print("stem\tbeforeHash\tafterHash\trefHash\tbeforeAlnum\tafterAlnum\trefAlnum"
          "\tbytesChanged")
    moved = closer = further = same = 0
    tot = Counter()

    for n in names:
        stem = n[:-4]
        cands = [f for f in os.listdir(ref)
                 if f.startswith(stem + "__") and f.endswith(".pdf")]
        if not cands:
            continue
        b, a, r = os.path.join(before, n), os.path.join(after, n), os.path.join(ref, cands[0])
        bh, ba, _ = analyse(b)
        ah, aa, _ = analyse(a)
        rh, ra, _ = analyse(r)
        changed = open(b, "rb").read() != open(a, "rb").read()
        moved += changed
        if changed:
            if abs(ah - rh) < abs(bh - rh):
                closer += 1
            elif abs(ah - rh) > abs(bh - rh):
                further += 1
            else:
                same += 1
        tot["beforeHash"] += bh
        tot["afterHash"] += ah
        tot["refHash"] += rh
        print("%s\t%d\t%d\t%d\t%d\t%d\t%d\t%d"
              % (stem, bh, ah, rh, ba, aa, ra, int(changed)))

    print("# renderings byte-changed: %d of %d" % (moved, len(names)), file=sys.stderr)
    print("# of those, ### count closer/unchanged/further: %d / %d / %d"
          % (closer, same, further), file=sys.stderr)
    print("# total ### : before %d, after %d, reference %d"
          % (tot["beforeHash"], tot["afterHash"], tot["refHash"]), file=sys.stderr)


if __name__ == "__main__":
    main(*sys.argv[1:4])
