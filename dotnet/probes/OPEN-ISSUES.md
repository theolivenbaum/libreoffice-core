# Open issues register

**Every entry here ends in one of two states.** Either it is *fixed in this tree*, or it is
*established as a LibreOffice 26.2 defect we deliberately do not reproduce*. "Known and tolerated"
is not a third state; anything genuinely not worth working is recorded as **NOT WORK** with the
measurement that says so, which is a form of the second.

Rounds add to this file and change entries' states. It supersedes hunting through ninety
`results.md` files for "left with its seat".

Reference is **26.2.4.2** (`/opt/libreoffice26.2/program/soffice`) throughout.

---

## Established LibreOffice 26.2 defects — closed, do not reproduce

| # | what | evidence |
|---|---|---|
| L1 | **On the draw layer the reference measures in one face and draws in another.** `FontAttribute` has no family-class field, so `getVclFontFromFontAttribute` rebuilds at `FAMILY_DONTKNOW` while the DX array was measured with the class on. `fc-match "Helvetica:bold"` → Liberation; `fc-match "Helvetica,sans:bold"` → DejaVu. | `probes/title-font-r92`; seventh confound in `CLAUDE.md`. Our output equals the reference's own class-less branch to 0.105 pt on a 155 pt line. |
| L2 | **`a:normAutofit/@fontScale` is not honoured.** Twenty one-attribute variants over two decks, drawn `Tf` sizes read from the reference's own PDF: 90000, 50000, 25000, absent, and each of those beside a stated `lnSpcReduction`, all draw identical sizes. Only removing the element moves anything; what is drawn is `constScaleLevels`' own row. | `probes/slides-ink-r94`. The trap: PowerPoint's stored value sits *near* the search's answer, so a witness measured at its own value cannot separate the hypotheses. Closed **without** code change. |

## Fixed in this tree — closed, kept so the mechanism is not re-derived

| # | what | evidence |
|---|---|---|
| O8 | **Frame capture, all three of its halves.** `bConsidered` is `bWrapThrough && !bTextBox` for a fly and `bWrapThrough \|\| !bTextBox` for a draw object, so a shape with **no text box** is never captured whatever its wrap; the area narrows from the sheet to the page **body** under `compatibilityMode` 15 at every relation but `PAGE_FRAME`/`PAGE_PRINT_AREA`; and **only where `mpAnchorFrame->FindBodyFrame()` finds one**, which a header, a footer and a footnote anchor do not. That last is what r85's wide rule was missing and what cost it `b053-19` and `Case-Study-Heathrow-Airport`. | `probes/words-close-r95`. 93 one-attribute fixtures at 26.2.4.2: **79 → 91 of 93** (2 unscoreable). Corpus reach **4 of 947 renderings and 0 of 676 converted words**; no page and no glyph moves on any of the four. |
| O9 | **`Body Text` and `caption` inherit the document's own `Normal`.** `PoolFormattingOf` answered a constant, which cannot express *Standard*; `PoolParentOf` answers `{None, Heading, Standard}` and the `\sbasedon` walk continues into style 0. | `probes/words-close-r95` §2. **Reach nil**: `caption` 0 of 338, `Body Text` 2 of 338 and neither moves, because both entries state their own `\fs` and a style's own `\fs` reaches no paragraph — 8 of 8 probes at the reference. |
| O20 | **The text and blank conditional-format families.** `containsText`, `endsWith`, `containsBlanks`, `notContainsBlanks` and `duplicateValues` are evaluated now, beside r94's `expression` and `cellIs`. Four arms are not what the specification would have you write, each read out of 26.2.4.2: `containsBlanks` is no mode at all but the substituted formula `LEN(TRIM(#B))=0` under `ScConditionMode::Direct` (`condformatbuffer.cxx`:860-866, :929-931), so three spaces are blank and a number never is; a text rule's `<formula>` is dead markup because only the `text` attribute is interned (:943-948); case is folded for a text cell (`IsValidStr`, `conditio.cxx`:1222-1229) and not for a numeric one (`IsValid`:1133-1145); and a multi-range `sqref`'s base cell is `ScRangeList::GetTopLeftCorner` over an ordering of `(tab, col, row)` (`rangelst.cxx`, `address.hxx`:396-399) — the smallest **column**, ties by row, not the componentwise minimum Excel writes the formula against. Precedence is document order between blocks (`ScDocument::GetCondResult` walks a `sorted_vector` of format keys) then priority within one (`CondFormat::insertRule` keys `maRules` by priority). | `probes/cond-format-r96`. Five fixtures under `tests/corpus/features/`, every expectation read out of 26.2.4.2's own rendering; all five fail at the base and pass after. Corpus reach **3 of 947 renderings**, sum `|ink|%` 27.62 → 27.02, no page count anywhere; `wiley-cancelled-title-list` 0.42 → 0.01 with its fill and font-colour operators 0/0 → 2/2 against the reference's 2/2. See N11 for why 1215 rules move three renderings. |

## Confounds — closed as measurement hazards, not defects

| # | what | note |
|---|---|---|
| C1-C5 | Five tarball font confounds | `CLAUDE.md`; move aside before measuring, never while a round is live |
| C6 | **`TODAY()`/`NOW()` recalculated on load.** Not reproducible: the delta changes daily. | `CLAUDE.md`; census before reading any glyph delta |
| C7 | Raster ceiling and outlining ceiling — the reference rasterises or outlines where we emit searchable text | `probes/odt-split-r82/overdraw.py` screens it |

---

## Open — being worked now

| # | what | round |
|---|---|---|

## Open — seated, not yet dispatched

| # | what | seat |
|---|---|---|
| O6 | `sistem-rekod-markah-srm`: **not spill columns — a row height.** Its forty student rows are 276 twips here and 298 at the reference, on exactly the rows its `C4:T43` conditional format covers; every cell in that range is Arial 9 and 298 is one measured line of the document default's **11 pt**. Six one-attribute variants pin it (rewriting every `9pt` to `6pt` leaves 298.2; rewriting every `11pt` to `20pt` gives 522.1 against the 489.3 arithmetic elsewhere), and a clean four-cell probe **refutes** the rule fitted to them — a conditional cell is measured in its *own* font, 298 for an 11 pt cell under a 20 pt `Default`. The two measurements contradict and the seat is `ScColumn::GetNeededSize`'s font construction, not `ScPrintFunc::CalcPages`. Implementing the fitted rule is +1 −1 on the gate and was not kept. | `probes/ods-residue-r95` §4 |
| O10 | **Chart category-axis rotation** needs a hyphenator and a pattern set — the trigger is `ParaIsHyphenation`, not width. A feature, not a round; wrong hyphenation turns axes the reference wraps. | `probes/chart-axisrot-r91` |
| O11 | `048_Expense_trends_budget`: a remaining automatic-interval cap disagreement (ours step 50, reference 100 on 0…500). Fixing it properly means laying out at model size and scaling the finished `ChartDrawing`. | `probes/chart-axis-r87` |
| O16 | **`TK-Syllabus` residual 205.57 is a row-height drift**, not formatting: 93.5 of it on pages 1-100, same rows in the same order with ours two rows lower, and 1235 pages on both sides. | `probes/sheet-ink-r94` |
| O17 | **`alle einzeln.xlsx` 225.44** — states no conditional formatting at all; it holds a pivot table. Uncharacterised. | same |
| O18 | **`Background_Declaration_Template.xls` 136.07** — BIFF `CONDFMT`/`CF`, a different reader from the one just fixed. Overlaps O4. | same |
| O19 | **BIFF `TXO` formatting runs unread** — `ReadText` takes the string and stops, `TextOf` hardcodes 10 pt regular. 155 boxes in 17 `.xls`, 62 mixed-format. Untouched: nothing in r94 reached the BIFF path. | `probes/sheet-shapefill-r92` |
| O21 | **A cell's drawn text and its compared value are two different strings.** `XlsxCellText.Of` drops a lone `U+0009` in a string with no line feed — right for drawing, wrong for a `duplicateValues` key, so `"\tD5758620001301"` and `"D5758620001301"` read as duplicates and the reference says they are not. Needs the raw shared string kept beside the normalised one. | `probes/cond-format-r96`, **reach 1 cell in 1 of 947, 2 spans** |
| O22 | **`dataBar` and `iconSet` unread.** A different object on the reference side — `ScDataBarFormat`/`ScIconSetFormat` in `colorscale.cxx`, not `ScConditionEntry` — and drawing rather than formatting, so it belongs in `XlsxConditionalFormats` beside the colour scale. | `probes/cond-format-r96`, **reach 9 + 2 rules in 7 documents**, none in the sheets ink ranking's top forty |
| O13 | **Escher WordArt is not drawn as Fontwork.** `pres_ioc_phuket.ppt` p26: the reference clips a gradient to the glyph outlines, we paint the rectangle, so the title reads as a blank yellow bar. No MS-binary reader reaches `Paperless.Ooxml/DrawingML/Fontwork*`. | `probes/slides-ink-r94`; **reach 2 shapes in 2 of 181** |
| O14 | The dark blue banner on that same page is **103.38 pt** tall in the reference and 64.88 in ours. Measured, uncharacterised, and separate from O13. | same |
| O15 | 41 `.ppt`/`.pptx` pages still differ on the dominant drawn size across 33 documents; the witness bullet is 17.773 pt at y 175.011 against 17.802 at 175.663. **Untouched** — that round spent its budget on the `@fontScale` measurement instead, which is why L2 exists. | `probes/ppt-fit-r85` |
| O23 | **`023_Unit_Circle_Chart_Circular_Percentage` draws its pie's data labels 32 pt right of the reference's inside an identical frame** — 10.718 → 16.424 of mean page ink once O8 put the frame where 26.2.4.2 puts it, and `021` 12.089 → 12.663 the same way, against `027` 12.396 → 11.265. The frames provably coincide: the chart is 682 pt wide on a 595 pt page, so the horizontal clamp saturates and three different stated offsets give one rendering on **both** sides. It is `ChartLayout`'s fit, not the capture. | `probes/words-close-r95` §1.5, `chart-variants.txt` |
| O24 | **Six more RTF style names reach *Standard*, and not by the route O9 closed.** `header`/`footer` (5 documents each) go through `COLL_HEADERFOOTER`, `toc 1`–`toc 3` (2/1/1) through `COLL_REGISTER_BASE` and `Figure` (1) through `COLL_LABEL` — intermediates with properties of their own that the import does **not** reset. A bare `Heading` (1) is a second gap: it is in no `ConvertStyleName` entry, so what reuses Writer's style is `xStyles->hasByName` on the *unconverted* name, a set nobody has censused. | `probes/words-close-r95/standard-census.txt`, 84 names over the 338 `.rtf` |
| O25 | **`Template Pilot Logbook`'s chart plot area is 596.30 pt wide against 26.2.4.2's 632.66** and starts 12.38 pt right of it, so its ink ends 23.98 pt short — the "cut 22 pt early" that was read as a band edge. The band is not it: our block rectangle and the reference's agree to **0.085 pt** on that very edge. Chart layout, beside O11 and O23. | `probes/clip-seats-r97` §2.1 |
| O26 | **A chart's own clip replaces the block clip instead of intersecting it.** An OLE is replayed from a metafile and `MetaClipRegionAction::Execute` calls `pOut->SetClipRegion` (`vcl/source/gdi/metaact.cxx`:1359-1369) where `MetaISectRectClipRegionAction::Execute` (:1394-1397) would intersect; the writer keeps the distinction (`vcl/source/pdf/pdfwriter_impl.cxx`:9608-9619 against :9657-9678), and 26.2.4.2's own stream writes the replace as `Q q … re W* n`. So the reference paints a chart into the page margin where we cut it at the block. Reach **3 documents**, 1.92 summed page-ink-%. **Blocked**: the rule does not say *which* charts escape — `044_Cash_flow_forecast` is clipped at the block and `012_Contextures` is not, and the block clip is worth 6.55 of `\|ink\|%` on `044`. | `probes/clip-seats-r97` §2.3 |

## Open — measured as nil reach, kept only so they are not rediscovered

| # | what | reach |
|---|---|---|
| N1 | `wp14:sizeRelH`/`sizeRelV` unread | 1873 elements in 146 DOCX, **3** with a non-zero percentage |
| N2 | `MSO_CLR` system-colour branch | **0** corpus occurrences |
| N3 | Escher palette index 67 (button face) | 11 shapes, all form controls, which take no ink |
| N4 | `FrameLayout` positions an anchored frame against `page.BodyArea` | **no** corpus document has a frame inside an indented text section |
| N5 | An `.xlsx` row with no `ht` and no `sheetFormatPr`: 12.800 pt from the reference, 13.777 from us | **no** corpus document reaches it |
| N6 | `c:layoutTarget val="inner"`; an axis `rot` of exactly ±90° | **0** corpus documents each |
| N7 | `NAS-Infrastructure-Roadmaps-Weather.pptx` as an ink defect — **refuted**. All 90 text records on its largest page sit at identical positions; the 3.52 % is a 0.13 % anisotropy seen through a 512 px instrument. | `probes/slides-ink-r94` |
| N8 | WMF raster op `0x7` (self-mask *and* invert the rectangle) — needs the destination back, and no corpus document reaches it | `probes/slides-ink-r94` |
| N10 | **Eight `cfRule` families with no corpus witness** — `beginsWith`, `notContainsText`, `uniqueValues`, `containsErrors`, `notContainsErrors`, `top10`, `aboveAverage`, `timePeriod`. The first three are one arm apiece of predicates already in `XlsxConditionalStyles`. | **0** rules each over 243 corpus `.xlsx`/`.xlsm`; `probes/cond-format-r96/cfrule-census.txt` |
| N11 | **A conditional-format rule count is not a reach figure.** 1215 newly-read rules move **3 of 947 renderings**: 358 search for `xxx` no cell holds, 663 sit on a `veryHidden` sheet or on hidden columns, 14 `duplicateValues` find no duplicate in 357 positions, and 66 `notContainsBlanks` hits are outside their printed block. The 27.04, 21.67 and 18.17 ink seats these were expected to take are **not** conditional formatting. | `probes/cond-format-r96` §3, `ink-nonmovers.tsv` |
| O12 | **A clipped shape's own text stays in the text layer** — the narrower question r93 left open, answered on a shape and not on cell text. 26.2.4.2 cuts an authored rectangle's fill at the block and writes its right-aligned word 51.9 pt past the clip anyway, on the paper, where `pdftotext` reads it; over the 947 banked renderings **150 pages in 61 documents, 5865 glyphs** of drawing-layer text lie wholly outside the drawing clip that governs them. **The change would have negative reach**: `ClipPath` in place of `ClipPathKeepingText` loses **3150 characters over 56 of the 74** renderings the clip touches and takes **8 of 947** outside the gate's `max(2%, 15)` glyph band. The trap: **MuPDF's text extraction applies the clip and poppler's does not**, so the same question asked through `pymupdf` answers the opposite, crisply. | `probes/clip-seats-r97` §1; guard test `SheetStraddlingDrawingTests.TheBlockClipKeepsTheShapesOwnGlyphs` |
| O7 | **The six documents the clip "made worse" are not one seat and four of them were never worse.** Counted three ways at 120 dpi, the clip removed 43,154 px on `Template Pilot Logbook` p18 and 26.2.4.2 leaves **every one of them** blank; `SIL_TDB609`/`605` p6-p7 the same, `PC1000` p1 two pixels. Their `|ink|%` rose because a region's `luma_gap` is the **signed mean** brightness difference over that region (`pdf-image-diff.py`:245-269) and `|ink|%` takes the absolute value of the mean rather than the mean of the absolutes (`:434-437`), so removing ink we wrongly drew stopped cancelling ink we wrongly miss **inside the column this project ranks on**. Corpus-wide the clip removes **31,384 px** the reference keeps against **10,216,274 px** it still wrongly draws. The residue re-seats as O25 and O26. | `probes/clip-seats-r97` §2, `overreach.tsv` |
| N9 | An `.xlsx` whose `indexedColors` are written `ffRRGGBB`, where oox reads the top byte as transparency (`decodeIntegerHex_impl`) — modelling it faithfully took the one affected document from 20.15 to 68.17, so it is left | **1** corpus document; `probes/sheet-ink-r94` |
| N15 | The `bConsidered` asymmetry of O8 — a draw object with no text box that does not wrap through — is the only case where the old reading and the C++'s differ | **4** objects in 4 of the 272 DOCX, of 6055 positioned objects (`probes/words-close-r95/capture-census.txt`) |
| N16 | `caption` / `Caption` as an RTF style whose `\sbasedon` does not resolve | **0** of the 338 converted `.rtf` apply one (`probes/words-close-r95/standard-census.txt`) |
| N12 | tdf#123002's escape — a header- or footer-anchored object whose top has passed the area's bottom is returned unadjusted. Modelled because leaving it out clamps where the reference does not; **no fixture and no census can reach it**, since it is a property of a computed position rather than of the markup | `probes/words-close-r95` §1.6 |
| N13 | **The ODF 1.2 `style:map` spelling of a conditional format** (was O3) | **0 of the 307 converted `.ods`** state a table-cell `style:map` without a `calcext:conditional-format`, and **0 cells** in the 53 that state both fall outside a `calcext:target-range-address`. LibreOffice writes both from one `ScConditionalFormatList` (`xmlexprt.cxx`:4779-4800 and `xmlstyle.cxx`:700-810), so it cannot. *The "1 of 307" this replaces counted `style:map` on **number** styles.* `probes/ods-residue-r95` §2 |
| N14 | **BIFF `CONDFMT` unread** (was O4) | 4 of the 64 `.xls` state one, 29 records over 35 ranges — and none can reach a row height, because `ImportExcel8::Read` holds its `AdjustRowHeight()` inside an `#if 0` (`read.cxx`:1284-1288) so **no BIFF8 row height is ever recomputed**: a workbook whose `ROW` records are patched to a uniform **100 twips**, `fUnsynced` clear, comes back from the reference at 100. Reading the record anyway leaves **64 of 64** renderings byte-identical. *The witness named with it, `Special-Procedures_2025-07-10.xls`, is an OPC zip wearing a `.xls` name and was never BIFF.* `probes/ods-residue-r95` §1 |

---

## How to close an entry

1. **Fixed here** — cite the C++ rule, show the reach, show confinement by building both ways
   with `obj`/`bin` cleared per leg and comparing byte for byte under `SOURCE_DATE_EPOCH`, and
   move the row into *Fixed in this tree* rather than deleting it: the next round needs the
   mechanism as much as the previous one needed the seat.
2. **LibreOffice defect** — show that the reference is internally inconsistent or contradicts its
   own documented rule, and that our behaviour matches the branch that is right. Add it to the
   table at the top and to `CLAUDE.md` if it is a measurement hazard as well.
3. **Nil reach** — census the corpus and move it to the table above with the number.
