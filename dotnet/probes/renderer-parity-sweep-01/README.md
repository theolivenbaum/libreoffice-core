# Renderer parity sweep 01 — eight-lane fix investigation

Output of one round: the whole `sample-files` corpus rendered by Paperless and by headless
LibreOffice, the 192 documents that did not match catalogued, then split across eight
parallel agents partitioned by **source-file ownership** so their proposed patches land on
disjoint files. The harness that produced the sweep is `dotnet/scripts/renderer-parity/`.

## The environment this was measured in

`dotnet/CLAUDE.md` records that a stored figure is evidence about an environment rather than
about the code, and that stored data almost never says which environment. So, first:

| | |
|---|---|
| Reference binary | **LibreOffice 24.2.7.2** 420(Build:2) — `Producer: LibreOffice 24.2` in every reference PDF |
| Fonts | Carlito, Caladea, Liberation installed; `fc-match Calibri` → Carlito, `fc-match Cambria` → Caladea |
| Paperless | `582c8c671`, built Release, `net10.0/linux-x64` |
| Corpus | `theolivenbaum/sample-files` @ 946 documents |
| Comparison | 150 dpi, first five pages per document; page counts compared in full |

**This is not the binary the tree is developed against.** `dotnet/CLAUDE.md` § "This
container" records the project on **26.2.4.2**. That gap is the single largest finding of the
round and it is why several patches here are marked as retargets or `DO-NOT-APPLY`.

## Read this before acting on anything below

**At least 30 of the 192 documents are version divergences, not defects** — the tree is
correct for 26.2.4.2 and the reference bank is a release behind:

| lane | documents | evidence |
|---|---:|---|
| L5 slides | 24 of 35 | 24.2.7.2 answers a 2.5% autofit grid; four of its nine measured steps cannot be produced by `constScaleLevels` at any rounding. `Lepore.ppt` p2: `SlideAutofit.cs:250-255` records 20.013/20.409 pt measured on 26.2.4.2, **we draw exactly that**, this reference draws 21.005/21.005 |
| L6 sheets | 3 of 24 | our page counts **are** the 26.2 numbers exactly — 449, 88, 201 — for the three documents `CLAUDE.md` already names as having moved |
| L1 text | 2 of 6 | `SlideAutofit.cs` and `SystemFontResolver.cs:490-499` both state in their own remarks that they were measured on 26.2.4.2 |
| L8 drawing | 1 of 14 | the corner→linear gradient branch was removed in round 59 on 26.2 evidence; round 59 and `probes/slides-b-01` both record that they *could not* test 24.2.7.2, so "the reference changed" was inference — **it is now a measurement**, six arms on the installed binary, condition reproduced exactly |
| L2 docx | contested | the `w:trHeight` floor and section-break space-before; 23 of 28 documents carry `trHeight` rows |

Three lanes state they would have patched the wrong way without this being flagged mid-round.

## Apply order and tooling

Patches are per-root-cause and were each verified with `git apply --check` **and**
`patch -p1 --dry-run` from the repository root. Nothing here was built or tested — the
checkout was read-only for the whole round, by design, because eight agents sharing one tree
and four cores is exactly the condition under which `CLAUDE.md` records a rebuild landing
under someone else's measurement and a loaded test run reporting `Failed: 0` after dropping
tests silently.

- **L4**: `field-instruction-nesting` **before** `form-checkbox` (both rewrite the same guard).
- **L7**: the six independent diffs in any order, then `series-name-from-range`, then
  `unnamed-series-legend` (its context does not exist on a pristine tree; its own `#` header
  says so).
- **L8**: RC-1's fill limb must land **before** RC-3's z-order work, or `#062` turns from
  "no background" into "background over everything".
- **L5**: `git apply` only — the diffs carry placeholder `index` lines that `patch(1)` rejects.
- **L6**: `DO-NOT-APPLY-digit-width-carry-24.2.diff` is exactly what its name says.

A caution worth keeping: `git apply --check --recount` **silently repairs drifted `@@` hunk
counts**, so a hand-assembled diff passes it and fails every consumer that does not pass the
flag. One lane reported four clean patches on that basis when three were corrupt. Verify with
both tools, without `--recount`.

## Three more read-but-never-consumed properties

`CLAUDE.md` records this pattern as the cause four times and advises grepping for it on
purpose. Doing so found three more, plus two declared-with-no-consumer record types:

| property | reach | symptom |
|---|---|---|
| `w:w` character scaling | 11 of 271 corpus DOCX | spans 2.6% narrow; `<w:w>` appears nowhere in `dotnet/src` |
| `c15:datalabelsRange` | 5 sheets documents | data labels print the literal `[CELLRANGE]` |
| preset sub-path `fill`/`stroke` | 96 of 320 sub-paths say `fill="none"` | every connector renders as a filled triangle |
| `PPT_PST_TextRulerAtom` (4006) | all 14 `.ppt` in L5 | shape-level indents fall back to the master's |
| `PPT_PST_ExtendedParagraphAtom` (4012) | 3 documents | `(a)(b)(c)` and `I. II. III.` come out as round dots |

## What redirects the roadmap

L1 measured **296,847 aligned glyphs** and found the advance-width divergence explains
**17 of the 107 reflow documents (16%)**, not the bulk — the project's working assumption is
wrong by about a factor of six. 40 of the 107 break every shared line *identically*; their
divergence is vertical. Per-face ratios (Carlito 1.00108, DejaVu Sans 1.00069, Liberation
Sans 1.00009, Liberation Serif 0.99958) confirm and sharpen the seat already in `CLAUDE.md`
and show it survives the version move. No patch: closing it means reproducing FreeType's
hinted advance at LibreOffice's ppem, which is architectural.

## Readings in the published catalogue that these lanes refuted

Corrections to the sweep's own case notes, each established by measurement rather than by a
second look at the same image:

- **#029, #133** — not a page-numbering offset; neither file contains `w:pgNumType`. The
  reference prints a frozen cached value on every page because the `PAGE` field sits in a
  text box in a footer table, where Writer's draw-layer outliner has no page field at all.
  **Ours is correct on both.**
- **#055** — the data-point markers are drawn by both engines, and both draw the same grey
  grid; only the *legend key* lacks its symbol. `TODO.batches.md:16815,16966` already records
  two reviewers making this identical misreading on this identical page.
- **#002** — the black plot area is stated by the file and drawn by the reference too, on
  *its* page 8; our pagination put a different page under the comparison.
- **#033** — the ideographic comma is drawn; both PDFs read `1、` and `A、`.
- **#080** — the document contains no empty paragraphs at all; the gap is a 280-twip auto
  margin never handed back when a list ends at a cell wall.
- **#066** — a print-zoom difference, not missing borders; both engines draw the rules and
  bands. **#180** — the title rows collide in *both* renderings.
- **#049** — the hatch is correct; the block looks bigger because the rows are taller.
- **#176** — same image at identical size on both sides; it is on the wrong page.
- **#113** — the file uses thirteen spaces, not a tab.
- **#175** — recommend re-tagging `lo-broken`: a bar-of-pie that 24.2 cannot draw and we can.

## A second pass, with the reference's version held out

The findings above are entangled with the reference binary: 29 of the 43 corrections
they produced are version divergences, true of 24.2.7.2 and not of the 26.2.4.2 the
tree is built against. A second pass therefore re-checked the catalogue using only
evidence that is a statement about **our own output** -- what our PDF's content stream
holds, in what order it paints, and at what size. None of it can be invalidated by a
reference move, because the reference is not party to it. The instruments are
`audit.py`, `claims.py` and `zorder.py` beside the harness.

**One cause behind five separate readings: paint order.** In `060_Human_Body_Concept_Map`,
`019_` and `013_Project_Timeline`, `050_Visual_Product_Roadmap` and `045_Visual_Product_Roadmap`,
content the metrics report as *missing* is drawn and then painted over. `045`'s
`2021` is shown at stream offset 2473 and the black year box is filled at 4180, on top
of it. Each was established three ways -- an opaque fill covering the text block's
anchor at a later offset, a uniformly flat patch where the glyphs should be, and the
crop beside the reference's -- and the reference hides nothing on any of the same pages.
57 text blocks in all. It is a different fix, in a different place, from missing content.

**A degenerate header scale, not a missing title.** `Hazard Analysis Template.xls`
emits its header at **0.120 pt** where the reference sets **7.887 pt**, in the same
face and colour, correctly centred. The page carries exactly two spans under 1 pt --
the header and the footer -- and every other span is 10 pt, so one scale fault accounts
for both of the losses that reading named.

**Two readings overstated a reflow as a loss.** `RobertQ_Service.doc`'s numbered line
is on our page 3 and `087_Printable_Graph_Paper`'s `Title:`/`Date:` line on our page 2.
Neither is missing; in the second the oversized grid pushing that line onto a page the
reference does not have *is* the page-count divergence.

**Seven readings were upheld** by an independent check, and one class of 25 candidate
refutations dissolved entirely on inspection -- every one was a quote the reading used
as context rather than as the thing it called missing. Both outcomes are recorded in
`pl-readings.json`, which carries the 192 readings without their images.

## Layout

```
paperless-differences.html   the published catalogue, self-contained (11.6 MB)
BRIEF.md              the shared contract every lane worked to
L<n>-<lane>/
  summary.md          root causes, seats, document counts, confidence
  findings.md         evidence, markup quotes, file:line, refuting probes
  patches/*.diff      one per root cause
```
