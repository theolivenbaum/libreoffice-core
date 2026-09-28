#!/usr/bin/env bash
# Per chart-type worst-page |ink|% over the sweep's rows.
#
#   ./aggregate.sh [rows.tsv]
#
# Grouped by the *set* of `c:`/`cx:` plot elements a document's chart parts state, so a
# combination chart is its own row rather than being counted under each of its types -- the
# ink of a page holding two plot kinds cannot be attributed to one of them.
set -uo pipefail
awk -F'\t' '$6=="ok"{n[$1]++; s[$1]+=$4; if($4+0>m[$1])m[$1]=$4+0}
  END{for(k in n) printf "%-56s %3d  mean %6.2f  worst %6.2f\n", k, n[k], s[k]/n[k], m[k]}' \
  "${1:-$(dirname "$0")/rows.tsv}" | sort -t't' -k2 -g -r
