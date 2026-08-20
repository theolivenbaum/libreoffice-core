# words-r53 — prediction

Committed **before anything was changed**. Environment: LibreOffice **26.2.4.2 620(Build:2)**,
`fc-match "DejaVu Sans"` → `DejaVuSans.ttf`, `fc-match Calibri` → `Carlito-Regular.ttf`,
worktree `wt-words-r50` on branch `wt-words-r53`, base `41445736a8c`,
`SOURCE_DATE_EPOCH=1700000000`, `TZ=UTC`.

## Baseline, reproduced first

`batch-check.sh … 'words/*' … 8` → `TOTAL 355  MATCH 335  MISMATCH 20`. Scored against
`MANIFEST.tsv`'s 337-path list rather than that total: **318 match, 19 open, 0 disagreements with
the manifest's status column, document for document.** The briefed baseline reproduces exactly.

## The change

The `w:br`-only paragraph. **The brief's observable holds and its stated reach does not.**

### Mechanism, established rather than assumed

`TextItemiser.AddWithoutControls` cuts every `IsFormatControl` character out of the item list, and
U+2028 — what all four word-processing readers and both DrawingML readers emit for a manual line
break — is in that set. `MeasuredParagraph.Measure` builds its runs by intersecting each
`FormattedRun` with those items, so **a paragraph whose whole text is breaks reaches
`MeasuredParagraph` with `_runs.Length == 0`**. `MeasureLine`'s three fallbacks then all miss: the
fold finds no run, the blanks-refold finds no run, and the last one — the empty-paragraph case,
`if (height == Length.Zero && _runs.Length > 0) Accumulate(_runs[0])` — is guarded on there being a
run. Every line of the paragraph comes out **0 pt tall**.

Measured directly, by laying the r52 probe documents out through `DocxLayoutSource` +
`ParagraphLayouter` and reading the boxes:

| document | paragraph text | measured runs | per-run path | single-face path |
|---|---|---:|---|---|
| `a-br` | `U+2028` | **0** | 2 lines, **0 pt** | 2 lines, 23.10 pt |
| `i-spacebr` | `SPACE U+2028` | 1 | 2 lines, 23.00 pt | 2 lines, 23.10 pt |

That is the whole of the brief's "one space is enough": a space is not a format control, so it
leaves a run behind and the fallback fires. `TextMeasurer` is **not** the seat — it returns the
right two lines in both cases, exactly as r52 said. The comment on `IsFormatControl` names this
defect in its own words ("a paragraph that is nothing but control characters then has no run and
no line") and moved the C0 range out for precisely this reason without moving U+2028.

### The fix

`MeasuredParagraph.Measure`: when every formatting run has been itemised away, keep one
zero-length `MeasuredRun` from the first formatted run so the paragraph still has a face and a size
to be as tall as. A zero-length run is invisible to `Fold` (`touches` and `contains` are both
false), invisible to `RunsBetween` (`to <= from`), and adds nothing to the prefix table, so it can
only be seen by the `_runs[0]` fallback — which is the empty-paragraph rule, and an all-break
paragraph is exactly as tall as the same number of empty ones in the reference.

## Reach — the brief's figure is refuted and the corrected one is 20× smaller

The brief and `words-r52/results.md` say **469 such paragraphs in 66 of 271 documents**, naming
`FAA 2025-26 Holdover Tables` (66), `24-25_FAA_Holdover_Tables` (58), `OM template …` (37) and
`EHEST-SMS` (35), and call that "the risk, and why this needs a whole-track sweep". **No census
committed with r52 produces those numbers and none I can construct reproduces them.** The script
beside this file counts, over `MANIFEST.tsv`'s own path list:

| reading | paragraphs | documents |
|---|---:|---:|
| `w:p` holding any non-page `w:br` | 3936 | 76 |
| …and no non-empty `w:t` | 23 | 14 |
| …and no tab, symbol, field, note or **as-character** drawing either — the defect | **22** | **13** |

The named witnesses do not survive: `FAA 2025-26 Holdover Tables` holds **1**, not 66;
`24-25_FAA_Holdover_Tables` **1**, not 58; `OM template` **1**, not 37 (it holds 37 `w:br`
*elements*, which is the likeliest source of the figure); and `EHEST-SMS` holds **0**, not 35 —
its 10 breaks all sit in paragraphs that also hold text. Every one of the 22 is a **single**
break, so each is worth two lines rather than more.

`097` holds **3**, which is the one figure of r52's that does reproduce.

Per document, with the manifest's status:

```
7  done  words/chartset-012/docx/096_Business_Case_Template_Editable_Layout_41fe0bc0.docx
3  open  words/chartset-006/docx/097_Business_Case_Template_Elegant_Layout_3ba9cbf2.docx
2  open  words/ceiling-001/docx/ABCD-FE-01-00 Flight Envelope - v1 08.03.16.docx
1  done  words/done-003/docx/Technical_Issue_Report_Form.docx
1  done  words/done-015/docx/Agile_Arc_SysDes.docx
1  done  words/done-015/docx/system_design__technical_architecture_template.docx
1  done  words/extra-001/docx/ABCD-SDE-23-00 - Avionic System Description - 17.02.16 - v1.docx
1  done  words/extra-001/docx/ABCD-WB-08-00 Weight and Balance Report - v1 08.03.16.docx
1  open  words/metrics-001/docx/OM template for non-complex NCC operators_August 2016.docx
1  done  words/pagination-001/docx/24-25_FAA_Holdover_Tables.docx
1  done  words/pagination-001/docx/ESPN-R - MCF - RA - Ed1.docx
1  done  words/pagination-001/docx/FAA 2025-26 Holdover Tables.docx
1  done  words/pagination-001/docx/template---tpr-technical-progress-report-with-guidance.docx
```

**The whole-track sweep is still run**, because the change is in `Paperless.Text` and a census over
parts is exactly the instrument HANDOVER §7 says under-reaches.

## What the census cannot see, written down before the sweep

* **`.doc`, `.rtf` and `.odt`.** Their readers emit the same U+2028 (`Ww8DocumentReader.Layout.cs`
  :1187, `RtfDocumentReader.State.cs`:945, `OdtLayoutSource.cs`:51) and the census reads none of
  the three. The words figure is a **floor**, and 47 of the 337 words documents are binary or RTF.
* **Paragraphs of some *other* control character** — a lone U+200E, U+2060, U+FEFF. Same defect,
  not counted, and not measurable from a `w:br` census at all.
* **Whether the paragraph takes the per-run path.** The single-face path measures these correctly
  today, so a reader that emits no runs is unaffected. DOCX always emits runs; the other three
  readers were not checked.
* **Inheritance and defaults of every kind.** This is a shape census over parts.
* **A page-break cascade.** Each fix adds ~23 pt to one paragraph; on a document with section
  breaks that can move a page boundary a long way from the paragraph, which is the mechanism
  `CLAUDE.md` records for `AWR OPS-AOC 044`. Nothing in the census sees it.

## Verdict movement predicted

**318 → 319**, one gain, no regressions.

| document | now | expected | why |
|---|---|---|---|
| `097_Business_Case_Template_Elegant_Layout` | `pages` 1/2 | **`match`** | r52 localised it as 20 pt short of a second page; two of its three break-only paragraphs are on page 1 and each is worth ~23 pt |
| everything else | unchanged | unchanged | 20 of the 22 paragraphs are one per document |

Named risks, each of which would be a **regression** and is stated so it cannot be netted away:

* `096_Business_Case_Template_Editable_Layout` — 7 occurrences, currently `match` at 1 page.
  ~161 pt of new height is enough to open a second page. **This is the most likely regression.**
* `ABCD-FE-01-00 Flight Envelope` — currently `pages,words` 14/15; two occurrences, ~46 pt. Could
  close, could stay, could go 16/15.
* `OM template for non-complex NCC operators` — currently `pages` 166/165, one occurrence. One
  more line in a 166-page document should not move a page count, but it is the document r52's
  own line-height work already moved and it is one line from a boundary.
* `FAA 2025-26 Holdover Tables` and `24-25_FAA_Holdover_Tables` — one occurrence each, both
  `match` on large page counts. The census says they are *not* the heavy witnesses the brief
  named; if either moves a page, the brief's reach claim was right by some route this census
  cannot see, and that is the tell to look for.

## Cross-track reach — measured, not argued

`Paperless.Text/Layout/MeasuredParagraph.cs` is a **shared layer** and is reached by
`SlideTextLayout` and `SheetTextLayout` as well as `PageContent`. The same census over the other
two families:

* **slides: 17 paragraphs in 7 decks**, 6 of them passing —
  `ghgp-supply-chain-initiative_20100323_wri.pptx` (4), `10. Drawings, circuit diagrams and
  schematics.pptx` (4), `8_P-Pavese_AIRBUS-ATB-journee-CRATB.pptx` (3, open),
  `Intersil_Italy_CAN_Bus_Transceiver_Presentation_Final.pptx` (3),
  `John_Broggio__RSS_Campion_event_John_Broggio.pptx`, `1_Country-Updates_DRC_English.pptx`,
  `B2B-Center-Readiness-and-Student-Retention.pptx` (1 each).
* **sheets: 0.** No `a:p` in any `xl/drawings` part is break-only.

A slide's text box is not paginated, so the expected slides movement is **ink, not verdicts**: a
break-only paragraph inside a body currently collapses to nothing and will start taking a line,
which moves everything below it in that box. `.ppt` and `.odp` are not read by the census.
**Predicted slides verdict movement: 0.** The five batches holding those seven decks are swept
both ways from here; the parent owns the full cross-track sweep.

## Tests

Every new test to be run through `verify-test.sh` by reintroduction — the mutation being the
restoration of the `_runs.Length > 0` guard with no run kept.
