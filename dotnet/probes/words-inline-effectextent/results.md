# `wp:effectExtent` on an inline drawing

*Measured 2026-09-04 in the container described at the top of `dotnet/CLAUDE.md`. **Both** installed
references were used throughout and they agree on every figure below: the distro's **24.2.7.2** at
`/usr/bin/soffice`, and the TDF tarball's **26.2.4.2** at `/opt/libreoffice26.2` with its 33 duplicate
metric-compatible fonts moved aside. `fc-match "DejaVu Sans"` resolves to DejaVu and `fc-match Calibri`
to Carlito. Paperless at `claude/renderer-comparison-artifact-m1g0wy`.*

## Why this exists

`words/drawingset-001/docx/WordArt_Shapes_Arrows_Catalog1.docx` is a 52-page catalogue of 340 inline
shapes. It rendered to **45 pages against both references' 52**.

Per `probes/words-version-screen/results.md`, the first question is whether a divergence is the gate
binary rather than a defect. **It is not, and this document is unusually clean evidence of that**:
24.2.7.2 and 26.2.4.2 paginate it *identically* — 52 pages each, the same shapes on every page — and
they agree to the twip on all seven fixtures below. Where the two references agree with each other and
we differ, there is no version question to settle.

## The cause

Every one of the 340 drawings is a `wp:inline` with no rotation, and every one carries a
`wp:effectExtent`. Three distinct values, censused off `word/document.xml`:

| `wp:effectExtent` (all four edges) | shapes |
|---|---:|
| `27432` (2.16 pt) | 182 |
| `137160` (10.8 pt) | 99 |
| `91440` (7.2 pt) | 59 |

LibreOffice folds all four edges straight into the object's own margins for this case —
`sw/source/writerfilter/dmapper/GraphicImport.cxx`:1036-1055, guarded by `IMPORT_AS_DETECTED_INLINE`
and `nOOXAngle == 0`, and commented there:

> EffectExtent contains all needed additional space, including fat stroke and shadow. Simple add it to
> the margins.

Those margins are then part of the portion Writer hangs on the line. `SwFlyCntPortion::SetBase`
(`sw/source/core/text/porfly.cxx`:401) sizes the portion from
`SwAsCharAnchoredObjectPosition::GetObjBoundRectInclSpacing()`, and `CalcPosition`
(`sw/source/core/objectpositioning/ascharanchoredobjectposition.cxx`) is where the object rectangle is
enlarged by its spacing:

```cpp
aObjBoundRect.AddTop( - nULSpaceUpper );
aObjBoundRect.AddHeight( nULSpaceLower );
```

So on an unrotated inline drawing the effect extent is simply **room on the line**, and Paperless read
none of it.

## The measurement

`make-fixture.py` builds minimal DOCX fixtures: a 12 pt `TOPLINE`, a paragraph holding one 144 × 50.4 pt
black inline shape, a 12 pt `BOTLINE`. `measure.py` renders each through both references and through
Paperless and reports the gap between the two text lines, plus the drawn box's own band.

Gap between `TOPLINE` and `BOTLINE`, in points:

| fixture | 24.2.7.2 | 26.2.4.2 | Δ vs control | ours, before | ours, after |
|---|---:|---:|---:|---:|---:|
| `ee0` — control | 64.25 | 64.25 | — | 64.20 | 64.20 |
| `ee27432` | 68.55 | 68.55 | **+4.30** | 64.20 | 68.50 |
| `ee91440` | 78.65 | 78.65 | **+14.40** | 64.20 | 78.60 |
| `ee137160` | 85.85 | 85.85 | **+21.60** | 64.20 | 85.80 |
| `ee-t-only` | 75.05 | 75.05 | **+10.80** | 64.20 | 75.00 |
| `ee-b-only` | 75.05 | 75.05 | **+10.80** | 64.20 | 75.00 |
| `dist-only` | 64.25 | 64.25 | **+0.00** | 64.20 | 64.20 |

Four things fall out of it:

1. **Top and bottom are independent and additive.** Each alone adds its own 10.8; together they add
   21.6.
2. **The growth is the stated EMUs rounded to the twip.** 2.16 pt is 43.2 twips, lands at 43, and
   doubles to 4.30 rather than to 4.32. Our reader's shared `Emu` helper already rounds that way, so
   the figure comes out right without a special case.
3. **`dist*` on a `wp:inline` is inert.** A fixture stating `distT="137160" distB="137160"` moves the
   line by **0.00**. That is not the attribute being ignored by accident — `GraphicImport.cxx`:1387-1398
   is four cases of `case NS_ooxml::LN_CT_Inline_distT: m_nTopMargin = 0;`, which never reads
   `nIntValue`. The attribute's *presence* zeroes the margin, discarding its value. A reader that added
   `dist*` to the extent would be 21.6 pt out per drawing on exactly this document, which states both.
4. **The residual 0.05 pt is pre-existing.** It is one twip, it is on the zero-extent control as well,
   and it did not move.

## Where the shape itself lands, and the one thing not reproduced

The box's own band, at 288 dpi, `ee137160` against `ee0` — identical on both references:

| | box top | box height | box left |
|---|---:|---:|---:|
| `ee0` | 85.75 | 50.25 | 72.00 |
| `ee137160` | 96.75 | 50.00 | 82.75 |

So the reference paints the shape at the **outer top plus the top extent**, and at the outer left plus
the left extent — the drawing sits inside the enlarged rectangle rather than filling it.

But a shape carrying a `wps:txbx` splits in two in LibreOffice, and the halves disagree. `tb-ee0` and
`tb-ee137160` are the same fixture with an `INSIDE` run in a centred text box:

| | box top | `INSIDE` y |
|---|---:|---:|
| `tb-ee0` | 85.75 | 104.66 |
| `tb-ee137160` | 96.75 | **104.66** |

**The fill moves by the extent and the text does not.** The text stays centred in the box's *unshifted*
rectangle. That is LibreOffice's draw-shape and TextBox halves failing to sync
(`SwTextBoxHelper::synchronizeGroupTextBoxProperty` is called from `SetBase` and does not carry this
offset), not a rule — and it is visible in the corpus document, where the reference's `WORDART` runs sit
at the same y as ours while the next label sits 21.6 pt lower.

A `PageFrame` is one object and cannot be in two places. It is placed where the reference puts the
**text**, i.e. at the outer top with no extent offset, because that is where the ink the shapes actually
carry ends up. The alternative — offsetting the frame by the extent — would match the reference's fill
rectangle and put every one of the catalogue's 340 text runs 2.16 to 10.8 pt below the reference's.
Recorded here so the next round does not read the un-offset placement as an oversight.

## Result on the corpus document

| | before | after | reference (both) |
|---|---:|---:|---:|
| pages | 45 | **52** | 52 |
| pages whose shape span differs from the reference's | 51 of 52 | **0 of 52** | — |
| words (`pdftotext`) | — | 2492 | 2468 |

Word count is inside the gate's `max(2%, 3)` band, which is 49.4 here against a delta of 24.

## The header case, and the one corpus row this moves the wrong way

A words sweep either side of the change moved **three** of 338 documents and no others:

| document | before | after |
|---|---|---|
| `drawingset-001/.../WordArt_Shapes_Arrows_Catalog1.docx` | `pages` 45/52 | **`match` 52/52** |
| `pagination-002/.../docs-quality-MA.IMS.00001-...manual.docx` | `match`, 12188 words | `match`, 12189 words |
| `done-016/.../TE.CAO.00125 ... OJT Logbook.docx` | `match` 15/15 | **`pages,words` 16/15** |

The third is the one to explain. Its `word/document.xml` holds no drawing at all; its only effect
extent is in `header2.xml` and `header3.xml`, on a 42.75 pt inline logo, and it is
`l="0" t="0" r="9525" b="9525"` — **0.75 pt** on the bottom edge.

`make-header-fixture.py` puts one inline shape in a header and measures where the body's first line
lands. Both references, again identical:

| header fixture | 24.2.7.2 | 26.2.4.2 | Δ | ours, before | ours, after |
|---|---:|---:|---:|---:|---:|
| `hdr-ee0` — control | 78.71 | 78.71 | — | 78.66 | 78.66 |
| `hdr-ee9525-rb` — the logbook's own | 79.46 | 79.46 | **+0.75** | 78.66 | 79.41 |
| `hdr-ee137160-b` | 89.51 | 89.51 | **+10.80** | 78.66 | 89.46 |
| `hdr-ee137160-all` | 100.31 | 100.31 | **+21.60** | 78.66 | 100.26 |

**A header grows by the extent exactly as the body does**, ours grew by nothing before and matches
after — to the same 0.05 pt the zero-extent control already carried, on all four.

So the logbook's row moving is **the change being right, not wrong**: the document sat within 0.75 pt
of a page boundary and two errors were cancelling. And the gate's verdict is the wrong instrument for
deciding it, because this document is itself a version-gap case:

| | pages |
|---|---:|
| 24.2.7.2 — what `batch-check.sh` scores against | 15 |
| **26.2.4.2 — what the tree is calibrated to** | **18** |
| ours, before | 15 |
| ours, after | 16 |

Against the version that decides, **16 is closer than 15**. The remaining three pages are a separate
and larger defect that this change neither caused nor addressed; `words-version-screen/results.md`'s
rule applies — where the two references disagree with each other, the document needs reading rather
than scoring, and it should not be worked from its gate row.

## Reproducing

```sh
export PAPERLESS_CLI=.../tools/Paperless.Cli/bin/Debug/net10.0/linux-x64/Paperless.Cli
python3 make-fixture.py /abs/scratch/fx
python3 measure.py /abs/scratch/fx /abs/scratch/out
```

`measure.py` needs Pillow for the box band and `pdftotext` for the text lines. It renders each fixture
through `/usr/bin/soffice`, `/opt/libreoffice26.2/program/soffice` and `$PAPERLESS_CLI`, so it re-checks
the version question every time rather than trusting this file's claim that there is none.

## A caution about the document's shape census

The round that opened this document was briefed that it holds *"291 VML `v:shape`, 96 carrying a
WordArt `textpath`, 204 with a gradient fill, 48 with a `scene3d`"*. Counted off the part itself, split
by which half of the `mc:AlternateContent` the count falls in:

| | `mc:Choice` (DrawingML — what both renderers use) | `mc:Fallback` (VML — what neither uses) |
|---|---:|---:|
| shapes | 340 `wps:wsp` | 148 `v:shape`, 88 `v:rect`, 57 `v:line` |
| WordArt | 123 `a:prstTxWarp`, 24 of them a real warp | 48 `v:textpath` |
| gradient shape fill | **0** `a:gradFill` | **0** `type="gradient"` |
| 3-D | **0** `a:scene3d` | **0** `o:extrusion` |

**There is no gradient shape fill and no `scene3d` anywhere in the file, in either branch.** The 208
gradients that do exist are `w14:textFill` on runs, which LibreOffice's DOCX import draws none of. Two of
the briefed figures are close to twice the fallback's real counts (96 = 2 x 48, 291 ~ 2 x 148) and two
have no counterpart at all.

Nothing was lost to it here, but it is `render-comparison`'s rule 6 arriving again: *ask what the
document actually contains before believing a theory about it.* A round that had gone looking for the
3-D extrusion path would have found no caller, and one that had gone looking for a shape-gradient defect
would have been reading a text-fill property the reference discards.
