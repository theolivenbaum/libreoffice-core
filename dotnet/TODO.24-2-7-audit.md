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
```

Progress is `git grep -c '24\.2\.7-audit'`, never a count of `24.2.7`.

## The measured size of the list

Taken with `git grep` at the time of writing — **hits and files are different numbers and an
earlier version of this table conflated them**, which a round caught:

| project | hits | files | reaches |
|---|---:|---:|---|
| `Paperless.Presentations` | 17 | 6 | slides |
| `Paperless.WordProcessing` | 11 | 8 | words |
| `Paperless.Spreadsheets` | 10 | 9 | sheets |
| **`Paperless.Text`** | **8** | **4** | **all three tracks** |
| `Paperless.Core` | 2 | 1 | all three tracks |
| `Paperless.Rendering` | 1 | 1 | all three tracks |
| `Paperless.Ooxml` | 1 | 1 | all three tracks |
| **total** | **50** | **30** | |

**The shared layer is 12 hits in 7 files, not 8 sites.** (These totals include the marker lines
added since; use the marker-excluding command above for the live figure.)

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
| 2026-08-20 | `Paperless.Presentations/Layout/SlideAutofit.cs` ×4 | **WRONG, fixed** | round 52; −155.40 `abs_ink`, −11.1% of the slides track |

**Two of the four shared-layer sites re-checked so far were wrong.** The prior on the remaining
44 is not "probably still fine".
