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

## Open — seated, not yet dispatched

| # | what | seat |
|---|---|---|
| O3 | **ODS conditional-format row height, second spelling.** ODF 1.2 `style:map` reaches 1 of 307 and that document fails in the other direction. | `probes/ods-notes-r92` |
| O4 | **BIFF `CONDFMT` unread**, so the `.xls` and `.ods` spellings of one workbook now disagree about a row height — a regression *between spellings* introduced by closing the ODF half. | same |
| O5 | `017_Timeline_Templates`: the reference prints a **blank page** from a print-area extent. | same, `SheetDrawingArea` |
| O6 | `sistem-rekod-markah-srm`: pages 2 and 4 are narrow **spill columns** we fit onto the previous page. | same |
| O7 | **Six documents made worse by the drawing-layer clip**, worst +0.48 — the clip now exposes our own band edge where it differs from the reference's (`Template Pilot Logbook` p18 cuts 22 pt early). A column-width/break question. | `probes/ink-pass-r92`, crop banked |
| O8 | **Frame capture, the half that can still move the eight**: r85 clamped to the sheet where the C++ clamps to the page **body** at every relation but `PAGE_FRAME`/`PAGE_PRINT_AREA` (`anchoredobjectposition.cxx`:562-573). Separately, `bConsidered` is `&&` for a fly and `\|\|` for a draw object (:130-141) — checked, does not move these ten. | `probes/words-seat-r94` |
| O9 | **`Body Text` and `caption` track the document's own `Normal`** — inheritance from *Standard*, not a pool value; needs `ConvertStyleName`'s two hundred names to be safe. `APoolStyleUnderStandardIsNotModelledYet` pins the gap. | `probes/rtf-bookmark-r88` |
| O10 | **Chart category-axis rotation** needs a hyphenator and a pattern set — the trigger is `ParaIsHyphenation`, not width. A feature, not a round; wrong hyphenation turns axes the reference wraps. | `probes/chart-axisrot-r91` |
| O11 | `048_Expense_trends_budget`: a remaining automatic-interval cap disagreement (ours step 50, reference 100 on 0…500). Fixing it properly means laying out at model size and scaling the finished `ChartDrawing`. | `probes/chart-axis-r87` |
| O12 | Whether a clipped **shape's own** text should leave the text layer. The 733 pages that refuted the general claim are mostly *cell* text, which the drawing clip never governed. | `probes/clip-textlayer-r93` |
| O13 | **Escher WordArt is not drawn as Fontwork.** `pres_ioc_phuket.ppt` p26: the reference clips a gradient to the glyph outlines, we paint the rectangle, so the title reads as a blank yellow bar. No MS-binary reader reaches `Paperless.Ooxml/DrawingML/Fontwork*`. | `probes/slides-ink-r94`; **reach 2 shapes in 2 of 181** |
| O14 | The dark blue banner on that same page is **103.38 pt** tall in the reference and 64.88 in ours. Measured, uncharacterised, and separate from O13. | same |
| O15 | 41 `.ppt`/`.pptx` pages still differ on the dominant drawn size across 33 documents; the witness bullet is 17.773 pt at y 175.011 against 17.802 at 175.663. **Untouched** — that round spent its budget on the `@fontScale` measurement instead, which is why L2 exists. | `probes/ppt-fit-r85` |

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

---

## How to close an entry

1. **Fixed here** — cite the C++ rule, show the reach, show confinement by building both ways
   with `obj`/`bin` cleared per leg and comparing byte for byte under `SOURCE_DATE_EPOCH`.
2. **LibreOffice defect** — show that the reference is internally inconsistent or contradicts its
   own documented rule, and that our behaviour matches the branch that is right. Add it to the
   table at the top and to `CLAUDE.md` if it is a measurement hazard as well.
3. **Nil reach** — census the corpus and move it to the table above with the number.
