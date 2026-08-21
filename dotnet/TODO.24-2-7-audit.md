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

**So the prior on these is not "probably still fine".** One in one had been wrong, and the one
that was wrong announced itself in its own comment.

Round 53 (sheets) then re-checked **four** and found **all four still correct** — so the running
score is **one wrong in five**, and the useful reading is not "these are fine" but *"a site cannot
be trusted either way until a probe has been pointed at it"*. A verified site is worth as much as a
broken one, because it is the only thing that stops the next round paying for the same probe.

| project | site | round | outcome |
|---|---|---|---|
| `Paperless.Presentations` | `Layout/SlideAutofit.cs` (4 sites) | 52 | **WRONG** — −155.40 `abs_ink`, 11.1% of the slides track |
| `Paperless.Spreadsheets` | `Layout/SheetFonts.cs` (2 sites) | 53 | **verified 26.2.4.2, 2026-08-21** — 30 of 30 authored column widths exact to 0.001 pt |
| `Paperless.Spreadsheets` | `Layout/SheetGeneralWidth.cs` | 53 | **verified 26.2.4.2, 2026-08-21** — 27 of 27, every `###` threshold crossing |
| `Paperless.Spreadsheets` | `Layout/SheetDeviceUnits.cs` | 53 | **verified 26.2.4.2, 2026-08-21** — 45 of 45 within 0.1% relative |
| `Paperless.Presentations` | `Ooxml/PptxSlideLayout.cs` :763 (table cell line spacing) | 54 | **verified 26.2.4.2, 2026-08-21** — 6 of 6 stated sizes put the reference's first baseline one em below the cell's top |

**A trap the probe harness cost half an hour to find, before anyone else pays for it again.** A
minimal authored `.xlsx` with no `<cellStyles>` element has its `cellXf` font **discarded entirely**
by LibreOffice, so a font-size probe reads a constant 10 pt on the reference at every stated size
and reports 46 of 48 cases wrong. `dotnet/probes/sheets-r53-totalsrow/audit_mkwb.py` is a fixture
generator that is known to be read correctly by 26.2.4.2; start from it. Confirm any new fixture by
`soffice --convert-to fods` and reading `fo:font-size` back before trusting a single measurement.

## Counting them: use `git grep`, not `grep -r`

```sh
git grep -n "24\.2\.7" -- 'dotnet/src/**/*.cs' | grep -v '24\.2\.7-audit'   # open sites
grep  -rn "24\.2\.7" dotnet/src --include=*.cs                              # exactly DOUBLE — do not use
```

`grep -r` returns twice as many hits and files as `git grep`, because every project under
`dotnet/src` has a lower-case alias directory entry on this case-insensitive mount (same inode,
link count 1, untracked). See `CLAUDE.md` § "This container".

**And the string alone is not the metric — it self-corrupts.** The first two rounds to work this
list annotated their sites with sentences like *"…was calibrated to 24.2.7.2 and has been
re-checked"*, which **adds** matches for the very string being counted. The list appeared to grow
while it was being worked. That is why a re-checked site now carries an explicit marker:

```
[24.2.7-audit: VERIFIED  <date>, <round> — …]     the claim still holds on 26.2.4.2
[24.2.7-audit: WRONG     <date>, <round> — …]     it does not; say whether it was fixed
[24.2.7-audit: UNDECIDED <date>, <round> — …]     the probe could not separate it; say why

## Where they are

| project | sites | reaches |
|---|---:|---|
| `Paperless.Presentations` | 15 | slides |
| `Paperless.WordProcessing` | 11 | words |
| `Paperless.Spreadsheets` | 9 | sheets — **4 re-checked r53, all still correct** |
| **`Paperless.Text`** | **4** | **all three tracks** |
| `Paperless.Core` | 2 | all three tracks |
| `Paperless.Rendering` | 1 | all three tracks |
| `Paperless.Ooxml` | 1 | all three tracks |

Densest files:

```
6  Paperless.Presentations/Layout/SlideTextLayout.cs
4  Paperless.Presentations/Layout/SlideAutofit.cs        <- re-checked r52, WAS WRONG
3  Paperless.Text/Fonts/SystemFontResolver.cs
3  Paperless.Presentations/Ooxml/PptxSlideLayout.cs
2  Paperless.WordProcessing/OpenDocument/OdtLayoutSource.cs
2  Paperless.WordProcessing/Ooxml/WriterPoolSpacing.cs
2  Paperless.WordProcessing/Ooxml/WordStyles.cs
2  Paperless.Spreadsheets/Layout/SheetFonts.cs          <- re-checked r53, still correct
2  Paperless.Presentations/Layout/SlideDrawing.cs
2  Paperless.Core/Graphics/GlyphRun.cs
```

Progress is `git grep -c '24\.2\.7-audit'`, never a count of `24.2.7`.

## Progress

| round | sites re-checked | outcome |
|---|---|---|
| 52 (slides) | `SlideAutofit.cs`, 4 sites | **WRONG.** −155.40 `abs_ink`, −11.1% of the track |
| 53 (slides) | `SlideTextLayout.cs`, **6 sites** | **all six still correct** — and the probes written to check them found a *fifth* branch none of the six described, worth another two fixes' worth of baseline accuracy. See `probes/slides-r53/results.md` § "The 24.2.7.2 audit" |
| 54 (slides) | `PptxSlideLayout.cs` :763, 1 site | **still correct** — and the version bump is *why* it is correct: the site records that 24.2.7.2 drew a cell's first baseline at the face's own 0.907 em, `a47776a938c` (tdf#165521) removed the leading for cells, and 26.2.4.2 draws it at one em on 6 of 6 stated sizes from 10 to 40 pt. A site whose comment says "the running binary disagrees with its own C++" is the highest-prior kind on this list |

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
| `Paperless.Presentations` | 12 | 5 | slides |
| `Paperless.WordProcessing` | 11 | 8 | words |
| `Paperless.Spreadsheets` | 10 | 9 | sheets |
| `Paperless.Text` | 8 | 2 | **all three tracks** |
| `Paperless.Core` | 2 | 1 | **all three tracks** |
| `Paperless.Rendering` | 1 | 1 | **all three tracks** |
| `Paperless.Ooxml` | 1 | 1 | **all three tracks** |
| **total** | **44** | **29** | |

Marked so far: **12** lines —
**9** verified,
2 wrong,
1 undecided.

Recomputed 2026-08-21 after round 54 with the two commands below: **44 open hits in 29 files**.
The file count stood at 26 here and was wrong; hits and files are different numbers and this table
has now conflated them twice.

**Round 54 tripped the self-corrupting-string trap this file warns about, in the marker itself.**
Its `[24.2.7-audit: VERIFIED …]` block ran to a second line, and that continuation line named
`24.2.7.2` in prose — so it did not carry the marker, and the open count went *up* by one while a
site was being cleared. The rule is sharper than "annotate with a marker": **no line of a
multi-line marker may contain the bare string.** Reworded to "the superseded note above".

Reproduce both numbers with:

```sh
git grep -n  '24\.2\.7' -- 'dotnet/src/**/*.cs' | grep -vc '24\.2\.7-audit'   # open
git grep -c  '24\.2\.7-audit' -- 'dotnet/src/**/*.cs' | awk -F: '{s+=$2} END{print s}'  # done
```

## Outcomes so far — two of five re-checked sites were wrong

| site | outcome |
|---|---|
| `SlideAutofit.cs` (4 hits, one claim) | **WRONG**, fixed r52 — 25.2 replaced 24.2's bisection with `constScaleLevels`. −155.40 `abs_ink`, **11.1% of the slides track** |
| `SystemFontResolver.cs:406` | **VERIFIED** r53 — unrecognised families still all land on DejaVu |
| `SystemFontResolver.cs:439` | **UNDECIDED** r53 — probe confounded, and it says why |
| `SystemFontResolver.cs:637` | **WRONG** r53, **not fixed** — see below |
| `MeasuredParagraph.cs:744` | **VERIFIED** r53 — unchanged on 26.2.4.2 |

**Two of five.** The prior on an unverified site is not "probably fine".

### The open one, and it is the largest single finding on the list

`SystemFontResolver.GenericFallbacks` says an unrecognised family resolves to **DejaVu Sans**.
On 26.2.4.2 **all ten unrecognised families probed answer DejaVu *Serif*** — one authored DOCX per
family through the installed `soffice`, face read out of the PDF, with four controls agreeing
(Liberation Serif → itself, Calibri → Carlito, Cambria → Caladea, Arial → Liberation Sans). Two
authored nonsense names, one with a serif hint and one without, **both** answer Serif, so the shape
of the name does not decide it either.

**The stated reason is falsified independently of the answer**: `fc-match Aptos` and `fc-match ""`
both return `DejaVuSans.ttf`, so whatever 26.2.4.2 does here, it is not "ask fontconfig and take
its default" — the second time this project has caught that assumption.

Cost, measured rather than assumed: over all 337 words renderings, **86 disagree with the
reference's embedded font list and 73 of those carry DejaVu Sans on our side**, mostly the plain
pair `ours=DejaVuSans, ref=DejaVuSerif`. The two faces have different advances, so each is a
line-breaking difference as well as a visible one.

**It is deliberately not fixed.** A one-line change in `Paperless.Text` owes a measured sweep of
all three tracks, and that is the parent's to run, not a track round's to slip in at the end.

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
| 2026-08-21 | `Paperless.Text/Layout/MeasuredParagraph.cs` (picture-alone descent) | **verified, unchanged** | `probes/words-r46/picture-alone-descent.py` re-run: 8 of 8 DOCX rows and 4 of 4 `fodt alone` rows exact, and the reference's own figures identical to round 46's 24.2.7.2 readings to the tenth at 20, 50 and 150 pt |
| 2026-08-21 | `Paperless.Text/Fonts/SystemFontResolver.cs` :406 (DejaVu, never Liberation) | **verified in that respect** | `probes/words-r53/font-fallback-recheck.py`: ten unrecognised families, all land on DejaVu, none on Liberation |
| 2026-08-21 | `Paperless.Text/Fonts/SystemFontResolver.cs` :629 (unrecognised → DejaVu **Sans**) | **WRONG — reported, not fixed** | same probe: all ten answer DejaVu **Serif**, with four controls agreeing; and `fc-match` answers Sans, so the stated mechanism ("fontconfig's default") is falsified too. **86 of 337 words renderings already disagree with the reference's font list and 73 carry DejaVu Sans on our side.** A change here owes a measured sweep of all three tracks |
| 2026-08-21 | `Paperless.Text/Fonts/SystemFontResolver.cs` :435 (no family → Liberation Serif) | **undecided** | the probe's no-family DOCX carries no `styles.xml`, so LibreOffice applies *Word's* default (Carlito) and the case never reaches `DefaultFonts`. Needs a fixture that does |
| 2026-08-21 | `Paperless.Presentations/Ooxml/PptxSlideLayout.cs` :763 (PPTX table cell line spacing) | **verified, and the claim had already been corrected** | `probes/slides-r54/make-cell-baseline-probe.py`: one table, one cell, zero margins, top-anchored, six stated sizes 10–40 pt. The reference's first baseline sits 1.0007 … 1.0002 ems below the cell's top edge on all six — one em, not the 0.907 em of 24.2.7.2 — and our own land on it to 0.000 pt. **The ODF half of the same claim, `OdpSlideLayout.cs:302`, is NOT covered** |
| 2026-08-20 | `Paperless.Presentations/Layout/SlideAutofit.cs` ×4 | **WRONG, fixed** | round 52; −155.40 `abs_ink`, −11.1% of the slides track |

**Two of the four shared-layer sites re-checked so far were wrong.** The prior on the remaining
44 is not "probably still fine".

## Still unverified in `Paperless.Spreadsheets` (5 of the 9)

`Layout/SheetNotes.cs`, `Layout/SheetOptimalRowHeights.cs`, `Layout/SheetPageDecoration.cs`,
`Layout/SheetShapeText.cs`, `Layout/SheetText.cs`, `Ooxml/XlsxNoteCaptions.cs`.

**`SheetOptimalRowHeights.cs` first.** Row heights — not column widths — are the axis this project
established for a 14-document sheets cluster after a column-width hypothesis had been refuted, and
that site claims thirty exact reproductions against a *24.2.7.2* flat ODF round trip. If any figure
on this track has moved with the binary, that is where it costs the most.
