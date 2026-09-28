# Every chart-bearing corpus document, scored by chart type — r186

Measured 2026-09-27 against **LibreOffice 26.2.4.2** (`/opt/libreoffice26.2/program/soffice`,
`0229ac93fcf0d7cbc6376066c6f35021cef002dc`), `SOURCE_DATE_EPOCH=0`, one output directory per
document.

## What this is for

A round was asked to review every chart implementation against LibreOffice's source and
implement what is missing. Reading source finds gaps; it does not say which of them matter.
This is the other half: **168 documents holding a DrawingML or chartex chart part, rendered
both ways and grouped by the set of plot elements their chart parts state**, so that a type
that is systematically wrong shows up as a type rather than as a document.

Grouping is by the *set*. A combination chart is its own row rather than being counted under
each of its members, because the ink of a page carrying two plot kinds cannot be attributed
to one of them.

`census-charts.py` produces the input, `sweep.sh` is the run, `rows.tsv` the result
(`kinds`, `path`, `worst`, `sum`, `pages`, `status`) and `aggregate.sh` the table.

> **The census in the first run was short by one and the reproduction is what found it.**
> `chartdocs.tsv` was originally built with an extension filter that omitted `.xlsm`, so
> `003_Contextures_chart_sample_9bda2719.xlsm` was not swept; it was measured afterwards and
> is row 168. `census-charts.py` filters on the *package contents* and not the extension,
> which is the same trap this project already records for `.xls` charts in BIFF substreams —
> and the reason `probes/chartbiff-r186/census.py` walks OLE2 files rather than `.xls`.

## The table

Ranked on **`worst`**, the worst single page's unsigned `|ink|%`. Summed ink is
length-weighted and would rank a long document above a badly wrong short one.

| plot elements stated | docs | mean worst | worst |
|---|---:|---:|---:|
| `areaChart,scatterChart` | 2 | 15.24 | 29.66 |
| `barChart,bubbleChart,doughnutChart,lineChart,radarChart` | 1 | 9.79 | 9.79 |
| `barChart,doughnutChart` | 1 | 3.08 | 3.08 |
| `barChart,scatterChart` | 1 | 2.96 | 2.96 |
| `barChart,lineChart,scatterChart` | 1 | 2.44 | 2.44 |
| `ofPieChart` | 2 | 2.06 | 3.58 |
| `pie3DChart` | 3 | 1.84 | 5.18 |
| `pieChart` | 15 | 1.56 | 10.25 |
| `scatterChart` | 15 | 1.43 | 5.56 |
| `barChart,lineChart` | 14 | 1.08 | 9.77 |
| `barChart` | 55 | 0.76 | 15.17 |
| `doughnutChart` | 13 | 0.68 | 3.26 |
| `lineChart` | 14 | 0.58 | 5.43 |
| `barChart,pieChart` | 2 | 0.47 | 0.74 |
| `radarChart` | 5 | 0.38 | 1.36 |
| `bubbleChart` | 9 | 0.13 | 0.89 |
| `lineChart,scatterChart` | 1 | 0.05 | 0.05 |
| `areaChart` | 9 | 0.05 | 0.20 |
| `cx:clusteredColumn,cx:paretoLine` | 2 | 0.01 | 0.02 |

## What it says

**No chart type is systematically wrong.** Every single-type row with a useful sample size
is under 1.6 mean — `barChart` 0.76 over 55 documents, `lineChart` 0.58 over 14,
`doughnutChart` 0.68 over 13, `radarChart` 0.38 over 5, `bubbleChart` **0.13** over 9,
`areaChart` **0.05** over 9 — and a mean under about 0.3 is the raster floor rather than a
defect. The rows that head the table are one or two documents each, so their mean *is* their
worst and they are documents rather than types.

**`cx:` chartex is 0.01 over both witnesses**, which is `DrawingChartex`'s deliberate choice
to draw what 26.2.4.2 draws — two polylines and no axis furniture — rather than what it
resolves.

The individual pages above 5, with what is known about each:

| worst | document | status |
|---:|---|---|
| 29.66 | `065_Weight_loss_tracker` | **ceiling.** The reference outlines its turned labels: 120 glyph-sized filled paths against our 12. Closing it would mean outlining glyphs to green a text gate. |
| 15.17 | `Intersil_Italy_CAN_Bus_Transceiver_Presentation_Final` | open |
| 10.25 | `3495.pptx` | open |
| 9.79 | `southern-classic-kennesaw-state-university-final` | open; five plot kinds on one deck |
| 9.77 | `171128IPAP` | open; its three series state `c:smooth val="1"` and 26.2.4.2 draws 801 segments through 132 points where we draw 131 — the splined line recorded in `dotnet/CLAUDE.md` |
| 8.24 | `100_Lime_and_Lemon_Data-Driven_Chart_for_PowerPoint` | open |
| 5.56 | `RPA P4 - Advanced Material` | open |
| 5.43 | `Demick_JetBlue` | open |
| 5.18 | `021_Unit_Circle_Chart_3D_Pie_Chart` | known: a 3-D pie drawn flat, task #19 |
| 5.11 | `Sector_Skills_Insights_Advanced_Manufacturing_summary_slide_pack` | open |

So the chart work left is **per-document layout**, not a missing chart type — which is what
sent this round to the two *readers* that could still be missing one: `probes/chartclass-r186`
(ODF) and `probes/chartbiff-r186` (BIFF). Both found a real gap, and neither has any reach
on this corpus, because this corpus's charts are all DrawingML.

## The census the type review produced

Read from LibreOffice's source rather than measured, and recorded here because the next round
should not re-derive it:

| path | state |
|---|---|
| DrawingML `c:` | all 16 plot elements read; confirmed by the table above |
| chartex `cx:` | the 8 layout ids of `PlotAreaContext::onCreateContext`; drawn as 26.2.4.2 draws them |
| ODF `chart:class` | **gap** — an unrecognised class drew nothing where the reference draws bars. `probes/chartclass-r186` |
| XLS BIFF | **gap** — no doughnut and no bubble chart. `probes/chartbiff-r186` |

`chart2/source/inc/servicenames_charttypes.hxx` declares nineteen chart types and
`VSeriesPlotter::createSeriesPlotter` (`VSeriesPlotter.cxx`:2882-2907) has a plotter for
eleven; the other eight reach `UnsupportedChart`, which draws
`STR_UNSUPPORTED_CHART_TYPE` and nothing else (`UnsupportedChart.cxx`:88-111). **So a type
LibreOffice "supports" in its service list is not one it draws**, and reproducing those eight
means reproducing a string, not a plot.
