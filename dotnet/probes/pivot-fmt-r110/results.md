# What a pivot cell keeps, what the reference puts back, and the census that said nil

Round 108 seated O45 on five properties a pivot cell keeps and the reference throws away, and on
one approximation: the clearing falls back to `cellXfs[0]` where the reference falls back to the
`Normal` `cellStyleXf`. This round censuses the four unmeasured properties, checks the clearing
against 26.2.4.2's own output rather than against the C++ alone, settles the default-style half
with an authored workbook — and **retracts one of r108's own censuses**, which is what changes
the shape of the seat.

**The headline.** The reference does not only empty the range. `ScDPOutput::Output` ends with
`maFormatOutput.apply` (`dpoutput.cxx`:1190), which lays the pivot's own `<format>`/`dxf` records
back over the generated styles — and r108's census of those records as **nil** was an instrument
artefact. There are **286 of them across 12 of the corpus's 28 pivot parts**. So clearing a
property is only half a rule, and whether clearing it *alone* moves towards the reference has to
be measured property by property. Measured against 26.2.4.2's own resolved view of 9878 cells:
the **colour**, yes; the **font identity** and the **size**, no — clearing them is 85 and 89 cells
worse, because the `dxf` records put back what the workbook's own cells happen to state; the
**fill** and the **border**, nothing to clear, the corpus states neither where anything is
generated.

So this round lands the colour and the `Normal`-`cellStyleXf` correction, declines the other four
with a number each, and hands on a `<format>`/`dxf` seat that is now sized rather than believed
empty.

---

## The retraction: `<format>` is never a direct child of `<pivotTableDefinition>`

`probes/pivot-res-r108/format-census.py` asked `root.findall(m('format'))` — a direct-child
search — and a `<format>` element is a child of `<formats>`. It therefore reported *"0 of the 28
pivot parts state a single `<format>` element"*, and r108's write-up, the O44 register row and
`SheetPivotStyle`'s remarks all carry that number.

`format-census.py` here is that script with one path changed. Run over the same eleven workbooks:

| workbook | parts stating `<format>` | records | what the `dxf` entries state |
| --- | ---: | ---: | --- |
| `033_Event_planning_tracker` | 1 | 84 | align, border, fill, font, numfmt |
| `053_Personal_asset_inventory` | 1 | 40 | fill, font |
| `026_Monthly_cash_flow_statement` | 4 | 96 | fill, font |
| `049_Expenses_calculator` | 1 | 23 | fill, font |
| `035_Project_plan_for_law_firms` | 1 | 18 | align, font+fill+border+align+numfmt+prot, numfmt |
| `037_Personal_money_tracker` | 2 | 23 | align, font, numfmt |
| `DynamicBubbleChart` | 1 | 1 | align |
| `007_Contextures_chart_sample` | 1 | 1 | align |
| **total** | **12 of 28** | **286** | every record names a `dxfId` and every `dxf` states something |

All five `applyNumberFormats` / `applyBorderFormats` / `applyFontFormats` / `applyPatternFormats` /
`applyAlignmentFormats` flags are `0` on every one of these definitions, and 26.2.4.2 applies the
formatting regardless — so the flags are not a filter to model.

**Banked**: `format-census.py`, `format-census.txt`.

---

## (1) The census: face, size, colour and fill inside a pivot range

`statedface.py`, the same shape as r108's `statedborders.py` and with three differences that
matter. It counts a cell's **resolved** format — its own `s`, else its row's when the row is
`customFormat`, else its column's, else `cellXfs[0]` — where `statedborders.py` counted only cells
carrying an `s` of their own. It counts a property as present only when it **differs from the
sheet's own default `cellXf`**, which is what the clearing would leave, rather than when it is
merely stated. And it counts the font's declared generic class — `<family val="N"/>` — as a
property of its own beside the face's name, because it is: `035_Project_plan_for_law_firms` states
`Cambria` `family="1"` on part of its pivot where its default states `Cambria` `family="2"`, and
the first census written here compared only the names and reported that document as stating
nothing at all. The sweep found it moving anyway, which is how the omission was caught.

Base rate is the cell count of the rectangle. Over all 28 pivot ranges of the eleven pivot-bearing
workbooks, and then over the 19 that are worksheet-cached — the only ones a grid is generated for,
and so the only ones the clearing can reach:

| property | all 28 ranges, 10086 cells | the 19 generatable ranges, 9878 cells |
| --- | ---: | ---: |
| face | 1 (0.01 %) | 1 (0.01 %) |
| declared class | 93 (0.92 %) | 93 (0.94 %) |
| size | 101 (1.00 %) | 101 (1.02 %) |
| colour | 191 (1.89 %) | 79 (0.80 %) |
| **fill** | 112 (1.11 %) | **0 (0.00 %)** |
| *border* (r108) | *0* | *0* |

**The fill is nil where it can be reached.** All 112 cells stating one are in `026` and `053`,
whose caches are `type="external"`: no data pilot is created for them, nothing is cleared and
nothing is generated. So the fill joins the border — by the seat's own rule, no code is written
for either.

Only three documents state any of the rest. The face is one cell, `037_Personal_money_tracker`'s
`B19`; the declared class is 91 cells of `033`, 1 of `035` and that same cell of `037`; the size
is 91 of `033` and 10 of `037`; the colour is 78 of `033` and that cell of `037`.

*The 9878 here is the stated `ref` rectangles; r108's 9970 is the **cleared** rectangle, the union
of `ref` with the page-field rows above it and the computed table. None of the eleven workbooks
states a page field, so on this corpus the two rectangles coincide and the difference is only in
what each script counted.*

**Banked**: `statedface.py`, `statedface.txt`.

---

## (2) The reference's own answer, cell for cell — and it does not say "clear"

The C++ says the range is emptied. What 26.2.4.2 *does* is a separate measurement, and it is the
one that decides. `clearfour.py` scores two models against the reference's own resolved view of
every cell of every generatable pivot rectangle, taken from `soffice --convert-to fods`:

- **merge** — the cell keeps what the workbook states, which is what this tree shipped;
- **clear** — the cell is taken back to the document's own default.

The comparison is the boolean *does this property differ from the document's default* on both
sides — from `cellXfs[0]` on ours and from the `Default` cell style of the reference's `.fods` on
the reference's — so no theme colour has to be resolved through two pipelines to be compared, and
it is exactly the question the two models disagree about.

| property | cells | merge agrees | clear agrees |
| --- | ---: | ---: | ---: |
| colour | 9878 | 9804 | **9873** |
| face | 9878 | 9877 | **9878** |
| declared class | 9878 | **9835** | 9750 |
| **font identity** (face and class together) | 9878 | **9835** | 9750 |
| size | 9878 | **9872** | 9783 |
| fill | 9878 | 9789 | 9789 |

The face and its declared class are scored jointly as well as separately because they are one
property to resolve: a face named without the generic class that qualifies it is a different font
to fall back from, and `XlsxCellFormats.Apply` already says so for a rich-text run. Taken jointly,
clearing the font identity is **85 cells worse** than leaving it alone.

*One piece of instrument noise in the class column, which does not move the comparison.* The
reference declares a second `<style:font-face>` for a family whenever two styles disagree about
its generic class — `Consolas` swiss and `Consolas1` modern in `033`, `Gill Sans MT` swiss and
`Gill Sans MT1` with no generic at all in `049` — so `049` reads as four cells whose class differs
from its default when nothing in the workbook says so. Those four are wrong under *both* models
and cancel out of merge against clear. The 85 is `033`'s, and `033`'s is a class its own cells
state.

Read out:

- **colour** — clearing is right on 69 more cells than merging.
- **font identity** — clearing is wrong on 128 and merging on 43.
- **size** — clearing is wrong on **95** and merging on 6.
- **fill** — the two models are the same model here, because no cell states a fill. Both are wrong
  on the same 89 cells of `033`, where the reference paints a **black** background that comes
  entirely from the `dxf` path. That is the *"a fill or background shading the reference has and we
  do not covers 17.4 % of the page"* r108 recorded against `033`'s page 3 and left unexplained.

**Everything clearing loses is `033_Event_planning_tracker`.** Its `dxf` records restore a 12 pt
`Consolas`-modern on 89 of its 91 cells, and its own cells happen to state the same 12 pt
`Consolas`-modern. Reproducing that by *not* clearing is an accident. It is an accident that
agrees with the reference 85 more times than clearing does, and no free parameter separates the
two, so the font identity and the size wait for the `dxf` half.

**And the colour's own margin is smaller than 9873 against 9804.** 73 of the 74 cells clearing wins
are `033` body cells carrying `style:use-window-font-color="true"` over the black `dxf` fill: the
reference is drawing *white on a dark ground*, and neither model draws the white or the fill. What
clearing genuinely fixes is one cell — `037`'s `B19`, a pivot corner cell the workbook leaves in
14 pt Times New Roman in an accent colour where the reference draws 11 pt Calibri black. What it
genuinely breaks is five cells of `033`'s row-label column, `B6:B10`, where a `dxf` states the
black this tree was reproducing. Both numbers are here rather than only the 69.

**Banked**: `clearfour.py`, `clearfour.txt`, `fods-face.py`.

---

## (3) What is implemented

**The colour is cleared. The face, the declared class, the size, the fill and the border are not.**
Each of those five has a number above and the numbers point the same way: clearing a property the
`dxf` path restores moves away from the reference, and clearing one the corpus never states moves
nothing.

`SheetPivotStyle` gains `Cleared`, the whole format the emptied rectangle falls back to, and takes
the colour from it. The three existing members — weight, horizontal alignment, indent — are
unchanged and still a differential the generated style may overwrite. `Cleared` carries the whole
format rather than one more nullable field precisely because which of its properties are taken is
a measurement that will move when the `dxf` records are read.

**And the base moved, which is the other half of what ships.** `XlsxPivotGrid.Apply` now takes
`XlsxCellFormatTable.StyleDefault` — the `Normal` `cellStyleXf` — where it took
`SheetCellFormats.SheetDefault`. Those are not the same thing twice over: `SheetDefault` is
`cellXfs[0]` *or*, where the sheet states a `<col>` spanning to the last column, that column's
format (`XlsxSheetFormats.cs`:60-68), and `033` and `035` both state one. The weight, the
alignment and the indent the clearing puts back come from this base too, so the correction is not
confined to the colour even though the colour is the only property newly taken from it — on this
corpus the two bases happen to agree on all three of those, which is why §6's movers are a colour
story.

---

## (4) The default-style half, settled by an authored workbook

`tests/corpus/regression/pivot-default-style.xlsx`, written by `make-default-fixture.py`. Three
fonts that differ in face, size and colour at once:

| where | font |
| --- | --- |
| the `Normal` `cellStyleXf` | Liberation Sans 11, black |
| `cellXfs[0]` | Liberation Serif 18, red |
| `cellXfs[1]`, on every cell of the pivot's rectangle | Liberation Mono 8, bold, green, yellow fill |

and three sheets: `Data` (the cache's source), `Cleared` (a hidden-header pivot over `A1:C4`, every
cell stating `cellXfs[1]`), and `Plain` — the same block three times with `cellXfs[1]`, with
`s="0"`, and with no `s` at all.

**26.2.4.2's own `.fods` of it:**

| cells | resolved to |
| --- | --- |
| `Cleared` `A1:C4`, all twelve | **Liberation Sans 11 black** — the `Normal` style. Row 4 additionally bold, which is the generated `Pivot Table Result`/`Title`. |
| `Plain` `A7:C10` (`s="0"`) | Liberation Serif 18 red — `cellXfs[0]` |
| `Plain` `A1:C4` (`s="1"`) | Liberation Mono 8 bold green on yellow — untouched |
| `Plain` `A13:C16` (no `s`) | the `Normal` style |

So **the clearing falls back to the `Normal` `cellStyleXf`, not to `cellXfs[0]`**, and the size is
cleared too when no `<format>` record puts one back — a second, independent confirmation of §2's
diagnosis.

`XlsxCellFormatTable` now carries `StyleDefault`, the `Normal` `cellStyleXf` resolved through the
same `Resolve` the cell formats go through, and `XlsxPivotGrid.Apply` takes it instead of
`SheetCellFormats.SheetDefault`. `XlsxCellFormats.NormalStyleXf` is the `builtinId="0"` lookup,
the same rule `XlsxStyles.DefaultFormatId` already applies to number formats and `probes/numfmt-r68`
already probed for them.

**The trap the corpus skill warns about caught this fixture once and was caught by a control.** The
first build omitted `<pivotCaches>` from `workbook.xml`; the pivot did not import at all, the cells
came back exactly as written, and the file read as evidence that *nothing* is cleared. What exposed
it was counting `table:data-pilot-table` in the output — 0 against the 8 that r108's known-good
fixture produces. The fixture that ships is r108's `make-fixture.py` with `styles.xml` and the sheet
list changed, so it is a modification of a file already known to import.

**Banked**: `make-default-fixture.py`, `fods-face.py`.

---

## (5) The other half of the default-style finding, sized and not landed

`XlsxSheetFormats` builds a sheet's own default — what a cell resolves to when neither it, its row
nor its column states a format — from `cellXfs[0]` (`XlsxSheetFormats.cs`:52). The fixture above
says that is the wrong entry there too: its `Plain` sheet's `A13:C16` states no `s` at all and
26.2.4.2 gives it the `Normal` style, not `cellXfs[0]`. LibreOffice's own reason is in the reader —
`rAttribs.getInteger(XML_s, -1)` and an immediate return on a negative id — and
`XlsxStyles.DefaultFormatId` already applies exactly this rule to number formats.

**It is not landed here, because unlike the pivot half it is not nil-reach.** Censused over the
sheets track: of the **242** `.xlsx`/`.xlsm` that carry both a `cellXfs` and a `cellStyleXfs`,
**6 give `cellXfs[0]` and the `Normal` `cellStyleXf` different content** —
`jobs-bulletin-51-22-december-2025` (`fontId` 1 against 0),
`sectors-defense-and-aerospace` (`fontId` 3 against 0, plus a wrap and a top alignment),
`esurf-12-135-2024-t01`, `essd-16-3433-2024-t02`, `Published_Issuances_2024` and
`ans_mappings_of_eccairs_terms` (each an `<alignment>` on one side only). None of the six holds a
pivot table, so the pivot change is unaffected by them; but changing the *sheet* default is a
change to six real renderings and belongs to a seat that sweeps for it.

*Banked in this round's `normalcmp.py`.*

---

## (6) Reach, measured honestly

**Confinement.** All **307** sheets-track documents rendered at both binaries with
`SOURCE_DATE_EPOCH` fixed, `%%EOF` checked before hashing, each render deleted as it went:
**0 failures, 0 truncations, and 3 documents moved** — `033_Event_planning_tracker`,
`035_Project_plan_for_law_firms` and `037_Personal_money_tracker`. The other **304 are
byte-identical**, the 64 `.xls` among them as the control, since the change is inside
`XlsxPivotGrid` and `XlsxCellFormats` which only the OOXML spreadsheet path enters.
`sheets-sweep-r110.tsv`.

*That sweep measured a wider variant than ships.* It was run against a build that cleared the
font identity as well as the colour; the shipped build clears a strict subset of the same cells,
so a document byte-identical there is byte-identical here. The eleven pivot-bearing documents were
re-swept against the shipped build to get the mover list exactly:
`pivot-sweep-shipped.tsv`, same three.

**Page counts and alphanumeric characters** — the two checks `batch-check.sh` makes in that order,
taken against the banked 26.2.4.2 reference in `/home/user/gate-orig-r83/ref` rather than
re-rendering it. Column 9's `glyphs`, not the token count:

| document | ref pages / glyphs | base | head |
| --- | --- | --- | --- |
| `alle einzeln` | 186 / 278868 | 186 / 278869 | 186 / 278869 |
| `Keywords_Mapping_Graphs_and_Charts` | 46 / 27201 | 46 / 27295 | 46 / 27295 |
| `DynamicBubbleChart` | 5 / 1728 | 5 / 1723 | 5 / 1723 |
| `049_Expenses_calculator` | 4 / 1548 | 4 / 1558 | 4 / 1558 |
| `007_Contextures_chart_sample` | 7 / 1849 | 7 / 1836 | 7 / 1836 |
| `037_Personal_money_tracker` | 5 / 2497 | 5 / 2476 | 5 / 2476 |
| `033_Event_planning_tracker` | 3 / 2650 | 3 / 2720 | 3 / 2720 |
| `035_Project_plan_for_law_firms` | 4 / 3879 | 4 / 3923 | 4 / 3923 |
| `027_Simple_personal_cash_flow_statement` | 10 / 7949 | 10 / 7932 | 10 / 7932 |
| `026_Monthly_cash_flow_statement` | 11 / 7852 | 11 / 7864 | 11 / 7864 |
| `053_Personal_asset_inventory` | 2 / 226 | 4 / 257 | 4 / 257 |

**Not one page count and not one alphanumeric count moves**, which is what a colour change should
do and is stated here rather than left to be assumed. `reach-r110.tsv`.

**And the ink is worse.** Summed unsigned `|ink|%` against the same banked reference, both legs
rendered in one run:

| document | base | head | Δ |
| --- | ---: | ---: | ---: |
| `033_Event_planning_tracker` | 11.27 | 11.40 | **+0.13** |
| `037_Personal_money_tracker` | 1.25 | 1.26 | +0.01 |
| `035_Project_plan_for_law_firms` | 1.13 | **1.12** | −0.01 |
| the other eight | | | 0.00 |
| **total** | **21.01** | **21.14** | **+0.13** |

No MAJOR verdict moves, at 4 before and 4 after. `ink-r110.tsv`.

**Why it is shipped anyway, and the number that argues against it.** The +0.13 is `033`, where
the reference paints a black `dxf` fill and draws its body text in the automatic colour — white —
and neither the old behaviour nor the new one draws either. The old behaviour's near-black text
scores marginally better against white-on-black than the new grey does. What the change actually
fixes is `037`'s corner cell, and what it actually breaks is `033`'s five row-label cells; both
are the same missing feature, the `dxf` records. Against that, the rule itself is not inferred —
the authored fixture in §4 shows 26.2.4.2 clearing the colour outright — and the cell-for-cell
score against the reference's own resolved view is better by every aggregation. This is r108's
own trade, on the same documents, at the same order of magnitude: it shipped a clearing that cost
0.09 for a cell score of 179 against 0. One line of `SheetPivotStyle.Over` reverses it if the next
seat weighs it the other way.

**One thing the sweep found that the census had missed, and it is worth carrying.**
`035_Project_plan_for_law_firms` moved although its census row was zero on all five properties.
Two reasons, both instructive. Its pivot cells state `Cambria` `family="1"` where its default
states `Cambria` `family="2"` — the same name, a different declared generic class, which the first
census compared away; that is why the class is its own column now. And the base this tree cleared
*to* was not `cellXfs[0]` at all on that sheet: `XlsxSheetFormats` promotes a `<col>` spanning to
the last column into the sheet default (`XlsxSheetFormats.cs`:60-68), and `033` and `035` both
state one — an 11 pt face where `cellXfs[0]` is 10 pt. So the shipped change moves the base twice
over, from the full-width column's format to the `Normal` `cellStyleXf`, and the reference's own
`.fods` agrees with the second: those cells resolve to `Default`.

---

## Tests

Every project was run on its own so a truncated run cannot hide as a pass, and the totals below
are read out of those runs' own output.

Ten non-fidelity projects — Containers 109, Core 560, Markup 259, OpenDocument 160,
Presentations 1107, Rendering 164, Spreadsheets 1337, Text 728, Vector 309, WordProcessing 1938 —
**6671 passed, 0 failed**.

**`Paperless.WordProcessing.Tests` had to be run twice, and the first run is exactly the trap
`CLAUDE.md` records.** Under a load average of 40 — three other rounds were building and sweeping
on this host — it printed `Catastrophic failure: Test process crashed with exit code 137` and then
`Passed! - Failed: 0, Passed: 1935, Total: 1935`. A green line with **three fewer tests than the
project has**. Re-run alone it is 1938 of 1938. Nothing in this round can reach the words track;
the count is what caught it, not the colour.

`Paperless.Fidelity.Tests`: **Failed: 10, Passed: 542, Total: 552** — the known ten and no
eleventh: `PageDrawingComparisonTests.EveryLineIsDrawnWhereLibreOfficeDrawsIt` on
`paginated.fodt`/`.doc`/`.docx`/`.rtf`,
`TabStopComparisonTests.AListLabelsTabAdvancesToLibreOfficesStop` on
`list-label-overrun.doc`/`.fodt`/`.docx`/`.odt`,
`SheetDrawingComparisonTests.APictureIsDrawnWhereLibreOfficeDrawsIt` on `sheet-rich-text.xlsx`,
and `JustificationShrinkComparisonTests.TheParagraphBreaksWhereLibreOfficeBreaksIt` on
`justify-shrink-2013.docx`. `tests-fidelity.log`.

**`SheetPivotDefaultStyleTests` is four tests over `regression/pivot-default-style.xlsx`, and
exactly one of them fails at the base — which is the honest count and is stated as such.** With
`XlsxPivotGrid.cs`, `XlsxCellFormats.cs`, `XlsxReader.cs` and `SheetPivotStyle.cs` taken back to
`a311b00e2`, `AnEmptiedPivotCellTakesTheNormalCellStylesColour` fails and the other three pass.
That is by design rather than by accident: the change is one property, so one test can pin it,
and the other three are the controls that make the first one mean something —
`ACellStatingTheDefaultCellFormatIsNotTheNormalStyle` shows the fixture really does state two
different formats where a base tree resolves the `s="0"` one correctly,
`OnlyTheColourIsClearedAndTheReferenceClearsTheWholeFont` records the two properties deliberately
left alone, and `TheGrandTotalRowIsStillBoldAndTheRowsAboveAreNot` shows the generated styles
still land on top of the new base. The failing one discriminates all three candidates at once: the
cleared colour is black (the `Normal` style), not red (`cellXfs[0]`) and not green (the cell's
own).

`SheetPivotPackedTests` and `SheetPivotGridTests`, r107's and r108's fifteen, are unchanged and
still pass: their fixture's `Normal` `cellStyleXf` and `cellXfs[0]` carry the same font, which is
exactly why it could not settle the question this round's fixture settles.

---

## (7) What is left, and what it is worth

- **The pivot's `<format>`/`dxf` records are the seat this round hands on, and it is now sized
  rather than believed empty.** 286 records across 12 of 28 pivot parts; 7 of those parts are in
  the 19 the grid is generated for. On the corpus their visible effect is concentrated in one
  document — `033_Event_planning_tracker`, where they paint a black background over 89 cells of
  the pivot body, restore a 12 pt `Consolas`-modern over all of it and put a black back on the
  five row-label cells — and that black background is `033`'s page-3 residual, 17.4 % of the page,
  which r108 recorded as unexplained. `037_Personal_money_tracker` is the only other one whose
  records show at all, and they are three cells of font size. `049`, `035`, `026`, `053`, `007`
  and `DynamicBubbleChart` state 179 records between them and 26.2.4.2's own `.fods` shows not one
  of them changing anything, so a first implementation has a large null to check against as well
  as a target — which is the part of this that a matcher can most easily get wrong in the
  permissive direction. What it costs is
  `FormatOutput`'s matcher: `sc/source/core/data/PivotTableFormatOutput.cxx`, `findMatchingLines`
  over each `<format>`'s `pivotArea` references, with grand-total and label/data special cases.
  That is a seat, not a corner of one.
- **Landing it flips three decisions in this round**, and each is a one-line change with its
  number recorded here: the font identity and the size become worth clearing (9750 and 9783
  today, both of which should reach 9878 once the records are read), and the fill becomes worth
  modelling.
- **The sheet default itself is still `cellXfs[0]`,** where 26.2.4.2 uses the `Normal`
  `cellStyleXf` for a cell stating no `s`. §5 sizes it at 6 of 242 corpus workbooks.
- **`053_Personal_asset_inventory` still cannot be scored.** 4 pages against the reference's 2 at
  base and at head, so `pdf-image-diff.py` reports none and its `0.00` is an absence of
  measurement. Its one pivot is externally cached and nothing is generated for it either way.
- **The border stays nil** and this round did not re-measure it; r108's `statedborders.py` is the
  census and this round's `statedface.py` agrees on the same rectangles.

---

## Citations, re-checked by hand

Every `file:line` was re-opened in `/home/user/libreoffice-core`. **That tree is not the reference
binary's source** — `configure.ac` declares 27.2.0.0.alpha0+, the binary is 26.2.4.2, and the
checkout is one bulk import — so each is *this tree*, and the arm that measures the actual
reference is the `--convert-to fods` oracle throughout.

| Citation | What it should be | Verified |
| --- | --- | --- |
| `pivottablebuffer.cxx`:1322-1338 | `PivotTable::finalizeImport`, the `clearContents(… HARDATTR \| STYLES …)` over the stated range | yes |
| `pivottablebuffer.cxx`:1415-1417 | the `maFormats` loop that finalizes each `PivotTableFormat` | yes |
| `dpoutput.cxx`:1190 | `maFormatOutput.apply(*mpDocument)`, the last statement of `outputDataResults` | yes |
| `dpoutput.cxx`:1193-1226 | `ScDPOutput::Output` — `CalcSizes`, `maFormatOutput.prepare`, then `DeleteAreaTab(…, ALL)` | yes |
| `PivotTableFormatOutput.cxx`:657 | `FormatOutput::apply`, and the grand-total / label / data arms under it | yes |
| `workbookhelper.cxx`:727-747 | `finalizeWorkbookImport` runs `getPivotTables().finalizeImport()` **after** every sheet | yes |
| `workbookfragment.cxx`:556-571 | `importSheetFragments` then `finalizeWorkbookImport`, which is why the clearing sees the applied formats | yes |
| `XlsxSheetFormats.cs`:52, :60-68 | `SetSheetDefault(pooled[0])`, and a full-width `<col>` overriding it | yes |
