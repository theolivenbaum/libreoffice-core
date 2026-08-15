# words-metrics-01 — four seats under `words/metrics-001`, two documents closed

Branch `wt-w-metrics`, from `ea37e4214b6` (the line-height law merged an hour earlier).

**Group `words/metrics-001`: 2 of 8 matching before, 4 of 8 after.**
**Whole words track: 169 of 200 before, 173 after — 4 won, 0 lost.**
**`done-*`: words 158/159, slides 144/144, sheets 156/156 — nothing lost in any track.**

---

## 1. The brief's classification was stale, and the baseline says so first

Re-measuring before touching anything is the first instruction and it earned its keep. The group
still fails 6 of 8, which is the briefed *count*, but it is **not the briefed set**:

| document | baseline | the brief said |
|---|---|---|
| `A320SimNotes.doc` | `pages` 41/42 | *never mentioned* |
| `手机免提系统TSB.doc` | `pages,words` 2/3 | CJK, lines never wrap |
| `02_mcar_part-2_and_IS_v2.10.docx` | `pages` 314/312 | one of three `mcar` |
| `AWR OPS-AOC 044 …docx` | `pages,words` 12/15 | breaks one word later |
| `FRE-03_mcar_part-3_and_IS_v2.9.docx` | **`match`** | one of three `mcar` |
| `OM template … NCC operators…docx` | `pages` 164/165 | TOC 13.80 vs 14.10 pt |
| `SPA-02_mcar_part-2_and_IS_v2.9.docx` | `pages` 267/266 | one of three `mcar` |
| `review-welsh-government-…docx` | **`match`** | `CONFIDENTIA L`, a 5-line cell |

So **the brief's central premise — "the three `mcar` documents share one seat, one fix would move all
three" — is refuted at the baseline**, before any work: one of the three already passes. Two of the
briefed failures were closed by the line-height merge, and one document the brief never names was
failing. `baseline.tsv` is the run.

## 2. What was wrong, in four seats

### (a) The uniform-paragraph shortcut measured a face it did not draw

**The largest of the four, and the one with reach outside this group.**

A paragraph whose formatting does not vary carries no `PageRun`s, and both layouters took a
shortcut for it: shape the whole text once in the paragraph's own face. The *drawing* pass has
never had that shortcut — `PageDrawing.RunsIn` sends every paragraph through
`FontItemiser.Split` whether it has runs or not, and its own comment says the cut is made *"as the
measurement cut them"*. For a uniform paragraph that was simply false.

Instrumenting the fill loop on `手机免提系统TSB.doc` gave the number outright:

```
SINGLEFACE face=Liberation Serif upem=2048 em=12.00 glyphs=106 adv=168858 width=989.40pt
FILL line[0] start=0 chosen=44 vis=44 w=410.70 limit=415.60 | 检查音响是否能够自动静音，…传出。
```

168858 / 106 = **1593 design units a character — Liberation Serif's `.notdef`**, 0.778 em, against
the em apiece WenQuanYi Zen Hei was actually drawing. So the filler put 44 characters on a line
that holds 34, and the painted line ran to x = 602.10 pt on a **595.30 pt page**. Poppler drops a
character whose origin is off the page, which is where the briefed "179 characters are clipped away"
came from: 1351 of 1530 characters extracted.

`GlyphFallbackWiringTests` already covered the split and never caught this, because it asks
`PageParagraph.Measure()` directly — and `Measure()` was right all along. **Nothing called it.**

*Fix.* `FontItemiser.NeedsFallback` asks `Split`'s question without building the list;
`PageParagraph.NeedsGlyphFallback` caches it; `Paginator` and `FlowLayouter` route on it. The
shortcut stays a shortcut and is taken only where it is equivalent.

### (b) The CJK 127% scale — and `lineheight-01` §7(a) states the wrong rule

With (a) fixed the document was 2 pages against 3 with every character present, and the whole
residue was vertical: intra-paragraph baseline pitch **16.25 pt against 20.30**. That is §7(a)'s
`lcl_ApplyCjkHeightAdjustment` (`fntcache.cxx`:270-292, tdf#129808), which §7 left undone because
it read as "one document to gain and three to risk".

§7 records the rule as `(gridded * 127) / 100` applied to the gridded ascent and the gridded line
height, and reports it exact on 39 of 39 IPAGothic pairs. **It is exact on those 39 and it is not
the rule.** `GetFontHeight` reads

```cpp
nRet = lcl_ApplyCjkHeightAdjustment(m_nPrtHeight, pSh, rRefDev) + GetFontLeading(pSh, rRefDev);
```

— the scale reaches the device's ascent-plus-descent, and the leading is added **afterwards,
unscaled**. `GetFontAscent` has the same shape. **IPAGothic's `hhea` lineGap is 0**, so on that face
the leading term vanishes and the two readings are the same number for every size; the 39-row table
could not have distinguished them. WenQuanYi Zen Hei has a gap of 92/1024 and does:

| | ascent+descent | leading | line height at 12 pt |
|---|---|---|---|
| the rule §7 states | (303 + 22) × 127/100 | — | **412 twips** |
| what the C++ does | 303 × 127/100 = 384 | + 22 | **406 twips** |
| what LibreOffice drew | | | **406 twips** |

`probe-cjk127.py` scores three candidate rules against 117 measured pairs over three faces —
WenQuanYi Zen Hei, IPAGothic, and Liberation Serif as a control that must not move:

```
 WHOLE: ascent  78/117   height  78/117      (the rule §7(a) states)
 PARTS: ascent  77/117   height  88/117
 TWIPS: ascent 117/117   height 117/117      (what GetFontHeight actually does)
```

per face, `WHOLE` is 39/39 on IPAGothic and **0/39** on WenQuanYi Zen Hei.

*Scope.* The flag is `MS_WORD_COMP_GRID_METRICS`, a document setting that defaults to false —
`DocumentSettingManager` initialises `mbMsWordCompGridMetrics(false)` and an ODF file carries its
own value. **Measured rather than reasoned:** the same two lines of WenQuanYi Zen Hei at 12 pt are
406 twips apart when LibreOffice reads them from a `.docx` and **325 apart from a `.fodt`**. So
`MetricGrid.AsWordDocument()` is asked for by the DOCX and DOC readers and not by ODF's.

### (c) Writer's fifth of an em between East Asian and Western text

That left the document at 3/3 pages with all 1530 characters and 81 drawn words against 95. The
gap was entirely inter-run spacing: LibreOffice sets `长安福特技术服务公告` ending at 331.800 pt and
`CAF` beginning at **334.200** — 2.400 pt at 12 pt, exactly a fifth of the em — and we ran them
together, so poppler read one word where LibreOffice draws two.

`SwTextFormatter::BuildPortions` (`itrform2.cxx`:707-734): *"The distance between two different
scripts is set to 20% of the fontheight"*, `rInf.GetFont()->GetHeight()/5`.

**The two exclusions are the whole of why this is safe to switch on**, and both are in
`fnRequireKerningAtPosition` (`:486-521`): one side must be `ScriptType::ASIAN` (tdf#89288, so Latin
beside Arabic or Hebrew gets nothing), and neither side may be Hangul (tdf#136663 — the space is a
Chinese and Japanese convention). The caller adds a third: both characters must be a letter or a
digit.

*Blast radius, measured before implementing rather than after.* Extracting all 200 words documents
and scanning for adjacent letter-to-letter script transitions found them in **exactly one document —
this one**. That is also why §7's "three CJK documents to risk" did not materialise: those three
embed a CJK face without ever putting CJK letters beside Latin ones.

Applied in `MeasuredParagraph` (into the prefix table, the way an inline object is) **and** in
`PageDrawing` (the pen, with the runs cut at the boundaries so the pen can pay it), from one
`ScriptSpacing.Opens`. A gap the filler charges and the pen does not is defect (a) again.

### (d) A WW8 list level's face was used only for symbol bullets

`A320SimNotes.doc` embedded 6 faces against the reference's 10, and the four it lacked were all
cuts of **Liberation Mono**. Its second-level bullet is Word's default `o` in Courier New;
LibreOffice draws it in Courier New, and we drew it in the item's own Liberation Serif Bold.

`DocReader.Label` read the level's `sprmCRgFtc0` and then used it *only* when `SymbolLabel`
recognised a recodeable symbol family — for anything else it fell back to the paragraph's face.
LibreOffice makes no such distinction: `WW8ListManager::ReadLVL` (`ww8par3.cxx`:1077-1095) reads the
`SvxFontItem` out of the level's character format and calls `SetBulletFont` whatever the character
is. `LevelLabel` is the missing other half, refused when the resolved face cannot draw the marker.

That alone moved the document from 41 pages to **42**.

## 3. What changed

| file | change |
|---|---|
| `src/Paperless.Text/Itemisation/FontItemiser.cs` | `NeedsFallback` — `Split`'s question without the list |
| `src/Paperless.Text/Fonts/OpenTypeTables.cs` | `Os2Table.CodePageRange1` and `DeclaresEastAsianCodePage` |
| `src/Paperless.Text/Fonts/LineSpacing.cs` | `MetricGrid.ScalesEastAsianFaces`, `AsWordDocument`, `EastAsianScaled`; applied before the leading |
| `src/Paperless.Text/Layout/ScriptSpacing.cs` | new — the fifth of an em and its three refusals |
| `src/Paperless.Text/Layout/MeasuredParagraph.cs` | the gap enters the prefix table; `SizeAt` |
| `src/Paperless.WordProcessing/Layout/PageContent.cs` | `NeedsGlyphFallback`, `HasScriptSpace`, `AddsScriptSpace` |
| `src/Paperless.WordProcessing/Layout/Paginator.cs` | routes on both |
| `src/Paperless.WordProcessing/Layout/FlowLayouter.cs` | the same, for cells, headers and text boxes |
| `src/Paperless.WordProcessing/Layout/PageDrawing.cs` | `ByScriptSpace`; the pen pays the gap |
| `src/Paperless.WordProcessing/Ooxml/DocxLayoutSource.cs` | Word grid, `AddsScriptSpace` |
| `src/Paperless.WordProcessing/Ww8/DocReader.cs` | the same, plus `LevelLabel` |

## 4. Reach, all 534 documents

Ours re-rendered with `SOURCE_DATE_EPOCH` set and verdicted against the banked references at
`/c/sandbox/workdir/refpdfs-26.2.4.2-fonts/`, using `lineheight-01`'s `render-track.py` and
`verdict.py` — `batch-check.sh`'s three checks column for column. The "before" is
`lineheight-01/words-after.tsv`, taken at this branch's own base commit by the same harness.

| track | before | after | won | lost | renderings changed |
|---|---:|---:|---:|---:|---:|
| words | 169 | **173** | 4 | **0** | — |
| slides | 147 | 147 | 0 | 0 | **0 rows differ** |
| sheets | 163 | 163 | 0 | 0 | **0 rows differ** |

The four won:

| document | group | before | after |
|---|---|---|---|
| `A320SimNotes.doc` | `metrics-001` | `pages` 41/42 | **`match`** 42/42 |
| `手机免提系统TSB.doc` | `metrics-001` | `pages,words` 2/3, 74/95 | **`match`** 3/3, 98/95 |
| `TE.CAO.00125 … OJT Logbook.docx` | `done-*` | `words` 2675/2793 | **`match`** 2793/2793 |
| `xx_SETIS_PWS_template_10.19.22.docx` | `done-*` | `words` 4375/4923 | **`match`** 4920/4923 |

The last two are seat (a) reaching outside the group: two `done-*` documents that were **already
failing** and are now exact. Two more improved without changing class —
`FO.FCTOA.00010 …docx` 4492 → 4514 words against 4513, and `EHEST-SMS-…docx` 19182 → 19199 against
19222. Nothing else in the words track moved at all.

**Slides and sheets did not move a single verdict row**, which is the falsification test that
mattered: `ScalesEastAsianFaces` and `AddsScriptSpace` both default to false and only the two Word
readers turn them on, so Impress's and Calc's devices are untouched.

## 5. The `done-*` tracks

Taken twice, by two harnesses, because the brief says it is not optional.

| track | documents | before | after |
|---|---:|---:|---:|
| `words/done-*` | 159 | 158 | **158** |
| `slides/done-*` | 144 | 144 | **144** |
| `sheets/done-*` | 156 | 156 | **156** |

And independently with the real thing, re-rendering the reference through `soffice`:

```
$ batch-check.sh … 'words/done-*' … 6
TOTAL 159  MATCH 158  MISMATCH 1  REF-CANNOT-RENDER 0
words/done-015/docx/airbus-pdf-information-package_v1-4.docx  9/9  1272/1299  words
```

The single mismatch is the known one the brief warns about, unchanged, and `words-regress-01` §2
established it is a missing repeat of a header row worth about thirty words rather than a metric.
**No `done-*` document lost its verdict in any track.**

## 6. Tests

61 new assertions in three files, plus two existing tests updated.

- `tests/Paperless.Text.Tests/EastAsianLineScaleTests.cs` — 38. Every expectation is a distance
  LibreOffice 26.2.4.2 drew, read out of its own PDF by `probe-cjk127.py`.
- `tests/Paperless.Text.Tests/ScriptSpacingTests.cs` — 24, including both tdf exclusions.
- `tests/Paperless.WordProcessing.Tests/UniformParagraphMeasurementTests.cs` — 6, through the
  layouters rather than through `Measure()`, which is where the choice is actually made.
- `tests/Paperless.Text.Tests/ItemisationTests.cs` — `NeedsFallback`, and the invariant it supports.
- `tests/Paperless.WordProcessing.Tests/ReferenceDeviceWiringTests.cs` — two tests updated: they
  correctly caught the grid changing, and now assert the device *and* the compatibility flag.

### Verified failing against the unfixed behaviour

Each mechanism reverted on its own, rebuilt, and measured:

| reverted | failures |
|---|---|
| `EastAsianScaled` → identity | **23 of 38** `EastAsianLineScaleTests` |
| `EastAsianScaled` → the rule §7(a) states | **12 of 38** — all 11 WenQuanYi rows and the discriminating test; **all 11 IPAGothic rows still pass** |
| `ScriptSpacing.Opens` → false | **7 of 24** `ScriptSpacingTests` |
| the routing in both layouters | **2 of 6** `UniformParagraphMeasurementTests` |

The second row is the one worth keeping: it is a live demonstration that the old evidence could not
have found this, and that the new suite can.

The first draft of the routing tests found only **1 of 6** and were rewritten — asserting
`Width <= column` tests nothing when the measurement is the thing that is wrong, because it is the
measured width that gets recorded. They now assert the line *count* and walk the drawn glyph runs.
`CoverageAndSplittingAgree` also failed as first written and was right to: the invariant holds one
way only, and the file now says which way and why.

### Counts, every project run individually

| project | result |
|---|---|
| `Paperless.Core.Tests` | 337 passed |
| `Paperless.Containers.Tests` | 109 passed |
| `Paperless.Text.Tests` | 485 passed |
| `Paperless.Vector.Tests` | 295 passed |
| `Paperless.Rendering.Tests` | 150 passed, 1 skipped |
| `Paperless.Markup.Tests` | 259 passed |
| `Paperless.OpenDocument.Tests` | 125 passed |
| `Paperless.Spreadsheets.Tests` | 847 passed |
| `Paperless.Presentations.Tests` | 710 passed |
| `Paperless.WordProcessing.Tests` | 903 passed |
| `Paperless.Fidelity.Tests` | 520 passed, **30 failed**, 0 skipped, 550 total |

**The Fidelity baseline was established first, at 30 of 550, and it is the same 30 afterwards — the
names, not merely the count.** The first capture of that baseline was truncated by piping the run
through `tail`, which made the comparison worthless; a true "before" binary was built and the two
lists diff clean. Nothing here is flaky-looking; no failure needed a re-run.

### The mtime trap, guarded

Every revert-and-restore used `cp` and `touch`, never `mv`, with `rm -rf obj bin` on both touched
projects before each rebuild. The check that catches it afterwards was run: both closed documents
re-render **byte-identical** to the run whose reach is claimed above.

## 7. Looking at the pages

Both pairs were composed with `page-vision` and handed to **fresh subagents**, blind, with no
numbers and no access to the repository. Three claims came back that a page "differs"; per the
standing rule each was checked in the operators, and **two of the three were refuted**:

| the reviewer said | in the operators |
|---|---|
| the reference has no space before `CAF`; ours does | **refuted** — 331.940 → 334.340 in ours, 331.800 → 334.200 in the reference. The same 2.400 pt gap, which is the fix in (c) working |
| ours drops the `、` after `1`, `A`, `B`, `C` | **refuted** — `1、 A、 B、 C、` are all present as drawn tokens in ours |
| ours puts more vertical space between the keyword box and `适用车辆` | **confirmed** — `适用车辆` sits at y = 156.32 in ours against 123.93 in the reference, **32.4 pt too low** |

That last one is a real defect this round did not fix and the gate cannot see, since the document
now passes. It is recorded in §9. The A320 reviewer found the bullet glyph, its shape and its indent
now matching everywhere, no text absent from either half, and one localised hanging-indent
difference in a single table cell.

## 8. Predictions, scored

Written after the baseline sweep and before any diagnosis (`prediction.md`). **Four right, three
wrong, one part-right** — a worse record than the round deserves, and the wrong ones are wrong in an
instructive direction: I under-rated the CJK document throughout.

| | claim | outcome |
|---|---|---|
| P1 | the remaining `mcar` pair is not closed by a kerning/advance change | **untested** — I did not work them |
| P2 | the `mcar` pair shares one seat with each other | **untested** |
| P3 | the CJK 127% scale is **not** what closes `手机免提系统TSB.doc` | **wrong, and doubly so.** It is exactly what closes the page count, once the wrap defect that masked it was gone — and the rule as recorded would not have closed it either |
| P4 | the 127% costs at least one passing CJK document; I decline to ship it | **wrong.** It cost nothing. The three "at risk" documents never had CJK beside Latin at all, which one extraction scan settled |
| P5 | `A320SimNotes.doc` is a font-resolution failure, not a metrics one | **right**, and the 6/10 font column was the whole clue |
| P6 | `OM template` is the cheapest of the six and closes | **wrong.** Not attempted; §9 |
| P7 | `AWR OPS-AOC 044` is the most expensive and I leave it open | **right** |
| P8 | two closed, between one and three | **right** — two closed, and two more won outside the group |

The lesson I would carry forward: **P3 and P4 were both reasoning from the brief's description of a
document rather than from the document.** "Lines never wrap" and "the leading is too small" read as
two separate problems; they were one cascade with the wrap defect on top, and one measurement in the
right place would have ordered them correctly an hour earlier.

## 9. What I did not do — plainly

**Four of the eight are untouched**, and none of them was even diagnosed to a seat:

- **`02_mcar_part-2` (314/312) and `SPA-02_mcar` (267/266)** — not worked at all. +0.6% and +0.4%
  of pages over 312 and 266. The brief's "one fix would move all three" is already refuted by
  `FRE-03` passing, so whether these two share a seat *with each other* is open and worth asking
  first: they are near-identical documents failing in the same direction.
- **`AWR OPS-AOC 044` (12/15)** — diagnosed only as far as a **table column-width** defect, not a
  metrics one. Page 3's `Manual` column starts at x = 244.85 in ours and **235.45** in the
  reference while the page header agrees to 0.1 pt, so our columns are wider from column 2 onward
  and every narrow cell wraps one word later. That is a table-layout seat and I stopped there.
- **`OM template` (164/165)** — not worked. Its first lines agree page for page across all 164, so
  it is a slow vertical accumulation with the divergence at the very end, in the glossary. The
  brief's "TOC 13.80 pt against 14.10" was classified before the line-height merge and I did not
  re-derive it.

Also left, on documents that now pass:

- **The 32.4 pt vertical gap** on `手机免提系统TSB.doc` §7, confirmed in the operators.
- **Hanging punctuation.** The reference lets one trailing full-width `：` hang past the right
  margin — its ordinary right limit is 573.7 pt, ours is 573.6, and exactly one line reaches 582.7
  and ends in punctuation. One line of the document, and the reason its word count is 98 against 95
  rather than closer. **That document passes at the band's edge** (`d > 3` fails; d = 3), which is
  worth knowing before something nudges it.
- **The WW8 label change has no unit test.** No committed corpus fixture names a non-symbol face on
  a list level — all 46 `.doc` fixtures were rendered and none embeds a mono or Courier face — and
  adding a binary `.doc` was not worth it. It is covered only by the corpus measurement in §4.
- **`MetricGrid.Printer.AsWordDocument()` is unmeasured.** No corpus document sets
  `usePrinterMetrics` and names an East Asian face. It is written that way because that is what the
  C++ does, not because it has been seen.
- **RTF does not get either Word flag.** It falls through to `PageContent`'s defaults. RTF is a Word
  format and probably should, but nothing measured it and no corpus document needs it.

## 10. Contradicting the brief

1. **"The three `mcar` documents … one fix would move all three."** `FRE-03_mcar_part-3` matches at
   the baseline. There are two, not three, and whether they share a seat is unestablished.
2. **`review-welsh-government-…docx` matches at the baseline** — the briefed `CONFIDENTIA L` split
   and 5-line cell are gone, closed by the line-height merge.
3. **`A320SimNotes.doc` was failing and the brief does not mention it.** It is now one of the two
   closed.
4. **`lineheight-01` §7(a)'s statement of the 127% rule is wrong**, though its measurement is
   sound. The scale reaches the ascent and the ascent-plus-descent, not the finished value; the 39
   IPAGothic pairs cannot tell the difference because that face's line gap is zero. §7's own
   framing — "reproduces 39 of 39 exactly, so the rule is not in doubt" — is the trap: a fit can be
   perfect on a sample that cannot discriminate.
5. **"One document to gain and three to risk" was the wrong shape of bet.** The three at risk were
   never at risk; one extraction scan over 200 documents established that only this document has CJK
   letters adjacent to Latin ones, and it cost less than re-rendering the track would have.
6. **The CJK document needed three fixes, not one.** The brief's two symptoms — lines running off
   the page, and leading of ~28 px against ~40 — were a cascade: the second was invisible until the
   first was fixed, and a third (the script gap) was invisible until the second was.

## Files

| file | what it is |
|---|---|
| `prediction.md` | written after the baseline, before diagnosis |
| `baseline.tsv` | `words/metrics-001` at `ea37e4214b6` |
| `final.tsv` | the same group after |
| `words-after.tsv` | all 200 words documents against the banked references |
| `probe-cjk127.py` | 117 measured pairs over three faces, scoring three candidate rules |
