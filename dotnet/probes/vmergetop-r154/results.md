# Round 154 — O78 seat B: a covered cell's rules are charged to the row's height

**Written incrementally.** Each section was written as its measurement finished.

## 0. What this round is

Round 152 named two seats for **O78** and round 153 shipped the first. This is the second:
`TableLayouter.OwnTopRule` answers wrongly because `DocxLayoutSource.Tables.Resolved` drops every
`w:vMerge` **continuation** cell before its stated top border can reach `PageTableRow.Cells`.

The seat's own scope note was *"scope it to `OwnTopRule` only — fixture 7 holds the upper row's
bottom at a 1 pt `single` in every arm, so it says nothing about whether a continuation cell's
borders should also feed `TopBand` or `BottomBand`. Widening the change to those two is unmeasured."*
**This round measured them, and the answer is that all three are charged** — plus a fourth, the band
at the boundary *below* a continuation cell.

### Environment

| | |
|---|---|
| tree | `/home/user/libreoffice-core`, base `791c5d874` *(round 153, O78 seat A)* |
| reference | `/opt/libreoffice26.2/program/soffice` — **LibreOffice 26.2.4.2 `0229ac93fcf0d7cbc6376066c6f35021cef002dc`** |
| C++ read | this checkout, `configure.ac` **27.2.0.0.alpha0+** — *not* the reference binary's source |
| date | 2026-09-18 |

---

## 1. [bin] The seat reproduces at HEAD, and three more fixtures widen it

Four fixtures, each a set of one-attribute arms. Fixture 7 is round 152's, rebuilt and re-measured;
8, 9 and 10 are this round's and each isolates one place the charge could land by stating `nil`
everywhere else, so that **the covered cell's own statement is the only one at that boundary**.

| fixture | what it isolates | arms | differed before | after |
|---|---|--:|--:|--:|
| 7 `make-fixture7.py` (r152) | a `w:trHeight` floor — `OwnTopRule` | 18 | **2** (`VBN`, `VBA`) | **0** |
| 8 `make-band.py` | the band above the row — `TopBand`'s own term | 8 | **2** (`VB`, `VBH`) | **0** |
| 9 `make-bottom.py` | the band below it — `TopBand`'s *above* term | 4 | **1** (`WB`) | **0** |
| 10 `make-outer.py` | the band below the table's last row — `BottomBand` | 4 | **1** (`ZB`) | **0** |

**34 of 34 arms agree with 26.2.4.2 after the change**, against 28 before.

### And the top edge is charged and NOT drawn

Fixture 8's `VB` is the sharpest arm in the round. Every edge at that boundary states `nil` except
the covered cell's own 2 pt top. 26.2.4.2 pushes the lower row's baseline down by the whole 2 pt —
gap **11.20** against `VN`'s 9.20 — and **draws no rule there at all**. Its `PB` control, the same
table without the merge, is 11.20 *and* draws the rule.

Fixture 9's `WB` is the mirror and behaves the other way: the covered cell's stated **bottom** is
charged at the boundary below it **and is drawn**, because that edge is the merge's own outer one
rather than an interior one. Both renderers already drew it; only the charge was missing.

So this is the one place in the table engine where the height and the ink are deliberately
different sets of statements, and the fix touches only the height.

## 2. [src] And that is what `lcl_GetTopSpace` does

`lcl_GetTopSpace` (`sw/source/core/layout/tabfrm.cxx`:5175-5194) walks **every** lower of the row
frame and takes the maximum of each one's own `SvxBoxItem::CalcLineSpace(SvxBoxItemLine::TOP, true)`:

```cpp
for ( const SwCellFrame* pCurrLower = static_cast<const SwCellFrame*>(rRow.Lower()); pCurrLower;
      pCurrLower = static_cast<const SwCellFrame*>(pCurrLower->GetNext()) )
```

There is **no row-span test in it**, and that is the point: eight other places in the same file test
`getRowSpan() < 1` to skip exactly this cell (`:351`, `:510`, `:1437`, `:1747`, `:1935`, `:5510`,
and `findfrm.cxx`:1849, :1867), so a covered cell does have a frame and this function does see it.
`lcl_CalcMinRowHeight` adds it at `:5093`.

## 3. The change

`PageTableRow` gains `CoveredTopRule` and `CoveredBottomRule` — the widest rules stated by the cells
the row *drops*. `TableLayouter` seeds `TopBand`, `BottomBand` and `OwnTopRule` with them instead of
with zero, which is the whole of the layout change; the drawing is untouched, and the top edge is
therefore still drawn nowhere.

Three readers fill them, at the three places that already dropped the cell:
`DocxLayoutSource.Tables.Resolved`, `RtfDocumentReader`'s layout-row build and
`Ww8DocumentReader.LayoutTables`'s. The WW8 one resolves the covered cell's borders through the same
`ResolveBorders` as a kept cell, because the reference sees the box format after the importer's
defaults.

## 4. [bin] Reach beyond DOCX, measured by converting the fixtures with 26.2.4.2 itself

Each fixture converted to `.rtf`, `.odt` and `.doc` by the reference, then rendered by both sides.
Arms that differ, before and after:

| | fixture 7 | 8 | 9 | 10 | after |
|---|--:|--:|--:|--:|---|
| `.rtf` | 2 of 18 | — | — | — | **0 of 34** |
| `.doc` | 6 of 18 | — | — | — | **5 of 34** |
| `.odt` | 10 of 18 | — | — | — | **13 of 34 unchanged by this round** |

**RTF is the same defect and the same fix closes it.** WW8 closes `VBN` and `VBA`; its other four
arms are a *different* defect — the reference charges 1 pt on arms where the round-tripped file
states no top rule at all (`VNN`, `VNA`, `VAN`, `VAA`), which means the covered cell's borders in a
`.doc` are not what the `.docx` stated, and reading them is a WW8 question rather than a layout one.

**ODT has two defects of its own and this round implements neither.** Ten of fixture 7's eighteen
arms fail there, and five of the ten hold **no merge at all** — so the first is not this seat: a
converted `.odt` states `MinRowHeightInclBorder = true` in its `settings.xml` and
`OdtLayoutSource` never reads it, so `PageTable.MinHeightIncludesInsets` is false for every `.odt`
and no row height charges its insets or its own top rule. All 337 converted `.odt` state it. The
second is this seat's ODF twin: three arms of fixtures 8 and 9 fail on `.odt` for the same reason
they failed on `.docx`, and ODF spells the covered cell `table:covered-table-cell`. Both are in
`OPEN-ISSUES.md`.

## 5. [bin] O78's witness

`FAA 2025-26 Holdover Tables.docx`, this tree against the banked 26.2.4.2 rendering:

| | 26.2.4.2 | r152 (base) | r153 (seat A) | **r154 (both seats)** |
|---|--:|--:|--:|--:|
| last grid line on page 82 | 544.70 | 549.70 | 542.65 | **544.65** |
| displacement at the foot of page 82 | — | +5.00 | −2.05 | **−0.05** |
| Σ of the 33 row deltas | — | +5.05 | −2.00 | **0.00** |
| pages | 167 | 167 | 166 | **166** |
| page 83 | 1751 characters | blank | correct | correct |
| pages whose orientation differs | — | 10 | 2 | **2** |

**The 33 row deltas sum to exactly zero and the table's foot is 0.05 pt from the reference's**,
which is the two writers' own text-origin constant — the same 0.05 that separates every rule in this
round's fixtures. Round 152 simulated both seats together and predicted −0.05; the two shipped fixes
reproduce its simulation exactly.
