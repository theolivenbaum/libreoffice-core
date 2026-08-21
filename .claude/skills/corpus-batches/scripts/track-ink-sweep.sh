#!/usr/bin/env bash
# Sweep a track for the word gate *and* for unaccounted ink, in one pass.
#
#   track-ink-sweep.sh <corpus-root> <batch-glob> <outdir> [workers] [cli] [refdir]
#
#   track-ink-sweep.sh /c/sandbox/workdir/sample-files 'slides/batch-0*' out-slides 2
#
# `batch-check.sh` answers "is the right text on the right page". Once a track passes that
# — slides is at 152/163 with every remaining failure attributed — the only instrument left
# is where the ink lands, and running `pdf-image-diff.py` by hand over 163 documents is how
# a round gets spent. This does both from one pair of renderings.
#
# Writes:
#   rows.tsv    the same seven columns batch-check.sh writes
#   parity.tsv  those, sorted, with a header
#   ink.tsv     path, pages, |ink|% (unsigned), ink% (signed), major pages, verdict
#               Two ink columns, deliberately, and both are named in the file's own
#               header row.  For eleven rounds this script wrote ONE column, summed the
#               *signed* figure into it, and labelled the total `INK` -- while
#               `probes/slides-r39/ink-ranking.py`, the other half of the same skill,
#               headlined the *unsigned* one.  Two different measurements circulating
#               under one name is the trap this project has paid for repeatedly, and
#               here it lived inside a single skill.  Rank on unsigned; decide on
#               signed.  A signed sum lets a deficit cancel a surplus, so filling the
#               deficit reads as a regression.
#   cmp/<id>.txt  the full per-page pdf-image-diff report for every document
#
# Three things it does that the obvious version does not:
#
#   * Sums the per-page ink column itself, and therefore does NOT pass --quiet.
#     pdf-image-diff.py totals the major-page count and nothing else, and a page that is
#     not major still carries ink — dropping those understates a document by most of its
#     figure on a deck whose error is spread thin.
#   * Takes the CLI as an argument, so a sweep can run against a snapshot while the tree
#     is rebuilt underneath it. Checksum the snapshot against the tree before starting;
#     a stale copy passes every other check this skill prescribes.
#   * Reuses reference PDFs from an earlier run when given a refdir. The reference cannot
#     change while nothing touches soffice, and it is half the wall clock. Compare the two
#     runs' reference columns row for row afterwards, which is the check that says so.
set -uo pipefail

ROOT_DIR="${1:?usage: track-ink-sweep.sh <corpus-root> <batch-glob> <outdir> [workers] [cli] [refdir]}"
GLOB="${2:?batch glob, e.g. 'slides/batch-0*'}"
OUT="${3:?outdir — name it after the agent or the cluster, never 'out' or 'base'}"
WORKERS="${4:-2}"

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
CLI="${5:-$REPO/dotnet/tools/Paperless.Cli/bin/Debug/net10.0/linux-x64/Paperless.Cli}"
REFDIR="${6:-}"

mkdir -p "$OUT" && OUT="$(cd "$OUT" && pwd)"
[ -x "$CLI" ] || { echo "no CLI at $CLI — build it first" >&2; exit 1; }

DIFF="$REPO/.claude/skills/render-comparison/scripts/pdf-image-diff.py"
[ -f "$DIFF" ] || { echo "no pdf-image-diff.py at $DIFF" >&2; exit 1; }

mkdir -p "$OUT/ours" "$OUT/ref" "$OUT/cmp"
: > "$OUT/rows.tsv"
: > "$OUT/ink.tsv"

# Extractable words, for check 2. A token counts as a word iff it carries at least one
# Unicode letter or digit. Verbatim from `batch-check.sh` and `ref-baseline.sh`, and it must
# stay verbatim: this script used a bare `pdftotext | wc -w` for eleven rounds after the gate
# moved off it, so its `verdict` column silently disagreed with `MANIFEST.tsv`'s `status` and
# with every batch-check sweep. A sweep whose verdict column is a different metric from the
# scoreboard's looks exactly like a regression. Emits "<words> <rawwords>"; the raw figure is
# kept as the last TSV column so an old run under this script is still reconcilable.
words_of() {  # words_of <pdf> -> "<words> <rawwords>"
  pdftotext "$1" - 2>/dev/null | python3 -c '
import sys
t = sys.stdin.buffer.read().decode("utf-8", "replace").split()
print(sum(1 for w in t if any(c.isalnum() for c in w)), len(t))'
}

# shellcheck disable=SC2086  # the glob is meant to expand
mapfile -t DIRS < <(cd "$ROOT_DIR" && ls -d $GLOB 2>/dev/null)
[ "${#DIRS[@]}" -gt 0 ] || { echo "no batches matched $GLOB under $ROOT_DIR" >&2; exit 1; }

mapfile -t FILES < <(
  for d in "${DIRS[@]}"; do
    # The in-scope extension list, kept identical to batch-check.sh's. It was NOT identical
    # until 2026-08-21: batch-check.sh was widened from thirteen extensions to thirty-four at
    # the start of this session, after two `.xlsm` in sheets/chartset-* turned out to have been
    # silently unmeasured -- and this sibling, written from the same list, kept the narrow one
    # and stayed blind to the same two documents for a dozen rounds. Fixing an instrument does
    # not fix its twin. If this list changes, change it in both.
    find "$ROOT_DIR/$d" -type f \
      \( -iname '*.doc'  -o -iname '*.docx' -o -iname '*.docm' -o -iname '*.dot' \
      -o -iname '*.dotx' -o -iname '*.dotm' -o -iname '*.rtf'  -o -iname '*.odt' \
      -o -iname '*.ott'  -o -iname '*.fodt' -o -iname '*.sxw' \
      -o -iname '*.xls'  -o -iname '*.xlsx' -o -iname '*.xlsm' -o -iname '*.xlsb' \
      -o -iname '*.xlt'  -o -iname '*.xltx' -o -iname '*.xltm' -o -iname '*.ods' \
      -o -iname '*.ots'  -o -iname '*.fods' -o -iname '*.csv'  -o -iname '*.sxc' \
      -o -iname '*.ppt'  -o -iname '*.pptx' -o -iname '*.pptm' -o -iname '*.pot' \
      -o -iname '*.potx' -o -iname '*.potm' -o -iname '*.ppsx' -o -iname '*.ppsm' \
      -o -iname '*.pps'  -o -iname '*.odp'  -o -iname '*.otp'  -o -iname '*.fodp' \
      -o -iname '*.sxi' \) 2>/dev/null
  done | sort
)
echo "documents: ${#FILES[@]}" >&2

one() {  # one <index>
  local idx="$1" i=-1 f base ext stem id o r op rp ow rw of rf un v ink major pages owraw rwraw
  local prof="$OUT/prof$idx"
  mkdir -p "$prof" "$OUT/t$idx"
  for f in "${FILES[@]}"; do
    i=$((i + 1)); [ $((i % WORKERS)) -eq "$idx" ] || continue
    base="$(basename "$f")"; ext="${base##*.}"; stem="${base%.*}"
    id="${stem}__${ext,,}"
    o="$OUT/ours/$id.pdf"; r="$OUT/ref/$id.pdf"

    rm -rf "${OUT:?}/t$idx"; mkdir -p "$OUT/t$idx"
    timeout 300 "$CLI" render "$f" --format pdf --outdir "$OUT/t$idx" >/dev/null 2>&1
    [ -f "$OUT/t$idx/$stem.pdf" ] && mv -f "$OUT/t$idx/$stem.pdf" "$o"

    if [ -n "$REFDIR" ] && [ -f "$REFDIR/$id.pdf" ]; then
      cp -f "$REFDIR/$id.pdf" "$r"
    else
      rm -rf "$OUT/t$idx"; mkdir -p "$OUT/t$idx"
      timeout 300 soffice -env:UserInstallation="file://$prof" \
        --headless --convert-to pdf --outdir "$OUT/t$idx" "$f" >/dev/null 2>&1
      [ -f "$OUT/t$idx/$stem.pdf" ] && mv -f "$OUT/t$idx/$stem.pdf" "$r"
    fi

    op="-"; rp="-"; ow="-"; rw="-"; of="-"; rf="-"; un="-"; owraw="-"; rwraw="-"
    if [ -f "$o" ]; then
      op=$(pdfinfo "$o" 2>/dev/null | awk '/^Pages/{print $2}')
      read -r ow owraw < <(words_of "$o")
      of=$(pdffonts "$o" 2>/dev/null | tail -n +3 | grep -c .)
      un=$(pdffonts "$o" 2>/dev/null | tail -n +3 | awk 'NF>=8 && $(NF-4)=="no"' | wc -l)
    fi
    if [ -f "$r" ]; then
      rp=$(pdfinfo "$r" 2>/dev/null | awk '/^Pages/{print $2}')
      read -r rw rwraw < <(words_of "$r")
      rf=$(pdffonts "$r" 2>/dev/null | tail -n +3 | grep -c .)
    fi

    if   [ ! -f "$r" ] && [ ! -f "$o" ]; then v="both-failed"
    elif [ ! -f "$r" ];                  then v="ref-failed"
    elif [ ! -f "$o" ];                  then v="ours-failed"
    else
      v=""
      [ "$op" = "$rp" ] || v="pages"
      if [ "$rw" -gt 0 ] 2>/dev/null; then
        awk -v a="$ow" -v b="$rw" 'BEGIN{d=(a>b?a-b:b-a); exit !(d > b*0.02 && d > 3)}' \
          && v="${v:+$v,}words"
      elif [ "${ow:-0}" -gt 3 ]; then v="${v:+$v,}words"
      fi
      [ "${un:-0}" = "0" ] || v="${v:+$v,}unembedded"
      [ -n "$v" ] || v="match"
    fi

    printf "%s\t%s\t%s/%s\t%s/%s\t%s/%s\t%s\t%s\t%s/%s\n" \
      "${f#"$ROOT_DIR"/}" "${ext,,}" "$op" "$rp" "$ow" "$rw" "$of" "$rf" "$un" "$v" \
      "$owraw" "$rwraw" >> "$OUT/rows.tsv"

    # Ink, whenever both sides rendered and the page counts agree. The tool refuses a
    # document whose counts differ, and rightly: page 3 against a different page 3 makes
    # every region it reports an artefact.
    ink="-"; sink="-"; major="-"; pages="-"
    if [ -f "$o" ] && [ -f "$r" ] && [ "$op" = "$rp" ]; then
      rm -rf "$OUT/c$idx"
      timeout 900 python3 "$DIFF" "$o" "$r" --outdir "$OUT/c$idx" > "$OUT/cmp/$id.txt" 2>&1
      rm -rf "$OUT/c$idx"          # the PNGs are large and disposable; the report is not
      # pdf-image-diff.py prints: page  diff%  ink%(signed)  |ink|%(unsigned)  regions  verdict
      ink=$(awk -F'\t' '$1 ~ /^[0-9]+$/ && $4 ~ /^[0-9.]+$/ {s+=$4} END{printf "%.2f", s}' \
            "$OUT/cmp/$id.txt")
      sink=$(awk -F'\t' '$1 ~ /^[0-9]+$/ && $3 ~ /^-?[0-9.]+$/ {s+=$3} END{printf "%.2f", s}' \
            "$OUT/cmp/$id.txt")
      major=$(awk '/pages, .* with major differences/{print $3}' "$OUT/cmp/$id.txt")
      pages="$op"
      [ -n "$ink" ] || ink="?"
      [ -n "$sink" ] || sink="?"
      [ -n "$major" ] || major="?"
    fi
    printf "%s\t%s\t%s\t%s\t%s\t%s\n" \
      "${f#"$ROOT_DIR"/}" "$pages" "$ink" "$sink" "$major" "$v" >> "$OUT/ink.tsv"
  done
}

for w in $(seq 0 $((WORKERS - 1))); do one "$w" & done
wait

{
  printf "# words = tokens carrying at least one Unicode letter or digit; rawwords = pdftotext | wc -w\n"
  printf "path\text\tpages\twords\tfonts\tunemb\tverdict\trawwords\n"
  sort "$OUT/rows.tsv"
} > "$OUT/parity.tsv"
{
  printf "# abs_ink = sum of the per-page UNSIGNED |ink|%% column -- rank the track on this one\n"
  printf "# signed_ink = sum of the per-page SIGNED ink%% column -- decide direction on this one\n"
  printf "path\tpages\tabs_ink\tsigned_ink\tmajor\tverdict\n"
  sort "$OUT/ink.tsv"
} > "$OUT/ink.tsv.tmp" && mv -f "$OUT/ink.tsv.tmp" "$OUT/ink.tsv"

total=$(wc -l < "$OUT/rows.tsv")
match=$(awk -F'\t' '$7=="match"' "$OUT/rows.tsv" | wc -l)
reffail=$(awk -F'\t' '$7=="ref-failed" || $7=="both-failed"' "$OUT/rows.tsv" | wc -l)
echo
echo "BATCHES ${DIRS[*]}"
echo "TOTAL $total  MATCH $match  REF-CANNOT-RENDER $reffail"
# Both figures, both labelled, and the invariant between them checked.  A sum of signed
# page figures can never exceed the sum of the same pages taken unsigned; if it does, the
# two columns were not read off the same pages and no ranking built on them means anything.
awk -F'\t' '/^#/ || $1=="path" {next}
            $3!="-" && $3!="?" {a+=$3; s+=$4; m+=$5; n++}
            END{printf "ABS-INK %.2f (unsigned |ink|%%, ranks)  SIGNED-INK %.2f (ink%%, direction)  MAJOR PAGES %d  over %d documents\n", a, s, m, n;
                if ((s<0?-s:s) > a + 0.01)
                  printf "INVARIANT VIOLATED: |signed| %.2f > unsigned %.2f\n", (s<0?-s:s), a}' "$OUT/ink.tsv"
echo "TSV $OUT/parity.tsv  $OUT/ink.tsv"
