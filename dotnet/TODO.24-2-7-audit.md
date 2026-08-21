# The 24.2.7.2 calibration audit

`CLAUDE.md` records that the reference binary moved from **24.2.7.2** to **26.2.4.2**, and that
"individual claims calibrated to 24.2.7.2 behaviour … are now claims about a superseded binary and
each needs one re-check before it is relied on". It named three. There are **48 such sites in 30
files**, and this is the list.

It exists because round 52 (slides) stopped treating that sentence as advice. `SlideAutofit.cs`
carried four of these sites; its remarks *said* it was a port of 24.2.7.2's bisection and *said* to
check the reference version first. 25.2 had replaced the search with a twelve-row
`constScaleLevels` table. Re-checking that one site was worth **−155.40 `abs_ink`, −11.1% of the
whole slides track** — where the two preceding rounds moved −10.34 and −15.33.

**So the prior on these is not "probably still fine".** One in one has been wrong, and the one
that was wrong announced itself in its own comment.

## Counting them: use `git grep`, not `grep -r`

```sh
git grep -n "24\.2\.7" -- 'dotnet/src/**/*.cs'      # 48 hits, 30 files
grep  -rn "24\.2\.7" dotnet/src --include=*.cs      # 96 hits, 60 files — exactly double
```

Every project under `dotnet/src` has a lower-case alias directory entry on this case-insensitive
mount (same inode, link count 1, untracked by git), so anything walking the filesystem visits both
spellings. See `CLAUDE.md` § "This container".

## Progress

| round | sites re-checked | outcome |
|---|---|---|
| 52 (slides) | `SlideAutofit.cs`, 4 sites | **WRONG.** −155.40 `abs_ink`, −11.1% of the track |
| 53 (slides) | `SlideTextLayout.cs`, **6 sites** | **all six still correct** — and the probes written to check them found a *fifth* branch none of the six described, worth another two fixes' worth of baseline accuracy. See `probes/slides-r53/results.md` § "The 24.2.7.2 audit" |

**48 → 42 sites, 30 → 29 files.** Two in one: a re-check is worth running even when it comes back
clean, because authoring the probe is what exposes what the site does *not* say. Round 53's
`make-linespace-probe.py` confirmed all four of `SlideTextLayout.cs`'s EditEngine sites and, in the
same rendering, showed that `SvxLineSpaceRule::Fix` and `::Min` — the two arms *before* the ones
those sites describe — were not transcribed at all. That was a 9.58 pt vertical displacement on
every paragraph stating an exact line height, 769 of them in 23 documents.

It also promoted one recorded *divergence* from a judgement to a measurement: `LineSpacingRule`'s
50% clamp is Writer's and not EditEngine's, and at 40% the reference draws `fround(0.40 × natural)`
rather than clamping. 26.2.4.2 has no such clamp either.

## Where they are

| project | sites | reaches |
|---|---:|---|
| `Paperless.Presentations` | 15 → **9** | slides |
| `Paperless.WordProcessing` | 11 | words |
| `Paperless.Spreadsheets` | 9 | sheets |
| **`Paperless.Text`** | **4** | **all three tracks** |
| `Paperless.Core` | 2 | all three tracks |
| `Paperless.Rendering` | 1 | all three tracks |
| `Paperless.Ooxml` | 1 | all three tracks |

Densest files:

```
6  Paperless.Presentations/Layout/SlideTextLayout.cs     <- re-checked r53, ALL SIX STILL CORRECT, cleared
4  Paperless.Presentations/Layout/SlideAutofit.cs        <- re-checked r52, WAS WRONG
                                                            (its four datelines are historical narrative
                                                             about the superseded port and were left in;
                                                             they are not unverified claims)
3  Paperless.Text/Fonts/SystemFontResolver.cs
3  Paperless.Presentations/Ooxml/PptxSlideLayout.cs
2  Paperless.WordProcessing/OpenDocument/OdtLayoutSource.cs
2  Paperless.WordProcessing/Ooxml/WriterPoolSpacing.cs
2  Paperless.WordProcessing/Ooxml/WordStyles.cs
2  Paperless.Spreadsheets/Layout/SheetFonts.cs
2  Paperless.Presentations/Layout/SlideDrawing.cs
2  Paperless.Core/Graphics/GlyphRun.cs
```

Full site list: `dotnet/probes/slides-r52/results.md`, and reproducible with the `git grep` above.

## How to work it, and the order

**The eight shared-layer sites first** — `Paperless.Text` 4, `Core` 2, `Rendering` 1, `Ooxml` 1.
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
