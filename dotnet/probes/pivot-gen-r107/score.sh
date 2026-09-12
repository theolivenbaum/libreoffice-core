#!/bin/bash
# Score each pivot-bearing document at two binaries against its banked 26.2.4.2 reference.
#
#   score.sh <list> <base-cli> <head-cli> <ref-dir> <out.tsv>
#
# Ink is `|ink|%` from pdf-image-diff.py summed unsigned over the document's pages; MAJOR is that
# script's own verdict. Each render is deleted as soon as it is scored, because the box has four
# gigabytes free and a 186-page workbook is half of one.
set -u
LIST=$1; BASE=$2; HEAD=$3; REF=$4; OUT=$5
DIFF=/home/user/libreoffice-core/.claude/skills/render-comparison/scripts/pdf-image-diff.py
export SOURCE_DATE_EPOCH=1700000000
printf 'document\tpages\tbaseInk\theadInk\tbaseMAJOR\theadMAJOR\n' > "$OUT"
while IFS= read -r doc; do
    [ -n "$doc" ] || continue
    b=$(basename "$doc"); n="${b%.*}"; e="${b##*.}"
    ref="$REF/${n}__${e}.pdf"
    [ -f "$ref" ] || { printf '%s\tNO-REFERENCE\n' "$b" >> "$OUT"; continue; }
    t=$(mktemp -d)
    "$BASE/Paperless.Cli" render "$doc" --outdir "$t/base" >/dev/null 2>&1
    "$HEAD/Paperless.Cli" render "$doc" --outdir "$t/head" >/dev/null 2>&1
    pb=$(find "$t/base" -name '*.pdf' | head -1); ph=$(find "$t/head" -name '*.pdf' | head -1)
    read -r bp bi bm <<<"$(python3 "$DIFF" "$ref" "$pb" 2>/dev/null | awk -F'\t' 'NF>=6 && $1 ~ /^[0-9]+$/ {s+=$4; n++; if($6!="ok") m++} END {printf "%d %.2f %d", n, s, m}')"
    read -r hp hi hm <<<"$(python3 "$DIFF" "$ref" "$ph" 2>/dev/null | awk -F'\t' 'NF>=6 && $1 ~ /^[0-9]+$/ {s+=$4; n++; if($6!="ok") m++} END {printf "%d %.2f %d", n, s, m}')"
    [ "$bp" = "$hp" ] || bp="$bp/$hp"
    printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$n" "$bp" "$bi" "$hi" "$bm" "$hm" >> "$OUT"
    rm -rf "$t"
done < "$LIST"
