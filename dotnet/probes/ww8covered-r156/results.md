# Round 156 — O105: a `.doc`'s covered cell carries the merge master's borders

## 0. What this round is

Round 154 gave all four readers a carrier for the rules a cell a vertical merge covers states, and
closed the seat in DOCX, RTF and ODF. WW8 closed only two of its six failing arms, and the round
recorded the rest as **O105**: *"a `.doc`'s covered cell carries borders that the file does not state,
so reading them is not enough"* — on four arms 26.2.4.2 charged a 1 pt top rule that no cell in the
file states.

### Environment

| | |
|---|---|
| tree | `/home/user/libreoffice-core`, base `b322e47b3` *(round 155)* |
| reference | `/opt/libreoffice26.2/program/soffice` — **LibreOffice 26.2.4.2 `0229ac93fcf0d7cbc6376066c6f35021cef002dc`** |
| date | 2026-09-19 |

---

## 1. [bin] The instrument is the reference's own view of the file, and it answers in words

`--convert-to fodt` on the `.doc` prints every cell's resolved borders and every cell's style name.
Over round 154's fixture 7, converted to `.doc` by 26.2.4.2 itself
(`data/doc-covered-borders.txt`):

| the lower-left cell | its style | its border |
|---|---|---|
| an ordinary cell (the nine `M` arms) | `TableN.A2` — **its own** | what the file states: `none`, or 1 pt |
| a covered cell (the nine `V` arms) | `TableN.A1` — **the master's** | `1pt solid` on all four sides, in every arm |

A covered cell comes out carrying **the merge master's own cell style**. That is consistent with two
rules — *the master's borders* and *the thicker of the two* — and one attribute separates them.

## 2. [bin] Two arms, and the master wins in both directions

`make-master.py` builds four two-row tables, each on a `w:trHeight` floor of 450 twips (22.50 pt),
one per page:

| arm | the master states | the covered cell states | 26.2.4.2's row height | what it means |
|---|---|---|--:|---|
| **A** | 3 pt | `nil` | **25.50** = 22.5 + 3 | the master's, where the cell states nothing |
| **B** | 1 pt | 3 pt | **23.50** = 22.5 + 1 | the master's, where the cell states **more** |
| C | 3 pt | `nil`, **no merge** | 22.50 | the control: its own |
| D | 1 pt | 3 pt, **no merge** | 25.50 | the control: its own |

**Arm B is the whole of it**: the thicker-of-the-two reading predicts 25.50 there and the reference
draws 23.50. Before this round we drew **21.50** on A and **25.50** on B — the covered cell's own
statement, exactly backwards, which is round 154's WW8 half doing the wrong thing with the right
carrier.

## 3. The change

`ResolveVerticalMerges` already walks upwards to find the merge's owner so it can count the span, so
this is one line beside that: `cell.Borders = owner.Borders`. Nothing draws a covered cell, so it
reaches exactly one number — the row's height, through `PageTableRow.CoveredTopRule`.

## 4. [bin] All four fixtures, all four formats

Round 154's four fixtures converted to `.doc` by the reference:

| | before r154 | after r154 | **after r156** |
|---|--:|--:|--:|
| arms differing on `.doc` | 6 of 34 | 5 of 34 | **0 of 34** |

With round 155's ODF half and this, **all four word-processing formats agree with 26.2.4.2 on all 34
arms**: `.docx` 34, `.rtf` 34, `.odt` 34, `.doc` 34.

## 5. The tests and the pin

`words-vmerge-master-borders.doc` is §2's fixture — 13 kB, four arms, one per page — and its
expectations are 26.2.4.2's own rendering of it. `Ww8CoveredCellBordersTests` is four tests, two of
them the controls that stop the rule being read as *"a lower cell's top rule is ignored"*.
Mutation-pinned (`mutate.sh`) against three rival rules:

| arm | result |
|---|---|
| the covered cell keeps its own borders (round 154's state) | **2 of 4 failed** |
| the thicker of the two | **1 of 4 failed** — arm B alone |
| no borders at all (before round 154) | **2 of 4 failed** |
| base (the master, always) | 4 passed |

## 6. [bin] Confinement and reach: 4 of 1620, all `.doc`

| family | moved | of |
|---|--:|--:|
| words `.doc` | **4** | 66 |
| everything else | **0** | 1554 |

`confinement.txt`. Per span, over the four movers (`score2.py`): **529 closer to 26.2.4.2 and 84
further**, 3 documents net better and 1 worse.

| document | spans moved | closer | further |
|---|--:|--:|--:|
| `150_5300_13_chg10.doc` | 329 | **329** | 0 |
| `150_5300_13_chg12.doc` | 143 | **143** | 0 |
| `foca_form_1.doc` | 2 | **2** | 0 |
| `P200904290238_0238_51880.doc` | 140 | 55 | **84** |

## 7. [bin] The one document that worsens is a deeper defect, and it is not this one

`P200904290238_0238_51880.doc` goes from a mean baseline distance of 7.84 pt to 17.20, and the shift
is on **pages 2 and 3 only** — page 1 *improves*, from −8.12 to −5.93 — so a table early in the
document grew by about 18 pt. It holds **merges of depth 2, 3, 4 and 7** (`paperless extract
--format xhtml`, counting `rowspan`), and depth is the attribute this round's arms do not vary.

`make-deep.py` varies it: a four-row merge whose master states a 3 pt top and a 3 pt bottom, with a
no-merge control. 26.2.4.2 gives the merged arm **25.5, 25.5, 25.5** — every covered row charged the
master's 3 pt — and this tree gives **25.5, 19.5, 22.5**, *before this round's change as well as
after it*, so the gap is neither caused nor closed here.

**And it is not `CoveredTopRule` failing at depth.** `make-deep2.py` is the same fixture with the
master's bottom `nil`, so nothing but the covered cell can charge a row: both renderers give
**25.5, 25.5, 25.5** for the merge and **22.5, 22.5, 22.5** for the control — four rows of a
four-row merge, exact. What deep.doc adds is a covered cell that carries a **bottom** rule as well,
and there the two disagree from the second covered row on. Seated as **O107**; the witness is
`deep.doc` and the corpus one is this document.

## 8. The suite

| project | result |
|---|---|
| `Paperless.WordProcessing` | **2088** passed — 2084 at the round's base plus this round's 4 |
| `Paperless.Core` / `Text` / `Vector` / `Containers` | 591 / 750 / 309 / 109 passed |
| `Paperless.Spreadsheets` / `Presentations` / `Markup` | 1484 / 1205 / 259 passed |
| `Paperless.Fidelity` | **10 failed of 552**, 0 skipped — the base figure exactly, and the known set |

Build: 0 warnings, 0 errors.
