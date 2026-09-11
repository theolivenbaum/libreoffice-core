# A `.ppt` shape resizes itself around its own text, and the seat that hid it is a dropped paragraph

Round 97, slides track. Three seats — **O14 closes**, **O15 is characterised and stays open with a
correction to its own witness**, **O13 stays open with a reference-side census it did not have**.

## Environment

| | |
|---|---|
| worktree / branch | `/home/user/wt-slidefont`, `agent/slidefont`, base `61bc19e03` |
| reference | `/opt/libreoffice26.2/program/soffice` — **26.2.4.2**. `/usr/bin/soffice` (24.2.7.2) is used for nothing here |
| reference bank | `/home/user/gate-orig-r83/ref`, 947 PDFs, rendered 2026-09-08 — after the 2026-09-07 font move, so valid |
| corpus | `/home/user/sample-files`; the slides binary column is **51 `.ppt`** (`git ls-files slides \| grep -i '\.ppt$'`) |
| C++ tree | `/home/user/libreoffice-core`, read only, never built |
| fonts | the five tarball confounds aside, untouched during the round |

Every C++ citation below was opened in this checkout in this round. No build overlapped a sweep:
the base binary was made by copying the three changed sources aside, `git checkout --`,
`touch`, `rm -rf obj bin` on `Paperless.Presentations` and `Paperless.Cli`, and the head binary the
same way afterwards; both are kept whole under `/home/user/r97-slides/cli-{base,head}`.

---

# 1. O14 — the banner is 103.38 pt because the shape grows to its text, and closes

## 1.1 What the reference does

`pres_ioc_phuket.ppt` page 26's dark blue banner states a client anchor of **519 master units —
64.88 pt** — and 26.2.4.2 draws it **103.38 pt** tall. `--convert-to fodp` gives the reason
outright: the shape comes out as `svg:height="3.647cm"` under a graphic style stating
`draw:auto-grow-height="true"`, and it holds **two empty paragraphs** of 40 pt Arial Black.

The mechanism, read rather than inferred:

- **Escher states it as bit 1 of `DFF_Prop_FitTextToShape` (191)** —
  `include/svx/msdffdef.hxx`:119, the boolean group at 188-191. That shape's property 191 is
  `0x00020002`: the bit and its own "used" flag.
- **`svdfppt.cxx`:1049-1110 turns it into `SdrTextAutoGrowHeightItem` on every horizontal text
  object it builds.** The `SdrObjCustomShape` branch at `:1053-1055`
  (`bAutoGrowHeight = bFitShapeToText`) and the `SdrRectObj` branch at `:1090` both; a *vertical*
  text object gets auto-grow-**width** instead and no height growth at all (`:1077-1080`).
- **The arithmetic is `SdrTextObj::AdjustTextFrameWidthAndHeight`**
  (`svx/source/svdraw/svdotxat.cxx`:44-236) and its custom-shape twin
  (`svx/source/svdraw/svdoashp.cxx`:2249-2421), which agree line for line on the height. The text
  is broken to the frame's **open** width less the two horizontal distances, floored at two units
  (`svdotxat.cxx`:76-77, :110-116); the height is the outliner's **plus one unit of tolerance**;
  that is clamped to the minimum frame height; and only then are the two vertical distances added.
  The growth moves the edge the vertical anchor does not hold — bottom for a top-anchored body,
  top for a bottom-anchored one, half each way for a centred one (`:210-226`).
- **The minimum is where the two branches differ, and it decides whether a shape may *shrink*.**
  `makeSdrTextMinFrameHeightItem` is set only on the `SdrRectObj` branch
  (`svdfppt.cxx`:1116-1119), to `rTextRect.GetHeight() - (nTextTop + nTextBottom)` (`:982`) — so a
  real placeholder can only grow. A custom shape gets no such item, its minimum falls through to
  1, and a box with less text than room shrinks to fit. The branch is taken on one condition
  (`:1041-1055`): the text object's kind is rewritten to Rectangle, and the custom shape therefore
  keeps its own text, **unless** the shape names a placeholder *and* its text kind is not
  `TextInShape`.
- `ImpCalculateTextFrame` (`svdoashp.cxx`:2424-2450) scales the text frame's movement back onto
  the shape's own rectangle by `shapeHeight / textRectHeight`, which is the identity whenever the
  preset's text area is the whole shape.

`PptSlideLayout.Fitted` and `MinimumFrameHeight` are those rules. Nothing in
`Paperless.Presentations` had any answer for auto-grow-height at all before this — `git grep
AutoGrow` over `dotnet/src` found two prose mentions and no code.

## 1.2 The half that made the first cut draw 55.38 pt instead of 103.38

With the growth wired and nothing else, the banner came out **shorter** than the anchor states,
because our reader was measuring one line where the reference measures two. Two further rules,
both read out of the same function and both confirmed against 26.2.4.2's own resolved view:

- **A trailing return leaves an empty paragraph behind and it is a line.**
  `PPTStyleTextPropReader::Init` reads the string under `while (nCharReadCnt < nStringLen)` and
  then appends one more portion, in one more paragraph, whenever the last portion it made belongs
  to the paragraph before the counter — `svdfppt.cxx`:5403-5409, which is exactly the case where
  the text ended on a newline marker. `PptTextBody.Build` had dropped it since the reader was
  written, under the comment *"an artefact of the terminator rather than a paragraph the author
  wrote"*, and that comment was never measured. The banner's whole text is a single `\r` and the
  reference's fodp gives it **two** `<text:p>`.
- **That paragraph is measured in the run BEFORE it.** A `.ppt` states one more character run than
  it has characters, and PowerPoint writes something different in it: the banner's
  `StyleTextPropAtom` states two one-character runs, **40 pt then 8 pt**. The reference never reads
  the second — its loop stops at `nCharReadCnt < nStringLen` and the portion it appends is a copy
  of `aCharPropList.back()` — so both of the banner's lines are 40 pt. 3.647 cm is
  `2 x fround(1411 x 1.2) + 2 x 127` hundredths of a millimetre, and one line of 40 and one of 8
  would be 2.29 cm, which is the anchor and not what the reference draws.
- **A `PageTitle`'s returns are line breaks, not paragraph ends**, and the two go together: keeping
  the trailing paragraph without this would give every title ending in a return a spurious empty
  one. `Init` rewrites every `0x0d` to `0x0b` when the text header names instance 0 and records a
  `PPT_SPEC_NEWLINE` marker for every other instance (`svdfppt.cxx`:5241-5246 for the Unicode
  record, :5261-5266 for the byte one); only the markers split paragraphs. `TSS_Type::PageTitle`
  is 0 and `TSS_Type::Title` — the title of a *title slide* — is 6
  (`include/filter/msfilter/svdfppt.hxx`:157-169), so following the name rather than the number
  would apply it to the wrong one. `PptTextReader.Broken`.

## 1.3 The witness, after

| | anchor | 26.2.4.2 | base | head |
|---|---:|---:|---:|---:|
| `pres_ioc_phuket.ppt` p26 banner, height | 64.88 pt | **103.38** | 64.88 | **103.37** |

Read out of the two PDFs' own content streams (`pdf-ops.py dump --page 26`): the reference's fill
is `(-18.03, 436.65)-(719.97, 540.03)` and ours is now `(-18.00, 436.63)-(720.00, 540.00)`.

## 1.4 Reach

**On the file side**, over the 51 `.ppt`'s live persist directories: **1691 slide shapes in 38
documents state `fFitShapeToText`**, of which **1348 hold a `ClientTextbox`**
(`fitshape-census.txt`, `growth.py`'s `shapes.py`).

**On the reference side**, comparing each shape's stated Escher anchor against the height 26.2.4.2
resolves for it in its own flat-ODP export, matched on (x, y, width) — the three coordinates a
top-anchored grow leaves alone — **101 shapes in 22 documents differ by more than a millimetre, 83
taller and 18 shorter**, 30 of them by more than a centimetre (`growth.py`, `growth.txt`). The
largest are `Fundamentals_Module_1_basics` −6.978 cm, `undp_presentation_revised_17_may` +3.437 and
`RRM-training-syllabus…` +3.072.

*What that census cannot see:* it matches on `svg:x`/`svg:y`, which a **rotated** shape's export
does not state, so it says nothing about one. The rotation compensation at `svdotxat.cxx`:229-236
is therefore left unmodelled and that is unmeasured rather than measured as nil.

## 1.5 What moved

**The gate — no verdict in either direction.** Our half of the `.ppt` column re-rendered at both
binaries and scored against the banked 26.2.4.2 reference with `batch-check.sh`'s own rule (column
9, `glyphs`, within max(2 %, 15)):

| | base | head |
|---|---:|---:|
| documents | 51 | 51 |
| `match` | **49** | **49** |
| page counts moved | — | **0** |
| alphanumeric counts moved | — | **0** |
| sum \|glyph distance\| | 1320 | **1320** |
| renderings whose bytes changed | — | **20** |

`gate-base.tsv`, `gate-head.tsv`.

**Ink, on the 20 that moved**, `|ink|%` summed over the pages of each against the same bank
(`ink.sh`, `ink-base.tsv`, `ink-head.tsv`):

| | base | head |
|---|---:|---:|
| sum \|ink\|% over the 20 | **118.08** | **103.08** |
| MAJOR pages | 37 | **29** |
| improved / worsened / level | — | **11 / 2 / 7** |

The two that worsen are `ws_prod-g-doc-Events-industrymeeting18112004-European-Safety-Strategy-Initiative`
(+0.36) and `0335fab9-79f0-4944-b92c-f223837ca2d8` (+0.01). The largest gains are
`undp_presentation_revised_17_may` 19.76 → 10.70, `pres_ioc_phuket` 7.78 → 4.77,
`pods05` 8.47 → 7.20 and `8.16_AOD_FINAL…` 21.69 → 20.42.

**Confinement.** The diff is three files that only the binary presentation path compiles;
`git grep` over `dotnet/src` finds `PptTextBody`, `PptTextReader` and `PptSlideLayout` named
outside `MsBinary/` three times and all three are doc-comment prose. Measured anyway: every 79th
non-`.ppt` corpus document, **12 of 12 renderings byte-identical** between base and head with
`SOURCE_DATE_EPOCH` pinned.

## 1.6 The two tests that fail at the base

- `PptBlankParagraphTests.ATrailingEmptyParagraphIsALine` — the inverted assertion, with the
  citation and the fodp measurement in its remarks. Its sibling
  `TheTrailingEmptyParagraphTakesThePrecedingRunsSizeAndNotTheOneAfterIt` is new and pins the 40/8
  pair.
- `PptOdfPlacementTests.TheSameDeckPlacesItsShapesIdenticallyThroughTheBinaryAndOdfPaths` — **its
  premise has one measured exception now.** `--convert-to fodp` on the two fixtures gives the
  deck's ellipse `svg:height="2.067cm"` out of the `.ppt` and `"2.012cm"` out of the `.odp`, and
  the reason is in the graphic styles the reference writes beside them: the binary shape's carries
  `draw:auto-grow-height="true"` and the ODF shape's does not. So the two vocabularies do *not*
  describe the same rectangle there, and pinning the binary side to the ODF side pinned it 1.55 pt
  short of what the reference draws. The height of that one shape is now asserted against
  26.2.4.2's own 2.067 cm; every other coordinate of every other shape is unchanged.

---

# 2. O15 — the register's witness is the bullet, and the census is the autofit's row

**Not closed. Characterised, and the entry's own witness figures are corrected.**

## 2.1 The two numbers in the register are two different things

O15 reads *"41 `.ppt`/`.pptx` pages still differ on the dominant drawn size across 33 documents;
the witness bullet is 17.773 pt at y 175.011 against 17.802 at 175.663"*. Those are not the same
defect and the second is not a witness for the first:

- **17.773 / 17.802 is one hundredth of a millimetre.** 17.802 pt is 628 units and 17.773 is 627;
  the model's own quantisation step is one unit. It is the *bullet* of
  `2015-Civil-Rights-Website-training.ppt` page 2, whose text baselines round 85 already agrees
  with 26.2.4.2 on to 0.001 pt, and its seat is `Outliner::ImpCalcBulletFont`
  (`editeng/source/outliner/outliner.cxx`:851-855) — one multiplication and one
  `basegfx::fround`, already transcribed in `SlideAutofit.ScaledMarker`. **Calling it "a 0.16 %
  size difference across a third of the deck corpus" conflates it with the census.** The 0.65 pt
  is that bullet's own vertical placement and is likewise a bullet question.
- The census is not 0.16 % of anything: its differences are **whole points**, 0.99 to 24.92 pt.

## 2.2 The `.ppt` half of the census, re-measured at this round's head

Per-page dominant drawn text size — the size carrying the most alphanumeric characters, round 84's
statistic — for all 1534 pages of the 51 `.ppt`, our two binaries against the banked reference
(`size-sweep.sh`, `sizes-ref.tsv`, `sizes-base.tsv`, `sizes-head.tsv`):

| | base | head |
|---|---:|---:|
| pages whose dominant size differs by more than 0.15 pt | **19** | **19** |
| documents holding one | 15 | 15 |
| total \|size error\| over 1534 pages | 71.10 pt | 71.10 pt |
| fixed / newly wrong | — | **0 / 0** |

**O14's fix moves none of them**, which is worth stating: the bodies on these pages do not end in a
return, so neither the kept paragraph nor the grown shape reaches them.

## 2.3 What those 19 pages are

Two facts split them, and neither is fitted.

**Character counts.** 13 of the 19 draw the *same* number of alphanumeric characters on both sides,
so the statistic is comparing the same text at two sizes. The other 6 do not, so on those it is
comparing different text and the "size difference" is partly or wholly a content difference —
`W3_Case_Study` p10 and `Thailand17` p8 are 500 characters against 92.

**`style:shrink-to-fit`.** Taken out of 26.2.4.2's own flat-ODP export of each document, asking
only whether a style named on that page states it (`shrink-census.txt`): **16 of the 19 pages carry
a shrink-to-fit shape, and the three that do not are three of the six character-count
mismatches** — `W3_Case_Study` p10, `Thailand17` p8 and `Thailand17` p11. **All 13 same-character
pages carry one.**

So the same-character group is an autofit group, established from the reference's own model rather
than from a fit.

**The row.** `autofit-rows.txt` searches, for each page, for an integer stated size and two rows of
`constScaleLevels` (`editeng/source/editeng/impedit3.cxx`:286-300) that give our drawn size and the
reference's. **18 of 19 rows admit one**, and the one that does not is
`Fundamentals_Module_1_basics` p6, which round 85 already established is an embedded chart whose
axis labels the reference does not draw as text at all. The commonest pattern is a stated 20 with
ours at row 0 (1.000) and the reference at row 1 (0.925), four times.

**This fit has two free parameters and is weak on its own** — that is why the shrink-to-fit census
is beside it. What the pair supports is the shape of the claim rather than a particular stated
size: these are autofit placeholders whose search terminated at different rows, not text drawn at a
fractionally different size.

**The sign.** On 12 of the 13 same-character pages **we draw the larger size**, so our search stops
earlier, so **our measured block height is short of the reference's**. The exception is
`gillikin_online_user_mtg_2010` p2, where ours is 31 against 34.

## 2.4 What is left on O15, sharpened

1. **The height our fit measures is short.** That is one direction and one mechanism family, and it
   is round 85's area (`ImpEditEngine::CreateLines`, the appended-line arms, `CalcHeight`'s
   truncation, `scaleYSpacingValue`). A round taking it should instrument `SlideAutofit`'s walk on
   `RESPA_-_Section_8_Webinar.ppt` page 18 — 314 characters on both sides, ours 20.01 and the
   reference 18.99, outline placeholder `pr23` with `style:shrink-to-fit="true"` and a 9.964 cm
   minimum height — and report the block height at each row against the box, which is the only
   measurement that separates "one line too few" from "every line a fraction short".
2. **The bullet is a separate, one-unit seat** at `outliner.cxx`:851-855 with its own vertical
   placement question, and it is *not* what the census measures.
3. **The `.pptx` half was not re-measured this round.** Round 85's figure of 41 pages over both
   columns stands only as of its own base; the `.ppt` half of it is 19 here.

---

# 3. O13 — Escher WordArt, with the census the two earlier ones did not have

**Not closed, and not implemented.** What this round adds is the *reference side* of the reach,
which both `probes/escher-wordart-census` and `probes/fontwork-reach` established from the file
alone.

`--convert-to fodp` over all 51 `.ppt` and a grep for `draw:text-path="true"`:

```
2 shapes, in 2 of the 51 .ppt
  pres_ioc_phuket.ppt                                       draw:type="fontwork-plain-text"
  8.16_AOD_FINAL_Provider_Training_Presentation_9_2009.ppt   draw:type="fontwork-arch-up-curve"
```

That is 26.2.4.2 itself saying which shapes it makes Fontwork and **which preset name it gives
each** — and the two names are exactly the ones `Paperless.Ooxml/DrawingML/Fontwork*` is keyed by,
which is the strongest statement yet that the engine is not the obstacle and the Escher reader is.
It also reproduces the two earlier censuses' shape types (136 `mso_sptTextPlainText` and 144
`mso_sptTextArchUpCurve`) from the other side.

**The residual it costs.** After O14, `pres_ioc_phuket` is 4.77 summed `|ink|%` over 26 pages, and
its worst page is **page 26 at 2.04**, which is 43 % of the whole document's remaining ink and is
the WordArt: the reference clips a gradient to the glyph outlines and we paint the rectangle.

**Left seated.** Two shapes in two of 51 is a small reach, the engine exists, and the missing
piece is a reader: the `gtextUNICODE` (192), `gtextFont` (197), `gtextSize`, `gtextAlign` (194)
and `gtextFStrikethrough` (255) properties, and the shape-type-to-Fontwork-name mapping at
`msdffimp.cxx`:2516-2600. `probes/fontwork-reach` has already read all five of the corpus's shapes'
property 255 and established that **none** states `SameLetterHeights` or `ScaleX`, so the two knobs
that would need measuring are unreachable here. What is *not* established, and is what a round
would need, is what the reference does with the shape's **fill** when the text is a path — the
gradient on page 26 is clipped to the glyphs, and nothing in this tree does that yet.

---

# 4. The suite

Run per project at this round's head; see `tests.log` in the round's work directory.

`Paperless.Fidelity.Tests`: **542 passed / 10 failed of 552, 0 skipped** — the briefed baseline
exactly, and the same ten (PageDrawing x4, TabStop x4, SheetDrawing, JustificationShrink).

# 5. The scripts

| | |
|---|---|
| `fodp-sweep.sh` | 26.2.4.2's flat-ODP of all 51 `.ppt` — the instrument the whole round runs on |
| `shapes.py`, `persist.py` | the live-record-tree walk: every slide shape's type, anchor and `msofbtOPT` properties |
| `growth.py` | stated Escher anchor against the height the reference resolves, matched on (x, y, width) |
| `fitshape-census.txt` | how many live `.ppt` shapes state `fFitShapeToText`, and how many hold text |
| `run-sweep.sh` | our half of the `.ppt` column, scored against the banked 26.2.4.2 reference |
| `ink.sh` | summed `\|ink\|%` per document against the same bank, rendering and deleting one at a time |
| `autofit-rows.txt` | each dominant-size row fitted to a stated size and two `constScaleLevels` rows |
| `shrink-census.txt` | whether each of those pages carries a `style:shrink-to-fit` shape at the reference |
