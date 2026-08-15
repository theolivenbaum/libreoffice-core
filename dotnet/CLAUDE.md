# Paperless — working notes

Paperless is a **pure C# / .NET** library set for content extraction and headless
rendering of the file formats LibreOffice's Writer, Calc and Impress support.

It lives in the `dotnet/` subdirectory of a LibreOffice source checkout. The surrounding
C++ tree is **reference material, not a build dependency** — we read it to learn how the
formats behave, and we run an installed `soffice` to generate ground truth.

## Scope

**In scope.** Word processing (`docx docm dotx dotm doc dot rtf odt ott fodt`),
spreadsheets (`xlsx xlsm xltx xltm xlsb xls xlt ods ots fods csv`), presentations
(`pptx pptm potx potm ppsx ppsm ppt pot pps odp otp fodp`), plus the legacy
OpenOffice.org 1.x forms (`sxw sxc sxi`).

**Out of scope.** Draw, Math and Base. Do not add them. Also: writing/export of any
format (Paperless reads), macro execution (never — Paperless only reports that macros are
*present*), and editing.

## Absolute rules

1. **Never build the C++ tree.** It takes hours and is never needed. Use an installed
   `soffice` for reference output — see the `libreoffice-reference` skill.
2. **Never execute macros.** Macro-enabled formats are read as data. `CanCarryMacros` on
   `FormatInfo` exists so callers can surface the risk; nothing executes.
3. **Rasterise with SkiaSharp, shape with HarfBuzzSharp.** HarfBuzz is what LibreOffice
   shapes with, which is why it was chosen. Font metrics come from a hand-rolled OpenType
   reader in `Paperless.Text` — matching LibreOffice's line heights needs raw `hhea`/`OS/2`
   access and our own precedence rules. Before adding any graphics dependency, read the note
   at the top of `Directory.Packages.props`.

   **"So advance widths agree by construction" used to stand here and is measurably false.**
   Shared shaper or not, the two stacks do not agree: there is a real ~0.1% advance divergence,
   and tab stops are exact to **0.0000 pt**, so the pen is right and the drift accumulates
   *between* them. It underlies 8 of the Fidelity failures.

   **It is not kerning, and the "LibreOffice kerns 19% harder" line that stood here was wrong.**
   That claim came from one line of one document. Measured 2026-08-15 on a probe built to
   separate the two — the same string set three ways, pair-kerned, with `w:kern w:val="0"`, and
   with a space between every letter so no pair can form — across two faces and two sizes:

   | | ref | ours | ours/ref |
   |---|---:|---:|---:|
   | kerning's own contribution, Liberation Serif 12 pt | 16.500 pt | 16.588 pt | **1.0053** |
   | Liberation Serif 24 pt | 32.904 | 33.176 | 1.0083 |
   | Carlito 12 pt | 10.344 | 10.412 | 1.0066 |
   | Carlito 24 pt | 20.616 | 20.825 | 1.0101 |

   **Kerning agrees to better than 1%, and we kern very slightly *harder*, not softer.** The
   divergence is still there with kerning switched off (+0.041% on Liberation Serif, +0.113% on
   Carlito) and with every pair broken by a space, so it lives in the *base advances*.

   What it actually is, per-glyph from the PDFs' own geometry:

   - **It is face-dependent by an order of magnitude.** On the space-separated line the total is
     **+0.011% for Liberation Serif and +0.115% for Carlito**.
   - **Liberation Serif does not accumulate.** Its pen offset stays within 0.06–0.10 pt of the
     reference's across the line — a constant, which is the text origin, not a drift.
   - **Carlito accumulates**, about **+0.0125 pt per glyph** at 12 pt: the pen starts 0.100 pt
     behind the reference's and is 0.063 pt ahead by the fourteenth glyph.
   - Per-glyph *ink* widths differ by at most 0.010 pt and do not accumulate, so the ink is not
     the driver — the advances are.

   So the seat is the advance of a **metric-compatible substitute face**, not the shaper and not
   the kern table. Carlito exists to match Calibri's advances at whatever quantisation the
   consumer uses, which is exactly where a rounding rule would show up; Liberation Serif, whose
   advances are its own, barely moves. Anyone opening this should start by asking what
   quantisation LibreOffice applies to an advance on the 8640 dpi Writer reference device, and
   should **not** re-derive the kerning question — the probe above settles it.

   Treat it as a real open defect with a known seat, not as a rounding artefact — and do not
   re-derive "our pen is off", because the declared-margin probe already refuted that.

   **Section breaks amplify it from a fraction of a line into whole pages, and that is why some
   documents are wildly out.** Worked through on `AWR OPS-AOC 044` (metrics-001, ours 12 pages
   against 15). A narrow table cell whose text wraps one line short makes its row shorter; a
   shorter row lets one extra row onto the page; and the document's **ten `nextPage` section
   breaks** each convert that one-row overshoot into a full blank page, because the section ends
   wherever the overshoot has left it. Measured, full-width rules per page: **page 1 ours 11 to
   the reference's 10, page 2 ours 34 to 33, page 3 ours 29 to 27.** The reference's page 4 holds
   its header, the single row `ACAS II System (with Version 7.1 or later)`, and nothing else.

   Two suspects were refuted on the way, both by probe, and neither should be re-derived:

   - **`w:trHeight` is exact.** Twenty rows at each of 324, 432 and 576 twips: total table height
     agrees with the reference to **0 twips** at all three. `atLeast`, an absent `w:hRule` and
     `exact` all behave. Only *auto* rows (no `w:trHeight` at all) differ, and AWR has none —
     all 141 of its rows declare one.
   - **A `nextPage` section break after a table is honoured**, both as a bare `w:sectPr` alone in
     a `w:pPr` and in AWR's actual shape, where `pStyle`, `tabs`, `spacing`, `ind` and a `w:rPr`
     precede it. 3 pages of 3 in both.

   The probes cost twenty minutes and the first cut of them was wrong in an instructive way: the
   CLI rejects `-o` (it is `--outdir`), so "our" PDF was never written, and a `glob` picked up the
   *reference's* raster instead. That reported the two sides as pixel-identical — a clean,
   confident, entirely fabricated match. **Assert your instrument produced output before
   comparing it**; the guard is one line and it is the difference between a refutation and a
   fiction.
4. **Detect formats by content, never by extension.** Mislabelled files are common, and
   some distinctions (DOCX vs DOCM, which application owns an OLE2 file) cannot be made
   from a name at all. The extension is a tie-breaker hint only.
5. **Be lenient when reading.** Real files violate their own specifications constantly.
   Repair what you can, skip what you cannot, record it as a `Diagnostic`. Reserve
   exceptions for genuinely unreadable input.
6. **Zero build warnings.** `TreatWarningsAsErrors` is on solution-wide. Keep it that way.

## Layout

```
dotnet/
  Directory.Build.props        shared MSBuild settings; read the licensing note
  Directory.Packages.props     central package versions
  Paperless.slnx               solution (the newer XML format; dotnet 10 default)
  research/                    in-depth notes on the LibreOffice implementation
  src/                         the libraries
  tools/Paperless.Cli          the `paperless` command-line tool
  tests/                       unit tests, the test kit, and the fidelity harness
```

### Dependency layering

Arrows point at dependencies. Nothing may point back up.

```
                       Paperless.Core          (zero external dependencies)
                            |
      +---------------+-----+------+---------------+-------------+
      |               |            |               |             |
 Containers        Text          Vector        Rendering       Markup
 (OLE2/OPC/ODF)  (fonts,        (EMF/WMF/SVG)  (Skia, PDF,   (XHTML and
                  shaping,                      SVG)          Markdown out)
                  layout)
      |               |            |
      +-------+-------+------------+
              |
    +---------+----------+-------------+
    |                    |             |
  Ooxml            OpenDocument     MsBinary      (shared per-family infrastructure)
    |                    |             |
    +---------+----------+-------------+
              |
   +----------+-----------+--------------+
   |                      |              |
 WordProcessing      Spreadsheets    Presentations
   |                      |              |
   +----------+-----------+--------------+
              |
          Paperless          (facade: sniff and dispatch)
              |
        Paperless.Cli
```

**`Paperless.Markup` serves all three families, so it cannot live in any of them.** It projects
the shared `ContentNode` tree onto semantic XHTML and then onto Markdown, needs nothing but
`Paperless.Core`, and sits beside the other Core-only libraries rather than inside Core, which
holds the abstractions everything agrees on rather than projections of them.

**`Paperless.Core` has no external dependencies and must stay that way.** It holds the
abstractions everything else agrees on: units, geometry, colour, the format catalogue, the
document model, and the drawing IR. A dependency added here is inherited by every
consumer.

`Core/Charts` is the test of that rule and shows where the line falls. A chart's *model* and its
*layout* — `ChartPlot`, `ChartScale`, `ChartLayout` — are geometry over the abstractions Core
already holds, so they belong here; the readers that turn a `c:chartSpace` or a `chart:chart` into
that model parse XML and stay in `Paperless.Ooxml` and `Paperless.OpenDocument`. Putting the model
one layer up instead is what forced the ODF reader into `Paperless.Presentations`, where a
spreadsheet could not reach it.

`Core/Numbers` came down for the same reason and by the same test, and it is worth stating as a
rule rather than as a second exception. **The question is not "who uses it" but "what does it
depend on".** The number-format engine — parsing `#,##0.00` and rendering a double through it —
began in `Paperless.Spreadsheets` because a cell is what wanted it, and a chart's axis composed in
`Core/Charts` then could not reach it; every tick was written in its shortest round-trip form, which
is right for a whole-number scale and wrong for every currency, percentage and date axis. The move
was safe because the engine is pure computation over a string: its five files import
`System.Globalization` and `System.Text` and nothing else, so Core's zero-dependency rule is intact.
Read it as: **a thing belongs in Core when it depends on nothing above Core, whatever it was written
for.** What did *not* move is the reading — `XlsxStyles`, `OdsCellFormats` and
`OdfNumberFormat` parse markup and stay in their own libraries, the last of them compiling an ODF
`number:*-style` element tree into a format code exactly as `xmloff` does before handing it to one
formatter.

## Key design decisions, and why

**All lengths are EMUs, in a `Length` struct.** 914400 per inch divides evenly by twips
(the DOC/DOCX/RTF unit), 1/100 mm (the ODF and draw-layer unit), and points. Storing a
single exact integer avoids the rounding drift that accumulates when converting through
`double` at every boundary.

**Extraction and rendering are separate paths.** `IDocument` gives you content;
`IPaginatedDocument.Layout()` is a distinct, deferred step. Extraction is the common case
and must not pay for fonts, layout or a rasteriser — it costs a small fraction of
rendering.

**One drawing IR, `IDrawingSink`.** Modelled on LibreOffice's `GDIMetaFile`/`MetaAction`
display list and its `drawinglayer` primitives, because those are the two chokepoints all
LibreOffice output passes through — so anything a supported document can express fits
through them. Coordinates stay resolution-independent; text stays glyph runs rather than
outlines so PDF output can be real searchable text.

**One content tree for all three families.** Callers indexing a mixed corpus want text,
tables and structure without branching on whether a file was a deck or a spreadsheet.

**Shared infrastructure is factored by what the formats actually share**, not by
tidiness: Escher/MS-ODRAW is one library because DOC, XLS and PPT all delegate their
drawings to it, so implementing it once buys shapes in all three.

## Fidelity: the thing that will bite you

### Look at the rendering. Do not chase it through metrics alone.

**This is the standing instruction and it comes before the rest of this section.** The gate is
page count, extractable words within max(2%, 3), and unembedded fonts. **It is blind to most real
defects** — a whole track can be 163 of 163 page-exact while the pages are visibly wrong.

*The word band was described here as "2%+3" for several rounds and that is not the rule.
`batch-check.sh:195` fails a document when `d > b*0.02 && d > 3` — an AND, so the band is
**max(2%, 3)**, not their sum. It matters at the boundary: a 1299-word document tolerates
25.98 words and not 28.98, and one regression found on 2026-08-14 sat at exactly 27.*

```bash
export PAPERLESS_CLI=<the tree you mean to measure>/dotnet/tools/…/Paperless.Cli
python3 .claude/skills/render-comparison/scripts/look.py "<doc>__pptx" --worst   # two PNGs
.claude/skills/page-vision/scripts/pair.sh "<doc>__pptx" --worst --outdir /abs/pairs  # one labelled image
```

It renders the most divergent page both ways. **Open them and read them.**

**Better: do not read them yourself — hand the pair to a fresh subagent.** You cannot un-see
a page, so your second look at one is recall rather than observation, and it will agree with
whatever you already believed. A reviewer that has never seen the document and is forbidden
to grep the repo is the only reader whose agreement is evidence. The `page-vision` skill has
the brief to give it, the pixel-budget arithmetic that decides your dpi, and when to crop.

Three things this changes about how a round is run:

1. **Look before you theorise, and look at documents that PASS.** The failing set is picked
   over. Rank the *passing* documents by `|ink|%` and open the worst — the first three tried
   that way produced three findings, two of them previously unrecorded (a missing custom bullet,
   and a hanging indent we invent where the reference has none).
2. **Looking gives direction and kind; it does not give cause.** *"Every line breaks earlier in
   the reference, so our glyphs are narrower"* is a lead that no ink percentage contains. But an
   image cannot tell a picture bullet from a character bullet in a substituted symbol font.
   **Name the causes the image cannot decide between, then measure.**
3. **Describe before checking the record.** Reading a page blind and only then looking up what is
   known is a control on the reading, and it works: a gradient description produced that way
   matched a diagnosis made a week earlier from source.

The user's own visual reviews remain **primary evidence** — see
`dotnet/probes/user-review-slides-02/review.md`, where 17 of 30 observations turned out to be a
single class no gate column can see. Where a brief has contradicted one of their observations,
the brief has been wrong.

### Rendering errors cascade

One wrong measurement — a font metric, a margin, a line
break — shifts everything after it, so a single bug manufactures hundreds of unrelated-
looking failures across a corpus. Fix cascades before anything else; they are cheap to fix
and expensive to work around.

The three highest-risk areas, in order:

1. **Font resolution and metrics.** A substitution that is not metric-compatible changes
   advance widths, hence line breaks, hence pagination. The machine must have Carlito and
   Caladea installed (`fc-match Calibri` → `Carlito`) or every OOXML comparison is
   meaningless. Line height derivation from hhea vs OS/2 metrics has specific precedence
   rules — see `research/06-rendering.md` section B.
2. **DrawingML theme colour resolution.** Get the `lumMod`/`shade`/`tint` chain wrong and
   every themed shape on every slide is the wrong colour at once.
3. **Vector import (WMF/EMF/EMF+).** Full support is committed and there is no C# prior
   art — roughly fifty EMF+ record types alone. Real `.pptx` and `.docx` files embed these
   constantly, so this is the largest single body of work in the project rather than a
   tail-end detail. Port from LibreOffice's `emfio/`. SVG is the exception: it reuses
   `Svg.SceneGraph`/`Svg.Model`, translated from `ShimSkiaSharp`'s command list into
   `IDrawingSink`.

## Workflow

```bash
cd dotnet
dotnet build Paperless.slnx          # must stay warning-free
dotnet test  Paperless.slnx          # ~1100 tests, a few minutes
```

**Do not add `-r`/`--runtime`.** The SDK rejects it on a solution outright —
`NETSDK1134: Building a solution with a specific RuntimeIdentifier is not supported` — and it
is unnecessary: `Directory.Build.props` already pins every test and tool project to the host
RID, computed from the OS and process architecture. Passing `-r linux-x64` to an individual
project is accepted and does nothing, which is the intended state. Read the comment beside the
setting before changing it; it records two traps that both look exactly like the property
having no effect.

That pin is not a tidiness measure. Without it the build resolves SkiaSharp's and
HarfBuzzSharp's native binaries for **twenty-one** runtime identifiers and copies all of them
into every output directory — 687 MB per test project, of which the host can run one. A clean
whole-solution build costs **463 MB with the pin and 6095 MB without it**, which is the
difference between fitting in a container's disk allowance and exhausting it.

### Running less than everything

A full run rebuilds nothing if the tree is already built, so the cost is the tests themselves —
and **essentially all of it is `Paperless.Fidelity.Tests`**, which shells out to `soffice` once
per document. It is also the *only* project that does: the other seven reach LibreOffice not at
all, so they need none of the setup below and finish in seconds.

| Project | Needs `soffice` | Rough cost |
|---|---|---|
| `Paperless.Fidelity.Tests` | yes, 23 files | minutes |
| everything else | no | seconds |

Those are wall-clock figures on an already-built tree; most of each is the SDK's up-to-date
check rather than the tests, which is why naming one project is worth doing but naming one
*test* rarely is.

So when iterating, name the project — and reach for the filter only inside the slow one:

```bash
dotnet test tests/Paperless.Text.Tests/Paperless.Text.Tests.csproj                # ~10 s
dotnet test tests/Paperless.WordProcessing.Tests/Paperless.WordProcessing.Tests.csproj   # ~15 s
dotnet test tests/Paperless.Fidelity.Tests/Paperless.Fidelity.Tests.csproj \
    --filter "FullyQualifiedName~TableComparisonTests"                            # ~45 s
```

Run every project before committing anyway. The failure this project cares about most is the
cascade — one wrong measurement moving every line after it — and it surfaces in projects you had
no reason to think you had touched.

### Under load a test run can also report failures that are not there

The truncation above is one half of it. The other half was seen twice on 2026-08-14, in
`Paperless.Vector.Tests`, on a binary nothing had touched: one run reported **1 failed of 295**
and nine subsequent runs reported 0; another agent, hours later, saw **16 failed of 295**
followed by four clean runs. Neither captured a failing name.

So a run under load can drop tests *and* invent failures. Both look like signal.

The habit that survives both: **a failure you cannot reproduce on a second run is not a
failure yet.** Re-run the project alone before acting on it, and say in the write-up that you
did — an agent that reports "16 failed, then 0 on four re-runs, nothing here touches Vector"
has given a far more useful account than one that reports either number on its own.

### Never pipe `batch-check.sh` into `head` or `tail`

It runs its documents in parallel workers writing to stdout. Closing the pipe early sends
SIGPIPE to a worker, which dies without a word — and the run **silently writes 155 of 156
rows** while the summary line still looks entirely plausible. There is no error and no warning.

Redirect to a file and read the file:

```sh
batch-check.sh "$CORPUS" 'sheets/done-*' out 3 > sweep.log 2>&1
grep '^TOTAL' sweep.log
```

The `TOTAL` line is computed by the script from what it actually processed, so it is the
column to check — a run that lost a worker reports a smaller total, not a wrong verdict. But
that is only a safety net if you read it; a truncated per-document TSV looks fine on its own.

### A sweep and a rebuild must never overlap

`batch-check.sh` reads `PAPERLESS_CLI` per document, so a rebuild that lands mid-sweep swaps
the binary under it and the run silently mixes two trees. The output looks entirely normal —
there is no error, no warning, and the totals are plausible.

It has bitten once: an agent building the "unfixed" binary to check that its new tests fail
started that build while its own `done-*` sweep was still running, and had to kill and re-run
the sweep. It noticed. The next one might not.

Two habits: sequence them explicitly rather than backgrounding a sweep and then working, and
when a fix must be merged while a sweep is in flight, **merge the source but do not rebuild**
until the sweep finishes — the built binary is what the sweep is measuring, and it is unaffected
by a source-only merge.

### A truncated run reports success

**Check the count, not just the colour.** Under heavy load the test host can die part-way and
still print `Passed! - Failed: 0`, having silently dropped the tests it never reached. Measured
on one commit with several parallel builds running: the fidelity project reported **470 passed**
on one run and **353 passed** on the next, both `Failed: 0`, against **471 discovered**
(`dotnet test --list-tests`). Nothing had changed between them.

This is worse than a failure, because it looks like a pass. Two habits make it safe:

- Compare the passed count against the previous known-good count for that project. A drop with
  zero failures is a truncated run, not a fixed test.
- `dotnet test Paperless.slnx` is the most likely to truncate and the least likely to say so —
  it has also been OOM-killed outright. Run the projects individually and total them yourself.

### Before trusting a green run

`Paperless.Fidelity.Tests` needs an installed LibreOffice and **skips with a reason when it is
missing**, so a bare `dotnet test` on a fresh container reports a green run while that project
covers nothing at all. A fresh container has none of what it needs. Install it, then confirm
with `check-env.sh` below:

```bash
apt-get install -y --no-install-recommends \
    libreoffice-writer libreoffice-calc libreoffice-impress \
    fonts-crosextra-carlito fonts-crosextra-caladea fonts-liberation \
    poppler-utils
```

`libreoffice-core` alone gives an `soffice` that starts, reports a version and then fails on
every document — which is why `LibreOfficeRunner.IsAvailable` decides by converting a probe file
rather than by finding the binary. The fonts are not optional either: without Carlito and
Caladea every OOXML comparison measures a substituted face and is meaningless. A correct run
reports **0 skipped**; any other number means part of the suite covered nothing.

Comparing against LibreOffice — use the skills, they encode hard-won details:

| Skill | Use for |
|---|---|
| `libreoffice-reference` | Generating reference PDFs, page PNGs and text with headless `soffice` |
| `render-comparison` | Comparing renderings and diagnosing *why* they differ |
| `page-vision` | Actually looking at a page — resolution, cropping, and getting it read by someone uncontaminated |
| `extraction-comparison` | Comparing extracted text; also the right first step for a visual bug |
| `paperless-corpus` | Building and curating test documents |

### The sample corpus

`theolivenbaum/sample-files` holds 534 real-world documents — collected from the open web
and kept as found, mislabelled extensions and malformed markup included — ordered by what
their LibreOffice rendering demands of a renderer and cut into batches of at most ten:

**The corpus is no longer batched by complexity — it is grouped by what is wrong.** As of
2026-08-14, with 459 of 534 passing, the old ordering had stopped earning its keep: the 75
remaining failures were scattered across sixty batches, so a session taking "the next batch"
got nine documents it could learn nothing from and one it could.

```
<family>/done-NNN/      459 documents that pass the gate
<family>/<kind>-NNN/     75 that do not, grouped by what is wrong with them

  ceiling 20   pagination 20   metrics 10   extra 9
  missing 7    table 6         chart 2      unstable 1
```

Every failing document was classified by **looking at its rendered page** — six reviewers,
one fixed vocabulary, each pairing the two renderings with `page-vision` and measuring rather
than eyeballing. The kinds are defined in `.claude/skills/corpus-batches/` and the regrouping
is reproducible with `regroup-batches.py`.

Documents keep their complexity score and are ordered by it **within** each group, so
`pagination-001` is still the cheapest ten pagination failures.

**`MANIFEST.tsv` is the undo.** Batch membership is the directory layout — `batch-check.sh`
globs directories — so every stored figure naming a batch path stopped resolving when this
landed, and `dotnet/probes/` is full of archival scripts that name them. The manifest keeps
`source` untouched and gained `previous_batch`, `status` and `kind`; any old path can be
followed forward through it.

What the grouping surfaced immediately, and the old layout hid: three `ABCD-*` documents share
one bug, the two Holdover Tables share one bug and one 13-page gap, three documents share
rotated cell text drawn upright a glyph per line, two share a background raster emitted after
the text, and two share a first-page header repeated on every page. Every one of those was
split across different batches before.

Some of those extensions are **upper-case on disk** — four files are `.DOC`, `.XLS`, `.XLSX`.
A case-sensitive glob quietly counts 530 instead of 534, which is the same mistake as
trusting an extension at all, in miniature. Match case-insensitively or, better, do not
filter by extension.

Per-family tracks, because a single global ordering front-loads the easy end almost
entirely with word processing and leaves the other two families idle for forty batches.
Three tracks let three workers run in parallel and never touch the same file.

**Sheets is not deferred.** It was originally scheduled last on the grounds that a
spreadsheet's value is in its cells rather than its pagination; that was retired once the
track turned out to hold the corpus's largest systematic defects — one workbook paginating
1170 pages against 220 — so deferring it was hiding them rather than deprioritising them.
All three tracks now advance in parallel and never wait for one another.

```sh
.claude/skills/corpus-batches/scripts/batch-check.sh /c/sandbox/workdir/sample-files 'words/batch-003' out 3
.claude/skills/corpus-batches/scripts/batch-check.sh /c/sandbox/workdir/sample-files 'words/batch-00[1-2]' out 3
```

**Both of those runs are the workflow, and the second is not optional.** Make the current
batch match, then re-prove every earlier batch in the track. This is the cascade rule
again in corpus form: a fix aimed at batch *n* routinely breaks batch *n−4* in a way that
looks nothing like the change, and advancing on the first condition alone is how a corpus
rots from the front.

**Set `SOURCE_DATE_EPOCH` when comparing two renderings byte for byte.** Reach is measured by
rendering a track twice and diffing, and a document that prints the date — a spreadsheet header
holding `&D` or `&T` — draws different ink on a different day. Measured on the sheets track:
rendering all 171 twice in succession is byte-identical once `/CreationDate` is masked, and
rendering them a day apart moves **17 of 171**. `paperless render` honours the
reproducible-builds convention (seconds since the Unix epoch, read as UTC) in both the PDF's
`/CreationDate` and the header fields, so with it set two runs are byte-equal with nothing masked
at all. Leave it unset for ordinary rendering; a printout's date is meant to be today's.

**`TODO.raster-ceiling.md` lists 37 pages the word gate cannot win.** LibreOffice rasterises
an embedded object on those, so its PDF holds a picture where ours holds real searchable text —
ours is the better output and `wc -w` scores it as failure. An embedded metafile is the
commonest cause and not the only one: 16 of the 37 are on documents holding none. Check that
list before working any word-count failure; several agents have each re-derived it the hard way.

The `corpus-batches` skill holds the rest — why the ordering and the batch size are what
they are, what parity does and does not prove, and what a dispatch brief for a parallel
agent has to contain. `TODO.batches.md` is the scoreboard.

Verify the environment before trusting any comparison:

```bash
.claude/skills/libreoffice-reference/scripts/check-env.sh
```

## This container — read before reproducing any stored figure

The project has moved containers, and two of the three things a measurement depends on are
not what the stored figures were taken against. Neither is a defect in the tree.

**Roots.** The repository is at `/c/sandbox/workdir/libreoffice-core` and the corpus at
`/c/sandbox/workdir/sample-files`. The live scripts and documents have been rewritten to
these. The archival probe scripts under `dotnet/probes/` and `dotnet/research/probes/` have
**not** been, deliberately — they are the record of what a given round actually ran, and
rewriting them would falsify it. A `/workspace/sample-files` symlink points at the corpus so
they remain runnable as written.

**The reference binary is `26.2.4.2`, not the `24.2.7.2` every stored figure was measured
against.** The base image is Ubuntu 26.04 and its archives offer no earlier LibreOffice.
This is not a nuance to note and move past — ground truth genuinely moved, measured over the
whole corpus by re-rendering the reference half of the gate at both versions:

| track | reference page count changed | total \|Δ\| pages | reference words beyond the 2% band |
|---|---:|---:|---:|
| words | **47 of 200** | 453 | large |
| slides | **0 of 163** | 0 | 160 of 163 moved at all |
| sheets | **16 of 171** | 305 | large |
| total | **63 of 534** | 758 | **210 of 534** |

So **the 465/534 scoreboard is not reproducible here**, and the §7 rule "if your baseline
sweep does not reproduce the briefed numbers, stop" would fire on almost every round. It has
to be re-baselined against 26.2.4.2 before any verdict movement means anything. Slides is the
exception worth knowing: a deck's page count is its slide count, so check 1 is structurally
stable there and slide-count claims survive the version change intact.

**The table above is confounded, and the correction is the more useful fact.** Two things
differed from the environment the stored figures were taken in, not one: the LibreOffice
version *and* a missing `fonts-dejavu-core`. Attributing all of that movement to the version
bump was wrong. Holding LibreOffice constant at 26.2.4.2 and varying only the font set moves
**53 of 534 page counts and 426 pages** on its own — the same order as the whole figure above,
on overlapping documents (`AC-150-5370-10G` appears in both). See `MISSING_PACKAGES.md` in the
repository root for the per-track split and the reasoning that establishes DejaVu *was* present
originally: `SheetColumnDigitsTests` pins its metrics against values read from 24.2.7.2's own
output, so the repository's test suite is a statement about the environment.

The lesson generalises past this container. **The gate's inputs include the font set**, and
nothing in the harness declares it. Before trusting any figure, check `fc-match "DejaVu Sans"`
resolves to DejaVu rather than a fallback — `fc-match` never fails, it always returns
*something*, which is why the gap survived a whole pass unnoticed.

**But do not use `fc-match` as ground truth for what LibreOffice resolves.** Measured over the
296 families the corpus names, it agrees with the installed 26.2.4.2 on 288 — and **all eight
disagreements are `FcNameParse`**, which reads `-` in a family name as a size and `,` as a family
separator. LibreOffice does no such parsing, so it and `fc-match` genuinely answer different
questions for any punctuated name. `fc-match "Century Schoolbook"` is safe; `fc-match
"Foo-Bar, Inc Sans"` is not. When the answer matters, render a one-cell probe through `soffice`
and read the face out of the PDF.

**Do not use `fc-match` as ground truth for what LibreOffice resolves.** Measured over the 296
families the corpus names, it agrees with the installed 26.2.4.2 on 288 — and **all eight
disagreements are `FcNameParse`**, which reads `-` in a family name as a size and `,` as a family
separator. LibreOffice does no such parsing, so the two genuinely answer different questions for
any punctuated name. `fc-match "Century Schoolbook"` is safe; `fc-match "Foo-Bar, Inc Sans"` is
not. When the answer matters, render a one-cell probe through `soffice` and read the face out of
the PDF.

**And check it at the start of every session, because the install does not survive.**
`fonts-dejavu-core` was installed and documented as fixed, and a later session found
`fc-match "DejaVu Sans"` answering `wqy-zenhei.ttc` again — the package was simply absent
from `dpkg -l` in the new container. Everything else the reference needs (Carlito, Caladea,
Liberation, OpenSymbol, IPAGothic, WenQuanYi) *was* still installed, so nothing looks wrong
until you check the one font that decides 267 of 534 reference renderings.

Reinstalling has a trap of its own worth writing down, because it reads as the package having
been withdrawn:

```sh
apt-get install -y --no-install-recommends fonts-dejavu-core
# E: Package 'fonts-dejavu-core' has no installation candidate
apt-get update && apt-get install -y --no-install-recommends fonts-dejavu-core   # works
```

The container's package index is stale, not the archive. `apt-get update` first, always, and
re-check `fc-match` afterwards rather than trusting the installer's exit code.

Canonical reference renderings for this environment, all 534 documents at 26.2.4.2 with the
correct font set, are kept at `/c/sandbox/workdir/refpdfs-26.2.4.2-fonts/` with a
`ref-baseline-all.tsv` beside them. Reuse them rather than re-rendering the reference.

Individual claims calibrated to 24.2.7.2 behaviour — "the document-level `w:widowControl` is
inert", the 720 dpi device round trip, the reference's own table-only-header import defect —
are now claims about a superseded binary and each needs one re-check before it is relied on.
*The table-only-header one has now had its re-check (2026-08-15, round `words-ug-01`): the
mechanism survives the version move unchanged on 26.2.4.2, and re-measuring the **cost** of not
reproducing it is what reversed the standing decision — it was a page count as well as words. See
`SectionInheritedHeaderTests`, which asserted the opposite until that round.*
The largest single movers were `sectors-defense-and-aerospace.xlsx` (reference 227 → 449
pages), `CIS_Debian_Linux_8_Benchmark_v1.0.0.xls` (109 → 88), `A_320.doc` (150 → 118) and
`grants-2005.xls` (220 → 201).

### Stored evidence decays silently, and the prose knows it while the data does not

Three cases surfaced in a single day: a `words-after.tsv` carrying numbers from a sweep that
had overlapped a rebuild; a "39/39 exact" CJK fit measured on a face whose line gap is zero,
so it could not have discriminated between the hypotheses it was cited for; and a
`printer-metric-advance.py` whose "exact on all 96" is 16 of 96 against this container's
binary. None of the three announced itself. Each stayed quotable.

Censused over all 410 stored figures under `probes/` and `research/probes/`, the pattern is
sharp:

| | records the environment it was measured in |
|---|---|
| prose write-ups | **122 of 154** |
| stored TSVs | **3 of 256** |

**The write-up says what it was measured against; the data does not — and the data is what a
later round greps, pastes into a brief, and acts on.** A TSV is a grid of numbers with no way
to tell its reader that the reference bank behind it no longer exists. 215 of the 410 predate
the 2026-08-13 container move, and **35 of those are still cited by live guidance**.

`probes/PROVENANCE.tsv` is the index — path, date added, era, which LibreOffice, whether
DejaVu was present, and which live documents cite it. Regenerate it after adding probe output:

```sh
python3 dotnet/probes/provenance-index.py          # rewrite the index
python3 dotnet/probes/provenance-index.py --check  # exit 1 if stale
```

It deliberately does **not** stamp the probe files themselves. They are the record of what a
round actually ran; rewriting them would falsify it, and a `#` header would break every
consumer that reads line 1 as the column names. A sidecar records provenance without touching
the record.

The rule that follows, and it is the one to carry: **a stored figure is evidence about an
environment, not about the code.** Before quoting one, check which era produced it. What
survives a reference change is the *mechanism* a round identified; what does not survive is
every number attached to it.

**The package feed was firewalled and is now open; the build works.** `dotnet restore`
succeeds for all 26 projects and `dotnet build -v q -nologo` gives **0 warnings, 0 errors**.
Recorded because the diagnosis cost a round and the shape of it recurs:

- The host that must be allowed is **`api.nuget.org`**, named literally. The policy matches
  hosts **exactly**, so an apex allow does not cover subdomains — and `nuget.org` was already
  allowed here throughout (it answers 301 from IIS) while `api.nuget.org` answered the proxy's
  403. A request phrased as "allow nuget.org" therefore changes nothing and looks like the
  allow having failed. A wildcard (`*.nuget.org`) does cover it.
- `api.nuget.org` alone is sufficient: it serves the service index, `RegistrationsBaseUrl`,
  the `PackageBaseAddress` flat-container download endpoint, and the `VulnerabilityInfo` feed.
  `www` / `globalcdn` / `azuresearch-*` are the gallery UI, the legacy V2 redirect target and
  `SearchQueryService` — a V3 restore under central package management touches none of them.
- There is **no offline route**, established rather than assumed: the SDK's five bundled packs
  are all first-party, there is no fallback folder or cache anywhere on the filesystem, and
  every upstream GitHub release for these packages ships **zero** attached assets. Even a
  dependency-free project cannot restore offline, because the bundled apphost is
  `ubuntu.26.04-x64` while `Directory.Build.props` correctly computes the portable
  `linux-x64` — so `Microsoft.NETCore.App.Host.linux-x64` is always one download.
- `NuGetAudit` is on by default and `TreatWarningsAsErrors` promotes an unreachable
  vulnerability feed to a hard error (NU1900). Any scheme that leaves that feed unreachable
  also needs `<NuGetAudit>false</NuGetAudit>`.
- `HOME=/tmp` here, so the package cache lands in `/tmp/.nuget/packages` on the 20 GB overlay,
  not on the large host mount. It counts against the disk budget.

`github.com` and `archive.ubuntu.com` are reachable; the LibreOffice download hosts are not,
which is why the reference binary cannot be pinned back to 24.2.7.2.

**`git status` shows 56 files modified that are not modified.** This mount reports a
symlink's size as 0, so git reads every symlink in the tree as having been emptied — the
`sysui` and `android` icon PNGs, `.vsconfig`, 56 in all. They are all mode `120000` in HEAD
and all still correct on disk; `readlink` returns the right target for every one.

The consequence is the dangerous part. **`git add -A` or `git add .` in this container
replaces 56 symlinks with empty files** and commits that as real work — a corruption of the
LibreOffice tree that no test would catch, because nothing under `dotnet/` reads them. Stage
explicit paths, always. `git status --short | grep -v '\.png$'` is not sufficient as a filter
either: `.vsconfig` is in the list and is a symlink too. The reliable test is the mode:

```sh
git ls-files -s <paths-you-are-about-to-stage> | awk '$1=="120000"'   # must print nothing
```

The second consequence is milder and shows up at the end of a round rather than the start:
**`git worktree remove` refuses**, with `contains modified or untracked files`, because it sees
those same 56 phantom modifications. Check that the branch is genuinely merged and that nothing
else is dirty, and then `--force` is correct rather than a shortcut:

```sh
git -C <primary> merge-base --is-ancestor <worktree-head> HEAD   # must succeed
git -C <worktree> status --short | grep -vE '\.(png|ico)$' | grep -v '\.vsconfig'   # must be empty
git -C <primary> worktree remove --force <worktree>
```

### Three worktree branches hold commits that must NOT be merged

Triaged 2026-08-15. `wt-paint-b` (2 commits), `wt-slides-chart` (4) and `wt-slides-text` (5) each
carry work that never reached this branch, and merging any of them **reverts newer work**. They
are survivors of the round that crashed, and the fixes in them were subsequently re-derived and
landed by another route — better, in at least the autofit case.

The tell is in the diff direction. Against this branch they show large *deletions*:
`ChartLayout.cs` −251, `SlideAutofit.cs` −213, `SlideText.cs` −207, `PptxTextBody.cs` −155. That
is not work to recover, it is an older file. Confirmed by content rather than by inference —
`percentStacked` is already in `Charts/ChartPlot.cs` and `DrawingML/DrawingChartPlot.cs`, the
twelve `constScaleLevels` autofit rows are already in `SlideAutofit.cs:32-116` with the 0.250
floor, and `a:noFill` suppression is already at `DrawingChartPlot.cs:405,1583`.

**Keep the branches; do not merge them, and do not delete them without reading this.** The one
thing they hold that this branch does not is *test coverage*: `wt-slides-chart` has
`ChartStackingTests.cs` (288 lines) and `DrawingChartStackingTests.cs` (252). They do not compile
here — they are written against a `ChartPlot.CategoryTotal` / `ChartPlot.CategoriesReversed` API
this branch never adopted. Most of what they assert is covered under other names
(`APercentStackIsDrawnZeroToOneHundredInTenSteps`,
`EveryPercentStackedColumnIsTheSameHeightAndSplitByRatio`,
`AReversedAxisRunsFromTheMaximumDownwards`), but four assertions appear to have no counterpart:
a reversed *category* axis putting the first category at the top, moving its labels with the
bars, and swapping series within a category; and a series with `a:noFill` still holding its place
in a stack. Adapting those four is worth a round; merging the branch to get them is not.

The general point, which is the reason this is written down at all: **a branch that is behind is
indistinguishable from a branch that is ahead until you look at which side the deletions are
on.** `git log --oneline main..branch` shows commits either way and says nothing about it.

**`git stash` is repository-global, and this clone has many worktrees.** Stashing a file in
one worktree to build a "before" binary, and popping it later, popped *another branch's* stash
into the wrong worktree — the stash stack is one per repository, not one per worktree. Nothing
was lost that time (both entries were recovered with `git stash store` and the sweeps either
side re-checked), but the failure is silent and lands in a tree an agent is mid-measurement in.
**Copy the file aside instead.** `cp file file.before` costs nothing and cannot reach another
branch.

**And restore it with `cp`, never with `mv` — this is where that advice has actually failed.**
`mv file.before file` keeps the *original* modification time, so the restored source looks older
than the compiled assembly and **MSBuild's up-to-date check skips the project**. The build then
reports `0 Warning(s), 0 Error(s)` in fourteen seconds and the binary still carries the
experiment. Measured on 2026-08-15: a one-twip throwaway patch to `LineSpacing.cs` survived
*three* subsequent builds whose whole purpose was to be free of it, and silently contaminated a
`words/done-*` sweep, a 200-document reach measurement and two `--page` comparisons before a
contradiction — a line height one twip *above* a value the source cannot produce — gave it away.

There is no output that distinguishes "nothing needed rebuilding" from "the thing you just changed
was skipped", so the habit has to be unconditional:

```sh
cp file.before file && touch file      # or: git checkout -- file && touch file
```

`rm -rf src/<project>/{obj,bin}` before the rebuild is the certain version and costs one project's
compile. Worth it whenever a measurement is about to be trusted, and the check that catches it
afterwards is cheap: render one document and compare it byte for byte against the run you are
claiming to have reproduced.

**The reference half of the gate can be banked without a build.** `batch-check.sh` refuses to
start without a CLI, which is right for a round and wrong when the reference binary is what
changed. `ref-baseline.sh` is the reference-only half, with `batch-check.sh`'s conventions
column for column, so the two are comparable:

```sh
.claude/skills/corpus-batches/scripts/ref-baseline.sh \
  /c/sandbox/workdir/sample-files 'words/batch-0*' /abs/out 6
```

It is resumable, records the binary version in its header, and was validated against an
independent known answer before use — reference page counts against `ppt/slides/slideN.xml`
counts taken from the zip, 4 of 4 exact.

## Research notes

Written from a deep read of the C++ implementation. Consult the relevant one *before*
implementing an area — they contain exact record layouts, algorithms and file:line
citations, and will save far more time than they cost to read.

| File | Covers |
|---|---|
| `research/01-formats-and-detection.md` | The filter/type registry; the detection algorithm with concrete signatures |
| `research/02-writer.md` | Writer's document model, layout engine, and the DOCX/DOC/RTF/ODT importers |
| `research/03-calc.md` | Calc's cell storage, formula engine, importers, and print pagination |
| `research/04-impress.md` | The shape model, custom-shape geometry, PPTX/PPT/ODP importers, slide rendering |
| `research/05-infrastructure.md` | OLE2/CFB byte layouts, ZIP/OPC/ODF packaging, encryption, EditEngine, item sets, encodings |
| `research/06-rendering.md` | VCL output, fonts and metrics, drawinglayer primitives, PDF export, headless entry points |

## Conventions

- British spelling in identifiers and prose (`Colour`, `normalise`) — consistent with the
  existing code.
- XML doc comments on public API. Say *why*, not just what; the what is usually evident
  from the signature.
- Avoid the name `Path` for new types: it collides with `System.IO.Path` under implicit
  usings. The geometry type is `GraphicsPath`.
- Prefer `readonly record struct` for small value types, `sealed record` for immutable
  reference types.
- `Span`/`ReadOnlySpan` for binary parsing hot paths. `AllowUnsafeBlocks` is on.
