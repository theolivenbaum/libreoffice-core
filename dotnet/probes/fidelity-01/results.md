# fidelity-01 — the 40 Fidelity failures, enumerated and classified

Round: Fidelity. Worktree `/c/sandbox/workdir/wt-fidelity`, branch `wt-fidelity`.
The prediction is in `prediction.md`, committed at `2420db95528` **before** the first measurement.

Throughout: **measured** means I ran it and read the number; **inferred** means I reasoned to it
and could not close the loop. They are labelled separately everywhere.

---

## 0. Environment, quoted

`.claude/skills/libreoffice-reference/scripts/check-env.sh`, run first (measured):

```
== 1. soffice binary ==
  OK    LibreOffice 26.2.4.2 620(Build:2)
== 3. metric-compatible fonts ==
  OK    Calibri -> Carlito          OK    Cambria -> Caladea
  OK    Arial -> Liberation Sans    OK    Times New Roman -> Liberation Serif
  OK    Courier New -> Liberation Mono
  OK    DejaVu Sans -> DejaVu Sans
== 4. PDF rasteriser ==   OK  pdftoppm 26.01.0
== 5. PDF extractor ==    OK  pdftotext 26.01.0
Environment is good.
```

`df -h /` → **13 GB free, 34 % used** (measured). The full-disk signature the brief warns about —
every test in a class dying at <1 ms — **does not apply here**: the 40 failures take 1–11 s each
and carry real assertion values. This was checked before any mass-failure pattern was believed.

---

## 1. The reconciliation of 550

| | total | passed | failed | skipped |
|---|---:|---:|---:|---:|
| Handover, at `0fb6d41e0`, on LibreOffice **24.2.7.2** | 550 | 550 | 0 | 0 |
| Today, at `HEAD`, on LibreOffice **26.2.4.2** | 550 | 510 | 40 | 0 |

**The total did not change; only the pass/fail split did.** Measured two ways:

- `dotnet test --list-tests` discovers **550** tests today.
- The run reports `Failed: 40, Passed: 510, Skipped: 0, Total: 550`.

550 = 510 + 40 exactly, and 0 skipped means no part of the suite silently covered nothing.
**Prediction P1 confirmed.**

### Two corrections to the brief's expected non-Fidelity state

The brief states the ten non-Fidelity projects must hold at **"3465 total, 0 failed"**. Measured
today, project by project:

```
Core 284  Containers 109  Text 287  Vector 295  Rendering 121  Markup 259
OpenDocument 125  WordProcessing 761  Spreadsheets 621  Presentations 592
                                                     TOTAL 3454, FAILED 0
```

**Every project matches its handover figure exactly. The total is 3454, not 3465** — and the
handover's own table sums to 3454 as well. The brief's 3465 is an arithmetic slip of 11, not a
regression and not a change. Nothing moved; the expected figure was wrong. **0 failed confirmed.**

**But one thing did move, and nothing had flagged it.** The handover says "0 skipped and 0 warnings
throughout". Today `Paperless.Rendering.Tests` reports **120 passed, 1 skipped, 121 total**:

```
Skipped Paperless.Rendering.Tests.PdfFontTests.ACffFlavouredFaceIsNotClaimedToBeTrueType
```

It skips on `Assert.SkipUnless(TestCffFace.IsAvailable, "no CFF-flavoured face on this machine")`.
`TestCffFace` scans the machine for any sfnt beginning `OTTO` (`DrawnPage.cs:146-170`). Measured:
`find /usr/share/fonts -name '*.otf'` returns **zero files** on this container.

This matters more than a skip count suggests. The test's own remarks record what it guards: poppler
reporting *"Mismatch between font type and embedded font file"* and then *"No font in show"* **161
times**, leaving 161 glyph runs blank on a document that passed every other check. That guard is
currently not running at all.

**This is a thirteenth environment variable, undeclared: the container has no CFF-flavoured font.**
It belongs in `MISSING_PACKAGES.md` beside `fonts-dejavu-core`, and it presents exactly the way that
file warns about — as nothing at all except a skip count nobody was totalling. I have **not**
installed a font to fix it: three other agents are measuring against this machine's fontconfig
chain right now, and adding a face mid-session would silently move their reference renderings. It
is a recommendation, not a change.

**21 names, 40 cases — confirmed, not assumed.** The 40 failing cases resolve to exactly 21
distinct test names; the 19 extra failures are additional `[InlineData]` rows on 9 of those names
(the largest being `ExtractionComparisonTests` ×5, and four names at ×4/×3). There is no second
mechanism hiding in the 19. **Prediction P2 confirmed.** Full lists: `fails-head.txt`.

---

## 2. The decisive experiment: it is not our code

The brief asked for a bisect where it is cheap. It was cheap, and it is conclusive.

The handover records Fidelity green at **`0fb6d41e0`**, and records the installed binary at that
time as **24.2.7.2**. The container swap landed today in `4cbaeb41c3b`. So I checked out
`0fb6d41e0` — the last commit known green — and ran Fidelity **in today's environment**:

```
0fb6d41e0  ->  Failed: 40, Passed: 510, Skipped: 0, Total: 550
HEAD       ->  Failed: 40, Passed: 510, Skipped: 0, Total: 550
diff fails-head.txt fails-0fb6d41e0.txt  ->  IDENTICAL SET
```

The failing set is **byte-identical** at the two commits (`fails-0fb6d41e0.txt`). `git merge-base
--is-ancestor 0fb6d41e0 HEAD` passes, and only four commits touched `dotnet/src` in between.

**Therefore: no Paperless code change caused any of the 40.** The environment did.

**But that is where the easy answer stops, and I want to be explicit about it.** "Our code did not
change" is *not* the same as "our code is right". A reference binary that gets **more** correct
will newly disagree with a defect we have always had. So every one of the 40 was pushed through a
second question: **which side is now wrong?** That question is what the rest of this document
answers, and it splits the 40 four ways — including four cases where the answer is *we are*.

---

## 3. Which environment variable can physically reach these tests

Two of the three suspects in the brief are **excluded by measurement** for the families I took,
and this matters because the brief's leading hypothesis names all three.

**The font set is excluded for 18 of the 40.** `MISSING_PACKAGES.md` is right that DejaVu moves
53 of 534 corpus reference page counts — but that is the *corpus*, not this suite. The Fidelity
fixtures are Latin text in Carlito. Measured, by listing the fonts LibreOffice actually embedded
in its own reference PDF for each failing document:

| document | fonts embedded in the reference PDF |
|---|---|
| `paginated.docx` | `Carlito-Regular`, `Carlito-Bold` — nothing else |
| `list-label-overrun.odt` | `Carlito-Regular` only |
| `table-exact-row.docx` | `Carlito-Regular` only |
| `table-autofit.fodt` / `.docx` | `Carlito-Regular` only |

No DejaVu, no WenQuanYi, no fallback of any kind. **A font that is never resolved cannot move a
measurement.** Prediction P4 (2–6 font failures) is heading to **zero** in my share.

**Poppler is excluded as a *geometric* variable.** I checked whether `pdftotext -bbox` faithfully
reports what the PDF says, rather than computing its own boxes. For `paginated.pdf` line 1, I
reconstructed the pen position directly from the PDF's own content stream — the `Td` origin plus
the `/Widths` array minus the `TJ` kerning adjustments:

```
pen start (Td)                    70.950
advance total from /Widths + TJ  461.461
pen end                          532.411
minus the trailing space (2.486) 529.925
poppler's reported xMax          529.903     <- agrees to 0.022 pt
```

Poppler reports the PDF's own geometry to **0.022 pt**, and correctly excludes the trailing space
from the word box. It is a faithful conduit, not a variable, for every position comparison.
(Poppler remains a *plausible* variable for tests that compare word **counts** or **text**, since
word segmentation is its own decision — that is a different surface, covered in §4.4.)

By elimination, for the position-comparing families the variable is the **LibreOffice version**.

---

## 4. The 40, classified

| # | class | cases | verdict |
|---|---|---:|---|
| 4.1 | environment (LibreOffice) — **reference regressed, we are right** | 5 | LO's text filter triples a paragraph |
| 4.2 | environment (LibreOffice) — **reference regressed, we are right** | 3 | LO adds a cell margin to an exact row |
| 4.3 | environment (LibreOffice) — **reference artifact, we are right** | 3 | LO emits a trailing space as its own portion |
| 4.4 | **genuine defect in Paperless** | 4 | format-blind footnote separator |
| 4.5 | environment (LibreOffice) — **exposes a real sub-point divergence** | 8 | accumulated advance drift crossing two tolerances |
| 4.6 | *(covered separately — see §5)* | 17 | notes/pagination and sheets/slides |

### 4.1 `ExtractionComparisonTests` × 5 — the reference oracle is wrong

Cases: `NothingTheReferenceFindsIsMissingFromTheFeatureDocument` for `tables.doc`, `.docx`,
`.fodt`, `.odt`, `.rtf`. All five report the identical shortfall:

> content the reference filter found is absent from the Paperless extraction:
> `'the' x2, 'tables.' x2, 'After' x2`

The reference here is **LibreOffice's `Text (encoded)` filter**, not poppler — `ExtractionComparisonTests.cs:46-51`
calls `_libreOffice.ExtractText`. So **neither poppler nor the font set can reach this test at all.**

Measured, three ways:

1. **The file says it once.** `tables.odt`'s `content.xml` contains exactly one
   `<text:p>After the tables.</text:p>`, after the `Spans` table.
2. **LibreOffice's text filter says it three times** (measured, `grep -c` → 3):
   ```
   7  Wide cell | Tall cell
   8  Plain a   | Plain b
   9            | After the tables. | After the tables.      <- phantom row
   10 After the tables.                                       <- the real paragraph
   ```
   The filter also mis-flattens the nested table: it merges the inner table's rows into the outer
   table's grid (line 3 reads `Outer body left | Inner one`), and then absorbs the following body
   paragraph into a row that does not exist, duplicating it across the remaining columns.
3. **LibreOffice's own PDF rendering of the same file says it once** (measured, `pdftotext | grep -c`
   → 1), and renders the nested table correctly.

So LibreOffice **contradicts itself between its own two exports**, and its rendering agrees with
the file. Paperless's extraction is exactly right — it reproduces the nesting faithfully and emits
the paragraph once (measured with the CLI).

**Verdict: environment (LibreOffice version). The reference oracle regressed; Paperless is correct.**

*Measured vs inferred:* everything above is measured. I could **not** reduce it to a minimal
trigger — a hand-built FODT with a covered cell, with a row span, and an exact replica of the
`Spans` table geometry all failed to reproduce the phantom row. **The precise trigger is
unexplained**; the misbehaviour itself is not.

### 4.2 `TableComparisonTests` × 3 — LibreOffice adds a margin it should not

Cases: `EveryCellHoldsItsTextWhereLibreOfficeDoes` for `table-exact-row.doc`, `.docx`, `.rtf`.
All three: word 20 ("Golf") sits **58.150 pt** below the first word for us, **61.000 pt** for
LibreOffice. Δ = 2.850 pt against a 0.1 pt tolerance.

**`.odt` and `.fodt` are in the same `[Theory]` and pass.** That polarity is the whole finding.

Measured, from the reference PDFs' word boxes:

| | row 2 top | row 3 top ("Golf") | exact row height |
|---|---:|---:|---:|
| LibreOffice, `.odt` | 92.179 | 114.879 | **22.700 pt** |
| LibreOffice, `.docx` | 92.179 | 117.729 | **25.550 pt** |

The document declares `<w:trHeight w:val="454" w:hRule="exact"/>`. **454 twips = 22.700 pt** — which
is exactly what LibreOffice produces for the ODF twin, and exactly what Paperless produces for all
five formats.

The 2.850 pt excess is not a mystery: the same file declares
`<w:tblCellMar><w:top w:w="57" w:type="dxa"/>…`, and **57 twips = 2.850 pt exactly**.

LibreOffice 26.2.4.2's Word-import path is adding the cell's declared **top margin on top of** an
`hRule="exact"` row height. In OOXML an exact row height is the row's total height; the cell
margins live inside it. Word does not add them, LibreOffice's own ODF path does not add them, and
at 24.2.7.2 LibreOffice's Word path did not add them either (the test passed).

**Verdict: environment (LibreOffice version). The reference regressed; Paperless is correct.**
The delta is *exactly* the declared `w:tblCellMar/w:top`, which is as strong as attribution gets.

### 4.3 `TableAutoLayoutComparisonTests` × 3 — a trailing space counted as a run

Cases: `EveryCellStartsWhereLibreOfficeStartsIt` for `table-autofit.fodt`, `table-autofit-full.fodt`,
`table-autofit-mixed.fodt` — "line 3 run count". The `.docx` and `.rtf` rows of the same `[Theory]`
pass.

The assertion (`TableAutoLayoutComparisonTests.cs:120-121`) compares how many text **runs** each
side put on a line. Measured: on the failing line LibreOffice writes **four** portions and we draw
**three**. The fourth, dumped from the PDF content stream, is:

```
Td 509.4 739.239:   /F1 11 Tf <08> Tj
```

A single glyph. Decoding the font's `ToUnicode` CMap gives **`0x08` → `' '` (space)**. LibreOffice's
ODF path emits the line-ending space as a text portion of its own; `pdftotext` correctly reports no
word there, and Paperless correctly draws nothing.

**Verdict: environment (LibreOffice version). A reference-side portion-splitting artifact.**
Drawing a trailing space or not is invisible in the output; a run count is a producer's internal
choice, not a fidelity property.

### 4.4 The footnote separator × 4 — **a genuine defect in Paperless**

Cases:
- `FootnoteComparisonTests.TheRuleAboveTheNotesGoesWhereLibreOfficeDrawsIt` — `footnotes.doc`, `footnotes.docx`
- `PdfOutputComparisonTests.EveryShadeAndRuleIsFilledWhereLibreOfficeFillsIt` — `footnotes.doc`, `footnotes.docx`

Again the ODF twins (`footnotes.odt`, `footnotes.fodt`) are in the same `[Theory]` and pass.

Measured, straight out of the reference PDFs' content streams:

| | separator width | separator top |
|---|---:|---:|
| LibreOffice, `.odt` | 120.450 pt | 757.451 pt |
| LibreOffice, `.docx` | **144.000 pt** | **755.251 pt** |
| Paperless, every format | 120.475 pt | 757.465 pt |

The test's reported figures reproduce to the digit: it says `144.000 pt rendered` and
`755.251 pt rendered`, which is what I get from the PDF arithmetic independently.

**144.000 pt is exactly 2 inches** — Word's footnote separator. I confirmed it is *absolute*, not a
percentage, by editing the DOCX's `w:pgMar` from 1134 to 3000 twips a side (text width 481.9 pt →
295.3 pt) and re-rendering: **the rule stayed exactly 144.000 pt**. A percentage would have shrunk
with the column; it did not.

Paperless's side is `Paperless.WordProcessing/Layout/Paginator.cs:170`:

```csharp
public double NoteSeparatorWidth { get; init; } = 0.25;
```

consumed at `Paginator.cs:1888`:

```csharp
Length width = page.TextWidth * _options.NoteSeparatorWidth;
```

There is **one global fraction and no format distinction**, and nothing anywhere in `src/`
overrides it. The doc comment on line 166-168 records the measurement it came from — "LibreOffice's
PDF export draws the rule from 56.7 to 177.15 pt… 120.45 of 481.89 is exactly 25%" — and that
measurement is *still exactly right today*, **for ODF**. It was generalised to every format.

**Verdict: genuine defect in Paperless.** LibreOffice 26.2.4.2 corrected its Word import to Word's
2-inch separator; we still apply Writer's 25 %-of-text-width rule to Word documents. The environment
change is what made the defect *visible*; the defect is ours and it is real.

*Confidence, stated honestly:* **high** on the width (2 inches is Word's separator, and the
absoluteness is measured). **Medium** on the 2.214 pt vertical shift — I measured that it moved and
that it moved only for Word formats, but I did not independently establish that Word's separator
sits there, so I have not proven LibreOffice's new vertical placement is the correct one.

### 4.5 `PageDrawingComparisonTests` × 4 + `TabStopComparisonTests` × 4 — one phenomenon, two tolerances

These eight look like two unrelated families. They are **one**, and this is the finding I would
most want carried forward.

**`PageDrawingComparisonTests`** (`paginated.doc/.docx/.fodt/.rtf`): line 1's right edge,
530.423 pt drawn against 529.903 pt rendered — over a 0.5 pt tolerance by **0.020 pt**.

A Shouldly assertion stops at the first failure, so the reported case is only the first. I
instrumented `Close()` to record every comparison instead of throwing, ran it, and restored the
file (`git status` clean; the instrumented run is not part of any commit). Full distribution over
all four documents — `pagedrawing-deltas.tsv`, **732 comparisons per side**:

| | n | median | p95 | max | over 0.5 pt |
|---|---:|---:|---:|---:|---:|
| left | 732 | 0.1000 | 0.1000 | 0.1000 | 0 |
| right | 732 | 0.4501 | 0.5180 | 0.5570 | **88 (12.0 %)** |

The left edge is a **constant 0.1000 pt** on all 732 — a fixed export offset, not an error. The
right edge is a systematic positive drift whose whole distribution hugs the tolerance: the 0.5 pt
bound cuts through the middle of it. This assertion was never bounding an error; it was bounding a
systematic offset that happened to sit under 0.5.

**`TabStopComparisonTests`** (`list-label-overrun` × 4): word 4 at 117.571 drawn / 117.442 rendered,
Δ 0.129 against a 0.1 pt tolerance. Instrumented the same way (`overrun-deltas.tsv`), the whole
line is diagnostic:

```
  w  1 A          delta=+0.0000   <- margin
  w  2 label      delta=+0.0186
  w  3 wider      delta=+0.0737
  w  4 than       delta=+0.1287   <== OVER
  w  5 its        delta=+0.1598   <== OVER
  w  6 stop.      delta=+0.1889   <== OVER
  w  7 Paragraph  delta=+0.0000   <- margin, drift resets
  w  9 Overrun    delta=+0.0000   <- tab stop, exact
  w 19 1.         delta=+0.0000   <- tab stop, exact
  w 20 Within     delta=+0.0000   <- tab stop, exact
```

**Every tab stop and every margin is exact to 0.0000 pt.** The test is named for tab stops and its
tolerance comment is about tab stops — and the tab stops are perfect. What fails is the drift that
accumulates *between* them, along a run of text, and resets at every hard position.

So both families are measuring the same quantity: **Paperless's horizontal advances run
systematically ~0.1–0.2 % wider than LibreOffice's**, accumulating along a line (~0.03–0.055 pt per
word) and resetting at any absolutely-positioned point.

**Verdict: environment (LibreOffice version) as the trigger — but it exposes a real, pre-existing,
sub-point divergence in our text measurement.** This is not a new defect and it is not noise; it is
a genuine ~0.1 % disagreement that both tolerances were absorbing until LibreOffice's advances
moved slightly.

It also **contradicts a stated project assumption.** `dotnet/CLAUDE.md` says: "HarfBuzz is what
LibreOffice shapes with, so advance widths agree by construction." Measured, they do not — not by
much, but systematically and cumulatively. That sentence should not be relied on again without a
number beside it.

*Inferred, not measured:* the drift steps are close to whole multiples of 1/100 mm (0.02835 pt) —
1.94, 1.94, 1.10, 1.03 units on consecutive words — which would suggest LibreOffice quantises pen
positions to its internal 1/100 mm grid while we stay in continuous points. Other steps (0.66, 2.50,
3.47, 4.20 units) do **not** fit that cleanly. **I am recording the accumulation as measured and the
quantisation mechanism as an unproven hypothesis.**

---

## 5. Remaining 17 cases

`EndnoteComparisonTests` ×1, `FootnoteComparisonTests.EveryNoteSitsAtTheFootOfItsOwnPage` ×2,
`NoteRestartComparisonTests` ×2, `JustificationShrinkComparisonTests` ×2,
`FrameComparisonTests` ×2, `SheetDrawingComparisonTests` ×1, `SheetSpilledTextComparisonTests` ×2,
`SheetTextComparisonTests` ×1, `SlideAutofitParagraphSpaceComparisonTests` ×1,
`SlideChartFaceComparisonTests` ×1, `SlideTableComparisonTests` ×2.

*(filled in below)*

---

## 6. What I propose to repair, and what I actually changed

**Nothing has been re-baselined.** No expectation was loosened to make a test green. Both files I
instrumented were restored byte-for-byte and `git status` on `dotnet/tests/` is clean.

*(proposals below)*
