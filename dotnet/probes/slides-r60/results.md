# slides-r60 — results

Environment: LibreOffice **26.2.4.2 620(Build:2)**, `fc-match "DejaVu Sans"` → `DejaVuSans.ttf`,
base `2870991a4dd`, branch `wt-slides-r60`, `SOURCE_DATE_EPOCH=1700000000`, `TZ=UTC`.
`prediction.md` beside this file was committed as `3a5638b5953`, before anything was built or
rendered post-change.

## The baseline reproduces on the gate and is 0.31% under it on ink

| | briefed | measured at `2870991a4dd` |
|---|---|---|
| passing over `MANIFEST.tsv` | 200 of 302 | **200 of 302, 0 disagreements** |
| `abs_ink` | 1039.95 | **1036.75** |
| major pages | 375 | **365** |
| differing pixels | — | **19402.35 over 4530 pages** |

The gate reproduces exactly, which is the non-negotiable. The ink residue was chased far enough
to say what it is *not*: `git status` shows nothing modified under `dotnet/`, and the reference
renderings reused from round 59 were checked against freshly rendered ones — `Intersil…` and
`3495` come out with the *same* `|ink|%` on every one of their 39 and 26 pages, so reference
reuse is not the cause. It is a difference between the parent's whole-corpus gate and this track
sweep, and it is recorded rather than explained away.

## 1. The brief's item 1 is not a right-edge reservation. It is `c:crossBetween`

The brief asked for the plot rectangle's right edge, unmoved on 31 of 57 chart pages. Reading the
census' own tightest cluster first — four `advanced_powerpoint_area` and four
`advanced_powerpoint_line` pages at `dRight −9.68 / −9.66`, against four
`advanced_powerpoint_column` pages at `−2.73` with an *identical* reference rectangle — put the
question one level down: **why do an area chart and a column chart with the same frame, the same
legend and the same eight categories give up different amounts of right edge?**

Because `ChartPlot.ShiftedCategories` reads the chart *type* and nothing else. It is `Bar` or
`Stock`, so an area or line chart is drawn unshifted — its eight categories as eight points, the
first on the plot's left edge and the last on its right — and its last label then overhangs, and
`EndLabelOverhang` takes half of it off the right edge to make room. The reference does no such
thing, because it does not draw those categories unshifted at all.

### The probe, nine arms, one property each

`make-crossbetween-probe.py` patches `c:crossBetween` — and only that — in three corpus decks,
three arms each (`between` / `midCat` / element deleted), rendered through 26.2.4.2 and read back
by `cbread.py` from the category labels' own pen positions. The ratio of the label span to the
plot width is `(n−1)/n` when the categories are slots and 1 when they are points, and the label
width cancels because every label in those decks is the same width.

| | `between` | `midCat` | absent |
|---|---|---|---|
| `c:areaChart` | shifted 0.8750 | unshifted 1.0000 | unshifted 1.0000 |
| `c:lineChart` | shifted 0.8751 | unshifted 1.0000 | **shifted 0.8751** |
| `c:barChart` (column) | shifted 0.8750 | **shifted 0.8750** | shifted 0.8750 |

Nine of nine, and two of them are the ones worth having.

* **`lineChart` with the element absent is shifted where an area chart is not.** That is
  `oox/source/drawingml/chart/axisconverter.cxx:300-301`'s fall-back, which names `TYPEID_LINE`
  alongside the bar category and leaves everything else on the axis' created default.
* **`barChart` with `midCat` is shifted anyway**, and its rendering is byte-identical to its own
  `between` arm. So a bar or column chart ignores the element outright.

### Where the source and the running binary disagree, and the binary wins

The 27.2 tree in this checkout reads `c:crossBetween` *ahead* of the chart type for everything but
a 3-D bar or stock chart (`axisconverter.cxx:292-301`), which predicts `column + midCat` →
unshifted. 26.2.4.2 does not do that. The implementation puts the bar test first, in
`ChartPlot.ShiftedCategories`, and reads the element into a new `ChartPlot.CategoriesBetween`
behind it. `CategoriesBetween` is `null` when the format made no statement, which is every ODF
chart and every binary workbook chart, and null keeps exactly today's answer — so nothing outside
the OOXML reader moves.

The element is read from **the axis the category axis crosses** (`c:catAx/c:crossAx`), not from
the first `c:valAx` in the part, which is what `plotareaconverter.cxx:229-231` hands the
converter as its `pCrossingAxis`.

### Reach and result

`crossbetween-census.py` over every OOXML chart part in the corpus: **28 slides chart parts in 13
documents** state `between` on a line or an area chart; two more state `midCat` and must not move
(`171128IPAP.pptx` `chart7`, `stacked_area_chart.pptx`); one slides combination chart
(`combo_bar_line_chart.pptx`) states `midCat` on a bar and must not move either.

| | base | after §1 |
|---|---:|---:|
| passing over `MANIFEST.tsv` | 200 of 302 | **200 of 302** |
| `abs_ink` | 1036.75 | 1041.20 |
| major pages | 365 | 369 |
| differing pixels | 19402.35 | **19366.62 (−35.73)** |

**Thirteen documents moved, thirteen improved, none worsened** — the predicted set exactly, and
both controls held.

## 2. `8_P-Pavese_AIRBUS…pptx`: four claims from a page reading, four instruments, four answers

The brief's item 2, and its instruction was to check the claims before fixing anything. Three
instruments were written for it and all four claims come back confirmed — one of them with a
correction that changes what the defect *is*.

`fills.py` reports every filled path on a page with its colour and its device-space box.
Page 8, before anything:

| | reference | ours |
|---|---|---|
| chart-object background | `#000000` (0.00, 36.03)–(719.97, 427.49) | **nothing** |
| plot wall | `#454545` (68.74, 85.52)–(708.95, 377.18) | **nothing** |
| the two bars | **16 nested rectangles each**, `#FEFEFE` at the top through `#DDDDDD` at the base | one flat `#F9F9F9` each |

`textcols.py` reports the fill colour in force at every show-text operator. Page 8: the reference
draws **14 white runs**; we drew **22 black** ones.

**And the correction.** The reading said our bars are "a nearly flat, extremely pale near-white
that is almost invisible against the white page". They are — and so are the reference's, which
run `#FEFEFE` to `#DDDDDD`. What makes the reference's bars visible is the **black panel behind
them**, not their own colour. The bar colour was never the defect.

### The two backdrops

`ObjectFormatter`'s `spChartSpaceFill` and `spPlotArea2dFills` (`objectformatter.cxx:174-197`),
ported for styles 33 and up only. Below 33 a pptx chart paints neither, and that is a quirk rather
than a table row: `ObjectTypeFormatter`'s constructor overrides the fill *style* for exactly these
two object types when the style is 32 or less (`objectformatter.cxx:956-959`) and
`PptGraphicHelper` answers `XML_noFill` where the base helper answers `XML_solidFill`
(`oox/source/ppt/pptimport.cxx:309-312`). 160 of the corpus' 163 slides chart parts are style 2,
so that case is nearly the whole corpus and it must stay empty.

Pavese's chart states `c:style val="42"`, so `dk1` for the chart space and `dk1 tint 95000` for
the wall. Rendered:

| | reference | ours, after |
|---|---|---|
| background | `#000000` (0.00, 36.03)–(719.97, 427.49) | `#000000` (0.00, **36.00**)–(**720.00**, **427.50**) |
| wall | `#454545` (68.74, 85.52)–(708.95, 377.18) | `#454545` (**68.75**, **85.44**)–(**709.00**, **377.16**) |

`#454545` is `dk1` under `tint 95000` through the reader's existing colour path, to the byte, and
it is the strongest evidence yet that `ChartTint` is right.

### The one hardcoded black

`ChartLayout` drew every piece of chart text in one `private static readonly Colour AxisColour =
Colour.Black`. It is now five properties on `ChartPlot` — `LabelColour`, `TitleColour`,
`AxisTitleColour`, `DataLabelColour`, `LegendColour` — read from the parts that state them, each
defaulting to black so that a format stating nothing keeps the answer it had. `AxisColour` stays,
documented down to what it still is: the fallback for a radar spoke, a candlestick whisker and a
marker with no fill, none of which any format states a colour for.

Pavese page 8, after: **14 white runs against the reference's 14.** (We still draw 8 black runs to
its 6 — two extra runs that predate this and are not this.)

`8_P-Pavese_AIRBUS-ATB-journee-CRATB.pptx` page 8, whole:

```
   base   diff% 51.45   ink% 43.44   |ink|% 43.67   16 regions   MAJOR
   after  diff%  3.53   ink%  0.01   |ink|%  0.23   23 regions   shifted
```

The document goes **47.26 → 3.92** on unsigned ink and loses its only major page. It was the
track's largest.

Reach: `style-census.py` finds **three chart parts above style 32 in the whole corpus** — Pavese
and `DynamicBubbleChart.xlsx` — all of them 42. `txpr-census.py` finds a non-black chart text
colour stated in **26 slides, 68 sheets and 10 words documents**, which is the outer bound for the
second half; most of those name `tx1`, which resolves to black on a light theme and changes
nothing.

## 3. The unstacked area chart paints its first series last

Not in the plan. It came out of this round's own vision reading (see below) and it is the largest
single page movement after Pavese.

`AreaChart::createShapes` reverses its own slot list before it draws anything —
`lcl_reorderSeries(m_aZSlots)` under `m_nDimension == 2 && (m_bArea || !m_bCategoryXAxis)`
(`chart2/source/view/charttypes/AreaChart.cxx:565-568`), which is tdf#127813's switch — so series
1 ends up on top of the pile rather than under it.

`006_advanced_powerpoint_area.pptx` page 1:

```
   base      diff% 21.11   |ink|% 0.19   24 regions   shifted
   after §1  diff% 18.67   |ink|% 1.54   26 regions   MAJOR
   after §3  diff%  0.84   |ink|% 0.03   39 regions   ok
```

**A stacked area is exempt, and that is a measurement rather than the source.** `m_bArea` does not
exempt it, but `stacked_area_chart.pptx` is `diff% 1.82, |ink|% 0.16` in file order and
`1.87 / 0.22` reversed: its bands abut rather than nest, so the shared edge belongs to whichever
polygon is drawn last, and the reference draws them in file order. Only the *emission* is
reversed; the accumulation stays in file order or a stacked chart's bands come out in the wrong
sequence, and a test asserts exactly that.

The same source condition covers a scatter chart (`!m_bCategoryXAxis`). **Not implemented**: no
corpus scatter page has overlapping series, so there is nothing to measure it against, and a
change with no measurement behind it is what this list keeps warning about.

## 4. The 24.2.7.2 audit — two re-checks, both VERIFIED

```
open sites 37 in 26 files
   WordProcessing 11   Spreadsheets 9   Presentations 8   Text 5   Core 2   Rendering 1   Ooxml 1
markers 25
```

Round 59 counted 39 open in 30 files with 22 markers; the three that closed are round 59's own and
the other tracks'.

**`PptxSlideLayout.Shadow`** claims that the *first source stating an outer shadow* wins rather
than the first source that exists, and that `EffectProperties::assignUsed` — which replaces the
whole effect list when the source states any effect at all — does not describe the binary. Re-run
on 26.2.4.2 through this site's own fixture (`tests/corpus/features/slide-drop-shadow.pptx`,
flat-ODF export), its five shapes come out

```
  Angled       0.149 / 0.149  #000000  100%
  Themed       0     / 0.056  #000000   38%
  EmptyList    0     / 0.056  #000000   38%     <- the arm the rule exists for
  Glow         0     / 0.056  #000000   38%     <- and the other one
  Translucent  0     / 0      #000000   60%
```

**3 of 3.** The empty `a:effectLst` and the lone `a:glow` still keep the theme's shadow.

**`SlideAutofit.Quantised`** claims the twip → 1/100 mm conversion is `(n * 127 + 36) / 72`.
It still is — but the *citation* has moved. `convertTwipToMm100` no longer spells it out; it
delegates to `o3tl::convert(n, twip, mm100)` (`include/tools/UnitConversion.hxx:23-26`), whose
positive branch is `(n * num + den / 2) / den` with `num = 127`, `den = 72` — `den / 2` being the
36. `SvxFontHeightItem::PutValue` still rounds through twips with `fPoint * 20.0 + 0.5`
(`textitem.cxx:943, 976`). The worked case still divides the two readings: 13.33 pt is 267 twips,
`(267 * 127 + 36) / 72 = 471` against the direct ratio's 470. **VERIFIED**, with the citation
corrected so the next reader is not sent to a line that no longer holds the formula.

Both markers are written into the sites themselves.

## 5. What the plot rectangle's right edge actually is, now that this round has been through it

Plot rectangles over the 57 chart pages the census can measure:

| | r59 base | this round's base | **final** |
|---|---:|---:|---:|
| all four edges within 0.5 pt | 10 | 13 | **14** |
| `dLeft` over 1 pt | 22 | 9 | **9** |
| `dBottom` over 1 pt | 23 | 11 | **11** |
| `dTop` over 1 pt | 10 | 9 | **9** |
| `dRight` over 1 pt | 31 | 31 | **27** |

The prediction said 20 … 24 and it missed, upward, for a reason the round can name exactly. The
eight `line`/`area` template pages did not go to zero: they went from `−9.66 / −9.68` to
**`−2.71 / −2.73`** — and that residual is the same number the four `column` pages and the five
`bar` pages were already sitting at.

**Seventeen of the twenty-seven remaining pages sit at exactly `−2.71`, `−2.73` or `−2.88`, and
`legend-census.py` says what it is.** On all four families the legend's key, the key's own size
and the key-to-text gap agree between the two stacks to within 0.02 pt — and the whole legend sits
**2.70 pt to the left** of the reference's:

| | key left, ours | key left, ref | Δ | key width ours/ref | Δ text pen |
|---|---:|---:|---:|---|---:|
| `001_advanced_powerpoint_bar` | 594.90 | 597.60 | **−2.70** | 6.00 / 5.98 | −2.69 |
| `002_advanced_powerpoint_column` | 594.90 | 597.60 | **−2.70** | 6.00 / 5.98 | −2.69 |
| `003_advanced_powerpoint_line` | 410.51 | 412.72 | −2.21 | **7.00 / 6.01** | −2.33 |
| `006_advanced_powerpoint_area` | 594.90 | 597.60 | **−2.70** | 6.00 / 5.98 | −2.69 |

The plot's right edge is `frame.Right − margin − legend.Width − LegendMarginX`, and the legend is
drawn at `frame.Right − LegendMarginX − legend.Width`, so both move together: the legend box is
**2.70 pt too wide on its right**, and the plot gives that up. The line family is 2.21 rather than
2.70 because a line series' legend key is drawn 7.00 pt wide against the reference's 6.01 — a
second, smaller defect visible in the same table.

That is the next round's first item and it is worth seventeen of fifty-seven chart pages.
