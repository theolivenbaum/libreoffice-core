# crop-wiring-01 — Escher picture cropping on the words and sheets paths

An implementation round with two named call sites and a measurement. Both sites are wired, the
long-standing "**Escher picture cropping, implemented nowhere in the word path**" closes, and the
measurement is below.

**Headline.** Reach is **7 of 200 words renderings**, **0 of 171 sheets** and **0 of 163 slides**.
**Zero verdicts move**, which was predicted before measuring. The seven that change reconcile the
reference: `RMI…GettingOffOil.doc` draws its cropped picture at 294.55 pt against LibreOffice's
294.45, where the base leg was 20.1 pt short; `absrc-pac-01-info-note-en.doc` at 71.2 against 71.1,
where the base leg was 6.3 pt short.

**Three things the brief said, and none of them survived.**

1. **"Each is one call from `EscherPicture.Cropped`" is wrong, identically on both paths.** Both
   named lines sit inside a `PictureOf(EscherShape)` with **no rectangle in scope**, because on
   neither track does the rectangle exist when the picture is read.
2. **Worse: neither painter clipped a picture at all.** A larger destination without a clip does
   not crop anything — it spills the whole picture across the page. Shipping "one call" would have
   been a regression, not a fix.
3. **And the inline `.doc` case needed a second refutation, of my own first answer.** See §4. It
   is the most useful thing in this round.

---

## 1. Environment

```
LibreOffice 26.2.4.2 620(Build:2)   pdftoppm 26.01.0   pdftotext 26.01.0
Calibri -> Carlito   Cambria -> Caladea   Arial -> Liberation Sans   DejaVu Sans -> DejaVu Sans
```

`check-env.sh` reports **Environment is good**. Reference renderings are the canonical
`/c/sandbox/workdir/refpdfs-26.2.4.2-fonts/` set, reused rather than re-rendered. Every sweep ran
with `SOURCE_DATE_EPOCH=1700000000` and `TZ=UTC`, and every byte comparison reports the raw and
the date-normalised count side by side — they agree everywhere, which is the check that the dates
were actually pinned rather than merely masked.

`PAPERLESS_CLI` was set explicitly to the worktree's own binary for every sweep.

## 2. The prediction, and how it did

`prediction.md` is `0980cf241f89`, committed before either sweep leg was rendered, and is unedited.

| # | Predicted | Outcome |
|---|---|---|
| "one call each" | **refuted, both sites** | **holds** — and understated; see §3 |
| words reach | 4 or 5 of 200, ceiling 5 | **wrong, under** — **7 of 200**, and the ceiling was wrong too |
| sheets reach | **0 of 171** | **right** |
| slides reach | **0 of 163** | **right** |
| verdicts | **zero movement, both tracks** | **right** — 0 of 7, every gate column identical to the digit |
| direction | no changed page worse | see §6 |
| the `+ 1` | not ported | **holds**, nothing reintroduces it |

**The words band was wrong because its ceiling was.** At prediction time the census reported 5
documents; the corrected instrument reports 7, and the measurement reports 7 — the same seven
documents, name for name. The correction is §5 and it was found while implementing, not by the
sweep.

## 3. What the two sites actually needed

Both named lines are the same statement — `uint pib = shape.Properties.Value(EscherPropertyIds.Picture);` —
inside a method whose only parameter is an `EscherShape`. `EscherPicture.Cropped(properties,
destination)` needs a `destination` and there is none, because:

- **sheets** — a `SheetDrawing` carries a *cell* anchor. A two-cell anchor has no size at all until
  the page's column widths and row heights are resolved, in `SheetPageGraphics.Place`.
- **words** — a `PageFrame` carries a size and an anchor; `FrameLayout` resolves the rectangle and
  `PageDrawing.DrawFrame` receives it as `PlacedFrame.Area`.

So the crop has to travel. `Paperless.Core.Geometry.PictureCropFractions` is the value it travels
as — four fractions, an `IsNone`, and an `Apply` that is `PictureCrop.Uncropped` with the
keeps-nothing null turned back into the rectangle it was given. `EscherPicture.Crop(properties)`
returns it and `EscherPicture.Cropped` becomes `Crop(properties).Apply(destination)`, so the three
hosts cannot drift into two arithmetics.

**The clip is the half that would have been missed.** `SheetPageGraphics` called
`sink.DrawImage(image, box)` and `PageDrawing.DrawFrame` called `sink.DrawImage(image,
frame.Area)`, both bare. The `.ppt` path only worked because `SlideDrawing.DrawPicture` already
wrapped every picture in `Save`/`ClipPath(shape.Outline)`/`Restore` for its own reasons — a picture
frame need not be rectangular on a slide. Both painters now take a clip, **and only when there is a
crop**: both backends already confine a picture to the rectangle they are given, so an
unconditional clip would be invisible on the page and would change the bytes of every rendering in
the corpus carrying a picture. That is pinned by a test on each track.

So each site is three changes — carry, apply, clip — not one.

**Two things this touched that the brief expected to be untouched**, and both are why the slides
sweep below is load-bearing rather than a formality: `Paperless.Core` gains one new file, and
`EscherPicture.Cropped` is re-expressed through `Crop(...).Apply(...)`. Neither changes behaviour,
and **0 of 163 slide renderings** is the evidence rather than the argument.

## 4. The refutation that matters: what `dxaGoal` means

**I got this wrong first, shipped it into a sweep, and the corpus caught it. The reason it was
wrong is worth more than the fix.**

An inline `.doc` picture is placed from the `PICF`'s `dxaGoal`/`dyaGoal` scaled by `mx`/`my`.
Applying `Uncropped` to that made the fixture's picture **800 pt wide instead of 480** — the crop
applied twice — so I concluded that a `.doc` states the *whole* picture where every other host
states the *visible* rectangle, and inverted the operation: inset the frame, draw at the goal.

That passed the fixture. It is wrong on all seven corpus documents.

| file | `dxaGoal × mx` | PICF `dxaCrop*` | Escher 256–259 |
|---|---|---|---|
| `picture-crop.doc` (LibreOffice's own export) | 480 × 540 pt = the **whole** picture | **150/300/450/600 twips** | 0.10 / 0.20 / 0.30 / 0.40 |
| `RMI…GettingOffOil.doc` (Word) | 468.00 × 274.37 pt = the **visible** rectangle | **0 / 0 / 0 / 0** | top 0.0686 |

**Across the whole words track: 52 `.doc` files with a `Data` stream, 32 cropped inline pictures,
and the PICF crop is zero on every single one.** LibreOffice's DOC export is the only writer in
play that states the crop twice, and it is the file I had authored.

One formula covers both, and it is LibreOffice's own reader's:

```
visible frame = (dxaGoal − dxaCropLeft − dxaCropRight) × mx / 1000
destination   = Uncropped(visible frame, the Escher fractions)
```

For the export: (1500 − 150 − 450) × 6.4 = 288 pt, grown to 480. For a Word file: the crop fields
are zero so the goal passes through unchanged at 274.37 pt, grown to 294.6 — and LibreOffice draws
it at **294.45**. So `Uncropped` is right on all four paths after all, including the floating `.doc`
one, whose `FSPA` measures **287.95 × 215.95 pt** for the same 480 × 540 pt picture — read out of
the `PlcSpaMom` of a floating twin of the fixture built for exactly this question.

**The general lesson, which is this project's oldest one arriving by a new route: a fixture round-
tripped through the reference implementation is a statement about the reference implementation.**
It cannot be the only thing an importer is tested against, because the exporter and the importer
of the same program agree with each other by construction and need not agree with the format's
other writers. `picture-crop-goal.doc` exists for that reason — it is `picture-crop.doc` patched
in place into the shape Word writes, and it fails against the implementation that passed the
round-tripped one.

## 5. The census, and the byte that hid two documents

Reach was predicted by walking records, never by regex — the previous round's regex census over XML
was wrong by a factor of sixteen.

**Validated against a known answer before use.** On the 51 `.ppt` decks the walker reports **16
documents / 100 cropped shapes**, which is `slides-b-01`'s figure to the digit, both before and
after the correction below.

**Its first two answers for words were both wrong, and the control column is what caught them.**

| instrument | words: documents reached | shapes | with a `pib` | crop | crop + `pib` |
|---|---:|---:|---:|---:|---:|
| walk `fcDggInfo` only | **0** | 0 | 0 | 0 | 0 |
| + scan the `Data` stream | 23 | 101 | 87 | 7 / 32 | 5 / 30 |
| + handle the `dgglbl` byte | **60** | **575** | **135** | **9 / 40** | **7 / 38** |

1. An inline picture's `SpContainer` is **not** in the `fcDggInfo` blob — it hangs off a
   `sprmCPicLocation` offset into the `Data` stream, behind a `PICF`. A walker that reads only
   `fcDggInfo` reaches nothing at all.
2. A `.doc`'s `OfficeArtContent` is a `DggContainer` followed by `OfficeArtWordDrawing`s, and each
   Word drawing puts a **one-byte `dgglbl`** in front of its `DgContainer`. A walk that trusts the
   previous record's length reads that byte as the next header and finds no floating shape in any
   document in the corpus. This is the one that cost the prediction: `A_320.doc` and
   `absrc-pac-01-info-note-en.doc` are floating and were invisible.

The fix is to scan for `SpContainer` headers and validate three ways — version nibble `0x0F`, the
length fits, and the first child is an `FSP` (`0xF00A`) of exactly 8 bytes. The scan reaches fewer
*total* shapes than the walk on the tracks where both apply (14606 against 17563 on slides), because
a container whose first child is not an `FSP` is refused; it reaches **the same 16 documents and
the same 100 cropped shapes**, which is the answer being measured.

**Final census**

| track | documents reached | shapes | with a `pib` | **crop** | **crop + `pib`** | measured reach |
|---|---:|---:|---:|---:|---:|---:|
| words | 60 | 575 | 135 | 9 / 40 | **7 / 38** | **7** |
| sheets | 26 | 471 | 60 | **0** | **0** | **0** |

**The census and the measurement agree exactly, 7 and 7, document for document.** That is the
strongest form this comparison has taken on the project — the slides round's best was 1 and 1 on a
single deck, and its crop census overshot by one.

The two documents carrying a crop on a shape with **no** picture —
`644730BRI0mna000BOX361539B00public0.doc` and `SFSP_2013-02_Bulletin.doc` — cannot move a rendering
and did not.

**The sheets zero is real and is stated plainly: the `XlsDrawing` half of this round is dead code
on this corpus.** 26 workbooks carry Escher shapes, 60 of those shapes carry a picture, and not one
states a crop. It ships because the arithmetic is right and tested, not because it buys anything
measurable here.

## 6. Reach, verdicts and direction

### Reach

| track | renderings | differ raw | differ after date normalisation |
|---|---:|---:|---:|
| **words** | 200 | **7** | **7** |
| **sheets** | 171 | **0** | **0** |
| **slides** | 163 | **0** | **0** |

The seven: `150_5300_13_chg10`, `150_5300_13_chg12`, `150_5300_13_chg8`, `150_5335_5a`, `A_320`,
`RMI_Document_Repository_Public-Reprts_GettingOffOil`, `absrc-pac-01-info-note-en` — all `.doc`.

Every sweep re-counted from the directory at the end, as `sweep.sh` has done since the round that
reported 163 and wrote 158: **200 of 200, 171 of 171, 163 of 163**, on both legs, six sweeps.

### Verdicts: zero, and predicted

| rendering | base | after | moved |
|---|---|---|---|
| `150_5300_13_chg10__doc` | pages,words 80/77 | pages,words 80/77 | no |
| `150_5300_13_chg12__doc` | pages 32/31 | pages 32/31 | no |
| `150_5300_13_chg8__doc` | pages 20/18 | pages 20/18 | no |
| `150_5335_5a__doc` | pages 63/64 | pages 63/64 | no |
| `A_320__doc` | pages 141/118 | pages 141/118 | no |
| `RMI…GettingOffOil__doc` | **match** 6/6 | **match** 6/6 | no |
| `absrc-pac-01-info-note-en__doc` | pages 6/7 | pages 6/7 | no |

**0 of 7.** Page counts are identical between the legs on all seven and extracted word counts move
by at most 145 of 24 000 without crossing the 2%-and-3-word band in either direction. Words stays
where it was and sheets stays where it was.

This is the right outcome rather than a weak one: the gate asks how many pages, how many
extractable words, and whether the fonts are embedded, and a crop is none of the three.

### Reference agreement, which the gate cannot see

| document | base leg | **after leg** | LibreOffice 26.2.4.2 |
|---|---:|---:|---:|
| `RMI…GettingOffOil.doc`, the cropped picture | 274.35 pt tall | **294.55** | **294.45** |
| `absrc-pac-01-info-note-en.doc`, the cropped picture | 64.8 pt wide | **71.2** | **71.1** |

Both to **0.1 pt**, from 20.1 pt and 6.3 pt out. These are the two documents in the seven whose
cropped picture lands on a page our pagination and the reference's agree about, so they are the two
where the comparison is unambiguous.

## 7. Tests

26 new tests — Core +7, Spreadsheets +7, WordProcessing +8, Presentations +4. Split by what
`verify-test.sh` actually established, not by intent. Every row below is an exit-0 run naming a
failing test, on a tree that built clean with the mutation applied.

### Verified by reintroduction

| mutation | detected by |
|---|---|
| the sheets clip removed | `SheetPictureCropTests.ACroppedPictureIsClipped` |
| `Crop = EscherPicture.Crop(...)` → `default` in `XlsDrawing` | `TheFourEscherPropertiesReachTheModel`, + 3 |
| sheets picture painted at the box, not the grown rectangle | `ACroppedPictureIsDrawnLargerThanItsAnchor`, `TheWholePictureStartsAboveAndLeftOfTheAnchor` |
| the sheets clip taken for every picture | `SheetPictureCropTests.AnUncroppedPictureIsNotClipped` |
| the words clip taken for every picture | `FramePictureCropTests.AnUncroppedPictureIsNotClipped` |
| `Crop` dropped in `Ww8DocumentReader.PictureOf` | `ACroppedPictureInADocIsDrawnLargerThanItsFrame`, `ACroppedPictureIsClipped`, `TheWholePictureStartsAboveAndLeftOfTheFrame` |
| `Crop` dropped between `Ww8LayoutFrame` and `PageFrame` (`DocReader`) | the same three |
| words picture painted at `frame.Area`, not the grown rectangle | `ACroppedPictureInADocIsDrawnLargerThanItsFrame`, `TheWholePictureStartsAboveAndLeftOfTheFrame` |
| the PICF crop not subtracted from `dxaGoal` | the same two |
| **the goal inset by the Escher fractions — the wrong implementation this round shipped first** | the same two, **on `picture-crop-goal.doc` only** |
| `PictureCropFractions.Apply` ignores the fractions | `ApplyIsUncropped`, + 3 |
| the keeps-nothing fallback returns `default` instead of the rectangle | `ACropThatKeepsNothingFallsBackToTheFrame` |
| `EscherPicture.Crop` returns the four fractions out of order | `TheFractionsAreTheFourPropertiesInOrder`, + 5 |
| `EscherPicture.Cropped` no longer goes through the fractions | `ApplyingTheFractionsIsCropped`, + 3 |

The tenth row is the one worth keeping: it is the round's own first answer, put back, and it is
caught by the fixture that could not be produced by a round trip and by that fixture alone.

### Drift guards only — kept deliberately, and labelled

- `PictureCropFractionsTests.NoneIsTheIdentity` and `TheDefaultValueIsUncropped` — pin that the
  default value is the uncropped case, which is what lets every model carrying a crop leave it
  unset. Every mutation that breaks them breaks a detector too.
- `EscherPictureCropTests.AShapeStatingNoCropHasNoFractions` — the same, for the property read.
- `SheetPictureCropTests.TheSameCropOnTheXlsxPathIsNotReadYet` — pins an **unwired** state, so
  there is no line to mutate. That is the definition of a drift guard, and it is here so that
  wiring `a:srcRect` later has to come past a test that says the two paths must then agree.
- `SheetPictureCropTests.TheUncroppedHalfIsDrawnAtItsAnchor` and
  `FramePictureCropTests.TheUncroppedHalfIsDrawnAtItsFrame` — the controls that make the
  offset assertions differences rather than absolutes.

### Fixture provenance

Four new corpus fixtures, all **authored for this round**, none collected, no third-party content
and no licence question.

- `picture-crop.docx` and `picture-crop.xlsx` are written element by element by
  `make-crop-fixtures.py` in this directory: a 100 × 100 PNG of four quadrants in a minimal
  package, one picture 288 × 216 pt stating `<a:srcRect l="10000" t="20000" r="30000" b="40000"/>`.
- `picture-crop.doc` and `picture-crop.xls` are those two through `soffice --convert-to`, which is
  what turns the `a:srcRect` into the four Escher properties.
- `picture-crop-goal.doc` is `picture-crop.doc` patched **in place**, same field widths and no
  structural change, into the shape Word writes — `dxaGoal` at the visible size and no PICF crop.
  §4 is why it exists.

**The image is 100 × 100 and the size is load-bearing.** BIFF export states the crop in the
*bitmap's* pixel space, so at the slide round's 8 × 8 a 10% crop came back as 0.0990 and a 20% as
0.1981. At 100 pixels the round trip is exact to four places, which is what lets the `.xls`
assertions be tight enough to catch anything.

## 8. Final state

```
dotnet build Paperless.slnx -v q -nologo     0 Warning(s)   0 Error(s)
```

| project | before | after |
|---|---:|---:|
| Core | 294 | **301** |
| Containers | 109 | 109 |
| Text | 289 | 289 |
| Vector | 295 | 295 |
| Rendering | 121 (1 skipped) | 121 (1 skipped) |
| Markup | 259 | 259 |
| OpenDocument | 125 | 125 |
| WordProcessing | 769 | **777** |
| Spreadsheets | 628 | **635** |
| Presentations | 605 | **609** |
| **total** | **3494** | **3520**, 0 failed, 1 skipped |

The brief's per-project baseline reproduced exactly, project for project, before any addition.
`Paperless.Fidelity.Tests` was not run.

## 9. Read versus inferred

**Measured or read:**

- All six sweeps, complete and re-counted from disk; all three byte comparisons, raw and
  date-normalised, agreeing everywhere.
- Every gate column on all seven changed renderings, on both legs, against the canonical reference.
- The image placement operators of `RMI…GettingOffOil`, `absrc-pac-01-info-note-en` and
  `150_5300_13_chg8` in the base leg, the after leg and the reference, read out of the PDF content
  streams.
- The `PICF` of the fixture and of every cropped inline picture in the words corpus — 52 `.doc`
  files with a `Data` stream, 32 cropped inline pictures, `dxaCrop*` zero on all 32.
- The `FSPA` rectangle of the floating fixture, out of `PlcSpaMom`: 287.95 × 215.95 pt.
- The census on all three tracks, with its control columns, and its reproduction of
  `slides-b-01`'s 16 documents / 100 shapes both before and after the `dgglbl` correction.
- Every reintroduction result in §7, from `verify-test.sh` exit 0 with a named failing test.

**Inferred, and flagged:**

- That the intermediate implementation's direction figures were noise from a reflowed layout
  rather than a second defect. It was replaced rather than diagnosed, because the corrected one
  reconciles the reference and it did not.
- That `A_320.doc`'s and `absrc-pac`'s cropped shapes are floating rather than inline. Established
  from which blob the census scanner found them in, not by following the `FSPA` for each.
- That no `.doc` in the corpus states a `PICF` crop **without** an Escher crop. The probe searched
  for both together and reported zero of the first kind; a document with a `PICF` crop on a picture
  whose `SpContainer` the scan refused would not have been counted.

## 10. What the next round should take from this

1. **`a:srcRect` on the `.docx` and `.xlsx` paths is unwired** — 11 words documents / 14 instances,
   0 sheets, 63 slides decks / 318 instances. `DrawingFill.SourceRect` already reads it and
   `DocxFrames`, `OdfFrames` and the SpreadsheetML drawing reader all drop it. The Core arithmetic,
   the model properties and both clips now exist, so it is a read and a hand-off on each.
2. **ODF's `fo:clip` is not read at all**, on any of the three families.
3. **A `.doc` may state a crop in the `PICF` alone.** Nothing in this corpus does, so nothing here
   measures it; the frame would come out right and the picture would be squashed into it rather
   than cropped. Deriving the fractions from `dxaCrop*/dxaGoal` when the Escher properties are
   absent is three lines and needs its own fixture.
4. **Do not test a binary importer against a round trip alone.** §4. The exporter and importer of
   one program agree by construction; the corpus is written by a different program, and on the one
   field that mattered here they disagree completely.
