#!/usr/bin/env bash
# Render one corpus track with a given CLI into per-format-identity PDFs.
#
#   sweep.sh <cli> <track> <outdir> [jobs]
#
# <track> is words|slides|sheets. Output is <outdir>/<stem>__<ext>.pdf, the same
# identity the canonical reference renderings use, so a rendering can be compared
# across two builds without an extension collision hiding half the corpus.
set -u

CLI=$1
TRACK=$2
OUT=$3
JOBS=${4:-6}
CORPUS=${CORPUS:-/c/sandbox/workdir/sample-files}

export SOURCE_DATE_EPOCH=1700000000
export TZ=UTC

mkdir -p "$OUT"

render_one() {
    local file=$1
    local base ext stem tmp
    base=$(basename "$file")
    ext=${base##*.}
    stem=${base%.*}
    ext=$(printf '%s' "$ext" | tr '[:upper:]' '[:lower:]')
    tmp=$(mktemp -d)
    if "$CLI" render --quiet --outdir "$tmp" "$file" >/dev/null 2>&1; then
        if [ -f "$tmp/$stem.pdf" ]; then
            mv "$tmp/$stem.pdf" "$OUT/${stem}__${ext}.pdf"
        else
            echo "NOPDF	$file" >> "$OUT/_failures.tsv"
        fi
    else
        echo "FAIL	$file" >> "$OUT/_failures.tsv"
    fi
    rm -rf "$tmp"
}
export -f render_one
export CLI OUT

find "$CORPUS/$TRACK" -type f -not -name '.*' -print0 \
    | xargs -0 -P "$JOBS" -I{} bash -c 'render_one "$@"' _ {}

echo "rendered $(ls "$OUT" | grep -c '\.pdf$') of $(find "$CORPUS/$TRACK" -type f -not -name '.*' | wc -l)"
