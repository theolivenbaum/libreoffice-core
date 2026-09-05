#!/usr/bin/env bash
# Render every one-family probe with both stacks and read the face out of each PDF.
#   run.sh [outdir]
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="${1:-/tmp/fontprobe}"
CLI="${PAPERLESS_CLI:-$HERE/../../tools/Paperless.Cli/bin/Debug/net10.0/linux-x64/Paperless.Cli}"
mkdir -p "$OUT/ours" "$OUT/ref"
face() { pdffonts "$1" 2>/dev/null | tail -n +3 | awk 'NF{print $1}' | sed 's/^[A-Z]\{6\}+//' | sort -u | paste -sd, -; }
i=0
printf "%-26s %-22s %-22s\n" "requested" "reference(24.2)" "ours"
while IFS= read -r fam; do
  n=$(printf "f%02d" "$i"); i=$((i+1))
  src="$HERE/src/$n.docx"
  [ -f "$OUT/ref/$n.pdf" ]  || { soffice --headless --convert-to pdf --outdir "$OUT/ref" "$src" >/dev/null 2>&1; }
  "$CLI" render "$src" --format pdf --outdir "$OUT/ours" >/dev/null 2>&1
  printf "%-26s %-22s %-22s\n" "$fam" "$(face "$OUT/ref/$n.pdf")" "$(face "$OUT/ours/$n.pdf")"
done < "$HERE/families.txt"
