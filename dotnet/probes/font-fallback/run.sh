#!/usr/bin/env bash
# What does each stack resolve an absent family to?
#
#   run.sh [outdir]
#
# One family per file, so `pdffonts` attributes the answer without ambiguity. Renders every
# probe with *both* reference binaries and with Paperless, and prints the three columns beside
# each other, because the whole rule this tree implements turns on the two references
# disagreeing:
#
#   * 24.2.7.2 answers whatever fontconfig files the *name* under -- DejaVu Sans for a name
#     nothing files, Liberation Sans for Helvetica through 30-metric-aliases.conf, DejaVu Serif
#     for Georgia and Garamond through 45-latin.conf. This is what `soffice` is here and what
#     every stored figure in this tree is calibrated against, so it is the target.
#   * 26.2.4.2 sends the item's family type to fontconfig as a second FC_FAMILY
#     (vcl/unx/generic/font/fontconfig.cxx:1075-1088), which 24.2 does not, so a word-processing
#     document's roman pool default routes every unknown family to a serif. Its *Noto* is a
#     second and unrelated difference -- the TDF tarball ships its own NotoSans/NotoSerif in
#     share/fonts/truetype and reads them -- and that half is a measurement artefact of the
#     tarball, not of the version. A distro 26.2 would answer DejaVu Serif here.
#
# LO24, LO26 and PAPERLESS_CLI override the three binaries. A missing LO26 is skipped rather
# than failing: the column is evidence, not a gate.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="${1:-/tmp/fontprobe}"
LO24="${LO24:-soffice}"
LO26="${LO26:-/opt/libreoffice26.2/program/soffice}"
CLI="${PAPERLESS_CLI:-$HERE/../../tools/Paperless.Cli/bin/Debug/net10.0/linux-x64/Paperless.Cli}"
mkdir -p "$OUT/ours" "$OUT/ref24" "$OUT/ref26"

face() {
  pdffonts "$1" 2>/dev/null | tail -n +3 | awk 'NF{print $1}' \
    | sed 's/^[A-Z]\{6\}+//' | sort -u | paste -sd, -
}

i=0
printf "%-26s %-22s %-22s %-22s\n" "requested" "24.2 (the target)" "26.2 tarball" "ours"
while IFS= read -r fam; do
  n=$(printf "f%02d" "$i"); i=$((i + 1))
  src="$HERE/src/$n.docx"
  [ -f "$OUT/ref24/$n.pdf" ] \
    || "$LO24" --headless --convert-to pdf --outdir "$OUT/ref24" "$src" >/dev/null 2>&1
  if [ -x "$LO26" ] && [ ! -f "$OUT/ref26/$n.pdf" ]; then
    "$LO26" --headless --convert-to pdf --outdir "$OUT/ref26" "$src" >/dev/null 2>&1
  fi
  "$CLI" render "$src" --format pdf --outdir "$OUT/ours" >/dev/null 2>&1
  printf "%-26s %-22s %-22s %-22s\n" \
    "$fam" "$(face "$OUT/ref24/$n.pdf")" "$(face "$OUT/ref26/$n.pdf")" "$(face "$OUT/ours/$n.pdf")"
done < "$HERE/families.txt"
