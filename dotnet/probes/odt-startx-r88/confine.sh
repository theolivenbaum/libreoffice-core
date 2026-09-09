#!/usr/bin/env bash
# Prove the change cannot reach the other readers, by building both ways and comparing renderings.
#
#   confine.sh <worktree> <list> <outroot>
#
# Builds the tree with the round's source files reverted to the base commit, renders the list, then
# restores them, rebuilds and renders again — `obj`/`bin` cleared on both legs, because MSBuild's
# up-to-date check will otherwise skip a project whose source was restored with an older mtime.
set -euo pipefail
TREE="${1:?worktree}"; LIST="${2:?list}"; OUT="${3:?outroot}"
BASE="${BASE_COMMIT:-a3700bba2}"
CLI="$TREE/dotnet/tools/Paperless.Cli/bin/Debug/net10.0/linux-x64/Paperless.Cli"
HERE="$(cd "$(dirname "$0")" && pwd)"

FILES=(
  dotnet/src/Paperless.WordProcessing/Model/PageGeometry.cs
  dotnet/src/Paperless.WordProcessing/Layout/Paginator.cs
  dotnet/src/Paperless.WordProcessing/Layout/ContinuousPageDescriptors.cs
  dotnet/src/Paperless.WordProcessing/OpenDocument/OdfPageGeometry.cs
  dotnet/src/Paperless.WordProcessing/OpenDocument/OdfSectionGeometry.cs
  dotnet/src/Paperless.WordProcessing/OpenDocument/OdtLayoutSource.cs
  dotnet/src/Paperless.WordProcessing/OpenDocument/OdtWordDocument.cs
)

leg() {
  local name="$1"
  rm -rf "$TREE"/dotnet/src/Paperless.WordProcessing/{obj,bin}
  rm -rf "$TREE"/dotnet/tools/Paperless.Cli/{obj,bin}
  # The CLI alone, not the solution: the round's own tests name a type the base leg does not have,
  # and a build that fails takes the whole leg with it.
  (cd "$TREE/dotnet" && dotnet build tools/Paperless.Cli/Paperless.Cli.csproj -v q -nologo)
  rm -rf "${OUT:?}/$name"
  PAPERLESS_CLI="$CLI" bash "$HERE/render-list.sh" "$LIST" "$OUT/$name" 3
}

mkdir -p "$OUT"
for f in "${FILES[@]}"; do cp "$TREE/$f" "$TREE/$f.after" 2>/dev/null || true; done

# The base leg: the same tree with this round's files as the base commit had them.
for f in "${FILES[@]}"; do
  git -C "$TREE" show "$BASE:$f" > "$TREE/$f" 2>/dev/null || rm -f "$TREE/$f"
  [ -f "$TREE/$f" ] && touch "$TREE/$f"
done
leg before

# And back, with cp (never mv) and an explicit touch, for the reason dotnet/CLAUDE.md records.
for f in "${FILES[@]}"; do
  [ -f "$TREE/$f.after" ] && cp "$TREE/$f.after" "$TREE/$f" && touch "$TREE/$f"
done
leg after
for f in "${FILES[@]}"; do rm -f "$TREE/$f.after"; done

python3 "$HERE/movers.py" "$OUT/before" "$OUT/after"
