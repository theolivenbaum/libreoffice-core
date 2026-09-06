# The nine chart readings, re-screened against both references, and what is left in each

Round `agent/charts2`, 2026-09-06. Both reference binaries on this container: `soffice` 24.2.7.2
and `/opt/libreoffice26.2/program/soffice` 26.2.4.2, the tarball's eight Latin `Noto` faces and
its 33 metric-compatible duplicates moved aside, so both answer DejaVu. `verdict.py` at 150 dpi,
every page of every document, against both.

## Why the bucket was re-opened, and what changed

The previous round declared `Demick_JetBlue.pptx` and `057_Simple_balance_sheet` **unscoreable**
because the tarball drew their charts in Noto where we drew DejaVu. With the Latin Noto aside
that confound is gone: `verdict.py` reports **no font swap on either document against either
reference**, and both scored for the first time.

## The re-screen, before any change this round

| document | verdict | pages ours/ref | worst page mae (24.2 / 26.2) | worst page ink |
|---|---|---|---|---|
| `Demick_JetBlue.pptx` | text-layer-differs | 10/10 | 0.1029 / 0.1031 | −0.0036 |
| `015_Project_Timeline_Template_Colored_Background` | **unscoreable-font vs 24.2** | 2/2 vs 26.2 | — / 0.0330 | −0.0039 |
| `040_Blood_pressure_tracker` | text-layer-differs | 1/1 | 0.0368 / 0.0369 | −0.0010 |
| `047_Date_tracker_Gantt_chart` | *(page counts differ, 5/8 — not scoreable page by page)* | 5/8 | — | — |
| `EHEST-Pre-departure-checklist` | content-differs | 24/24 | 0.0839 / 0.0839 | +0.0865 |
| `033_Event_planning_tracker` | content-differs | 3/3 | 0.0441 / **0.1255** | +0.0136 / **−0.1135** |
| `057_Simple_balance_sheet` | content-differs | 4/3 | 0.0398 / 0.0397 | +0.0959 / +0.0965 |
| `DynamicBubbleChart.xlsx` | **unscoreable-font vs both** | — | — | — |
| `053_Personal_asset_inventory` | content-differs | 4/2 | 0.0208 / 0.0201 | +0.0040 / +0.0056 |

## 1 · A turned category label hangs from its tick by its far end — fixed

See the commit `fix(charts): a turned category label hangs from its tick by its far end`.
`lcl_correctRotation_Bottom`'s `if( !bRotateAroundCenter )` term
(`chart2/source/view/main/LabelPositionHelper.cxx:241-282`) is `-sign(sin a)·W·cos(a)/2` in all
four of its branches, and `bRotateAroundCenter` is `m_bComplexCategories`
(`chart2/source/view/axes/VCartesianAxis.cxx:147-148`) — false for a simple category axis.

**26.2.4.2 draws a rotated axis label as vector outlines, not as text**, on both witnesses, which
is why no text-layer instrument can see it and why `pdftotext` reports none of these labels. The
measurement is therefore taken from the PDF's own paths: on `057` the 309 paths in the band below
the axis cluster into exactly twenty groups, one per label, and each group's right-hand edge
advances by

```
28.67  28.92  29.14  28.78  28.36  30.12  28.39  28.92  29.19  28.92
28.86  28.81  29.23  28.83  28.92  28.22  29.67  29.00  28.73
```

against a category slot of **28.9465 pt** — constant, and equal to the pitch, over label widths
from 22 to 141 pt. Ours advanced by `11.47 23.31 21.45 … 53.51`, which is `W/2` each time.

After the change our own advance is `32.10 32.10 32.09 …` against our slot of 32.098, with the far
end a constant **2.49 pt** past the tick on all twenty.

## 2 · The automatic tick count is capped by how many labels fit, and our divisor is one step short

**Not fixed. Bracketed, and the bracket is the deliverable.**

`040_Blood_pressure_tracker`'s two value axes are both fully automatic — no `c:min`, `c:max` or
`c:majorUnit` on either. 26.2.4.2 labels the primary `0 20 … 160` and the secondary `60 65 70 75
80`; we label the primary the same and the secondary `62 64 … 80`. That is the whole of the +5
word delta on the document's single page.

Running `ScaleAutomatism::calculateExplicitIncrementAndScaleForLinear`
(`chart2/source/view/axes/ScaleAutomatism.cxx:738-964`) by hand on the secondary axis' data
(68…78) for every allowed interval ceiling *N* shows the answer is decided by *N* alone:

| N | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|
| result | 60..80/10 | 60..80/10 | **60..80/5** | **60..80/5** | **60..80/5** | **60..80/5** | **60..80/5** | 62..80/2 | 62..80/2 |

and the primary axis (0…142) needs N ≥ 8 to keep its distance of 20. **So the reference is at
N = 8 on both axes and we are at N = 9.**

*N* is `VCartesianAxis::estimateMaximumAutoMainIncrementCount`
(`chart2/source/view/axes/VCartesianAxis.cxx:1559-1618`) — the axis line's length over the tallest
label measured so far, clamped into [2, 10] (`ScaleAutomatism.cxx:143-151`). The length is not in
doubt: LibreOffice's own flat-ODF export of this workbook states
`<chart:coordinate-region svg:height="4.208cm">` = **119.27 pt**, and our plot rectangle is
119.30 pt. Only the divisor differs.

So, at the axis' stated 11 pt:

| | needed, in points | as a multiple of the em |
|---|---|---|
| ours (`IntervalsThatFit`, the face's own hhea line height, DejaVu Sans) | 12.805 | 1.1641 |
| 26.2.4.2, bracketed by `N = 8` | **(13.252, 14.909]** | **(1.2047, 1.3554]** |

Three of the tree's own chart fixtures bracket it from the other side, read the same way — the
flat export for the plot height, the PDF for the tick count, both at 26.2.4.2, all three drawn in
Liberation Sans at 10 pt:

| fixture | coordinate-region height | reference's labels | intervals | what that requires |
|---|---:|---|---:|---|
| `chart-bar-sheet.xlsx` | 54.60 pt | `0 50 100 150 200` | 4 | needed ∈ (6.067, 13.65] |
| `chart-bar-sheet.ods` | 108.79 pt | `0 20 … 180` | 9 | **needed ≤ 12.088** |
| `chart-bar-deck.odp` / `.pptx` | 242.02 pt | `0 20 … 180` | 9 | nothing — `N` is clamped at 10 here |

The deck row is worth keeping because it looks like a constraint and is not: 242.02 over any
plausible label height exceeds ten, the clamp fires, and the deck cannot discriminate.

**Read those four brackets as ratios against the coordinate region, not as label heights.** They
assume `nTotalAvailable` is the *inner* plot rectangle, and the probe below shows it is not: the
coordinate region jumps by 5–11 pt exactly at the boundary, so it cannot be the quantity the
estimate is taken on, while the outer `chart:plot-area` crosses it smoothly. The brackets still
separate ours from the reference's — that comparison is between two numbers taken the same way on
the same document — but the em figures in them are not LibreOffice's label height.

### The swept probe was run, and it refutes every simple divisor including our own

`chart-bar-sheet.fods` is a nine-kilobyte hand-authored flat sheet with one chart, so it
parameterises cleanly. Its eight data cells were replaced with a range spanning 68…78 — the same
shape as `040`'s secondary axis, so `N ≤ 8` gives `60 65 70 75 80` and `N ≥ 9` gives `62 64 … 80`,
and the presence of the token `62` in `pdftotext` is a clean one-bit predicate for which side of
the boundary a rendering is on. The chart frame's height was then bisected, thirteen halvings, to
a frame resolution of 0.0034 cm, and the *outer diagram* rectangle read out of each boundary
rendering's own flat export — `chart:plot-area`, which is `maRemainingSpace` and is smooth
across the boundary where `chart:coordinate-region` jumps by 5–11 pt as the tick count
changes.

With the value axis given a style of its own, so that the category band and both axis titles stay
at the default 10 pt and only the value labels move:

| face | value-label size | frame at the N=8→9 boundary | plot-area height there |
|---|---:|---|---:|
| Liberation Sans | 10 pt | [6.93555, 6.93896] cm | 131.131 → 131.216 pt |
| Liberation Sans | 24 pt | [12.26074, 12.26416] cm | 272.976 → 273.061 pt |
| DejaVu Sans | 10 pt | [6.93555, 6.93896] cm | 131.131 → 131.216 pt |
| DejaVu Sans | 24 pt | [12.76660, 12.77002] cm | 286.469 → 286.554 pt |

**Two faces, identical at 10 pt to the last digit of the bisection, and 13.5 pt of plot-area apart
at 24 pt.** Their hhea line heights differ by 1.23% at every size — 0.14 pt at 10 pt and 0.34 pt at
24 pt — so the label height is *not* the line height, quantised or otherwise: a metric difference
that vanishes at one size and is amplified fourfold at another is not a metric difference at all.
Ours is the line height, which is why `040` comes out one interval long.

Differencing the two sizes of each face gives what the divisor's *slope* would be if the rest of
the composition held still — 1.126 em for Liberation Sans and 1.233 em for DejaVu Sans — but the
two then imply different constants for the band below the axis (29.9 pt against 20.2 pt) when the
band is the same in both, so the model is wrong and neither slope should be quoted as the answer.

**The probe's own confound, named so the next one avoids it.** Raising the value-label size widens
the left reservation, which narrows the plot, which can change how the category labels below it
are arranged — and that band is inside the plot-area rectangle being measured. So the third
variable was not held. **The probe that settles this has no category labels and no axis titles at
all**, so that the only thing between the plot-area rectangle and the axis line is a constant, and
it needs at least three sizes per face to separate a slope from an intercept.

**Nothing was changed on this evidence.** A flat 1.206 em fits the five corpus observations that
prompted the probe and is refuted by the probe itself; the existing divisor is at least principled
and is pinned from both sides by two of the tree's own fixtures. A constant fitted to `040` here
would be the fudge factor the notes warn about, arrived at by a longer route.

## 3 · `053`'s Grand Total is LibreOffice detaching the chart from the sheet, measured

The previous round left this unestablished and recorded three refutations. A fourth measurement
settles what the reference actually does, and it is not a chart rule.

LibreOffice's own flat-ODF export of `053_Personal_asset_inventory_5446d84b.xlsx` gives its chart

```xml
<chart:categories table:cell-range-address="local-table.$A$2:.$A$7"/>
```

— an **internal data table of six rows**, where an ordinary XLSX chart keeps a sheet reference.
The same export of `Keywords_Mapping_Graphs_and_Charts.xlsx`, which the previous round named as
the counter-example, contains **no `local-table` at all**: all eleven of its charts keep sheet
references, `'TOGAF Role Mapping DA'.A5:.A7` among them, and the reference duly draws that chart's
third point. Both files have the identical signature — a `c:strCache` exactly one point short of
the range named in `c:f`, `Grand Total` in the missing cell, a `c:pivotSource`, no
`totalsRowCount` anywhere — so no rule reading the chart part can tell them apart, and the earlier
refutation stands.

What differs is one thing, and it is in the sheet, not the chart: **`053` contains an Excel table
whose `displayName` is `Assets`, which is also the name of the sheet its chart references.**
LibreOffice imports that table as a database range of the same name —
`<table:database-range table:name="Assets" table:target-range-address="Assets.B4:Assets.C10"/>` in
the export — and `Assets!$H$24:$H$30` then resolves to nothing, so the chart falls back to its
cached points. `Keywords_Mapping` has no `xl/tables/` part at all.

Two consequences worth writing down:

- **`ScChart2DataSequence::BuildDataCache`'s totals-row rule is not the mechanism**, contrary to
  the candidate the previous round offered. It fires on `GetDBAtCursor(...)->HasTotals()`
  (`sc/source/ui/unoobj/chart2uno.cxx:2612-2632`), and `053`'s only database range is `B4:C10`
  with `totalsRowShown="0"`; the chart's last cell `H30` is not in it and it has no totals row.
- **Excel drew six on both documents.** The cache is what Excel last rendered, and it holds six of
  seven on `053` and two of three on every one of `Keywords_Mapping`'s eleven. So a rule that
  honoured the cache would match Excel on both and would *diverge from LibreOffice* on
  `Keywords_Mapping`, where LibreOffice draws the seventh point that Excel does not. There is no
  rule that matches this reference on both files, because the reference is not following one.

`053`'s 4-against-2 pagination is independent of the chart; its two compared pages agree to
mae 0.0201 / 0.0208 and ink +0.0056 / +0.0040.

## 4 · The five that are not chart defects, each with its measurement

- **`015_Project_Timeline_Template_Colored_Background_6434b0e8.docx` — no chart part.**
  `unzip -Z1 … | grep -i chart` is empty — the package holds no `word/charts/` part and no
  `chart` relationship (mind that the archive's own path carries `chartset-011`, so a `grep` over
  `unzip -l`'s header line matches and reads as a hit). Its recorded reference disagreement (6.78 against 16.64) is
  the 24.2/26.2 font-class rule: `verdict.py` refuses to score it against 24.2 —
  *reference only `DejaVuSans`, `DejaVuSans-Bold`; ours only `DejaVuSerif`, `DejaVuSerif-Bold`* —
  and against 26.2 it is 2 pages of 2 with page 2 matching and page 1 at ink −0.0039. The defect
  is the page gradient, the table-cell fills and a chevron preset drawn as a rectangle. Shapes and
  backgrounds, not charts.
- **`DynamicBubbleChart.xlsx` — a font-resolution defect, and not in the chart.** Unscoreable
  against **both** references, so it is not the version rule: each side draws Carlito for
  everything except one shape, where 24.2.7.2 and 26.2.4.2 both resolve **Liberation Serif** and we
  resolve **Liberation Sans**. Read out with PyMuPDF, the text in that face is the slicer
  placeholder on page 1 — *"This shape represents a slicer. Slicers can be used in at least Excel
  2010…"* — a `xdr:sp` in `xl/drawings/drawing1.xml` whose run states `sz="1100"` and **no
  typeface at all**. The chart draws none of it. This is the DrawingML unstated-Latin default, and
  it belongs to whoever owns font resolution.
- **`047_Date_tracker_Gantt_chart_bf34f3a8.xlsx` — formula recalculation and row height.**
  The reference's milestone dates are `9/15/2026`, `10/1/2026` … against our stored `1/4/2024`;
  the sheet recalculates `TODAY()` and we replay the cached values, which is why its bars fall
  outside our axis. Its 8 pages against our 5 are not the chart either: the reference's pages 3–5
  are almost entirely one wrapped comment per page — *"Information about the columns in the
  milestone table are in this row from cells B4 through E4"* set a glyph or two per line in a
  narrow row — which we set compactly.
- **`EHEST-Pre-departure-checklist-Rev.-1-06-12-2016.xls` — scale-to-page, on the recorded page.**
  Page 1, the recorded one, carries no chart and differs by where cell text is clipped
  (`…evaluate the actual risk of the flight or of the mainten` against `…of the maint`), 80 words
  over 24 pages of 24. The workbook *does* hold **nine chart substreams** (BIFF `BOF` `dt=0x0020`),
  and pages 10–11 carry a real and unrecorded chart difference: the reference splits its `RISK
  LEVEL` area chart across the page break, drawing its title, axis and the coloured
  `ACCEPTABLE`/`CAUTION`/`HIGH RISK` band at the foot of page 10 and a 30 pt strip of the plot on
  page 11, where we draw the whole chart on page 11 with a legend box instead of the band. Left
  for a later round; the recorded page is not it.
- **`033_Event_planning_tracker` page 3 — both references are drawing it wrongly, differently.**
  Its ink of −0.1135 and worst tile of 1.0 against 26.2 is **26.2.4.2 filling the whole table body
  with solid black**, over which none of its currency values is legible; 24.2.7.2 draws the same
  cells as **dates** (`9/17/1919`, `7/27/1906`, …) where the file holds currency, and adds a
  `Data` title and an `INFO` column the file does not have. We draw the currency, upright and
  legible, on a page that is otherwise the reference's. The page is unscoreable against either,
  for two unrelated reasons that are both LibreOffice's.

## Method notes

- **Read a rotated axis label out of the reference's *paths*.** 26.2.4.2 outlines them, so
  `pdftotext`, `pdffonts` and `pdf-ops.py` all report nothing and a word count reads as an excess
  on our side. `pymupdf`'s `get_drawings()` plus a cluster on `x + y` recovers one group per label
  and its bounding box; twenty paths-clusters against twenty labels is the check that the
  clustering is right.
- **`verdict.py` scored 8 of the 9 and crashed on `047`** with `KeyError: 'shifted_tiles'`, which
  is `compare-images.py` declining to compare pages of different sizes; a page-count mismatch of
  5 against 8 reaches it as one. Worth a guard in the tool.
- **Ask LibreOffice what it computed.** Three of the findings above — the plot height in §2, the
  `local-table` in §3, the database range that shadows the sheet name — are one `--convert-to
  fods` each. None of them is inferable from a rendering.
