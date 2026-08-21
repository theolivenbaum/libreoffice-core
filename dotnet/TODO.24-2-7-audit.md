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

**A trap the probe harness cost half an hour to find, before anyone else pays for it again.** A
minimal authored `.xlsx` with no `<cellStyles>` element has its `cellXf` font **discarded entirely**
by LibreOffice, so a font-size probe reads a constant 10 pt on the reference at every stated size
and reports 46 of 48 cases wrong. `dotnet/probes/sheets-r53-totalsrow/audit_mkwb.py` is a fixture
generator that is known to be read correctly by 26.2.4.2; start from it. Confirm any new fixture by
`soffice --convert-to fods` and reading `fo:font-size` back before trusting a single measurement.

## Counting them: use `git grep`, not `grep -r`

```sh
git grep -n "24\.2\.7" -- 'dotnet/src/**/*.cs'      # 48 hits, 30 files
grep  -rn "24\.2\.7" dotnet/src --include=*.cs      # 96 hits, 60 files — exactly double
```

Every project under `dotnet/src` has a lower-case alias directory entry on this case-insensitive
mount (same inode, link count 1, untracked by git), so anything walking the filesystem visits both
spellings. See `CLAUDE.md` § "This container".

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

## Still unverified in `Paperless.Spreadsheets` (5 of the 9)

`Layout/SheetNotes.cs`, `Layout/SheetOptimalRowHeights.cs`, `Layout/SheetPageDecoration.cs`,
`Layout/SheetShapeText.cs`, `Layout/SheetText.cs`, `Ooxml/XlsxNoteCaptions.cs`.

**`SheetOptimalRowHeights.cs` first.** Row heights — not column widths — are the axis this project
established for a 14-document sheets cluster after a column-width hypothesis had been refuted, and
that site claims thirty exact reproductions against a *24.2.7.2* flat ODF round trip. If any figure
on this track has moved with the binary, that is where it costs the most.
