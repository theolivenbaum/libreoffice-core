# A page-anchored positioned table, and one taller than the page

*Measured 2026-09-04 in the container described at the top of `dotnet/CLAUDE.md`, against **both**
references — the distro's 24.2.7.2 and the TDF tarball's 26.2.4.2 with its duplicate fonts moved
aside. **They agree on every row below**, so nothing here is a version question.*

## Where this started

`Case-Study-Heathrow-Airport.docx` is the worst words document that survives the version screen —
40.42 first-page ink against 26.2.4.2, where the eleven documents above it in the raw ranking are
the gate binary. Its whole first page is one table, and the table states

```xml
<w:tblpPr w:leftFromText="180" w:rightFromText="180"
          w:vertAnchor="text" w:horzAnchor="page" w:tblpX="705" w:tblpY="662"/>
```

The reference draws its first cell's text at **(40.50, 108.03)**; we draw it at **(77.65, 74.70)** —
37 pt across and 33 pt up. Both errors are the same defect: we honour none of that `w:tblpPr`.

Two separate reasons, and each is a deliberate exclusion rather than an oversight:

* `DocxLayoutSource.PositionedLeftEdge` is called only when `horzAnchor` is **not** `page`.
* `Paginator.PlaceFloatedTable` opens with `if (height > area.Height) return false;` — *"too tall to
  float, so it stays in the flow and paginates as it always did"* — and this table runs to three
  pages.

## The probe

`place.py` writes one positioned table of 400-twip rows, each row's text naming its own index, and
sweeps the horizontal anchor, the vertical anchor, the two offsets, and the row count: **20 rows**
fits on one page, **90 rows** needs three. The observables are the page and (x, y) of `R000`, the
first row, and of `R033`, which is past the first page's worth.

## What both references do

| `horzAnchor` | `vertAnchor` | `tblpX` | `tblpY` | rows | `R000` | `R033` |
|---|---|---:|---:|---:|---|---|
| page | text | 705 | 662 | 20 | p1 35.1, 105.6 | — |
| page | text | 705 | 662 | 90 | p1 35.1, 105.6 | **p2 35.1, 72.5** |
| page | text | 2880 | 0 | 20 | p1 143.8, 72.5 | — |
| page | text | 2880 | 0 | 90 | p1 143.8, 72.5 | p1 143.8, 732.5 |
| margin | text | 705 | 662 | 20 | p1 **107.1**, 105.6 | — |
| margin | text | 705 | 662 | 90 | p1 107.1, 105.6 | p2 107.1, 72.5 |
| page | page | 705 | 1440 | 20 | p1 35.1, **72.5** | — |
| page | page | 705 | 1440 | 90 | p1 35.1, 72.5 | p1 35.1, 732.5 |
| page | margin | 705 | 662 | 90 | p1 35.1, 105.6 | p2 35.1, 72.5 |

Three things, and the third is the one that matters:

1. **`horzAnchor="page"` measures `tblpX` from the sheet's own left edge.** 705 twips is 35.25 pt and
   the text lands at 35.1 — outside the 72 pt margin, which is exactly what makes Heathrow's table
   hang into the left margin. `margin` puts the same table at 107.1, which is 72 + 35.25.
2. **`vertAnchor` behaves the same way vertically.** `text` and `margin` both put the first row at
   105.6 = 72 + 33.1 when the flow is at the top margin; `page` with `tblpY="1440"` puts it at 72.5.
3. **A fly taller than the page splits; it does not fall back into the flow.** `R033` lands at the
   top of page two — `72.5`, the body's own top — with the **same x** as the first part. The stated
   offset positions the fly's *first* part only, and the follow starts at the frame top, which is the
   rule `Paginator.ContinueFloatedTables` already implements for the tables it does float.

Those three numbers explain Heathrow exactly: 705 twips from the page edge is 35.25, plus the cell's
own left margin gives the 40.50 the reference draws, and 72 + 33.1 gives the 105.1 its first row sits
at against the 108.03 of its text's own top.

## What this asks of the tree

Both exclusions have to go, and they are not equally safe.

The horizontal one is contained: `PositionedLeftEdge` needs a `page` arm that measures from the sheet
rather than the column, which is a coordinate conversion and touches nothing else.

The vertical one is the guard in `PlaceFloatedTable`, and removing it means floating a table that
cannot fit the page it starts on. The machinery for that is already there —
`FloatedTablePart`, `pendingFloats` and `ContinueFloatedTables` carry a split fly's rows onto the
next page and place them at the frame top — so what the guard is really protecting against is a fly
whose *first* part does not fit either. The measurement says the reference splits it regardless, and
the row above showing `R033` at `p2 35.1, 72.5` is what a correct implementation has to reproduce.
