# Round 107 — an Escher pattern fill, a cell hidden one stage too early, and a "row grid" that is a chart

    ours   = Paperless.Cli @ d07a9ce75 (base) and @ d07a9ce75 + this round's diff (head)
    ref    = /opt/libreoffice26.2/program/soffice — LibreOffice 26.2.4.2 — and the banked
             reference PDFs at /home/user/gate-orig-r83/ref
    corpus = /home/user/sample-files/sheets — 241 `.xlsx`, 2 `.xlsm` and 64 `.xls`, 307 in all;
             /home/user/corpus-odf/sheets — the converted `.ods`
    rule   = ink is `|ink|%` from `pdf-image-diff.py`, summed unsigned over the document's pages
    date   = 2026-09-12

## 0. The headline

| seat | what it was | what it is |
|---|---|---|
| **O40** an Escher pattern fill is painted as its solid foreground colour | *"2 shapes in 1 corpus `.xls`"*, and the whole of round 103's net regression | **fixed.** The four `mso_fillPattern` shapes of `apron-area.xls` are drawn as the recoloured 8 × 8 tile 26.2.4.2 draws, at a tile pitch of **2.9904 pt against the reference's 3.0098** and in the reference's own blended grey. Ink **1.51 → 0.44** and the document's one MAJOR page clears. r103's base for it was 0.88, so this is **0.44 below the figure that round started from** as well as below the one it left |
| **O39** a `showValue="0"` cell loses its text one stage too early | a row measured as though it were empty | **fixed, and the corpus reach is nil rather than two documents.** `XlsxHiddenValues` is deleted; `SheetDecoration.HidesValue` carries it alone at paint time. On `sheet-cf-icon-set-x14-custom.xlsx` the row goes **276 → 298 twips** against 26.2.4.2's own `style:row-height="0.2071in"` (298.2). **3** corpus documents state the attribute, not 2, and **all three render byte-identically** either way; what does move on them is extraction, which now carries the values 26.2.4.2's own csv export has always carried |
| **O41** `EHEST`'s row grid is about 2.5 pt out on some sheets | *"a row-height question rather than a drawing one"* | **the mechanism is refuted and the seat stays open, re-characterised.** Every cell span and every cell border on page 13 agrees with 26.2.4.2 to **0.05 pt**. The three rules r103 measured are the **embedded gauge chart's value-axis ticks**, and the disagreement is that chart's **plot rectangle**: ours 404.75 × 150.78 pt where the reference's is 413.76 × 160.04, inside an outer frame the two agree on to 0.2 pt |

## 1. O40 — the pattern fill

### 1.1 The census, redone with grouped shapes read

`fill-census.py` assembles each substream's drawing the way `XlsDrawingCollector` does —
`MSODRAWING` records and the `CONTINUE`s directly behind one, at that substream's own `BOF`
depth, so an embedded chart's drawing is a stream of its own — walks it as a record tree, and
reports `fillType` (property 384) per `msofbtSpContainer` together with `fFilled`, `fillColor`,
`fillBackColor`, `fillOpacity` and the form of `fillBlip` (`fill-census.txt`,
`fill-census-summary.txt`):

| | |
|---|---:|
| `.xls` read (one is not OLE2 and is skipped) | **63 of 64** |
| shape containers in them | **562** |
| stating `mso_fillPattern`, `mso_fillTexture` or `mso_fillPicture` | **4**, in **1** document |
| of which pattern / texture / picture | **4 / 0 / 0** |

So the comment's *"2 shapes in 1 corpus `.xls`"* is now **4 in 1**, all of them
`apron-area.xls`'s, all of them patterns, and none of them is a grouped shape — the count was
stale for a different reason than the brief supposed. The census does **not** see a shape
container that arrived as a bare `CONTINUE` behind an `OBJ` or a `TXO` (r103 §1.1), so 562 and 4
are floors.

*A correction to r103 that falls out of the same script.* That round's §1.5 reports **489** shape
containers, **299** setting `fAutoTextMargin` and **138** carrying the group flag. Re-running its
own `autotm.py` with the file list quoted gives **562 / 361 / 149**: seven of the 64 paths hold a
space, and an unquoted `$(cat …)` split them into words, so those documents contributed nothing.
The **44** child anchors in **2** documents reproduce exactly, and no conclusion of that round
turns on the other three numbers.

### 1.2 What 26.2.4.2 draws there, read out of its own resolved view

`soffice --convert-to fods` on `apron-area.xls` takes eight seconds and renders nothing. Its
`content.xml` gives four graphic styles `draw:fill="bitmap"` — `gr9`, `gr14`, `gr15`, `gr16` —
each naming a `draw:fill-image` and each carrying a `draw:opacity` of 50 %, 60 %, 40 % and 35 %.
The four images are 8 × 8 PNGs and decode to exactly two colours apiece: `#808080` or `#969696`
where the workbook's blip has a **clear** bit, `#ffffff` where it has a set one.

The workbook's blip store holds six entries; 4 and 6 are `msofbtBlipDIB`, 8 × 8, one bit per
pixel, palette `{white, black}`, bits `00110011` and a 45° diagonal (`dib.py` in the scratch
work; the two DIBs are quoted verbatim in `EscherPatternFillTests`). The four shapes state
`fillBlip` 6, 4, 6, 4 and `fillColor` `0x08000037`, `0x08000037`, `0x08000017`, `0x08000017` —
palette 55 and 23, which that workbook's `PALETTE` makes `#969696` and `#808080` — and none of
them states `fillBackColor`.

That is `DffPropertyReader::ApplyFillAttributes` exactly
(`filter/source/msfilter/msdffimp.cxx`:1388-1441 **in this tree**, which declares
27.2.0.0.alpha0+ and is not the 26.2.4.2 binary's source): guard the recolouring on the bitmap
being 8 × 8, test each pixel against `Color(0)`, write `fillBackColor` there and `fillColor`
everywhere else, both defaulting to `COL_WHITE`. **Both legs agree** — the source explains it and
the reference's own `fods` states the answer.

### 1.3 How big one tile is, measured rather than derived

A pattern states no tile size, so the tile is the bitmap's own — pixels at the device's
resolution, which headless LibreOffice reports as 96 dpi (`SvpSalGraphics::GetResolution`,
`vcl/headless/svpgdi.cxx`:44 in this tree). That would be 0.75 pt per pixel and 6 pt per tile.

26.2.4.2's own PDF of page 1 says the same without the source. It rasterises the transparent fill:
`/Im212` is 629 × 209 pixels placed by `q 151.909 0 0 50.513 427.691 65.82 cm`. Counting the grey
runs of one scanline gives **51 stripes between x = 6.0 and x = 628.0**, a period of 12.44 image
pixels — **3.0044 pt** — and the eight-pixel tile holds two of those periods, so one tile is
**6.0087 pt** against the constant's 6.0. Its `/SMask` is a constant 101 under `/Decode [1 0]`,
which is an alpha of 0.604 — the shape's stated `fillOpacity` of 39322/65536.

### 1.4 What this tree draws after the change

The same page rendered at 300 dpi at both binaries and the same scanline counted
(`sheetdraw-r107` scratch, reproduced here):

| | stripes | first | last | period | dark pixel | light pixel |
|---|---:|---:|---:|---:|---|---|
| ours | 51 | 1785.0 | 2408.0 | **2.9904 pt** | (178, 178, 178) | (255, 255, 255) |
| 26.2.4.2 | 50 | 1788.5 | 2403.0 | **3.0098 pt** | (178, 178, 178) | (253, 253, 253) |

**The dark sample is the same byte on both sides**, and 178 is `#808080` at 60 % over white —
so the recolour, the palette resolution and the opacity are all right at once, not just the
geometry. The 3.5 px (0.84 pt) offset in where the pattern starts is the shape's own origin and
is the residual §1.6 leaves.

### 1.5 The fix

- **`EscherPatternFill`** (`Paperless.MsBinary/Escher`) turns a blip's bytes and two colours into
  the tile. `EscherBlips` already hands a DIB out behind a synthesised `BITMAPFILEHEADER`, so the
  pixels come from `DeviceIndependentBitmap.ReadPixels`, which is arithmetic over a byte array and
  needs no codec; the recolouring is the reference's black test; anything that is not an 8 × 8
  bitmap is handed on as authored, which is what the reference does with a texture and a picture.
  `Extent` is the tile's size at 96 dpi and `Opacity` reads the 16.16 fraction.
- **`EscherInk.Read`** no longer resolves the three bitmap fill types to `fillColor`. It reports
  `Ink.BitmapFill` instead and leaves `Ink.Fill` null; `HasInk` counts it, so such a shape is still
  kept.
- **`XlsDrawing.TextureOf`** resolves `fillBlip` against the workbook's blip store, resolves the
  two colours through the workbook's palette, and answers a `SheetShapeTexture`.
- **`SheetShapeInk.Draw`** composes it into a `BitmapPaint` against the shape's own box, applying
  the print zoom to the tile as it already does to the stroke width. *Whether the zoom scales the
  tile is not measured here* — `apron-area.xls` prints at `style:scale-to="100%"`, so no document
  in the corpus separates the two answers.
- **`SheetDrawing.HasInk`** counts a texture. **This is the hunk the round nearly lost**: without
  it `SheetPageGraphics.DrawInside` culls the shape before it is placed, the tile is built and
  thrown away, and the rendering merely loses the wrong grey block instead of gaining the right
  hatch — 1.51 → 0.74 rather than 1.51 → 0.44, which looks like a win.

### 1.6 The witness

`apron-area.xls`, three pages, against the banked 26.2.4.2 reference:

| | pages | sum `\|ink\|%` | MAJOR | page 1 |
|---|---:|---:|---:|---:|
| r103's base | 3 | 0.88 | 1 | — |
| this round's base (= r103's head) | 3 | **1.51** | 1 | 1.47 MAJOR |
| head | 3 | **0.44** | **0** | 0.41 shifted |

Page 1's largest region was *"marks displaced or reshaped"* at the base and its verdict is no
longer MAJOR. **This closes r103's whole net regression and more**: that round's nine movers
summed 31.44 → 31.58, and −1.07 on this one document takes the same nine to **30.51**.

## 2. O39 — the hidden value, and the row it was shrinking

### 2.1 The two answers this tree had to one question

`ScOutputData::DrawStrings` clears `bDoCell` for a cell whose data bar or icon set says
`showValue="0"`, and the line numbers in the brief are this tree's exactly:

```
1691:    // skip text in cell if data bar/icon set is set and only value selected
1692:    if ( bDoCell )
1693:    {
1694:        if(pInfo->pDataBar && !pInfo->pDataBar->mbShowValue)
1695:            bDoCell = false;
1696:        if(pInfo->pIconSet && !pInfo->pIconSet->mbShowValue)
1697:            bDoCell = false;
1698:    }
```

That is paint time, long after `ScColumn::GetOptimalHeight` has settled the row. This tree
answered the same question twice: `SheetDecoration.HidesValue`, asked by
`SpreadsheetPages.DrawCell` — the mechanism-correct one — and `XlsxHiddenValues`, a separate
partial `iconSet`/`dataBar` reader that dropped the cell's text in `XlsxSheetReader.ReadCell`.

The second one shortens the row because of a rule that has nothing to do with the string:
`SheetOptimalRowHeights` skips a cell whose `GetOwnText()` is empty (`column2.cxx`:100-103 —
`GetNeededSize` returns zero for an empty cell before it looks at the pattern), so such a cell
never reaches the branch that says a cell **whose pattern carries a conditional format** is
measured through the EditEngine rather than through the arithmetic (`column2.cxx`:937-941). A
`showValue="0"` cell is by construction covered by a conditional format, so dropping its text is
exactly the way to make it take the wrong branch.

### 2.2 The measurement

`soffice --convert-to fods` on `tests/corpus/features/sheet-cf-icon-set-x14-custom.xlsx` writes
`style:row-height="0.2071in"` — **14.911 pt, 298.2 twips** — on all five of its rows.

| | row 1 | icon pitch in our PDF |
|---|---:|---:|
| base | 276 twips | **13.775 pt** |
| head | **298 twips** | **14.88 pt** |
| 26.2.4.2 | 298.2 twips | — |

The 298 is the same number `SheetConditionalRowHeightTests` already pins for a Calibri 11 cell on
the measured branch, and 276 is the arithmetic one — `trunc(220 × 1.18) + 40 − 23`.

### 2.3 The confinement, which is the part the brief warned about

Restoring the text to `ContentTableCell` reaches extraction, `XlsxChartRanges` and the print-area
scan. Measured rather than reasoned about.

`showvalue-census.py` walks every worksheet part of all 307 corpus spreadsheets and counts the
`iconSet`/`dataBar` elements whose `showValue` is off, split by namespace
(`showvalue-census.txt`):

| document | rules |
|---|---|
| `066_Agile_Gantt_chart` | `x14:iconSet` × 6 |
| `077_Inventory_list_with_highlighting` | `x14:iconSet` × 1 |
| `036_Simple_to-do_list` | `main:dataBar` × 1 |

**Three documents, not the two the brief names** — the third states it on a main-namespace data
bar, which an `iconSet`-shaped census does not see.

All three render **byte-identically** at both legs under `SOURCE_DATE_EPOCH`, and their ink is
unchanged to two decimals (3.50, 0.01, 0.55). So the print-area scan and the chart ranges are
demonstrably untouched on every document in the corpus that can reach this code, and **the
rendering reach of O39 is 0 of 947**.

What does change is extraction, and it changes in the right direction.
`paperless extract --format text` on `077_Inventory_list_with_highlighting` goes from 1545 to 1557
characters, the twelve added being the `1`s of the *For reorder* column — and 26.2.4.2's own
`--convert-to csv` of the same workbook writes `,1,IN0001,…` for those rows. The reference's text
export has always carried them; ours did not.

### 2.4 The fix

`XlsxHiddenValues.cs` is deleted and its two lines in `XlsxSheetReader` with it. Nothing else
read it. `XlsxIconSets` and `XlsxDataBars` — round 96/97's readers — already cover strictly more
than it did (both namespaces, the `NoIcons` bucket, the extension-only rule, the reverse and
`gte` rules), and they are what fill the `SheetIcon`/`SheetDataBar` that `HidesValue` asks.

## 3. O41 — the mechanism is refuted, and the seat is a chart

### 3.1 The row grid is not out

`EHEST-Pre-departure-checklist`'s page 13, every text span matched to the reference's by its own
string (`cmp.py`): **79 spans matched, and the 72 outside the gauge agree within 0.05 pt in both
axes** — the largest of them is `Performance` at 245.29 against 245.34. Every
horizontal rule the page draws between y = 250 and y = 340 agrees within **0.03 pt**, including
the two 1.75 pt block borders and the twelve hairlines between them.

The seven spans that do not agree are all inside one object, and it is the same object on every
page that disagrees: the **RISK LEVEL gauge**, an embedded chart.

### 3.2 What the three rules actually were

r103 measured *"three horizontal rules at 267.5 / 289.0 / 310.5 pt where 26.2.4.2 draws three grey
ones at 264.8 / 287.7 / 310.6"*. Those are the gauge's **value-axis ticks**: this tree draws its
axis line at x = 73.31 from y = 267.46 to 418.24 with a tick every **21.54 pt**, at 267.46,
289.00, 310.54 …, and the reference draws a grey axis at x = 65.19 from 264.84 to 424.89 with its
own labels every **22.86 pt**. Both figures reproduce r103's to a tenth of a point.

### 3.3 The chart's frame agrees and its plot rectangle does not

Page 15, from the drawn paths of both PDFs:

| | x0 | y0 | x1 | y1 | size |
|---|---:|---:|---:|---:|---|
| chart frame, ours | 51.00 | 237.90 | 535.04 | 433.08 | 484.04 × 195.18 |
| chart frame, 26.2.4.2 | 50.98 | 238.09 | 535.14 | 434.06 | 484.16 × 195.97 |
| plot area, ours | **73.31** | 267.46 | 478.06 | **418.24** | **404.75 × 150.78** |
| plot area, 26.2.4.2 | **65.19** | 264.86 | 478.95 | **424.90** | **413.76 × 160.04** |

So the anchor is right and the bands inside the chart are not: our value-axis band is **22.31 pt**
wide against the reference's **14.21**, and our category-axis band **14.84 pt** tall against
**9.16**. The labels are the same face at the same size on both sides — Carlito 6.49 against 6.50,
6.73 pt wide against 6.50 — and are right-aligned **7.21 pt** clear of our axis against
**1.76 pt** clear of the reference's. We also draw a 4.25 pt outward tick at every label and the
reference draws none.

### 3.4 Reach, and why it is worth a round of its own

The gauge is on **9 of `EHEST`'s 24 pages** — 8, 10, 13, 15, 17, 19, 20, 22 and 24 — and those
nine carry **5.45 of the document's 8.06** summed unsigned ink. Nothing else in the document is
near that.

**Left open.** It is a chart-layout question — where `ChartLayout` puts a plot rectangle inside an
embedded BIFF chart's frame, and how wide a band an axis' labels and ticks reserve — and it is not
bounded to one document's row heights, which is what the seat said. Whether the same band rule is
wrong on every BIFF chart or only on this one is **not measured here**.

## 4. Corpus reach and confinement

### 4.1 What changed behaviour, and what could not

| file | reachable from |
|---|---|
| `Paperless.MsBinary/Escher/EscherPatternFill.cs` (new) | `XlsDrawing` alone |
| `Paperless.MsBinary/Escher/EscherInk.cs` | `XlsDrawing` alone — `git grep EscherInk` finds one caller |
| `Paperless.MsBinary/Escher/EscherRecordTypes.cs` | four new `const`, no code path |
| `Paperless.Spreadsheets/MsBinary/XlsDrawing.cs` | `.xls` |
| `Paperless.Spreadsheets/Layout/SheetDrawings.cs` | a new record and a field only `XlsDrawing` sets |
| `Paperless.Spreadsheets/Layout/SheetShapeInk.cs`, `SheetPageGraphics.cs` | one more parameter, null everywhere but `.xls` |
| `Paperless.Spreadsheets/Ooxml/XlsxSheetReader.cs`, `XlsxHiddenValues.cs` (deleted) | the `xlsx` family |

So the whole diff is the sheets track, and the sweeps below measure it there and control it
elsewhere.

### 4.2 The sweeps

*(filled in from `sheets-sweep.tsv` and `confine-sweep.tsv`)*

### 4.3 The movers scored

*(filled in from `movers.tsv`)*

## 5. Tests

*(filled in)*

## 6. What this round could not settle

- **O41 is not fixed**, §3. The mechanism the seat named is refuted and the real one is measured;
  what remains is the plot-rectangle layout of an embedded BIFF chart, which is the chart track's
  subsystem rather than a hunk.
- **Whether the print zoom scales a pattern tile is unmeasured.** `apron-area.xls` is the only
  document that states a pattern fill and it prints at 100 %, so no corpus document separates
  "the tile scales with the drawing layer" from "the tile is an absolute size". This tree scales
  it, by the same argument the stroke width is scaled.
- **A texture fill stating no `fillWidth`/`fillHeight` and carrying an encoded raster is
  stretched rather than tiled**, because its natural size is not known without a codec and the
  readers do not have one. **0 of the 562 corpus shape containers state a texture or a picture
  fill**, so no document exercises it.
- **The 0.84 pt by which our pattern starts left of 26.2.4.2's** is the shape's own origin and is
  the same offset the ungrouped BIFF shapes carry; it is not the fill.
- **The reference's gauge chart draws no outward axis ticks and we draw a 4.25 pt one per label**
  (§3.3). Whether that is the whole of the 8.12 pt band difference is not established.

## 7. Files

| file | what it is |
|---|---|
| `results.md` | this |
| `fill-census.py`, `fill-census.txt`, `fill-census-summary.txt` | every Escher shape container in the 64 `.xls` with its fill type, its two fill colours, its opacity and the form of its `fillBlip` |
| `showvalue-census.py`, `showvalue-census.txt`, `showvalue-summary.txt` | every corpus worksheet's `iconSet`/`dataBar` elements whose `showValue` is off, by namespace |
| `sweep.sh`, `sheets.list`, `sheets-sweep.tsv` | the 307 sheets documents rendered at both binaries, `%%EOF`-checked and hashed |
| `confine.list`, `confine-sweep.tsv` | the same over 60 words, 60 slides and 40 converted `.ods` |
| `score.sh`, `movers.tsv` | each mover rendered again and scored on ink against its banked reference |
| `tests-nonfidelity.log`, `tests-fidelity.log` | the two test runs §5 quotes, as they came out |
