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

   **Advance widths do agree, and the "~0.1% advance divergence" that stood here for four
   rounds does not exist.** It was an artefact of the instrument, and every figure that was
   ever quoted for it — "ours is exactly `hmtx × size / upem` and the reference's is not",
   "differs per glyph by up to 0.3%", "no quantisation grid fits", "the reference must be
   grid-fitting the outline at LibreOffice's ppem" — came from reading the reference's
   advances out of a LibreOffice PDF's *glyph positioning*, whether through
   `pdftotext -bbox` or from the `TJ` integers. **That channel is quantised to whole
   thousandths of an em, and one thousandth of an em is 0.36% of a Liberation Serif `i` and
   0.17% of a Liberation Mono digit.** The instrument's resolution was several times the
   defect it was used to measure.

   Two facts settle it, and `probes/advance-ppem/` holds both:

   - **LibreOffice shapes unhinted, exactly as we do.**
     `LogicalFontInstance::InitHbFont` (`vcl/source/font/LogicalFontInstance.cxx`:94-103)
     builds its HarfBuzz font with `hb_font_set_scale(font, upem, upem)` and
     `hb_ot_font_set_funcs` — HarfBuzz's own OpenType functions, reading `hmtx`. There is no
     FreeType font-funcs object in the advance path and no hinting anywhere in it.
   - **Its PDF writer truncates every declared width.** `registerGlyph` records
     `XUnits(upem, width)`, and `XUnits` is `(n * 1000) / nUPEM`
     (`vcl/inc/fontsubset.hxx`:29) — integer division, so every declared width is
     `floor(hmtx × 1000 / upem)` by construction and up to a thousandth of an em *short*;
     `drawHorizontalGlyphs` (`vcl/source/pdf/pdfwriter_impl.cxx`:5814) corrects a gap only
     when `trunc(declared − actual·1000/ppem + 0.5)` is non-zero, which a systematic
     sub-unit deficit never makes it. Measured over every glyph of every subset in three
     corpus documents, **every declared width is `floor(hmtx × 1000 / upem)` in both
     binaries, mean deficit 0.48–0.65 thousandths of an em per glyph.** A pen reconstructed
     inside one text object therefore falls behind the pen the layout intended by about half
     a thousandth of an em per glyph and resets at every `Td`. That is the whole of "0.1%,
     and it accumulates *between* the stops".

   Measured through a channel that is **not** quantised — differencing two right-aligned
   lines, so the `Td` the writer states is `margin − width(line)` and every fixed term
   cancels — over **5 faces × 6 units × up to 11 sizes = 314 cases**: ours agrees with
   26.2.4.2 to a **worst case of 0.0077%** and with 24.2.7.2 to **0.0107%**, which is the
   instrument's own floor. The units include `Hamburgefonstiv`, a six-kern-pair phrase, a prose sentence and
   ` o`, so shaping, kerning, ligatures and the space glyph are all inside that agreement.

   **The control is in the suite already.** `TabStopComparisonTests` runs one assertion at one
   tolerance over two documents: `tabbed.docx` passes and `list-label-overrun.docx` fails.
   In the first, every stretch after a tab is its own text object with its own `Td`, so the
   reference *states* each position and all three renderings agree at every word. In the
   second the whole line is one text object and every position after the first has to be
   reconstructed. Same test, same tolerance — what changes is whether the number was stated
   or reconstructed.

   **26.2.4.2 is not "further from the design metric" than 24.2.7.2.** On the failing
   documents the `Td` origins of every portion are *identical* between the two binaries: the
   layout did not move. What moved is the `TJ` arrays — 24.2.7.2 emits an adjustment at a
   handful of positions and 26.2.4.2 at nearly every one, because 26.2.4.2 encodes the
   kerning the layout applied and 24.2.7.2 dropped some of it, which made its lines wider and
   cancelled part of the truncation deficit.

   So there is nothing to reproduce in `Paperless.Text`, and **the fidelity failures in this
   family are left failing on purpose**: eight of them compare a position N glyphs deep
   inside one reference text object, where the channel's resolution is `N × 0.5/1000 em ×
   size` — 0.55 pt on `paginated.docx` line 1, against a 0.5 pt tolerance. Only writing our
   own PDF with LibreOffice's truncated integer widths would close them, and that is making
   our output worse to make a test greener. Each of those tests now carries the correction in
   its own remarks.

   **`SheetTextComparisonTests` was never this family and is now closed.** Its failure is a
   Calc *indent*: an OOXML `indent` level is three spaces of the workbook's default font
   (`sc/source/filter/oox/stylesbuffer.cxx`:1263) and one space is
   `xFont->getCharWidth(' ')` (`unitconverter.cxx`:139), which is
   `OutputDevice::GetTextWidth` cast to `sal_Int16` — a **whole number of twips**. It rounds;
   we truncated. Measured over the six default font sizes at which Liberation Sans' 5.5566
   twips per point separate the two rules, 26.2.4.2 rounds at six of six and 24.2.7.2 at four
   of six. `SheetDrawingComparisonTests` is not this family either — its own remark
   classifies it as 26.2.4.2 clamping a full-cell anchor offset, on 34 probe renderings.

   **`SlideChartFaceComparisonTests`' 5.839 pt digit was a separate defect, and it was ours.** A
   chart's text is not laid out by Writer, Calc or Impress: `chart2`'s view builds it as plain text
   shapes on the `VirtualDevice` that `DrawModelWrapper` creates from
   `Application::GetDefaultDevice()` with `MapUnit::Map100thMM`
   (`chart2/source/view/main/DrawModelWrapper.cxx`:88-99), and **that device is 96 dpi**
   (`SvpSalGraphics::GetResolution`, `vcl/headless/svpgdi.cxx`:44). An `OutputDevice` instantiates a
   font at a whole number of device pixels, so a 10 pt label is laid out at **13** rather than 13.34
   and every advance in it is **2.5% narrow**; at 11 pt the device sets 15 for 14.67 and they are
   2.3% *wide*. The scale is `round(size × 96/72) / (size × 96/72)` and it is
   `MetricGrid.Chart.PixelEmScale`, which `SheetBandText.ChartShape` has applied to a workbook's
   charts since round 62. `SlideChart` and `FrameChart` did not, and now do.

   Measured over **twelve sizes × two binaries**, with no free parameter: the drawn advance follows
   `round(px96)/px96` on the sign at 12 of 12 for **both** 24.2.7.2 and 26.2.4.2, magnitude within
   0.003; the same string in an ordinary slide text box on the same slide of the same deck stays
   within 0.7% of the design metric at every one of them; four chart frame widths give identical
   output, so no metafile scale is involved. `probes/chart-text-metafile/`.

   **The same device decides every *vertical* metric, and that half was on the sheets track
   alone.** A font instantiated at a whole number of device pixels answers a rounded ascent, a
   rounded descent and therefore a rounded line height, so a chart's baseline pitch is
   `round(asc/upem × hpx) + round(desc/upem × hpx)` device pixels of 0.75 pt, with `hpx =
   round(size × 96/72)` and **no external leading** — `IsAddExtLeading()` is false in EditEngine
   and a chart's label is an EditEngine text. Round 60 put `SheetBandText` on it; `SlideChart` and
   `FrameChart` are now on it too, through `SlideTextBody.Device` and `ChartFace`'s own grid.
   Measured over **three faces × twelve sizes × two binaries × a deck and a Writer document**:
   144 of 144 baseline-to-baseline distances within **0.019 pt**, against as much as **1.208 pt**
   for exact scaling. The ascent is measured separately, as a value-axis label's offset from its
   own tick read off the PDF's path operators: `ascent − height/2` is right on **72 of 72 with no
   free parameter** against 24.2.7.2, and on 72 of 72 against 26.2.4.2 once one constant is
   allowed. `probes/chart-vertical/`.

   **Two things about the vertical that the earlier rounds' figures did not cover.** (1) *The rule
   is not a property of 26.2.4.2.* Round 60 measured it on one workbook against 26.2.4.2 alone;
   **24.2.7.2 follows the identical vertical rule**, and the two binaries' pitches agree to
   0.002 pt at every one of twelve sizes. (2) *24.2.7.2's whole-pixel snapping is horizontal
   only.* Neither binary snaps a baseline; what separates them vertically is a single constant of
   **one hundredth of a millimetre** — 0.028 pt — in where 26.2.4.2 places a label's block, and
   that is the whole of it.

   **The height and the ascent have to move together, and a single-line label hides it.** A label
   is drawn at `blockCentre − height/2 + ascent`, so an error shared by the two cancels out of it:
   that is why round 60's sheets defect stayed invisible until a label wrapped or was measured for
   a fit. It is also a trap for a test — on Liberation Mono at 10 pt `chart2`'s device and
   Impress's differ by 0.014 pt in that quantity and on Liberation Serif by 0.043, so a deck-level
   assertion has to say which deck it is on and why.

   **Nothing about how much text fits moved with it**, which was the risk worth measuring: on the
   78 chart-bearing slides and words documents, 61 rendered differently and **0** changed a page
   or slide count, the number of text runs drawn, or the number of turned ones — so no label
   wrapped, none was thinned away, and no axis reached for `ChartAxisLabels.Resolve`'s 45 degree
   rotation. 77 of the 78 match 26.2.4.2's page count, before and after.

   **Three things this file used to say about it are wrong and should not be re-derived.**
   (1) *"The seat is in the metafile a chart is drawn into and replayed from"* — there is no
   metafile. `ViewContactOfSdrOle2Obj` takes a chart's content as **primitives** straight from the
   chart's own draw page (`ChartHelper::tryToGetChartContentAsPrimitive2DSequence`), and the
   quantisation happens when `chart2` measures, long before any playback. (2) *"`tdf#168002` and
   `GetSubpixelPositioning` are the leads"* — they explain only the difference **between** the two
   binaries: 24.2.7.2 additionally snaps each glyph position to a whole 96 dpi pixel (its gaps are 7
   or 8 px where 26.2.4.2's are a flat 7.79), and removing that made 26.2.4.2 *better*. (3) *"the
   chart's `Tm` origins move between the two binaries where a Writer document's do not"* — the
   chart's move by 0.17 to 0.31 pt, and a Writer document's move too, on **4 of 39** runs of
   `tabbed.docx`, by one twip each. The magnitudes are the claim.

   **And 24.2.7.2 never "sat on the design metric".** Its mean advance follows the same 96 dpi rule
   at every size. The 6.010 the fidelity test used to read there is 24.2.7.2 right-aligning the value
   axis' labels on their **design** widths while drawing them from the device's narrower array — it
   reserves 18.012 pt for `100` and draws 17.249. 26.2.4.2 uses one width for both, and so do we.

   **And a chart's line height was never "the face's own ascent plus descent plus leading".**
   `FrameChart` and `SlideChart.Measurer.Body` both said so — 1.1499 em for Liberation Sans — and
   both halves are wrong: the leading is not in it, and the metrics go through a device, so it is
   not a fixed fraction of the em at all. 1.1254 em at 10 pt and 1.1596 at 11.

   **Reach: 168 of 947 corpus documents carry a chart** — sheets 90, slides 68, words 10 — and 131
   of them are at 1% or worse, 111 at 2% or worse, at the sizes their charts declare. The 90 sheets
   documents were already right; the 78 slides and words ones are what this moved.

   **A single wrapped line short is amplified by section breaks into whole pages, and that is
   why some documents are wildly out.** Worked through on `AWR OPS-AOC 044` (metrics-001, then
   ours 12 pages against 15). *This used to be filed as the advance divergence arriving at
   corpus scale, and that attribution is withdrawn with the rest of it.*

   **`AWR OPS-AOC 044` no longer shows it, and the "12 against 15" has been quoted in briefs
   after it stopped being true.** At `260611dae` it is **15 pages against the reference's 15**,
   before and after the fonts round that re-measured it (`probes/fonts-r65`). The mechanism
   below — a short wrap amplified by a `nextPage` break — is real and general and is kept for
   that reason; the document that demonstrated it is not a witness for it any more, so
   re-measure before working from it.

   **What that document still shows is a `w:rFonts` family-code question, not a fallback one.**
   It draws 103 `U+2610` in runs naming `MS Gothic`, which its own font table files `modern` —
   a code `FontTable::lcl_sprm` maps to nothing
   (`sw/source/writerfilter/dmapper/FontTable.cxx`:127-141, only `roman` and `swiss` are
   mapped), so no `PROP_CHAR_FONT_FAMILY` is inserted and the class is whatever an ancestor set.
   Its face-set distance from 26.2.4.2 is **2 both before and after** the fonts round, and a
   probe reproducing the shape (`probes/fonts-r65/gen-awr.py`) answers FreeSerif on 26.2 exactly
   as the tree does — so the disagreement is about which layer supplies the class in the real
   file, and that is where whoever takes it next should start.

   A narrow table cell whose text wraps one line short makes its row shorter; a
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

**SmartArt is the third instance, and the first that moved *sideways* rather than down.** Ten
files implementing DrawingML diagrams — the parts, the baked `dsp:spTree`, and a layout-atom
evaluator — sat in `Paperless.Presentations` and were unreachable from a word-processing document,
which drew an empty frame where the reference drew the diagram. Nine of the ten imported nothing
above `Paperless.Ooxml`; the tenth needed two lookups from the package (*resolve a relationship id
stated on a part*, *load a part by name*) and those became two delegates. They belong in
`Paperless.Ooxml/DrawingML` and **not** in Core, by the second half of the same test: they parse
markup and emit an element tree, so they are readers, and readers of OOXML that serve more than one
family live one layer above Core rather than in it. Reach: 18 corpus documents carry a diagram —
slides 15, words 3, sheets **0** — and all 38 of their data parts have a usable baked drawing, so
the evaluator is not what the corpus needs. See `probes/words-diagram-01/results.md`.

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

   **`Paperless.Text` now ships those faces itself, as a floor rather than an override.**
   28 files under `Fonts/Bundled/`, copied to a `fonts` folder beside the assembly and
   searched **last**, so an installed face always wins and a machine missing one renders
   correctly instead of substituting silently. `BundledFonts` is the switch:
   `PAPERLESS_BUNDLED_FONTS=0` turns them off, `=prefer` puts them first.

   **The default direction is measured, not chosen, and the obvious choice is the wrong
   one.** Against LibreOffice 26.2.4.2 from the TDF tarball — the build these very files
   came from — `Paperless.Fidelity.Tests` over 552 comparisons:

   | | failed |
   |---|---:|
   | bundle as a fallback (installed wins) | **36** |
   | bundle preferred over installed | **68** |

   Preferring them is twice as bad, because **LibreOffice does not read its own bundled
   fonts either** — it resolves through fontconfig, which sees `/usr/share/fonts`. Its
   copies are its own floor for systems without them, exactly as ours are.

   Two things fell out of establishing that, and both are worth keeping:

   - **It comes down to one family.** Comparing the shipped files against Ubuntu 24.04's by
     their own `hmtx`, Carlito and Liberation Sans are *metrically identical* — bundling
     them changes no advance at all — while **Caladea genuinely differs**: `A` is 599 units
     installed against 623 shipped, `o` 480 against 531, `M` 888 against 815.
   - **Ship only the faces the distro packages ship.** The first cut bundled TDF's fuller
     DejaVu, and `fonts-dejavu-core` carries **no Sans or Serif italic at all**. LibreOffice
     therefore synthesises a lean for those, and a bundled real italic makes us draw
     something the reference does not — it broke ten synthetic-oblique tests across three
     projects and emptied three more into green skips. Trimming to the 28 faces the packages
     actually carry returned the suite to its baseline exactly.

   A test whose premise is "this face is not installed" is one that a later change to what
   ships can silently empty. `SyntheticObliqueResolutionTests.ARomanOnlyFamilyHasItsSlantDrawn`
   builds its own directory for that reason.
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
one bug, the two Holdover Tables were carried as sharing one bug and one 13-page gap (**they do
not — see the correction below**), three documents share
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

**Roots have moved again, and `/c/sandbox` does not exist here.** As of 2026-09-06 the primary
checkout is `/home/user/libreoffice-core`, agent worktrees are `/home/user/wt-*`, and the corpus is
`/home/user/sample-files`. The `grep -r` doubling described below does **not** occur on that corpus:
`find` and `git ls-files` both count 963. Check `pwd` before pasting any stored path.

**Roots.** The repository is at `/c/sandbox/workdir/libreoffice-core` and the corpus at
`/c/sandbox/workdir/sample-files`. The live scripts and documents have been rewritten to
these. The archival probe scripts under `dotnet/probes/` and `dotnet/research/probes/` have
**not** been, deliberately — they are the record of what a given round actually ran, and
rewriting them would falsify it. A `/workspace/sample-files` symlink points at the corpus so
they remain runnable as written.

**That is no longer this container, and the direction has reversed — check before you measure.**
As of 2026-09-04, `/usr/bin/soffice` is **24.2.7.2** and 26.2.4.2 is present only as the TDF tarball
under `/opt/libreoffice26.2`. So `batch-check.sh`, `ref-baseline.sh` and every ink figure taken here
are measured against **24.2**, while the tree is calibrated to **26.2** — the paragraph below, and
several stored figures, assume the opposite. One line settles which you have:

```sh
soffice --version                       # LibreOffice 24.2.7.2 420(Build:2)
/opt/libreoffice26.2/program/soffice --version
```

**A divergence from the gate is therefore not automatically a defect**, and one round has already
been spent finding that out. The seven `Printable_Graph_Paper_Template` documents sat at 32-to-51
first-page ink on a row pitch a fraction of a point out; we match 26.2.4.2's pitch **to the twip**
and 24.2.7.2 has no `MinRowHeightInclBorder` at all. Before working a difference, render the
document through both binaries — `probes/words-row-height/results.md` is the worked example, and
this is checkable without rendering anything:

```sh
strings /usr/lib/libreoffice/program/libswlo.so | grep -c MinRowHeightInclBorder   # 0
strings /opt/libreoffice26.2/program/libswlo.so | grep -c MinRowHeightInclBorder   # 1
```

Read `Installing a specific LibreOffice` below before treating the tarball as the target: it is a
fourth reference rather than the distro-packaged 26.2 the tree is really calibrated to, because it
bundles its own fonts. Move the 33 duplicates aside first and it is close enough to screen with:

```sh
D=/opt/libreoffice26.2/share/fonts/truetype
mkdir -p $D/.duplicates-aside && mv $D/{Carlito,Caladea,Liberation,DejaVu}*.ttf $D/.duplicates-aside/
# And the Latin Noto, which duplicates nothing installed and is the worse trap of the two:
mkdir -p $D/.noto-aside && mv $D/Noto{Sans,Serif}-*.ttf $D/.noto-aside/
```

**Move the Latin Noto aside too, and leave the script-specific Noto in place.** The line above
was written for the metric-compatible duplicates, and it is not sufficient. The tarball also
ships `NotoSans-*` and `NotoSerif-*`, which duplicate *nothing* on this system, so they are not
caught by that `mv` and they become fontconfig's answer for every unfiled family — which makes
`ink26` unscoreable on any document naming a font the system lacks. Two agents lost hours to it
independently in one session before anyone read the faces out of the PDFs. Keep
`NotoSansArabic`, `NotoSerifHebrew` and the rest: they carry script coverage the system genuinely
lacks (`fc-list :lang=ar` here answers DejaVu Sans Mono), and removing them changes what a CJK or
Arabic document can draw at all.

With only the eight Latin faces aside, the tarball answers **DejaVu**, like a distro build.

### The two references differ in a *rule*, not only in their fonts, and it decides font fallback

**24.2.7.2 lets the family name decide; 26.2.4.2 lets a declared family class beat it.**
`FontConfigManager::Substitute` appends `"serif"` as a second `FC_FAMILY` for `FAMILY_ROMAN` and
`"sans"` for `FAMILY_SWISS` (`vcl/unx/generic/font/fontconfig.cxx`:1075-1088). **That switch does
not exist in 24.2.** Measured on three hand-built DOCX naming one uninstalled family, differing
only in what `word/fontTable.xml` declares, with the Latin Noto aside:

| `w:family` | 24.2.7.2 | 26.2.4.2 | Paperless |
|---|---|---|---|
| *(no font table)* | DejaVu Sans | **DejaVu Serif** | DejaVu Serif |
| `roman` | DejaVu Sans | **DejaVu Serif** | DejaVu Serif |
| `swiss` | DejaVu Sans | **DejaVu Sans** | DejaVu Sans |

24.2 answers the bare `fc-match` of the name in all three; 26.2 honours the declaration, and a
DOCX with no font table still inherits Writer's roman default. Over 24 families the tree matches
clean 26.2 **24 of 24** and 24.2 only 7 of 24.

**So a font-family divergence measured against `/usr/bin/soffice` is very probably not a defect.**
A `pdffonts` census over the 947 gate renders showed **119 documents** drawing a different family
from the reference, 85 of them `DejaVu Sans` against our `DejaVu Serif` — and every one of those
85 is this version rule, not a bug. A round was dispatched to "fix" it and would have broken
correct behaviour; the agent challenged the brief from the source and was right. Read the faces
out of both PDFs, with the Latin Noto aside, before believing any of it.

**Measured over the whole gate, not a sample: 49 of the 87 mismatches — 56% — are this.** Round 66
screened every one against 26.2.4.2 before touching anything
(`probes/mismatch-classify-01/`): 49 the version gap, 7 the two references disagreeing with each
other, 7 the raster ceiling, 6 a cell holding `TODAY()`, and **no group of three sharing a cause**
in what is left. The largest movers are outright: `sectors-defense-and-aerospace.xlsx` is 449 pages
ours, 227 on 24.2 and **449 on 26.2**; `A_320.doc` 118/150/**118**; `CIS_Debian…xls` 88/109/**88**.
The two Holdover Tables, carried above as sharing a 13-page gap, are **page-exact against 26.2**
(155/155 and 167/167) and within 0.4% on glyphs.

**And the trap inside that: 21 of the 49 are sheets where 24.2 draws far more text than we do, with
our output carrying truncated fragments** — which reads exactly like "we clip cell text we should
spill" and is not. `essd-16-3433-2024-t02.xlsx` is the witness: our pages 2–4 are empty, 24.2's
carry an overflowing column's continuation, and **26.2 gives 2349 glyphs against our 2346**. A round
that takes that family on against the gate's own reference will implement 24.2 behaviour and
regress the tree.

**Screen a document against 26.2 before working it.** `probes/words-version-screen/screen.py` does
the whole queue and `bucket.py` one catalogued cause. Rescoring the worst thirty words documents
that way put **eleven of them** — the whole top of the table — in the version gap rather than in the
tree, three of them under 3.5 ink against the target; and it cleared two of the nine documents
catalogued under *overlap and clipping*, including the one carried as rendering blank, which matches
26.2 at **0.00**. It cuts the other way too: two of those nine paginate differently under 26.2 than
under both 24.2 and us, so where the references disagree with each other the document needs reading
rather than scoring.

### The same rule decides *glyph* fallback, and `fc-match` on a bare charset does not

**`FontConfigManager::Substitute` is one function and the glyph-fallback hook goes through it too.**
`FcGlyphFallbackSubstitution::FindFontSubstitute` calls it with the missing characters as an
`FC_CHARSET` (`vcl/unx/generic/font/fontsubst.cxx`:173-184), so the declared class appends `serif` or
`sans` to *that* pattern as well — and since `FC_CHARSET` outranks `FC_FAMILY`, the answer is **the
first face on that one generic's `<prefer>` list that covers the character**. Measured on 26.2.4.2
over six declared classes and thirteen characters (`probes/fonts-r64/gen-generic.py`, one DOCX per
cell, faces read out of the PDFs):

| character | roman / modern / script / decorative / undeclared | swiss |
|---|---|---|
| `U+2713` ✓ | **FreeSerif** (69-unifont's serif list) | **DejaVu Sans** (60-latin's sans-serif list) |
| `U+2011` non-breaking hyphen | **DejaVu Serif** | **DejaVu Sans** |
| `U+4E00` 一 | WenQuanYi Zen Hei | WenQuanYi Zen Hei |
| `U+2714` ✔, `U+2611` ☑, `U+263A` ☺ | Noto Color Emoji | Noto Color Emoji |

Only `swiss` differs, which is the same switch as for family substitution — and *undeclared* behaves
as roman because Writer's own pool default is roman, so a word-processing document lands on the serif
list unless its font table says otherwise.

**That table is the *western* item's, and two of its rows are not.** `U+4E00` is an East Asian
character and selects the CJK item whatever the class; the emoji rows are a language rule. Read the
next section before using it: a character's own script decides which of Writer's three font items
answers for it, and only the western one reads the declared class at all.

**So `fc-match ":charset=XXXX"` is not the question LibreOffice asks.** Asked bare it answers DejaVu
Sans for every one of the characters above, because `49-sansserif.conf` appends `sans-serif` to a
pattern that named no generic — which is the *swiss* row, not the common one. `fc-match
"Calibri,serif:charset=2713"` answers FreeSerif and `fc-match "Calibri,sans:charset=2713"` answers
DejaVu Sans; the bare form answers the second. A round has already been misled by this: the previous
fonts round's probe read `fc-match ":charset=25cf"` and concluded DejaVu Sans, which was right only
because its witness was a `.pptx`.

**The emoji row is a language rule, not a family one.** `getExemplarLangTagForCodePoint` answers
`und-zsye` for a character with the Unicode `Emoji` property (`fontconfig.cxx`:1026-1029) and
fontconfig scores `PRI_LANG` above `PRI_FAMILY_WEAK`, so an emoji code point goes to the emoji face
whatever generic the pattern named — `U+2714` answers Noto Color Emoji under all six classes although
FreeSerif holds it and is on the serif list. `U+2713`, which the property excludes, does not.

### A colour bitmap glyph is a Type 3 font, and `GlyphOutlines` was never in that path

**The emoji row above is only useful if the face can be painted, and until round 65 it could not
be.** Noto Color Emoji carries `CBDT`/`CBLC` and **neither `glyf` nor `CFF `**, so the PDF writer
embedded a `glyf`-less TrueType program, announced it as one, and drew a blank at exactly the right
advance; every gate the corpus harness has passed while it did. **A blank is worse for a reader
than a wrong glyph**, so this is closed rather than recorded.

**It was not `GlyphOutlines`.** That reader is `glyf`-only and is reached *only* by Fontwork
(`Ooxml/DrawingML/FontworkFitting.cs`); text never touches it. The two seats were
`PdfFontCatalogue`, which mis-described the program, and `SkiaDrawingSink.DrawOutlines`, which asks
*Skia's* `SKFont.GetGlyphPath` and gets an empty path. Widening `GlyphOutlines` would have fixed
nothing.

**What LibreOffice writes, measured on 26.2.4.2's own PDF of a `U+2714` probe:** a
`/Subtype/Type3` font with `/FontMatrix[0.001 0 0 0.001 0 0]`, a `/CharProcs` keyed by glyph, an
`/Encoding /Differences` naming them, a `/ToUnicode`, **no font program**, and one char proc per
glyph reading `1245.1171875 0 d0` then `q … cm /Im12 Do Q` over a `/DeviceRGB` image with an
`/SMask`. `pdffonts` says *Type 3, Custom, emb yes, uni yes* and `pdftotext` gives the character
back. Ours now writes that shape, the deflated colour plane byte-for-byte the same length, and the
page's content stream is untouched — **the text layer is what keeps a colour glyph searchable, so
the bitmap goes inside the font rather than beside the text as a picture.**

**The placement is `round(pixels × upem / ppem)` per side, and rounding rather than truncation is
measured, not assumed.** Noto is 2048 upem with one 109 ppem strike of 136 × 128 pixel glyphs at
`bearingY 101`; the reference's three constants are `2555`, `2405` and `−507` design units, and
2555.30 rounds down while 2404.99 rounds up. See `probes/colour-r65/results.md`.

**`COLR`/`CPAL` is deferred and `sbix`/`SVG ` with it, because the census says so.** Of 150
installed faces, 120 are `glyf`, 29 are `CFF `, **one** is `CBDT`/`CBLC` and **none** carries
`COLR`, `sbix` or `SVG `. Nothing on this machine can render a page to measure a layer composition
against. In its absence such a face is reported unpaintable and the fallback search moves to the
next candidate, which draws a monochrome glyph — visible, and wrong, rather than absent.

**That fall-through is the floor and it sits in `SystemFontResolver.Covers`**, which all three
fallback stages go through. It changes no preference: a candidate is only ever skipped, never
promoted, so the advance follows whichever face actually draws. It fires nowhere on this machine.
**`CFF ` counts as paintable on purpose** — the rasteriser draws it and only the PDF writer declines
to embed the program (`PdfFontCatalogue.IsCompactFontFormat`), so rejecting it here would move a
line break to work around a writer.

**The corpus reach is two documents and it predates the round that reported it.** Of 947,
**two** draw a character landing on the colour face — `019_Free_Blood_Sugar_Chart…xlsx` (six
distinct emoji) and `jobs-bulletin-51-22-december-2025.xlsx` (one) — and both appear in
`probes/fonts-r64/faces-before.tsv` as well as `faces-after.tsv`, so `fonts-r64` created the
*probe*'s blank and not the corpus's. **One** document reaches a `CFF ` face (Unifont, on
`vvsummit2022-Research-Roadmap…pptx`), where the reference draws `NotoSansArmenian-Regular` — a
resolution difference, not a painting one.

**The residual this did not close is the script-specific font item, and the section below settles
it.** The reading recorded here — that a CTL or CJK run takes an item with *its own family and its
own class* — was wrong: the class never reaches those two items at all. What decides them is the
item's own language.

### The script-specific font item decides it, and the deciding half is the *language*

**Writer keeps three character-font items and selects one per script item of the text**, and only the
western one behaves the way the two sections above describe. `SwScriptInfo::WhichFont` maps
`i18n::ScriptType` onto `SwFontScript` (`sw/source/core/text/porlay.cxx`:879-901); a **weak**
character — every symbol, dingbat, arrow and punctuation mark — takes the script of the text around
it, or the one `w:rFonts/@w:hint` names, and nothing else can move it
(`i18nutil/source/utility/scriptchangescanner.cxx`:246-268, `DomainMapper.cxx`:969-988).

**The class never reaches the other two items at all.** `LN_CT_Fonts_ascii` inserts
`PROP_CHAR_FONT_FAMILY`; `LN_CT_Fonts_eastAsia` and `LN_CT_Fonts_cs` insert the *name* and nothing
else (`sw/source/writerfilter/dmapper/DomainMapper.cxx`:436-508). So the CJK and CTL items keep the
pool default's family type, and `OutputDevice::GetDefaultFont` sets `FAMILY_SYSTEM` for `CJK_TEXT`
and `CTL_TEXT` — *"don't care, but don't use font subst config later…"*
(`vcl/source/outdev/font.cxx`) — which appends no generic to the pattern at all.

**And each item carries its own language, which outranks the generic's preference list.**
`SwDoc::SwDoc` resolves the document's three default languages through
`MsLangId::resolveSystemLanguageByScriptType` (`sw/source/core/doc/docnew.cxx`:383-398), which
answers `LANGUAGE_ENGLISH_US`, **`LANGUAGE_CHINESE_SIMPLIFIED`** and **`LANGUAGE_HINDI`**
(`i18nlangtag/source/isolang/mslangid.cxx`:135-165). `Substitute` puts it in the pattern as
`FC_LANG` (`fontconfig.cxx`:1092, 1118-1119) and `fcmatch.c` scores `PRI_LANG` above
`PRI_FAMILY_WEAK`. `mapToFontConfigLangTag` then reduces the tag to what `FcGetLangs()` knows:
`hi-IN` is not a member and `hi` is, `en-US` is not and `en` is, `zh-CN` **is**.

Measured on 26.2.4.2, one DOCX per cell, faces read out of the PDFs
(`probes/fonts-r65/gen-scriptitem.py`, 25 cells, **25/25** reproduced by the tree):

| run | 26.2.4.2 draws | the pattern that explains it |
|---|---|---|
| western, `U+2610` | FreeSerif (DejaVu Sans under `swiss`) | `Calibri,serif:lang=en:charset=2610` |
| `w:hint="eastAsia"`, `U+2610` or `U+2713` | **Unifont** | `Calibri:lang=zh-cn:charset=…` |
| complex, `U+05D0`, or `w:hint="cs"` `U+2610` | **FreeSans** | `Calibri:lang=hi:charset=…` |
| complex, `U+0E01` or `U+0627` | **FreeSerif** | `Calibri:lang=hi:charset=…` |
| asian, `U+4E00` | WenQuanYi Zen Hei | `…:lang=zh-cn:charset=4e00` |

**The declared class moves none of the CJK or CTL rows** — `roman`, `swiss` and no font table at all
give the same answer — which is what the paragraph this replaces got wrong when it said those items
have "their own family and their own class". They have their own *language*, and the class is simply
absent.

**A document that states `w:lang` overrides those defaults, and Word writes one into `docDefaults`
for nearly every file.** `<w:lang w:val="en-US" w:eastAsia="en-US" w:bidi="ar-SA"/>` is what both
`150-5370-10H.docx` and `AWR OPS-AOC 044…docx` carry, which is why their `w:hint="eastAsia"` runs
answer **DejaVu Sans** and not the Unifont a document stating no language gets. Round 64 measured the
answer and inferred the wrong cause from it.

**A face's language support cannot be read from the configuration this tree parses** — fontconfig
derives it from an orthography per language compiled into the library — so `FontLanguages` models it
as coverage of one exemplar character of the language's script. Checked against `fc-list :lang=X`
over 25 languages: **24 agree face for face**, and the twenty-fifth (Gurmukhi) names two fewer.
Two exemplars are deliberately not the first letter of their alphabet, because the first letter does
not discriminate: an accented Greek vowel excludes a face carrying only the mathematical Greek, and a
simplified-only Chinese ideograph excludes a Japanese face carrying only the shared ones.

**And the pattern carries a *set* of characters, not one.** `ImplGlyphFallbackLayout` gathers every
unmapped code unit of a layout into one `OUString`; every code point of it goes into one `FcCharSet`;
and `FcCompareCharSet` scores by *how many of the set the candidate is missing*, at `PRI_CHARSET` —
fontconfig's highest priority, above both the family list and the language. The chosen face is then
subtracted from the set and the next fallback level asks with the remainder
(`vcl/source/outdev/font.cxx`, `fontconfig.cxx`:1229-1245). So a face further down the family list
wins when it covers more of the run.

**The generic must travel with the *run*, not be recorded against the face it resolved to.** Round 64
recorded it against the face, first writer winning — and in a word-processing document the first
request to reach a face is the paragraph mark's, so a run on any other item silently took the
paragraph's. It hid the swiss row as well as the script items: `west-swiss-2713` answered FreeSerif
until the item was passed in, because the mark's own Calibri had already claimed Carlito for `serif`.

**That is also why round 64's stored 65/72 is 64/72.** Re-measured at `260611dae`, its own probe
agrees on 64 cells: the residual is Hebrew under all six declared classes plus `swiss__2713` and
`swiss__27A2`, and *not* the "Thai under swiss" it named — `swiss__0E01` agrees on both sides by the
accident that the complex item's Hindi answer for Thai and the western serif list's answer are the
same face. All three defects above are closed and both probes now agree 25/25 and 72/72; over round
64's own fourteen movers the corpus face-set distance goes **19 → 16**, and 4 of the 947 move.

---

**The reference binary is `26.2.4.2`, not the `24.2.7.2` every stored figure was measured
against.** *(Written of an earlier container; see the correction directly above.)* The base image is
Ubuntu 26.04 and its archives offer no earlier LibreOffice.
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

**`grep -r` and `find` over this repository return exactly double. Use `git grep`.**
The case-insensitive mount has produced alias directory entries *inside the checkout* as well as
in the corpus: every project under `dotnet/src` now has a lower-case twin —
`dotnet/src/paperless.core` beside `dotnet/src/Paperless.Core`, same inode
(`4785074604717685`), link count 1. `git ls-tree` lists only the canonical spelling and
`git status` is clean, so nothing is wrong with the tree; but anything that walks the filesystem
visits both names.

Measured 2026-08-20 on the same query:

| | hits | files |
|---|---:|---:|
| `grep -rn … dotnet/src --include=*.cs` | 96 | 60 |
| `git grep -n … -- 'dotnet/src/**/*.cs'` | **48** | **30** |

**Exactly 2×.** A reach census run with `grep -r` is therefore inflated by a factor of two, and
this project dispatches rounds on reach censuses. `git grep` and `git ls-files` operate on tracked
paths and cannot see an alias, so they are the correct instruments here; if you must walk the
filesystem, fold case and deduplicate before counting.

**Do not delete the aliases.** As in the corpus, `rm -rf dotnet/src/paperless.core` is a request to
unlink that inode, and the inode is the source tree.

**`/tmp` is on the 20 GB overlay and this workflow fills it, which reads as an 11-verdict
regression.** A words round's post-change sweep returned **`REF-CANNOT-RENDER 13`** with `/` at
100%: **~120 000 stale entries, 17 GB**. It discarded that figure rather than reporting it, which is
the right call and is `HANDOVER.md`'s "a full disk looks exactly like a catastrophic regression"
arriving for the second time.

Measured by the parent shortly afterwards, on a `/tmp` holding **119 512 entries and 14 GB**:

| class | aged >2 h |
|---|---:|
| `MSBuildTemp*` | **114 122** |
| `paperless-lo-*` (soffice profiles) | 759 |
| `clr-debug-pipe-*` | 350 |

**`MSBuildTemp*` is the bulk and it is ours** — every `dotnet build` leaves one, and this session
runs a build per merge. Clearing entries older than two hours took `/` from 4.6 GB free to 5.4 GB
and `/tmp` from 119 512 entries to 4 275, with a sweep running throughout and unaffected.

```sh
find /tmp -maxdepth 1 -name 'MSBuildTemp*'    -mmin +120 -print0 | xargs -0 -r rm -rf
find /tmp -maxdepth 1 -name 'paperless-lo-*'  -mmin +120 -print0 | xargs -0 -r rm -rf
find /tmp -maxdepth 1 -name 'clr-debug-pipe-*' -mmin +120 -print0 | xargs -0 -r rm -rf
```

**The age bound is what makes it safe to run beside a live sweep** — nothing the sweep owns is two
hours old. Better still, point a sweep's `TMPDIR` at the host mount (`/c/sandbox/workdir/...`),
which has 150 GB free where `/` has five.

**A reference PDF differs byte for byte between two sweeps and it means nothing — but one document's
reference genuinely is non-deterministic, and the two must not be confused.**

The spurious case: **98 bytes of XMP `dc:date`**, length unchanged, with page, word and font counts
**identical across three sweeps of all 337 words paths**. `soffice` stamps the conversion time into
the metadata. So a byte diff of two reference renderings is not evidence of anything until the date
is masked out, and a round that byte-compares reference PDFs will otherwise find "changes"
everywhere.

The real case: **`ans_mappings_of_eccairs_terms.xlsx`** renders **191 pages eight times and 190
once** over nine renderings, with words wandering across four values and our side pinned throughout.
No `TODAY`/`NOW`/`RAND` — layout instability. It is filed `unstable`.

**The discriminator is the gate columns, not the bytes.** Identical page/word/font counts with
differing bytes is the date. Differing counts across renderings of one unchanged input is the real
thing, and there is exactly one such document known.

**`verify-test.sh` rebuilds twice, so running it during a sweep replaces the binary under that
sweep.** The rule "a sweep and a rebuild must never overlap" has always been written as though the
rebuild would be an explicit `dotnet build`. It need not be: the mutation harness builds on both
legs by design, and a round that runs it while a cross-track sweep is in flight silently swaps
`Paperless.Core.dll` mid-sweep.

**It announces itself as documents moving between two sweeps of the same unmodified tree** — round
60 saw **31 words documents** differ that way, one by 19.82 of ink on a chartless questionnaire.
Rendering is deterministic, so that cannot happen; a fresh render matched one sweep's copy and not
the other's (157 696 against 157 807 bytes), which is what identified it.

The check is cheap and belongs in the routine: **re-render one document after a sweep and compare it
byte for byte against that sweep's own copy.** If they differ, the binary changed under you and the
sweep is void. Anything that builds — `verify-test.sh`, a test run without `--no-build`, an IDE —
counts as a rebuild.

**An agent's cross-track figure is measured at its own base, and the manifest tracks HEAD. They
disagree, and the disagreement is not an error.** Three times in one session a round has swept the
other two tracks, found a manifest row it could not reproduce, and proposed a correction — each time
because the round that closed that document merged *after* its own base commit. The clearest case:
a words round proposed re-opening two documents on the grounds that "278 recorded against 276
measured, three rounds running, both sides stable, so not date volatility". Its reasoning was sound
and its conclusion wrong; the fix that closed them was three commits newer than its base.

So: **a cross-track sweep from an agent's worktree is evidence about that worktree**, and only the
parent's gate at HEAD settles a manifest row. Agents should say which commit they measured at — and
when a cross-track figure disagrees with the manifest, `git log <agent-base>..HEAD -- <the relevant
source>` is the first thing to run, not a manifest edit.

**The reference half of the gate is not reproducible for date-bearing sheets, and it decays the
manifest on its own.** Measured across three sweeps hours apart in round 51: four documents'
*reference* word counts moved with the wall clock while ours stayed pinned, so **the two halves of
the gate do not have the same reproducibility properties**, and a stored verdict on such a document
can go stale with nobody touching the code.

**The mechanism given here was wrong and the correct one has no environment-variable fix.** This
said `batch-check.sh` renders the reference with no `SOURCE_DATE_EPOCH` while `paperless render`
honours it — but the script sets the variable on **neither** side, so a `&D`/`&T` header prints
today in both renderings and cancels. What actually diverges is **`TODAY()` in a cell**: the
reference recalculates the formula on open and we print the value cached in the file. Six documents
are affected and they drift *further apart* every day. Setting `SOURCE_DATE_EPOCH` on both sides
does not close it; only evaluating the volatile function would.

The practical rule when a sweep diff appears: **split it by which side moved.** Round 51 separated
nine real movements from three calendar ones that way. Volatile dates reach **16 of the 40 open
sheets documents**, not the ~7 previously carried.

**`/c/sandbox/workdir` is a case-insensitive virtiofs mount, and this invalidates sweep totals.**
The four corpus files described elsewhere as "upper-case on disk" are not a second file that a
case-sensitive glob would miss — on this mount `049_….pptx` and `049_….PPTX` are the **same
inode**, one md5, one file. The live trap is that a tool which probes both spellings *materialises*
the second one permanently: `look.py` resolves a document by `CORPUS.rglob(stem.ext)` **plus**
`CORPUS.rglob(stem.EXT)`, and a slides sweep total went **305 → 311 with the corpus unchanged**
because of it. Reconcile every `find`-based total case-folded, and treat a total that grew without
a corpus commit as this until proven otherwise.

**And the alias count is not static — it grows when you look at a page.** Measured across one
session: the corpus held 45 alias entries, then 38 more materialised on the sheets track alone, and
a whole-corpus sweep's `TOTAL` went **991 → 1033 with the corpus unchanged and not one commit to
it**. `look.py` and `pair.sh` create them by resolving a document. So a sweep `TOTAL` is not
comparable with the same sweep's `TOTAL` an hour earlier, let alone with a stored one. **Score every
sweep against `MANIFEST.tsv`'s path list, and have the scorer refuse to print unless every manifest
path found a row** — that check is what keeps the figure meaningful while the denominator drifts
underneath it.

Measured 2026-08-20, so the shape of it is not in doubt: `grants-2005.xls` and `grants-2005.XLS`
report the **same inode** (`35184372089472271`), the same size, and a **link count of 1**. `git
ls-files` lists only the lower-case name and `git status` reports **nothing untracked**, so git
resolves the second spelling to the tracked file. There is one file wearing two names in
`readdir`, and the `nlink` of 1 is the filesystem telling you so.

**Do not `rm` one. `rm <NAME>.XLS` deletes the document** — measured on a scratch file 2026-08-21:
three names, one inode, link count 1, and `rm` on one name destroyed the file while leaving the
others as stale entries pointing at nothing. The earlier form of this warning was inferred; it is now
demonstrated.

**They can be cleared safely, by renaming.** A rename round trip on the *tracked* name
(`mv x .tmp && mv .tmp x`) invalidates every case-variant entry for that inode and leaves the file
untouched. `.claude/skills/corpus-batches/scripts/dealias-corpus.py` does this; `--check` reports
without changing anything.

**Done 2026-08-21**: 77 aliased inodes carrying 87 extra names, all cleared, **zero hash changes**,
`git status` clean, and the corpus now holds **946 files in 946 distinct inodes with no case-only
collision anywhere**. A gate's `TOTAL` line therefore equals the manifest again, and the figures
below (355 / 311 / 325, and 1033 corpus-wide) are the *historical* over-counts, not current ones.

**git is the only authority for which spelling is real**, because no ordering rule works: some
aliases upper-case the extension and some lower-case the whole filename. `core.quotePath=false` is
required, since git escapes non-ASCII and the corpus holds a CJK filename.

**The aliases can come back.** They are created by case-variant lookups — `look.py`'s upper-case
`rglob` was one source and is fixed — so run `dealias-corpus.py --check` if a sweep `TOTAL` exceeds
the manifest again. There are **45** such entries corpus-wide
(words 18, sheets 18, slides 9), which is exactly the gap between `find` counts (words 355, slides
311, sheets 325) and manifest rows (337, 302, 307).

The corpus *does* separately contain four documents whose only name is upper-case — they are rows
in `MANIFEST.tsv` and are real. Distinguishing them from an alias is what the manifest is for,
which is the whole mitigation: **score against `MANIFEST.tsv`'s path list, never against a sweep's
own `TOTAL`.** Verdicts are unaffected either way, because per-format identity keys on the
extension as spelled (`report__xls` and `report__XLS` are two identities, so neither overwrites the
other) — it is only the counts that inflate.

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

**Do not regenerate it in this container — it destroys the era column, and the reason is
sharper than either guess.** `provenance-index.py` takes each file's `added` date from
`git log --diff-filter=A`. Not from filesystem mtimes, and not defeated by missing history:
this clone reaches back to 2026-07-29, which is *before* the boundary. What defeats it is that
the probe tree entered this clone in **one bulk commit dated 2026-08-21** — `3418` of the
A-records under `dotnet/probes` carry that single date, which is *after* the 2026-08-13
boundary, so every row classifies `current` and the committed index's 215 `pre-container`
rows collapse to zero.

The era column is the whole point of the file: it is what tells a later round that a stored
figure was measured against a reference bank that no longer exists. An index stale by a few
probe directories is much the lesser harm, so **add rows by hand**.

Three rounds have now run it, watched it rewrite hundreds of rows and reverted, each reporting
a different mechanism (`git log` cannot see the dates; the column comes from mtimes). Both are
wrong and the fix is the same either way: don't run it. Worth knowing if anyone rewrites the
script — `added_dates` also uses `setdefault` over `git log`'s newest-first output, so a file
added, deleted and re-added keeps its *newest* A-record, not the first one its comment claims.

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

### Installing a specific LibreOffice, and why the tarball is not the distro package

**One host has to be allowed and it must be a wildcard: `*.documentfoundation.org`.** The apex
alone is not enough — `download.documentfoundation.org` is a redirector, and the file server that
actually carries every release including superseded ones is
`downloadarchive.documentfoundation.org`. `www.libreoffice.org`,
`ppa.launchpadcontent.net`, `api.launchpad.net`, `api.snapcraft.io`, `dl.flathub.org`,
`flathub.org`, `dev-www.libreoffice.org` and `git.libreoffice.org` are each separately denied;
`launchpad.net` and `keyserver.ubuntu.com` happen to be allowed and are not sufficient on their
own. The archive route needs none of them.

```sh
V=26.2.4.2
curl -sSL -o /tmp/lo.tar.gz \
  "https://downloadarchive.documentfoundation.org/libreoffice/old/$V/deb/x86_64/LibreOffice_${V}_Linux_x86-64_deb.tar.gz"
mkdir -p /tmp/lo && tar xzf /tmp/lo.tar.gz -C /tmp/lo
dpkg -i /tmp/lo/LibreOffice_${V}_Linux_x86-64_deb/DEBS/*.deb     # ~220 MB, 42 packages
/opt/libreoffice26.2/program/soffice --version                    # does NOT take over `soffice`
```

It installs beside the distro's under `/opt/libreoffice<major>.<minor>` and leaves
`/usr/bin/soffice` alone, so switching is a symlink and reverting is the same symlink back.
**Record the old target before repointing** — `readlink -f /usr/bin/soffice` — because every
stored figure in this repository was measured against whichever one was live.

**And now the part that matters: a TDF tarball 26.2.4.2 is NOT the distro-packaged 26.2.4.2 this
tree is calibrated against.** Measured 2026-09-03, `Paperless.Fidelity.Tests`, same commit, same
corpus, only the reference binary swapped:

| reference | failed of 552 |
|---|---:|
| distro 24.2.7.2 (this container's own) | **18** |
| TDF tarball 26.2.4.2, as shipped | **36** |
| TDF tarball 26.2.4.2, bundled font duplicates removed | **31** |

The version move fixes six test classes outright — `TableComparisonTests`,
`SlideTableComparisonTests` (both), `PdfOutputComparisonTests`,
`FootnoteComparisonTests.TheRuleAboveTheNotes…` and
`SheetSpilledTextComparisonTests.EveryPageShowsAsManyWords…` — so those really were the version
gap and nothing else. But it *breaks* thirteen more, and they cluster: `TabStopComparisonTests`,
`LineHeightComparisonTests`, `JustificationShrinkComparisonTests`, `MixedRunComparisonTests`,
`TableAutoLayoutComparisonTests`, `SheetTextComparisonTests`. Every one of those is a text-metric
comparison.

**Half the cause is that the tarball bundles its own fonts, including the metric-compatible
families.** `/opt/libreoffice26.2/share/fonts/truetype` ships 136 faces, among them Carlito,
Caladea, Liberation and DejaVu — *different builds* from the system's: Caladea-Regular is 58 964
bytes bundled against 81 600 installed, Carlito-Regular 635 996 against 628 032, and all differ by
md5. LibreOffice reads its own; Paperless reads the system's through its own OpenType reader; so
the two stacks measure different files and every advance width diverges. Moving the 45 duplicates
aside takes 36 failures to 31, which confirms the mechanism and also shows it is **not the whole
story** — the remaining gap is the tarball's other bundled libraries (its own HarfBuzz, ICU and
FreeType) against the distro's.

So: **do not treat "install 26.2.4.2 from TDF" as reproducing the environment the stored figures
came from.** It is a fourth reference, not the third one. If a round needs the tree's real target,
it needs the distro package on the distro the project develops on, which is Ubuntu 26.04 — and
this container is 24.04, whose archives stop at 24.2 with a 25.8.7 backport. `fc-match` will not
warn you about any of this: it answers for the system font set and knows nothing about what a
bundled application resolves. Read the face out of a PDF the binary itself produced.

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
