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
| O14 | **A `.ppt` shape resizes itself around its own text, and three rules had to land together.** Escher's `fFitShapeToText` — bit 1 of `DFF_Prop_FitTextToShape` (191) — becomes `SdrTextAutoGrowHeightItem` on every horizontal text object `svdfppt.cxx`:1049-1110 builds, and the height is `SdrTextObj::AdjustTextFrameWidthAndHeight` (`svdotxat.cxx`:44-236, and its custom-shape twin `svdoashp.cxx`:2249-2421): the outliner's height **plus one unit of tolerance**, clamped to the minimum frame height, and only then the two vertical distances. **The minimum is what decides whether a shape may shrink**: `makeSdrTextMinFrameHeightItem` is set only on the `SdrRectObj` branch (`svdfppt.cxx`:1116-1119, :982), so a real placeholder can only grow and a `TextInShape` custom shape shrinks to fit. Two reader rules had to land with it or the shape grew to the wrong size: a trailing return leaves an **empty paragraph** behind (`PPTStyleTextPropReader::Init` appends one more portion after its loop, `svdfppt.cxx`:5403-5409) and that paragraph is measured in the run **before** it, not in the extra character run a `.ppt` always states (the banner's are 40 pt then 8 pt and the reference measures two lines of 40); and a `PageTitle`'s returns are **line breaks**, not paragraph ends (`:5241-5246`, `:5261-5266`). | `probes/slides-r97` §1. Witness `pres_ioc_phuket.ppt` p26: 64.88 → **103.37** against 26.2.4.2's 103.38. Reach — **1691 shapes in 38 of 51 `.ppt` state the bit**, 1348 with text, and the reference's own flat-ODP disagrees with the stated anchor by over a millimetre on **101 shapes in 22 documents**, 83 taller and 18 shorter. Gate `match` **49 of 51 before and after**, 0 page counts and 0 alphanumeric counts moved, 20 renderings changed; sum `|ink|%` over those 20 **118.08 → 103.08**, MAJOR pages 37 → 29, 11 improved / 2 worsened / 7 level. 12 of 12 non-`.ppt` renderings byte-identical. Two tests fail at the base. |

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
| O7 | **Six documents made worse by the drawing-layer clip**, worst +0.48 — the clip now exposes our own band edge where it differs from the reference's (`Template Pilot Logbook` p18 cuts 22 pt early). A column-width/break question. | `probes/ink-pass-r92`, crop banked |
| O10 | **Chart category-axis rotation** needs a hyphenator and a pattern set — the trigger is `ParaIsHyphenation`, not width. A feature, not a round; wrong hyphenation turns axes the reference wraps. | `probes/chart-axisrot-r91` |
| O11 | `048_Expense_trends_budget`: a remaining automatic-interval cap disagreement (ours step 50, reference 100 on 0…500). Fixing it properly means laying out at model size and scaling the finished `ChartDrawing`. | `probes/chart-axis-r87` |
| O16 | **`TK-Syllabus` residual 205.57 is a row-height drift**, not formatting: 93.5 of it on pages 1-100, same rows in the same order with ours two rows lower, and 1235 pages on both sides. | `probes/sheet-ink-r94` |
| O17 | **`alle einzeln.xlsx` 225.44** — states no conditional formatting at all; it holds a pivot table. Uncharacterised. | same |
| O18 | **`Background_Declaration_Template.xls` 136.07** — BIFF `CONDFMT`/`CF`, a different reader from the one just fixed. Overlaps O4. | same |
| O19 | **BIFF `TXO` formatting runs unread** — `ReadText` takes the string and stops, `TextOf` hardcodes 10 pt regular. 155 boxes in 17 `.xls`, 62 mixed-format. Untouched: nothing in r94 reached the BIFF path. | `probes/sheet-shapefill-r92` |
| O21 | **A cell's drawn text and its compared value are two different strings.** `XlsxCellText.Of` drops a lone `U+0009` in a string with no line feed — right for drawing, wrong for a `duplicateValues` key, so `"\tD5758620001301"` and `"D5758620001301"` read as duplicates and the reference says they are not. Needs the raw shared string kept beside the normalised one. | `probes/cond-format-r96`, **reach 1 cell in 1 of 947, 2 spans** |
| O22 | **`dataBar` and `iconSet` unread.** A different object on the reference side — `ScDataBarFormat`/`ScIconSetFormat` in `colorscale.cxx`, not `ScConditionEntry` — and drawing rather than formatting, so it belongs in `XlsxConditionalFormats` beside the colour scale. | `probes/cond-format-r96`, **reach 9 + 2 rules in 7 documents**, none in the sheets ink ranking's top forty |
| O12 | Whether a clipped **shape's own** text should leave the text layer. The 733 pages that refuted the general claim are mostly *cell* text, which the drawing clip never governed. | `probes/clip-textlayer-r93` |
| O13 | **Escher WordArt is not drawn as Fontwork.** `pres_ioc_phuket.ppt` p26: the reference clips a gradient to the glyph outlines, we paint the rectangle, so the title reads as a blank yellow bar. No MS-binary reader reaches `Paperless.Ooxml/DrawingML/Fontwork*`; the missing piece is `gtextUNICODE` (192), `gtextFont` (197), `gtextAlign` (194) and the shape-type-to-name mapping at `msdffimp.cxx`:2516-2600. **Now censused from the reference side too**: `--convert-to fodp` over all 51 `.ppt` and a grep for `draw:text-path="true"` finds **2 shapes in 2 documents**, and names the presets 26.2.4.2 gives them — `fontwork-plain-text` and `fontwork-arch-up-curve`, which are exactly the names `Fontwork*` is keyed by, so the engine is not the obstacle. What is *not* established is what the reference does with the shape's **fill** when the text is a path. Residual: after O14, page 26 is 2.04 of `pres_ioc_phuket`'s remaining 4.77 summed `|ink|%`. | `probes/slides-r97` §3; **reach 2 shapes in 2 of the 51 `.ppt`**, 5 in 4 over all 181 binaries |
| O15 | **The dominant-size census is the autofit search stopping at a different row, and the 17.773/17.802 in this entry was never part of it.** The `.ppt` half re-measured at r97: **19 pages of 1534 in 15 of 51 documents**, unmoved by O14. Thirteen draw the *same* alphanumeric count on both sides and **all thirteen carry a `style:shrink-to-fit` shape in 26.2.4.2's own flat-ODP view**; of the six that do not, three carry no such shape and are comparing different text. 18 of the 19 admit one integer stated size and two rows of `constScaleLevels` (the 19th is the `Fundamentals_Module_1_basics` p6 chart r85 already refuted), and on **12 of the 13 we draw the larger size**, so our block height is short of the reference's. Next instrument: `SlideAutofit`'s walk on `RESPA_-_Section_8_Webinar.ppt` p18 (314 characters both sides, ours 20.01 against 18.99, `pr23` min-height 9.964 cm) — the block height at each row against the box. The `.pptx` half was not re-measured. | `probes/slides-r97` §2; `probes/ppt-fit-r85` |
| O25 | **The bullet is a separate one-unit seat, split out of O15.** `2015-Civil-Rights-Website-training.ppt` p2: we draw 17.773 pt at y 175.011 where 26.2.4.2 draws 17.802 at 175.663, whose text baselines r85 already matches to 0.001 pt. 17.802 pt is 628 hundredths of a millimetre and 17.773 is 627 — **one unit, the model's own quantisation step** — so it is a rounding question in `Outliner::ImpCalcBulletFont` (`editeng/source/outliner/outliner.cxx`:851-855), already transcribed as `SlideAutofit.ScaledMarker`, and the 0.65 pt is that bullet's own vertical placement. Calling it *"a 0.16 % difference across a third of the deck corpus"* conflated it with O15's census, whose differences are whole points. | `probes/slides-r97` §2.1 |
| O23 | **`023_Unit_Circle_Chart_Circular_Percentage` draws its pie's data labels 32 pt right of the reference's inside an identical frame** — 10.718 → 16.424 of mean page ink once O8 put the frame where 26.2.4.2 puts it, and `021` 12.089 → 12.663 the same way, against `027` 12.396 → 11.265. The frames provably coincide: the chart is 682 pt wide on a 595 pt page, so the horizontal clamp saturates and three different stated offsets give one rendering on **both** sides. It is `ChartLayout`'s fit, not the capture. | `probes/words-close-r95` §1.5, `chart-variants.txt` |
| O24 | **Six more RTF style names reach *Standard*, and not by the route O9 closed.** `header`/`footer` (5 documents each) go through `COLL_HEADERFOOTER`, `toc 1`–`toc 3` (2/1/1) through `COLL_REGISTER_BASE` and `Figure` (1) through `COLL_LABEL` — intermediates with properties of their own that the import does **not** reset. A bare `Heading` (1) is a second gap: it is in no `ConvertStyleName` entry, so what reuses Writer's style is `xStyles->hasByName` on the *unconverted* name, a set nobody has censused. | `probes/words-close-r95/standard-census.txt`, 84 names over the 338 `.rtf` |

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
