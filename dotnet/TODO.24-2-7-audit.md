# The 24.2.7.2 calibration audit

`CLAUDE.md` records that the reference binary moved from **24.2.7.2** to **26.2.4.2**, and that
"individual claims calibrated to 24.2.7.2 behaviour … are now claims about a superseded binary and
each needs one re-check before it is relied on". It named three. There are **50 such sites in 30
files**, and this is the list.

It exists because round 52 (slides) stopped treating that sentence as advice. `SlideAutofit.cs`
carried four of these sites; its remarks *said* it was a port of 24.2.7.2's bisection and *said* to
check the reference version first. 25.2 had replaced the search with a twelve-row
`constScaleLevels` table. Re-checking that one site was worth **−155.40 `abs_ink`, −11.1% of the
whole slides track** — where the two preceding rounds moved −10.34 and −15.33.

**So the prior on these is not "probably still fine".** **Two of the first five re-checked were
wrong**, and both announced themselves in their own comments.

## Counting them: use `git grep`, not `grep -r` — and count the marker, not the string

```sh
git grep -n '24\.2\.7' -- 'dotnet/src/**/*.cs' | grep -v '24\.2\.7-audit'          # open sites
git grep -c '24\.2\.7-audit' -- 'dotnet/src/**/*.cs' | awk -F: '{s+=$2} END{print s}' # done
grep  -rn '24\.2\.7' dotnet/src --include=*.cs                                       # DOUBLE — never use
```

`grep -r` returns exactly twice as many hits and files as `git grep`, because every project under
`dotnet/src` has a lower-case alias directory entry on this case-insensitive mount (same inode,
link count 1, untracked, invisible to `git ls-tree` and `git status`). See `CLAUDE.md`
§ "This container". **Never `rm` one — it unlinks the source tree.**

**And the string alone is not the metric, because it self-corrupts.** The first rounds to work this
list annotated their sites with sentences like *"…was calibrated to 24.2.7.2 and has been
re-checked"*, which **adds** matches for the very string being counted: the list appeared to grow
from 48 to 50 while it was being worked. So a re-checked site carries an explicit marker, and
progress counts the marker:

```
[24.2.7-audit: VERIFIED  <date>, <round> — …]     the claim still holds on 26.2.4.2
[24.2.7-audit: WRONG     <date>, <round> — …]     it does not; say whether it was fixed
[24.2.7-audit: UNDECIDED <date>, <round> — …]     the probe could not separate it; say why
```

**A marker is a claim like any other and can be wrong in either direction.** One site was marked
`WRONG` and later verified correct; another was `UNDECIDED` and later settled. Re-marking is
normal and the date plus round is what makes it followable.


**The running score is one wrong in eleven** (see the outcomes table below), and the useful reading
is not "these are fine" but *"a site cannot be trusted either way until a probe has been pointed at
it"*. A verified site is worth as much as a
broken one, because it is the only thing that stops the next round paying for the same probe.

| project | site | round | outcome |
|---|---|---|---|
| `Paperless.Presentations` | `Layout/SlideAutofit.cs` (4 sites) | 52 | **WRONG** — −155.40 `abs_ink`, 11.1% of the slides track |
| `Paperless.Spreadsheets` | `Layout/SheetFonts.cs` (2 sites) | 53 | **verified 26.2.4.2, 2026-08-21** — 30 of 30 authored column widths exact to 0.001 pt |
| `Paperless.Spreadsheets` | `Layout/SheetGeneralWidth.cs` | 53 | **verified 26.2.4.2, 2026-08-21** — 27 of 27, every `###` threshold crossing |
| `Paperless.Spreadsheets` | `Layout/SheetDeviceUnits.cs` | 53 | **verified 26.2.4.2, 2026-08-21** — 45 of 45 within 0.1% relative |
| `Paperless.Spreadsheets` | `Layout/SheetOptimalRowHeights.cs` | 54 | **verified 26.2.4.2, 2026-08-21** — 30 of 30 wrapped row heights within half a twip, control at 300 |
| `Paperless.Spreadsheets` | `Layout/SheetPageDecoration.cs` | 55 | **WRONG, half of it** — the zero-band guard holds; "draws at every stated band above zero" does not. Reported, not fixed; a `Math.Min` defect the probe found beside it *was* fixed |

**A trap the probe harness cost half an hour to find, before anyone else pays for it again.** A
minimal authored `.xlsx` with no `<cellStyles>` element has its `cellXf` font **discarded entirely**
by LibreOffice, so a font-size probe reads a constant 10 pt on the reference at every stated size
and reports 46 of 48 cases wrong. `dotnet/probes/sheets-r53-totalsrow/audit_mkwb.py` is a fixture
generator that is known to be read correctly by 26.2.4.2; start from it. Confirm any new fixture by
`soffice --convert-to fods` and reading `fo:font-size` back before trusting a single measurement.

## Progress

| round | sites re-checked | outcome |
|---|---|---|
| 52 (slides) | `SlideAutofit.cs`, 4 sites | **WRONG.** −155.40 `abs_ink`, −11.1% of the track |
| 53 (slides) | `SlideTextLayout.cs`, **6 sites** | **all six still correct** — and the probes written to check them found a *fifth* branch none of the six described, worth another two fixes' worth of baseline accuracy. See `probes/slides-r53/results.md` § "The 24.2.7.2 audit" |
| 54 (sheets) | `SheetOptimalRowHeights.cs`, the `WrappedHeight` fit | **still correct** — 30 of 30 authored wrapped rows within half a twip on 26.2.4.2, read twice over (marker deltas in both PDFs, and the reference's own `fods` `style:row-height`) with the site's own 300-twip figure as the control. `probes/sheets-r54/audit_rowheight.py` |
| 55 (sheets) | `SheetPageDecoration.cs`, the header/footer band guard | **WRONG in half.** The zero-band claim holds and every negative band too; "the reference draws the footer at every stated band above zero" is false — nothing is drawn at 0.72 or 1.44 pt of 8 pt text, and the threshold *scales with the point size* (1.44–2.16 pt at 8 pt, 4.32–5.76 pt at 20 pt). A text-fit rule, not a constant. Reported and not implemented: four corpus worksheets have a positive band under 6 pt and all four pass today. The probe also found a **real 18 pt body displacement** at a *negative* band, which is fixed. `probes/sheets-r55/audit_pagedecoration.py`, `census-bands.py` |

**48 → 42 sites, 30 → 29 files.** Two in one: a re-check is worth running even when it comes back
clean, because authoring the probe is what exposes what the site does *not* say. Round 53's
`make-linespace-probe.py` confirmed all four of `SlideTextLayout.cs`'s EditEngine sites and, in the
same rendering, showed that `SvxLineSpaceRule::Fix` and `::Min` — the two arms *before* the ones
those sites describe — were not transcribed at all. That was a 9.58 pt vertical displacement on
every paragraph stating an exact line height, 769 of them in 23 documents.

It also promoted one recorded *divergence* from a judgement to a measurement: `LineSpacingRule`'s
50% clamp is Writer's and not EditEngine's, and at 40% the reference draws `fround(0.40 × natural)`
rather than clamping. 26.2.4.2 has no such clamp either.

## The measured size of the list

Recomputed from the tree with `git grep`, excluding lines that carry a `[24.2.7-audit: …]` marker.
**Hits and files are different numbers and an earlier version of this table conflated them**, which
a round caught.

| project | open hits | files with an open site | reaches |
|---|---:|---:|---|
| `Paperless.Presentations` | 11 | 4 | slides |
| `Paperless.WordProcessing` | 11 | 8 | words |
| `Paperless.Spreadsheets` | 10 | 9 | sheets |
| `Paperless.Text` | 8 | 2 | **all three tracks** |
| `Paperless.Core` | 2 | 1 | **all three tracks** |
| `Paperless.Rendering` | 1 | 1 | **all three tracks** |
| `Paperless.Ooxml` | 1 | 1 | **all three tracks** |
| **total** | **42** | **26** | |

**The two figures above did not reproduce and are corrected.** This table said `44` open and the
paragraph below said `12` markers. Re-derived at commit `e11ee5ac386` with the commands this file
itself gives: **42 open, 13 marked**. A figure quoted rather than re-derived decays — the same
lesson this file records about round 53's "73 of 337", arriving again in the file that records it.

Marked so far: **14** lines —
9 verified,
3 wrong,
1 undecided,
1 half-wrong (`SheetPageDecoration.cs`, round 55, marked `WRONG`).

The **open** count does not fall when a site is verified, and that is deliberate: the sentence that
names 24.2.7.2 stays, because it records what the figure was fitted to. Round 55's marker is the
fourteenth and the open count held at 42. Read the two numbers as "how many sites still carry an
unchecked 24.2.7.2 claim" and "how many have been checked", not as a total and a remainder.

Reproduce both numbers with:

```sh
git grep -n  '24\.2\.7' -- 'dotnet/src/**/*.cs' | grep -vc '24\.2\.7-audit'   # open
git grep -c  '24\.2\.7-audit' -- 'dotnet/src/**/*.cs' | awk -F: '{s+=$2} END{print s}'  # done
```

## Outcomes so far — eleven sites re-checked, **one** wrong

| site | outcome |
|---|---|
| `SlideAutofit.cs` (one claim, 4 hits) | **WRONG**, fixed r52 — 25.2 replaced 24.2's bisection with `constScaleLevels`. −155.40 `abs_ink`, **11.1% of the slides track** |
| `SlideTextLayout.cs` (6 sites) | **VERIFIED** r53 — and authoring the probe exposed a chain arm all six described and none implemented, worth 769 sites' worth of fix |
| `SheetFonts.cs` (2), `SheetGeneralWidth.cs`, `SheetDeviceUnits.cs` | **VERIFIED** r53 — 30/30, 27/27, 45/45 authored cases exact |
| `SystemFontResolver.cs:406` | **VERIFIED** r53 |
| `SystemFontResolver.cs:441` | **VERIFIED** r54 — was UNDECIDED; settled with two fixtures that actually reach `DefaultFonts` |
| `SystemFontResolver.cs:657` (`GenericFallbacks`) | **VERIFIED** r54 — **was recorded WRONG by r53 and is not.** See below. Re-confirmed r55 from a fifth caller: the DOC filter *does* reach it undeclared, and it answers correctly there too |
| `MeasuredParagraph.cs` | **VERIFIED** r53 |

### The one that was recorded WRONG and was not — read this before trusting any entry here

Round 53 probed `GenericFallbacks`, found ten unrecognised families answering DejaVu **Serif** where
the code says Sans, measured that **86 of 337 words renderings disagree with the reference's font
list**, and recorded the site `WRONG`. I read that as the largest single known defect on the project
and dispatched a round to fix it.

**The site is correct. The rule simply does not live there.** Round 54 established it on **126
authored files** through the installed `soffice`, with five known-answer controls:

| filter | an unrecognised family, nothing declared |
|---|---|
| DOCX, DOC, RTF | **DejaVu Serif** — only `w:family="swiss"` moves it, to Sans; RTF's `\fnil`/`\froman`/`\fswiss`/`\fmodern` are all inert |
| ODF text, XLSX, PPTX, flat ODS | **fontconfig's own generic** — `Consolas` → DejaVu Sans *Mono* |

**The answer belongs to the filter, not the resolver.** Round 53's probe was DOCX-only: it held the
*format* fixed without noticing the format was the variable. The discriminator it lacked is that
`45-latin.conf` files 60 families under a generic and none is installed here, so `fc-match`
separates three answers rather than one.

**Had the recommended one-line change been made in `Paperless.Text`, it would have reflowed 202
slides and 130 sheets renderings, every one of them currently correct.** The fix belongs in
`Paperless.WordProcessing/Layout/WordFallbackClass.cs`, and that is where it went; the diff in
`Paperless.Text` is comment-only, verified by diff.

**Three lessons, all of which this project already states somewhere and none of which stopped it:**

1. **A probe that varies one thing must know what it is holding fixed.** "One variable at a time" is
   satisfied by a DOCX-only sweep and is still wrong, because the constant was the answer.
2. **An audit entry is a claim like any other.** `WRONG` earns no more trust than the comment it
   contradicts — this is § 7's "a refutation inherits no privilege from being a refutation",
   arriving for the second time in three rounds.
3. **A figure quoted rather than re-derived decays.** Round 53's "73 of 337 carry DejaVu Sans"
   **does not reproduce at any reading** — the candidates are 70, 40 and 32. Its companion figure,
   86, reproduces exactly. I repeated the 73 in a brief and in a report.
4. **A verified site can still carry a wrong sentence, and round 55 found one here.** Round 54's
   own marker said the word-processing filters "do not reach this undeclared". Three of the four
   filters it tested were not word-processing filters at all, and the DOC arm — the one it could
   not measure, because its probe was a DOCX round trip — *does* reach it. The verdict was right
   and the reason was over-general. **Re-verifying a site that is already VERIFIED found a real
   correction**, which is the argument for re-marking with a date and round rather than treating
   `VERIFIED` as terminal.

## How to work it, and the order

**The twelve shared-layer sites first** — `Paperless.Text` 8, `Core` 2, `Rendering` 1, `Ooxml` 1,
across 7 files.
They reach all three tracks, so one wrong calibration there is three tracks' worth of error, and
`SystemFontResolver.cs` sits upstream of the font resolution that decides 267 reference renderings.
A shared-layer re-check owes a **measurement** across the other two tracks, not an argument.

Then per-track, each track taking its own, densest file first. `SlideTextLayout.cs`'s six are the
obvious next after `SlideAutofit` — they are the same subsystem, and inter-paragraph spacing on the
`.ppt` path is already the slides track's named front.

**A re-check is a probe against the installed binary, not a reading of the C++.** The tree in this
checkout is 27.2.0.0.alpha0+, which is *also* not the reference. Authored variants, one thing
varied at a time, at least two points so a slope is fixed. Five rounds have burned predictions on
reading the source instead.

**Record the outcome at the site**, in both directions. A site re-checked and found *still correct*
is as valuable as one found wrong, and is the only thing that stops the next round re-checking it:
change the comment to name **26.2.4.2** and the date it was verified. A site left saying `24.2.7.2`
is, by this file's convention, unverified.


## Re-check log

One row per site actually re-checked, newest first. A site is only "verified" when the comment at
the site itself names **26.2.4.2** and the date — this table is the index, not the record.

| date | site | outcome | how |
|---|---|---|---|
| 2026-08-21 | `Paperless.Text/Fonts/SystemFontResolver.cs` `GenericFallbacks` (unrecognised → DejaVu Sans) | **VERIFIED again, from a fifth caller** | round 55. Round 54 verified it from four filters that reach it undeclared and stated that the word-processing filters never do. **The DOC filter does**: `GetFontParams` maps `ff` 0, 6 and 7 onto `FAMILY_DONTKNOW` and `SetNewFontAttr` sets it on the item, so those runs arrive here with no class and this switch answers them — DejaVu Sans, and DejaVu Sans *Mono* for `Consolas`, which is this switch's own column. Nine flat-ODF fixtures exported to Word 97 and back, `probes/words-r55/doc-family-code.py`; `Garamond` is the control, forced `FAMILY_ROMAN` by `GetFontParams`'s name-override list and drawn Serif where the otherwise identical `Aptos` is drawn Sans |
| 2026-08-21 | `Paperless.Text/Fonts/SystemFontResolver.cs` `GenericFallbacks` (unrecognised → DejaVu Sans) | **VERIFIED, and round 53's WRONG reversed** | `probes/words-r54/font-fallback-rule.py` (98 authored files, 5 controls) + `cross-format-fallback.py` (28): the branch is right for every filter that reaches it undeclared — ODF text, XLSX, PPTX, flat ODS, all tracking fontconfig, `Consolas` → DejaVu Sans **Mono**. The DOCX/DOC/RTF answer is a **roman default applied by the reader**, now in `WordFallbackClass`. Cross-track evidence: 0 of 302 slides and 0 of 307 sheets renderings show the Sans-for-Serif pair |
| 2026-08-21 | `Paperless.Text/Fonts/SystemFontResolver.cs` `DefaultFallbacks` (no family → Liberation Serif) | **VERIFIED** | round 54; two fixtures that reach `DefaultFonts` rather than Word's default — a flat ODF declaring no font anywhere, and a DOCX whose `docDefaults` state an empty `w:rFonts` — **both Liberation Serif** on 26.2.4.2. `w:ascii=""` is a third state and answers DejaVu Serif, because the filter reads it as a named family |

| 2026-08-21 | `Paperless.Spreadsheets/Layout/SheetOptimalRowHeights.cs` (the thirty-row `WrappedHeight` fit) | **verified, unchanged** | `probes/sheets-r54/audit_rowheight.py`: six sizes x one-to-five unbreakable words, no `ht`/`customHeight`, marker-delta y off both PDFs cross-checked against the reference's `fods` `style:row-height`. 30 of 30 within 0.05 twips; the twelve-point single-line control reads the 300 the site already claims |
| 2026-08-21 | `Paperless.Text/Layout/MeasuredParagraph.cs` (picture-alone descent) | **verified, unchanged** | `probes/words-r46/picture-alone-descent.py` re-run: 8 of 8 DOCX rows and 4 of 4 `fodt alone` rows exact, and the reference's own figures identical to round 46's 24.2.7.2 readings to the tenth at 20, 50 and 150 pt |
| 2026-08-21 | `Paperless.Text/Fonts/SystemFontResolver.cs` :406 (DejaVu, never Liberation) | **verified in that respect** | `probes/words-r53/font-fallback-recheck.py`: ten unrecognised families, all land on DejaVu, none on Liberation |
| 2026-08-21 | `Paperless.Text/Fonts/SystemFontResolver.cs` :629 (unrecognised → DejaVu **Sans**) | **WRONG — reported, not fixed** | same probe: all ten answer DejaVu **Serif**, with four controls agreeing; and `fc-match` answers Sans, so the stated mechanism ("fontconfig's default") is falsified too. **86 of 337 words renderings already disagree with the reference's font list and 73 carry DejaVu Sans on our side.** A change here owes a measured sweep of all three tracks |
| 2026-08-21 | `Paperless.Text/Fonts/SystemFontResolver.cs` :435 (no family → Liberation Serif) | **undecided** | the probe's no-family DOCX carries no `styles.xml`, so LibreOffice applies *Word's* default (Carlito) and the case never reaches `DefaultFonts`. Needs a fixture that does |
| 2026-08-20 | `Paperless.Presentations/Layout/SlideAutofit.cs` ×4 | **WRONG, fixed** | round 52; −155.40 `abs_ink`, −11.1% of the slides track |

**Two of the four shared-layer sites re-checked so far were wrong.** The prior on the remaining
44 is not "probably still fine".

## Still unverified in `Paperless.Spreadsheets` (3 of the 9)

`Layout/SheetNotes.cs`, `Layout/SheetShapeText.cs`, `Layout/SheetText.cs`,
`Ooxml/XlsxNoteCaptions.cs`.

**`SheetPageDecoration.cs` was round 55's and came back half wrong** — see the log above. The next
sheets round should take `SheetNotes.cs` or `SheetShapeText.cs`; `XlsxNoteCaptions.cs`'s claim (a
VML anchor's offsets are 96-dpi screen pixels) was exercised in passing by round 55's legacy-picture
reader, which shares that arithmetic and reproduces the reference to 0.12 pt on two documents, but it
has not been probed in its own right and is **not** marked.

The running score across all rounds is now **three wrong in eleven**. Two of the three were
shared-layer sites; the third is this one. **Six** `Paperless.Spreadsheets` sites have been
re-checked and five were correct, so the sheets metric work has mostly held across the binary
change — but "mostly" is now the honest word, and the first sheets site to be found wrong was the
first one that was about page furniture rather than text metrics.
