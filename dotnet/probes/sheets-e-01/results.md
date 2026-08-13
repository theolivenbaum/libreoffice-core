# sheets-e-01 — `###`, the accounting `$`, and the grid

Three text-visible divergences the gate round exposed. One is implemented and swept, one is
diagnosed to a seat and deliberately not implemented, one has its rule measured in full and its
implementation handed over. Taken in the briefed order.

Reference: `/c/sandbox/workdir/refpdfs-26.2.4.2-fonts/sheets/` (171 PDFs, per-format identity
`stem__ext.pdf`). `check-env.sh` before anything else:

> **LibreOffice 26.2.4.2 620(Build:2)** · Calibri→Carlito, Cambria→Caladea, Arial→Liberation
> Sans, Times New Roman→Liberation Serif, Courier New→Liberation Mono, DejaVu Sans→DejaVu Sans ·
> pdftoppm 26.01.0 · pdftotext 26.01.0 · **"Environment is good."** · `df -h /`: 8.8 GB free.

Ours: worktree `/c/sandbox/workdir/wt-sheets-e`, branch `wt-sheets-e`, based on `735e08c525f`;
`PAPERLESS_CLI` set explicitly on every render; `SOURCE_DATE_EPOCH=1700000000`, `TZ=UTC`.

**Measured** means read out of a PDF or produced by an authored probe against 26.2.4.2.
**Inferred** means read out of the C++, which is **27.2.0.0.alpha0+ and not the reference
binary**; nothing is claimed on it alone.

---

## 0. The prediction, and the first thing it got right

`prediction.md` beside this file, committed as **`4933bb8f462`** before a single PDF was opened,
before a probe was rendered and before a line of source was changed. Scored in §7.

Its first item is the one that matters most:

> **P0.1 — the brief's direction on `###` is inverted.** The brief says *"we draw 1101 where the
> reference draws 2."* `gate-01` says the opposite in three places. Measurement will show
> reference ≈1101, ours ≈2.

**Measured, on `ODs-February-2022-Airbus-Commercial-Aircraft.xlsx`:**

| | `###` tokens |
|---|---:|
| reference (26.2.4.2) | **1101** |
| ours, before this round | **2** |
| ours, after | **1101** |

The brief is inverted. **We were under-producing `###`, not over-producing it**, and every
downstream direction in this round is therefore the reverse of what the brief implies. Read the
brief's sentence again — *"We are producing it at scale where LibreOffice is not"* — and then the
`gate-01` line it was built from: *"the reference emits `###` 1101 times … and we emit it twice."*
This is the round's required refutation of its own brief, and it was committed as a prediction
rather than discovered and back-filled.

---

## 1. `###` — the rule, measured

### 1.1 The census that named the seat before any rule was established

`ODs-February-2022-Airbus-Commercial-Aircraft.xlsx`, walked as records rather than regexed:
every `<c r="A…">` carrying a numeric `<v>` on all thirteen sheets, joined to `xl/styles.xml`'s
`cellXfs`.

| | count |
|---|---:|
| numeric column-A cells, all thirteen sheets | **1101** |
| of which `numFmtId="0"` — **`General`** | **1099** |
| of which `numFmtId="1"` — a stated `0` | **2** |
| reference `###` tokens in the PDF | **1101** |
| ours before | **2** |
| ours, `1E+00`/`1E+04`/`1E+11`-shaped tokens before | **1099** |

Column A is `width="0.42578125"` — narrower than one digit. **The two counts that agree are the
two non-`General` cells**, and they agree because our `Hash` already implemented the
non-`General` branch. The 1099 we got wrong are exactly the `General` ones, and we drew `1E+00`
for each.

### 1.2 The authored probe

`tests/corpus/features/sheet-hash.fods` (`probes/sheets-e-01/mkhash.py`), rendered by 26.2.4.2
itself. Fourteen rows, **one variable each**, across **twenty column widths** from 0.10 cm to
4.00 cm — so every variant is swept across its boundary rather than sampled on one side of it,
which is more than the brief's "at least two points" asked for and is what made the boundary
comparison below possible. Read back cell by cell by `probes/sheets-e-01/cells.py`, which
assigns a drawn word to a column by its **right** edge: a `###` that does not fit overhangs to
the *left* of its own cell, so a census keyed on the centre or the left edge mis-files exactly
the cells this round is about.

| variant | 26.2.4.2 draws | boundary |
|---|---|---|
| `General`, `1` | `###` ×4, then `1` ×16 | 0.25 → 0.30 cm |
| `General`, `12345` | `###` ×12, then `12345` ×8 | 1.00 → 1.20 cm |
| `General`, `123456789012` | `###` ×12, then `1E+11`, `1E+11`, `1.23E+11`, `1.235E+11`, `1.2346E+11`, `123456789012` ×3 | 1.00 → 1.20 cm |
| `General`, `1.5` | `###` ×4, then **`2` ×3**, then `1.5` ×13 | decimals dropped first |
| `General`, `-1` | `###` ×5, then `-1` ×15 | the sign costs one column |
| `0.00`, `1` | `###` ×9, then `1.00` ×11 | **never shortened** |
| `0`, `1` | `###` ×4, then `1` ×16 | control on the non-`General` branch |
| percent, `0.5` | `###` ×13, then `50.00%` ×7 | |
| date | `###` ×16, then `28/02/2022` ×4 | a date hashes; it is not text |
| string `XX` | never `###` at any width; shortened to `X` at one | |
| shrink-to-fit, `12345` | `12345` at **all twenty widths** | shrink suppresses `###` outright |
| wrap, `General` `12345` | identical to the unwrapped row | wrap does not save a plain number |
| wrap, date | `###` on **one line**, row keeps its single-line height | `###` is never broken |
| left-aligned `General` | same boundary as right-aligned | alignment does not move it |

Second sheet, the value-versus-string asymmetry at one width (0.30 cm) with a controlled
neighbour: `###` · `ABCDEFGH` · `A`+`Z` · `###`. **A value never borrows an empty neighbour's
width and a string always does**, so the same column hashes a number and shows a string whole.

**The rule, stated.** A cell draws `###` iff it holds a **value** (or a formula returning one),
its output area is **clipped** — decided against its own column width minus margins, never
widened into a neighbour — and then:

1. a **formula in error**, or "show formulas", hashes; else
2. a format other than **`General`** hashes **outright**, with no attempt to shorten; else
3. the value is re-rendered through `General` with as many characters as the column has
   max-digit widths, dropping decimals and falling back to scientific notation — **and if that
   re-rendered text is still wider than the column, the cell hashes after all.**

**Shrink-to-fit removes the clip before the gate is reached, so it hashes nothing at any width.**

*Inferred, and matching*: `ScDrawStringsVars::SetTextToWidthOrHash`
(`sc/source/ui/view/output2.cxx:610-716`); the gate at `:1974`; `bCellIsValue` suppressing the
spill loop at `:1330`. Step 3 is `:704-710`, three lines long, comment "Even after the decimal
adjustment the text doesn't fit. Give up."

### 1.3 What we had, and what was missing

`SheetTextLayout.Hash` implemented steps 1 and 2 and **stopped**. It rendered the shortened
`General` form and returned it unconditionally. There was no measurement of the result and no
fallback, so a column narrower than one digit produced `Render(value, 0)` → `1E+00`, drew it, and
that was that. The docstring on `SheetGeneralWidth` even states the *reason* the rule is not
"hash anything that does not fit" — and the missing half is the reason it is not "never hash a
`General` cell" either.

## 2. The implementation

`dotnet/src/Paperless.Spreadsheets/Layout/SheetTextLayout.cs`, one file, one project.

* **`Hash` (`:892-914`)** now shapes the shortened `General` text and compares its width against
  the available width, falling back to `###` when it still does not fit. It returns a third
  value, `Hashed`, because two things downstream need to know.
* **The wrap suppression (`:474-483`)**: `lines = breaks && !hashed ? Wrap(…) : [run]`. Measured
  on the fixture's `wrapdate` row — 26.2.4.2 draws `###` on one line in a wrapping cell and keeps
  the row's single-line height; we drew **three lines of one `#`**, which moves every row under it.
* Re-shape and re-measure rather than counting characters: the budget is in digit widths and the
  answer is a shaped run, so only the run can decide. Counting characters is what produced the
  original defect.

Nothing else in `src/` changed. The whole diff is inside `Paperless.Spreadsheets`, which
`Paperless.WordProcessing` and `Paperless.Presentations` are siblings of and cannot reach — so
P4.4's static cross-track argument holds and no 534-document sweep is owed (the `sheets-d-01`
distinction: a shared-layer move owes a measurement, a leaf change owes a proof that it is a leaf).

### 2.1 The probe, re-read after the fix

Every one of the fourteen sweep rows now reproduces 26.2.4.2 cell for cell, with **one residual**,
which is pre-existing and untouched:

| width | reference | ours |
|---|---|---|
| 1.60 cm | `1.23E+11` | `1.2E+11` |
| 1.80 cm | `1.235E+11` | `1.23E+11` |
| 2.00 cm | `1.2346E+11` | `1.235E+11` |

Our scientific mantissa carries **one digit fewer** than the reference's at three widths. That is
`SheetGeneralWidth.Scientific`'s budget arithmetic, not the hash rule, and it is *safe* in the one
direction that matters here: our form is **shorter**, so it fits wherever the reference's does and
cannot manufacture a spurious `###`. Recorded rather than fixed — changing the budget is a
separate change with its own sweep.

## 3. Reach, direction and verdict movement

Both banks rendered with `SOURCE_DATE_EPOCH=1700000000` and `TZ=UTC`, all 171, **re-counted from
disk** (171 and 171) after the sweeping process exited, not when the file count reached its
target. Both binaries **validated before use**: the pre-fix build draws `1E+00` on the fixture's
first row and the post-fix build draws `###`; the post-fix bank's `ODs-February` reproduces
**byte for byte** when re-rendered individually.

REACH_TABLE_PLACEHOLDER

## 4. The accounting `$`/`-` — refuted as a rendering defect, and then seated anyway

`fy2011-aip-grants.xls`. The brief: *"a workbook writes 11 538 `$`/`-` tokens against the
reference's 9020 … we are emitting more of them than LibreOffice does."*

### 4.1 We are not emitting more of them. We are emitting exactly as many.

Read with **`paperless analyze`**, one binary over both PDFs in one pass:

| | `$` characters | `-` characters | raw tokens | letter-or-digit | non-alphanumeric |
|---|---:|---:|---:|---:|---:|
| reference | **8242** | **5516** | **56 732** | **43 208** | **13 524** |
| ours | **8242** | **5516** | **56 732** | **43 208** | **13 524** |

**Identical in every column.** Not "within a few percent", which is what P2.1 predicted —
identical to the digit.

The briefed 11 538 / 9020 reproduces exactly, and only, under **poppler**:

| | `$` chars | raw | letter-or-digit | non-alphanumeric | top tokens |
|---|---:|---:|---:|---:|---|
| reference, `pdftotext` | 8242 | 52 221 | 43 201 | **9020** | `$`×6241, `-`×1881, `$-`×379, `$$-`×132, `$$$-`×75 |
| ours, `pdftotext` | 8242 | 54 739 | 43 201 | **11 538** | `$`×8236, `-`×3142 |

**Both PDFs hold the same 8242 `$` glyphs.** The 2518-token gap is entirely poppler joining `$`
to the `-` beside it on one side and not the other. It is not even stable inside poppler:
`pdftotext -bbox` produces **no** joined `$-` token on either side, so the same binary in its
other mode disagrees with itself about this document.

**So the brief's framing of subject 2 is refuted.** Nothing is drawn more often on our side.

### 4.2 What *is* different, measured, and it is 0.6 em

The joining is a spacing difference, so the spacing was measured. Over eight pages, every `$` and
`-` matched positionally between the two renders:

| glyph | our x − reference x | on |
|---|---:|---:|
| `-` | **+6.27 to +6.29 pt** | 350 of 474 |
| `$` | **−0.52 to −0.53 pt** | 350 of 804 |
| both | within 0.21 pt | the remainder |

On one cell: the reference puts `$` at 613.38 and `-` at 620.71, a **2.26 pt** gap that poppler
joins; we put them at 612.86 and 626.98, a **9.05 pt** gap that it does not.

### 4.3 The seat

The workbook's own `FORMAT` records, read by walking BIFF `0x041E` records:

```
41  _(* #,##0_);_(* \(#,##0\);_(* "-"_);_(@_)
44  _("$"* #,##0.00_);_("$"* \(#,##0.00\);_("$"* "-"??_);_(@_)
165 _("$"* #,##0_);_("$"* \(#,##0\);_("$"* "-"??_);_(@_)
```

The zero section is `_("$"* "-"??_)`. Two blanks decide the `-`'s position, and **we render both
as a plain space**:

* `?` → `output.Append(' ')` — `Paperless.Core/Numbers/NumberFormatter.cs:559` and `:580`.
  LibreOffice reserves a **digit-width** blank.
* `_x` → `FormatToken.Literal(" ")` — `Paperless.Core/Numbers/NumberFormatSection.cs:155-157`,
  whose own comment says *"There is no column to align to during extraction, so a single space
  stands in"*. LibreOffice reserves the width of `x`.

Arithmetic, in ems of Liberation Sans (digit 0.556, space 0.278, `(`/`)` 0.333):

| | LibreOffice | ours | difference | measured at a 10 pt face |
|---|---:|---:|---:|---:|
| leading `_(` | 0.333 | 0.278 | **0.055 em** | **0.052–0.053 em** (the `$`, −0.52 pt) |
| trailing `??` + `_)` | 1.445 | 0.834 | **0.611 em** | **0.627–0.629 em** (the `-`, +6.28 pt) |

The leading blank matches the prediction to 0.003 em and the trailing to 0.017 em, and the
**signs and the ratio are right on both**, which is what makes the attribution safe. The exact
blank widths LibreOffice uses are not pinned to the last digit and that is stated rather than
rounded away.

### 4.4 Not implemented, deliberately

Three reasons, in order:

1. **It moves no verdict and cannot.** The corrected gate already scores this document
   `match` — 43 201 letter-or-digit words on both sides. The gap is invisible to the metric by
   construction, because a `$` carries no letter and no digit.
2. **The fix is in `Paperless.Core`**, not in `Paperless.Spreadsheets`. `Core/Numbers` is reached
   by word processing, presentations and every chart axis, so it owes the 534-document sweep this
   round did not budget for — the `sheets-c-01` rule, applied to myself.
3. The change needs a *width* where the formatter currently has only a *string*: `_x` and `?` are
   blanks whose width depends on the face, and `FormatToken` carries neither. The comment at
   `NumberFormatSection.cs:155` already says why the space is there, and it is right for
   extraction; a rendering path needs a second channel rather than a different literal.

Handed over as a `Paperless.Core` item with the measurement attached, which is worth more than a
rushed change to a shared layer.

## 5. The grid

`6f9e605c-fded-11e3-bd0e-00144feab7de.xls` page 1: the reference draws **107** vertical 0.1 pt
rules where we draw **17**.

### 5.1 We do draw the grid, and the 107 is not 107 rules

The reference's 107 verticals sit at **eighteen** distinct *x* positions:

| x | segments |
|---|---:|
| 20.778 | 1 — the block's frame, not the grid |
| **170.561** | **30** |
| **187.143** | **31** |
| **215.631** | **31** |
| the other fourteen | 1 each |

So the reference draws seventeen grid rules and one frame edge; **we draw seventeen grid rules**
at the same positions. **P3.1 holds: we draw the grid on that page, and the rule *set* already
agrees.** The 107-against-17 is three rules split into one segment per row, with a ~0.113 pt gap
at each row boundary — and **no holes**: the split rules run continuously from 77.583 to 525.287.
**P3.4 is refuted** — I predicted the reference's grid would have missing rules where text spills,
and on this page it has none.

### 5.2 The rule, measured on an authored fixture

`tests/corpus/features/sheet-grid.fods` (`probes/sheets-e-01/mkgrid.py`) — four sheets, one
variable each, grid printing on. Rendered by 26.2.4.2, censused with `sheets-d-01/strokes.py`:

| sheet | what varies | reference draws | ours draws |
|---|---|---|---|
| `control` | nothing, five 2 cm columns | 5 whole-height rules | **5, identical** |
| `hidden-C` | column C hidden | column B's rule **split into 6 per-row segments**, no hole; C's rule absent | 4 whole-height rules, **not split** |
| `merge-CD` | C:D merged on one row | column C's rule split per row **with a hole at the merged row** | 2 runs with the hole, **not split** |
| `overflow-B` | a long string in B spilling across C, one row | column B's rule split per row **with a hole at that row** | **one whole-height rule, no hole** |

**The rule, stated.** A column's vertical grid rule is emitted **one segment per row** whenever
*any* row of the page would suppress it — because the next column is hidden, because a merge
covers the cell to its right, or because a string in that column overflowed across the boundary.
Within that per-row emission the segment is **omitted** on exactly the rows where the merge or
the overflow holds; a hidden neighbour splits without omitting anything. Otherwise the rule is one
stroke down the whole block.

*Inferred, and matching*: `ScOutputData::DrawGrid`'s `bSingle`
(`sc/source/ui/view/output.cxx:456-513`); `bHideGrid` set by `GetOutputArea` at
`output2.cxx:1338` and `:1345` on every column a string extended across.

### 5.3 Our gap, split into what is ink and what is not

* **Segmentation** — 107 against 17 — is not ink. Abutting hairlines with a 0.113 pt gap in a
  0.1 pt rule; the *set* of rules already agrees, position for position.
* **The hole on overflow is ink, and we do not draw it.** `overflow-B` above: we run a grid rule
  straight through a cell a label has spilled across; LibreOffice leaves it out. This is the
  actionable half.
* **The hole on a merge we already draw**, from `SheetMerges.IsOverlappedLeft` — `merge-CD` shows
  our hole in the right place. So the machinery for suppressing a rule exists; what is missing is
  the overflow record feeding it.

### 5.4 Reach, so the handover is sized rather than guessed

Page 1 of all 171, both banks (`probes/sheets-e-01/gridreach.py`):

| | documents |
|---|---:|
| printing a grid at all on page 1 (≥3 hairline verticals) | **13 of 171** |
| whose reference splits at least one rule | **10 of 13** |
| where the reference's rule *count* already equals ours | **11 of 13** |

**This is a 13-document item, not a track-wide one**, and that is the most useful thing this
section produces: it is worth doing after anything with a wider reach.

### 5.5 Not implemented, and why — plus a bigger defect found on the way

Implementing the overflow hole needs a per-(row, boundary) record produced where the text is laid
out (`SheetTextLayout`, which is the only place that knows a string spilled) and consumed by
`SheetPageDecoration.DrawGrid`. The paint order already suits it — `SpreadsheetPages.Draw` draws
cells and *then* the grid — but `SheetTextLayout.Draw` returns `void`, so the change is a new
channel through three files plus its own 171-document sweep. With subject 1 shipped and swept, the
brief's "a precise diagnosis of one beats a vague gesture at three" says to stop here.

**And the census found something larger than the item it was sizing.** `apron-area.xls` page 1:

| class | reference | ours |
|---|---:|---:|
| V 0.1 pt (the grid) | **70** | **0** |
| H 0.1 pt (the grid) | **56** | **0** |
| H 0.51 / 0.737 / 1.5023 pt | 10 / 34 / 9 | **0 / 0 / 0** |
| H 1.75 pt | 45 | 45 |

We draw **no grid at all** on that page and are missing three border width classes, while drawing
the 1.75 pt class exactly. The document nonetheless **matches the gate** — 3 pages and 417 words
on both sides — which is the cleanest demonstration in this report of what check 2 cannot see. Not
diagnosed; recorded as the largest single lead this round produced.

SCORING_PLACEHOLDER

TESTS_PLACEHOLDER

BUILD_PLACEHOLDER

## 10. What this round could not see

* **The gate cannot see subjects 2 or 3 at all.** `$` carries no letter or digit, and no glyph is
  emitted by `DrawGrid`. `apron-area` matching the gate with its whole grid missing is the
  demonstration.
* **`###` is the exception, and it is a *negative* signal.** Hashing a cell removes a real word
  and adds an invisible token, so the metric moves — but only on a document that was already
  wrong, and only in the direction of fewer words.
* **poppler's word-joining threshold is not observable from a PDF.** §4 measures the gap the two
  sides emit (2.26 pt against 9.05 pt); the threshold between them is inferred, and the fact that
  `pdftotext -bbox` joins neither says the threshold is mode-dependent as well.
* **`bHideGrid` has no PDF trace.** §5's attribution of a missing rule to overflow rather than to
  a merge rests entirely on the authored fixture separating them; on a corpus page the two are
  indistinguishable in the output.
* **The exact blank widths LibreOffice reserves for `?` and `_x`** are inferred from em ratios,
  not read out of the binary (§4.3).
* **Row heights are not asserted anywhere.** The fixture exposed that our auto row height wraps a
  plain-number cell where Calc does not — the `wrap-general-12345` row is ~93 pt tall on our side
  and 14.6 pt on the reference's — which is **pre-existing, unchanged by this round in either
  direction**, and outside the hash rule. Named in §11.
* **The 27.2 tree is not 26.2.4.2.** Every `output.cxx` / `output2.cxx` citation is a statement
  about a binary that was not measured.

## 11. Leads this produced

1. **The wrapping plain-number row height.** Our row-height pass wraps a `General` number in a
   wrapping cell; Calc disables automatic line breaks for a plain number format entirely
   (i#111387). On the authored fixture the row comes out ~6× too tall. `SheetTextLayout.Breaks`
   already encodes the rule for *drawing*; the height pass does not consult it.
2. **`apron-area.xls` page 1** — no grid and three missing border classes, on a document that
   matches the gate (§5.5). The largest single lead here.
3. **The grid's per-row segmentation and the overflow hole** (§5), sized at 13 documents.
4. **`?` and `_x` as blanks of a stated width** (§4), a `Paperless.Core/Numbers` item owing a
   534-document sweep.
5. **The scientific mantissa is one digit short at three widths** (§2.1) — a
   `SheetGeneralWidth.Scientific` budget question, safe in the direction that matters and
   unresolved.
