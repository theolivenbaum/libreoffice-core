#!/bin/sh
# Render every sheets-track document at two binaries, hash each PDF, and delete it again.
#
#   sweep.sh <corpus-root> <base-cli> <head-cli> <out.tsv>
#
# One temporary directory per document, %%EOF checked before hashing, SOURCE_DATE_EPOCH fixed so
# a header printing the date does not read as a mover.
set -u
ROOT=$1; BASE=$2; HEAD=$3; OUT=$4
export SOURCE_DATE_EPOCH=1700000000
: > "$OUT"
printf 'document\tbase\thead\tmoved\n' >> "$OUT"
find "$ROOT" -type f \( -iname '*.xlsx' -o -iname '*.xlsm' -o -iname '*.xls' -o -iname '*.xlsb' \) \
  | sort | while IFS= read -r doc; do
    name=$(basename "$doc")
    tb=$(mktemp -d); th=$(mktemp -d)
    "$BASE/Paperless.Cli" render "$doc" --outdir "$tb" >/dev/null 2>&1
    "$HEAD/Paperless.Cli" render "$doc" --outdir "$th" >/dev/null 2>&1
    pb=$(find "$tb" -name '*.pdf' | head -1); ph=$(find "$th" -name '*.pdf' | head -1)
    hb=failed; hh=failed
    [ -n "$pb" ] && tail -c 400 "$pb" | grep -q '%%EOF' && hb=$(md5sum "$pb" | cut -d' ' -f1)
    [ -n "$ph" ] && tail -c 400 "$ph" | grep -q '%%EOF' && hh=$(md5sum "$ph" | cut -d' ' -f1)
    m=no; [ "$hb" = "$hh" ] || m=YES
    printf '%s\t%s\t%s\t%s\n' "$name" "$hb" "$hh" "$m" >> "$OUT"
    rm -rf "$tb" "$th"
done
