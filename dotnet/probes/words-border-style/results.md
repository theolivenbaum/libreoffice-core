# What a border's line style draws, and how much room it takes

*Measured 2026-09-04 in the container described at the top of `dotnet/CLAUDE.md`: repository at
`/home/user/libreoffice-core`, corpus at `/home/user/sample-files`, reference `soffice` the
distro's **24.2.7.2**. Every figure below is a fresh measurement in that environment.*

## Where this started

`07-04.doc` draws a **3 pt double rule** across its head — `fo:border-top="3pt double"` in
LibreOffice's own reading of it — and we drew a single hairline. It is one of eighteen documents
catalogued under *table rules and shading*, and the census that followed showed the class is not
rare: **82 OOXML documents in the corpus state a border style that is not a plain single rule**,
`double` alone 2059 times across 56 of them, `dotted` 663 across 17, `outset` 417 across 3.

Nothing in the tree modelled a line style at all. `ParagraphBorder` and `TableBorder` each carried
a width and a colour, and every border was one solid stroke of the stated width.

## The probes

`rules.py` writes one document per (style, `w:sz`) pair — a paragraph reading `ABOVE`, one carrying
the border under test, one reading `BELOW` — and reads two observables off page one:

* every horizontal run of dark pixels across the border's band in a **300 dpi** raster, as
  (top, thickness) in points, which is what the style draws;
* the bordered paragraph's own y from the PDF text layer, which is the room the border took.

`dashlen.py` (in the scratch, reproduced below) does the same along the rule at **600 dpi**, which
is what a dash pattern needs.

## What 24.2.7.2 draws, at `w:sz="24"` — 3 pt

| style | strokes | room |
|---|---|---:|
| `single` | one of 3.12 | 3 pt |
| `double`, `triple` | 3.12, gap 2.88, 3.12 | **9 pt** |
| `thick` | one of 6.0 | **6 pt** |
| `thinThickSmallGap` | 3.12 then 0.72 | 4.5 pt |
| `thickThinSmallGap` | 0.72 then 2.88 | 4.5 pt |
| `thinThickThinMediumGap` | 1.68 then 2.88 | 6 pt |
| `outset` | 0.72 then 2.64 | 6.05 pt |
| `inset` | 2.64 then 0.96 | 6.05 pt |
| `dotted`, `dashed`, `dotDash`, `wave`, `dashSmallGap` | as `single` | 3 pt |

So **the stated width is the width of *a* line, not of the border**, and three of these cost the
page two to three times what we were charging for them.

## The dash lengths do not scale with the rule

The thing a reader guesses wrong, and the reason `DashPresets` is the wrong source — DrawingML's
presets *are* multiples of the pen. Measured at 600 dpi at `w:sz` 8 and 24, identical at both:

| style | ink | gap |
|---|---:|---:|
| `dotted` | 0.48 | 1.00 |
| `dashed` | 8.04 | 2.52 |
| `dashSmallGap` | 3.00 | 1.00 |
| `dotDash` | 8.04, 2.52, 2.50 | 2.52 |

Which is `svtools::GetDashing`'s table — 1/2, 16/5, 6/2, 16/5/5/5 — scaled by **10 twips**:
`fPatScFact` (`svx/source/sdr/primitive2d/sdrframeborderprimitive2d.cxx`:600) in the draw layer's
units, which are twips for Writer.

## What we do now

`BorderRules` ports `editeng`'s own arithmetic rather than fitting these numbers:
`ConvertBorderWidthFromWord` turns the stated width into the total, and `BorderWidthImpl` divides
the total into an outer rule, a gap and an inner rule — each of the three either a constant in
twips or a share of the total, with a scaling component having the *other two* constants taken off
it. That is what makes `thinThickSmallGap`'s scaling rule come out as the stated width exactly, and
it reproduces every row of both tables above, dash for dash.

`ParagraphBorder` gained the line and now carries the *drawn* width, so `Allowance` charges the
page correctly with no further change.

## Result

Words gate 337 documents, **310 match, verdicts identical row for row**; mean first-page ink
7.858 → 7.834. Four documents moved:

| document | ink | |
|---|---|---|
| `195584360` | 12.35 → **4.09** | |
| `system_design__technical_architecture_template` | 4.80 → **1.58** | |
| `Form-SM-76A-…-Compliance-Statement-…-11` | 41.98 → 40.63 | |
| `Technical_Issue_Report_Form` | 23.82 → **28.44** | worse by the metric, exactly right on the page |

The last one is worth the space, because it is the second compensating error this round has turned
up. Its running head carries `thickThinSmallGap w:sz="24"`, and we drew 3 pt of it where the
reference draws 4.5. Stroke for stroke on page one at 300 dpi:

| | head rule | first table rules |
|---|---|---|
| reference | (55.44, 2.88), (59.04, 0.72) | 125.52, 153.84 |
| before | (55.44, 2.88) | 123.84, 152.16 |
| after | (55.44, 2.88), (59.04, 0.96) | **125.52, 153.84** |

and the body's first line moves from 70.290 to **71.790** against the reference's 71.790764. The
missing 1.5 pt had been cancelling an accumulating row-height excess further down the page; with
the head right, the excess is no longer hidden. The ink is worse and the page is correct.
