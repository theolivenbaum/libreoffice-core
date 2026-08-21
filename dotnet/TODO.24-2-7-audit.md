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

## Where they are

| project | sites | files | reaches |
|---|---:|---:|---|
| `Paperless.Presentations` | 17 | 6 | slides |
| `Paperless.WordProcessing` | 11 | 8 | words |
| `Paperless.Spreadsheets` | 10 | 9 | sheets |
| **`Paperless.Text`** | **6** | **4** | **all three tracks** |
| `Paperless.Core` | 2 | 1 | all three tracks |
| `Paperless.Rendering` | 1 | 1 | all three tracks |
| `Paperless.Ooxml` | 1 | 1 | all three tracks |

*Corrected 2026-08-21. The first cut of this table counted **files** for three of the rows and
sites for the others; the totals it gave — Text 4, Presentations 15, Spreadsheets 9 — are file
counts. The site counts are above and still sum to 48. **So the shared layer is ten sites, not
eight**, and a brief that says eight is short by the two extra `Paperless.Text` ones.*

```sh
git grep -c "24\.2\.7" -- 'dotnet/src/**/*.cs' \
  | awk -F'dotnet/src/' '{print $2}' \
  | awk -F/ '{p=$1; split($0,a,":"); s[p]+=a[length(a)]; f[p]++} END{for(k in s) print k, s[k], f[k]}'
```

Densest files:

```
6  Paperless.Presentations/Layout/SlideTextLayout.cs
4  Paperless.Presentations/Layout/SlideAutofit.cs        <- re-checked r52, WAS WRONG
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
