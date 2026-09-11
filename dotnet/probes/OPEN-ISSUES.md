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
| O1 | Head of the sheets ink ranking: `TK-Syllabus-Comparison-Document-v2.xlsx` **304.57 summed \|ink\|% over 1235 pages, 142 MAJOR, and it passes the gate**; `alle einzeln` 225.44; `Background_Declaration_Template` 136.07. Plus BIFF `TXO` formatting runs unread — 155 boxes in 17 `.xls`, 62 mixed-format. | `agent/sheetink` |
| O2 | `FAA_Form_337` p67 and `pres_ioc_phuket` p26 carry ink missing from ours, both passing; `NAS-Infrastructure-Roadmaps-Weather` 9.58 uncharacterised. Plus 41 pages off on dominant drawn size across 33 documents, and `@lnSpcReduction` (209 elements in 40 of 251 `.pptx`, read from source and **never measured against a rendering**). | `agent/slidesink` |

## Open — seated, not yet dispatched

| # | what | seat |
|---|---|---|
| O6 | `sistem-rekod-markah-srm`: **not spill columns — a row height.** Its forty student rows are 276 twips here and 298 at the reference, on exactly the rows its `C4:T43` conditional format covers; every cell in that range is Arial 9 and 298 is one measured line of the document default's **11 pt**. Six one-attribute variants pin it (rewriting every `9pt` to `6pt` leaves 298.2; rewriting every `11pt` to `20pt` gives 522.1 against the 489.3 arithmetic elsewhere), and a clean four-cell probe **refutes** the rule fitted to them — a conditional cell is measured in its *own* font, 298 for an 11 pt cell under a 20 pt `Default`. The two measurements contradict and the seat is `ScColumn::GetNeededSize`'s font construction, not `ScPrintFunc::CalcPages`. Implementing the fitted rule is +1 −1 on the gate and was not kept. | `probes/ods-residue-r95` §4 |
| O7 | **Six documents made worse by the drawing-layer clip**, worst +0.48 — the clip now exposes our own band edge where it differs from the reference's (`Template Pilot Logbook` p18 cuts 22 pt early). A column-width/break question. | `probes/ink-pass-r92`, crop banked |
| O8 | **Frame capture, the half that can still move the eight**: r85 clamped to the sheet where the C++ clamps to the page **body** at every relation but `PAGE_FRAME`/`PAGE_PRINT_AREA` (`anchoredobjectposition.cxx`:562-573). Separately, `bConsidered` is `&&` for a fly and `\|\|` for a draw object (:130-141) — checked, does not move these ten. | `probes/words-seat-r94` |
| O9 | **`Body Text` and `caption` track the document's own `Normal`** — inheritance from *Standard*, not a pool value; needs `ConvertStyleName`'s two hundred names to be safe. `APoolStyleUnderStandardIsNotModelledYet` pins the gap. | `probes/rtf-bookmark-r88` |
| O10 | **Chart category-axis rotation** needs a hyphenator and a pattern set — the trigger is `ParaIsHyphenation`, not width. A feature, not a round; wrong hyphenation turns axes the reference wraps. | `probes/chart-axisrot-r91` |
| O11 | `048_Expense_trends_budget`: a remaining automatic-interval cap disagreement (ours step 50, reference 100 on 0…500). Fixing it properly means laying out at model size and scaling the finished `ChartDrawing`. | `probes/chart-axis-r87` |
| O12 | Whether a clipped **shape's own** text should leave the text layer. The 733 pages that refuted the general claim are mostly *cell* text, which the drawing clip never governed. | `probes/clip-textlayer-r93` |

## Open — measured as nil reach, kept only so they are not rediscovered

| # | what | reach |
|---|---|---|
| N1 | `wp14:sizeRelH`/`sizeRelV` unread | 1873 elements in 146 DOCX, **3** with a non-zero percentage |
| N2 | `MSO_CLR` system-colour branch | **0** corpus occurrences |
| N3 | Escher palette index 67 (button face) | 11 shapes, all form controls, which take no ink |
| N4 | `FrameLayout` positions an anchored frame against `page.BodyArea` | **no** corpus document has a frame inside an indented text section |
| N5 | An `.xlsx` row with no `ht` and no `sheetFormatPr`: 12.800 pt from the reference, 13.777 from us | **no** corpus document reaches it |
| N6 | `c:layoutTarget val="inner"`; an axis `rot` of exactly ±90° | **0** corpus documents each |
| N7 | **The ODF 1.2 `style:map` spelling of a conditional format** (was O3) | **0 of the 307 converted `.ods`** state a table-cell `style:map` without a `calcext:conditional-format`, and **0 cells** in the 53 that state both fall outside a `calcext:target-range-address`. LibreOffice writes both from one `ScConditionalFormatList` (`xmlexprt.cxx`:4779-4800 and `xmlstyle.cxx`:700-810), so it cannot. *The "1 of 307" this replaces counted `style:map` on **number** styles.* `probes/ods-residue-r95` §2 |
| N8 | **BIFF `CONDFMT` unread** (was O4) | 4 of the 64 `.xls` state one, 29 records over 35 ranges — and none can reach a row height, because `ImportExcel8::Read` holds its `AdjustRowHeight()` inside an `#if 0` (`read.cxx`:1284-1288) so **no BIFF8 row height is ever recomputed**: a workbook whose `ROW` records are patched to a uniform **100 twips**, `fUnsynced` clear, comes back from the reference at 100. Reading the record anyway leaves **64 of 64** renderings byte-identical. *The witness named with it, `Special-Procedures_2025-07-10.xls`, is an OPC zip wearing a `.xls` name and was never BIFF.* `probes/ods-residue-r95` §1 |

---

## How to close an entry

1. **Fixed here** — cite the C++ rule, show the reach, show confinement by building both ways
   with `obj`/`bin` cleared per leg and comparing byte for byte under `SOURCE_DATE_EPOCH`.
2. **LibreOffice defect** — show that the reference is internally inconsistent or contradicts its
   own documented rule, and that our behaviour matches the branch that is right. Add it to the
   table at the top and to `CLAUDE.md` if it is a measurement hazard as well.
3. **Nil reach** — census the corpus and move it to the table above with the number.
