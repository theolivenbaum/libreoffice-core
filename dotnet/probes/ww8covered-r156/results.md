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
