# clip-seats-r97 — the two clip seats, O12 and O7

Base `61bc19e03`, worktree `/home/user/wt-clipseats`, branch `agent/clipseats`, 2026-09-11.
Reference **26.2.4.2** — `/opt/libreoffice26.2/program/soffice`. `/usr/bin/soffice` is 24.2.7.2
and was not used. Reference PDFs are the bank at `/home/user/gate-orig-r83/ref` (947 documents);
nothing was re-rendered through `soffice` except the two authored probes, one repository fixture
and one `--convert-to fods`.
Our side is `dotnet/tools/Paperless.Cli` built in this worktree, `SOURCE_DATE_EPOCH` pinned.

Both seats end **closed**. Neither needed a change to the tree: O12's proposed change is
measurably wrong, and O7's stated cause is refuted and re-seats as two smaller, characterised
things. One guard test is added.

**Every reading of a page below is mine and is contaminated.** There is no `Task`/subagent tool
in this container and `create_session` spawns a sibling that cannot open `/home/user/...`; four
rounds have recorded the same. Each picture is corroborated by a measurement that does not
depend on it — the content-stream rectangles in §1.2 and §2.1, the three-way pixel counts in
§2.2 and §2.4, and the character counts in §1.4.

---

## 0. The instrument warning this round adds, because it cost the first answer

r93's warning was: *exclude the page-level clip or your census measures nothing.* It is applied
throughout (§1.3 reports the area distribution). This round adds a second one of the same shape.

**MuPDF's text extraction applies the clip. Poppler's does not.** Asked whether 26.2.4.2 keeps a
clipped shape's glyphs, `pymupdf`'s `page.get_text()` said **no**, crisply, on the authored probe
and on every corpus witness. `pdftotext` on the identical file says **yes**, and so does a direct
read of the content stream — the `Tj` is there. On the probe below MuPDF reads 27 alphanumeric
characters where `pdftotext` reads 46; over twenty corpus witness pages, 78,192 against 81,031.

The gate counts with `pdftotext` (`batch-check.sh`:162). **A question about what is in a PDF's
text layer must be asked with the extractor the gate uses, or of the content stream itself.**
Every count below is one of those two. The census scripts read the content stream.

---

## 1. O12 — does a clipped shape's own text leave the text layer? **No.**

### 1.1 What O12 actually asks

`sink.ClipPath` records the clip in `PdfContentSink._clip`, and `Hidden` (`:405-433`) drops a
glyph run that misses it entirely; `sink.ClipPathKeepingText` writes the same `W n` and does not
record it, so the ink is cut and the glyphs stay. `SheetPageGraphics.cs:157` takes the second.
r93 refuted the general claim on 733 pages of *cell* text and left the *shape* case open.

### 1.2 The reference's answer, on an authored shape

`make-shape-text-probe.py` writes a minimal `.xlsx`: sixteen 14-character columns, one
`xdr:sp` rectangle from column A to column H with a solid fill, a black outline and a two
paragraph text body — the first right-aligned (`ZQ`), the second left-aligned (`LEFTZ`).
26.2.4.2 paginates it 2 × 3 and its page 1 prints columns A–F, block `50.4 … 516.614`.
The rectangle's own right edge is at 594.255, so its right-aligned word lands **51.9 pt past the
block and still on the 595.3 pt paper** — which is the discriminator the first attempt lacked,
because a word merely off the *paper* would be dropped for a different reason.

Page 1 of `soffice --convert-to pdf`, read out of the content stream:

```
/Div<</MCID 342>>BDC
q 50.4 58.28 466.214 729.609 re
W* n                                     <- the block, round the shape
0.7529411765 0 0 rg
322.328 275.952 m … 594.255 275.952 l h  <- the fill, running 77.6 pt past it
f*
…
EMC
/P<</MCID 343>>BDC
q 1 1 1 rg
BT
568.545 759.089 Td /F2 14.003 Tf[<01>1<02>]TJ   <- ZQ, outside the clip, written anyway
ET
Q
EMC
```

`pdftotext -f 1 -l 1` reads `LEFTZ` **and** `ZQ`. The ink is cut at 516.614 and the glyphs are
all there. Page 3, the next column band, carries `ZQ` again at its own position — the shape is
drawn on both bands, cut on each, with its whole text on each.

### 1.3 The corpus census

`figure-clip-census.py` interprets each page's content stream — graphics-state stack, `cm`,
`W`/`W*`, the marked-content stack, and the text matrix — and records for every text-showing
operator the clip in force **and which marked-content element opened it**. 26.2.4.2 does not tag
every drawing the same way: a chart's clip is opened inside a `/Figure` element and a plain
shape's inside a `/Div` one, both measured above; `/P` is the cell text the drawing clip never
governed. Over the 947 banked renderings, 28,608 pages:

| | |
|---|---:|
| clip rectangles of any kind | 114,411 |
| — of them under 90 % of the page | 78,138 |
| drawing-layer (`/Figure`, `/Div`) clip rectangles | 7,124 |
| — under 90 % of the page | 5,881 |
| pages carrying one | **1,267** in 488 documents |
| pages drawing any text under one | **424** |
| **pages whose drawing-layer text lies wholly outside it** | **150** in **61** documents |
| glyphs in that text | **5,865** |

Area distribution of the drawing-layer clips as a fraction of the page: min 0.000, p10 0.001,
median 0.076, max 1.732 — r93's check, and they are not page clips.

**Two filters this number needs, and the second is new.** Only clips under 90 % of the page count
(r93). And only text that lands **on the paper** counts: a chart straddling a column break puts
most of its axis labels at a negative *x*, no extractor reads a glyph outside the MediaBox, and
counting them inflates the answer to 174 pages / 16,834 glyphs — the wrong number, and it is what
the first run of this script printed.

Cross-check on the twenty largest witnesses, `pdftotext` against MuPDF: 81,031 alphanumeric
characters against 78,192. The 2,839 difference is the clipped drawing text, and `pdftotext`
keeps all of it. On `EHEST-Pre-departure-checklist` page 11 MuPDF reads 4 characters and
`pdftotext` reads 171.

### 1.4 What the change O12 contemplated would cost us

`SheetPageGraphics.cs:157` switched to `sink.ClipPath`, rebuilt, and the 74 sheets renderings the
block clip touches rendered on both legs with `SOURCE_DATE_EPOCH` pinned (`cliptext-chars.tsv`):

| | |
|---|---:|
| renderings whose character count changes | **56 of 74** |
| alphanumeric characters, keeping the text | 1,168,118 |
| alphanumeric characters, hiding it | 1,164,968 |
| **lost** | **3,150** |
| **renderings that leave the gate's `max(2%, 15)` glyph band** | **8 of 947** |

Counted with the gate's own column 9 — `sum(1 for c in pdftotext(pdf) if c.isalnum())` — and its
own band, `d > 2% of ref AND d > 15` (`batch-check.sh`:158-174, :298-302), not a reimplementation
of them.

The eight: `012_Contextures_chart_sample_9900da76`, `019_Free_Blood_Sugar_Chart_for_Excel`,
`019_advanced_excel_pie`, `044_Cash_flow_forecast`, `064_Small_business_cash_flow`,
`EHEST-Pre-departure-checklist`, `Keywords_Mapping_Graphs_and_Charts`,
`microsoft_learn_multi_chart_examples`. The reference loses none of them.

### 1.5 The mechanism, in 26.2's own source

The block reaches the device as a **device** clip and nothing below it culls a glyph:

- `ScOutputData::PrePrintDrawingLayer` builds the rectangle from the page's own columns and rows
  and hands it to `BeginDrawLayers` (`sc/source/ui/view/output3.cxx`:40-104, the region at :92-95);
- `ObjectContactOfPageView::DoProcessDisplay` pushes it —
  `pOutDev->Push(vcl::PushFlags::CLIPREGION); pOutDev->IntersectClipRegion(rRedrawArea);`
  (`svx/source/sdr/contact/objectcontactofpageview.cxx`:158-171);
- `PDFWriterImpl::drawLayout` (`vcl/source/pdf/pdfwriter_impl.cxx`:5839) writes the glyphs with no
  test against `m_aGraphicsStack.front().m_aClipRegion` at all. The clip is written into the
  content stream; the text is written inside it.

A shape's text primitive is not handled differently from its fill: both go to the same device
under the same clip, and only one of them is a path.

### 1.6 State

**O12 closes: nil reach, no code change.** The tree already does what 26.2.4.2 does. Added
`SheetStraddlingDrawingTests.TheBlockClipKeepsTheShapesOwnGlyphs`, which counts the two kinds of
clip apart — no shared sink does, `PlacedDrawingSink` included, because it does not override
`ClipPathKeepingText` — and fails when `SheetPageGraphics.cs:157` is put back to `ClipPath`
(checked: 1 failed / 0 passed with the flip, 1265 passed / 0 failed without it). That is the hole
two rounds' clean auto-merge went through.

---

## 2. O7 — the six documents the block clip made worse

### 2.1 The band hypothesis is refuted

The register says *"the clip now exposes our own band edge where it differs from the reference's
(`Template Pilot Logbook` p18 cuts 22 pt early). A column-width/break question."* Our block
rectangle and 26.2.4.2's, read out of both content streams on every page of the six where the
clip changed the ink:

| document, page | ours | 26.2.4.2 | worst edge |
|---|---|---|---:|
| `Template Pilot Logbook` p18 | 53.830 91.725 243.808 524.462 | 53.830 90.964 **243.893** 524.466 | 0.761 pt (top); **0.085 pt on the right edge** |
| `Template Pilot Logbook` p17 | 53.830 91.725 750.416 524.462 | 53.830 90.964 750.671 524.466 | 0.761 pt |
| `SIL_TDB609`/`605` p6 | 50.400 260.844 553.606 738.000 | 50.400 260.249 553.663 738.000 | 0.595 pt |
| `SIL_TDB609`/`605` p7 | 50.400 260.844 394.979 738.000 | 50.400 260.249 395.093 738.000 | 0.595 pt |
| `PC1000` p1 | 55.551 64.460 737.170 576.000 | 55.389 63.751 737.235 576.000 | 0.709 pt |
| `013_Contextures` p1 | 54.000 401.414 507.798 720.000 | 54.000 401.046 507.855 720.000 | 0.368 pt |
| `013_Contextures` p2 | 54.000 401.414 183.883 720.000 | 54.000 401.046 183.883 720.000 | 0.368 pt |

**Our band is the reference's band, to under 0.8 pt everywhere and to 0.085 pt on the very edge
the register said was 22 pt out.** It is not a column-width or break question.

The sixth document, `012_Contextures`, has no row here because **26.2.4.2 emits no block clip on
either of its pages at all** — its stream carries the chart's own frame where `013`'s carries the
block, which is §2.3 and is not our band being wrong.

The 22 pt is real and it is the chart's **plot area**. `Template Pilot Logbook` pages 17 and 18
are one chart across two column bands; in page-17 coordinates its plot rectangle is

| | left | right | width |
|---|---:|---:|---:|
| 26.2.4.2 | 153.07 | 785.73 | **632.66** |
| ours | 165.45 | 761.75 | **596.30** |

— 12.38 pt right at the left edge and **23.98 pt short at the right**, which is the "cut 22 pt
early". The same chart's leftmost value-axis label moves by the same 12.38 (`116.16` → `128.54`).
The sheet states no print range, and `--convert-to fods` gives 26.2.4.2's own two chart frames as
`svg:width="8.9209in"` (642.30 pt) and `svg:width="11.263in"` (810.94 pt), both at
`svg:x="0.3961in"`. The pages-17-18 chart is the wider of the two — its ink alone spans 721.30 pt
in page-17 coordinates, more than the narrower frame is wide — but which frame is which was not
read out of the file, only inferred from that, so O25 should re-establish it before using the
number.

### 2.2 Four of the six were never worse

`clip-overreach.py` renders the reference, our pre-clip rendering and our clipped one at 120 dpi
and counts pixels three ways. The interesting count is the ink **the clip removed**, split by
whether 26.2.4.2 draws it:

| page | px the clip removed | of which the reference also leaves blank | of which it inks |
|---|---:|---:|---:|
| `Template Pilot Logbook` p18 | 43,154 | 43,154 | **0** |
| `SIL_TDB609` p6 | 737 | 737 | 0 |
| `SIL_TDB609` p7 | 1,028 | 1,028 | 0 |
| `PC1000` p1 | 17,447 | 17,445 | 2 |
| `012_Contextures` p2 | 7,306 | 85 | **7,221** |

On four of the six, **every pixel the clip took was ink the reference does not draw.** Their
`|ink|%` rose anyway, and the reason is in the metric. A region's `luma_gap` is the **signed mean
brightness difference over the whole region** (`pdf-image-diff.py:245-269`), and both ink figures
weight the region by it — `|ink|%` takes the absolute value of the region's mean rather than the
mean of the absolute values (`:434-437`). So ink we wrongly draw on one side of a connected region
cancels ink we wrongly miss on the other **inside `|ink|%` too**, which is the column this project
ranks on. `Template Pilot Logbook` p18 is one region before and
one after; before the clip our chart's ink ran from the paper's left edge to 65 pt and cancelled
the 24 pt of the reference's chart we do not reach, and removing it left that gap uncancelled.
**The clip did not make those pages worse; it stopped a cancellation.** What is left on p18 is
§2.1's plot area.

### 2.3 Two of the six are genuinely over-clipped, and it is not our band

`012_` and `013_Contextures_chart_sample`: 0.645 and 0.637 of summed page-ink, and all of it in
the page **margins** — x 0.0 … 53.4 pt on page 2, where the block's left edge is 54.0, and
x 508.2 … 571.2 on page 1, where its right edge is 508.2. The reference paints the chart's white
background, its border and its bars into the margin; we cut them at the block.

**A chart's own clip replaces the drawing layer's rather than intersecting it.** An OLE object is
replayed from a metafile, and `MetaClipRegionAction::Execute` calls `pOut->SetClipRegion(maRegion)`
(`vcl/source/gdi/metaact.cxx`:1359-1369) where `MetaISectRectClipRegionAction::Execute` (:1394-1397)
would call `IntersectClipRegion`. The distinction survives into the writer:
`PDFWriterImpl::setClipRegion` assigns `m_aClipRegion = std::move(aRegion)`
(`vcl/source/pdf/pdfwriter_impl.cxx`:9608-9619) while `intersectClipRegion` ANDs it (:9657-9678).
So the region `DoProcessDisplay` pushed is **discarded** for as long as the chart's own clip is in
force, and comes back only when the metafile sets another one.

It is legible in the reference's own content stream, which writes a replace as `Q q … re W* n`
rather than a `W n` intersect. `013_Contextures` page 2, clip operators in order:

```
q 0 0.028 611.972 791.972 re W* n          the page
Q q -333.862 416.072 457.297 220.494 re W* n   the chart's frame  (reaches x = -333.9)
Q q -329.657 429.526 447.836 202.555 re W* n   the chart's wall
…
Q q 54 401.046 129.883 318.954 re W* n         the block  (54 … 183.883)
Q q -329.657 429.526 447.836 202.555 re W* n   and the block is gone again
```

`012_Contextures` page 2 is the same stream with the block clip never appearing at all.

### 2.4 The clip's over-reach across the corpus is 0.3 % of its under-reach

The same three-way count over all 867 pages of the 74 renderings the clip moved
(`overreach.tsv`):

| | |
|---|---:|
| pages where the clip removes ink 26.2.4.2 keeps | **61** |
| that ink | **31,384 px**, summed 2.319 page-% |
| ink our clipped rendering still draws that the reference does not | **10,216,274 px** |

and 1.92 of the 2.319 is `012_Contextures` (0.645), `DynamicBubbleChart` (0.642) and
`013_Contextures` (0.637). Everything else is a pixel or two at a block edge our rounding puts
0.1–0.3 pt from the reference's — `Template Pilot Logbook` p17 is 474 px in one column at
x 750.6, where our right edge is 750.416 and the reference's 750.671.

### 2.5 State

**O7 closes as stated — refuted — and re-seats as two things and a fact about the metric.**
It is not one cause and it is not a band:

- **O25** (the register's number for it). `Template Pilot Logbook`'s chart plot area is 596.30 pt wide against 26.2.4.2's 632.66
  and starts 12.38 pt right of it, so its ink ends 23.98 pt short. Chart layout, beside O11 and
  O23. Measured, uncharacterised.
- **O26.** A chart's own clip replaces the block clip instead of intersecting it
  (`metaact.cxx`:1359-1369 against :1394-1397). Reach **3 documents** and 1.92 summed page-ink-%:
  `012_`/`013_Contextures_chart_sample`, `DynamicBubbleChart`. Modelling it means giving the sink
  a clip that *replaces* rather than intersects, which is a change to `IDrawingSink` and not a
  change to `SheetPageGraphics`; not attempted here.
- and `SIL_TDB605`, `SIL_TDB609` and `PC1000` leave the seat entirely: 0, 0 and 2 pixels of ink
  the reference keeps. Their `|ink|%` movement is `pdf-image-diff.py`'s per-region signed sum,
  not the tree.

---

## 3. What is in this directory

| file | what |
|---|---|
| `figure-clip-census.py` | the census of §1.3 — a content-stream interpreter that answers, per text run, which marked-content element opened the clip in force |
| `census-onpaper.txt` | its output over the 947 banked reference renderings |
| `make-shape-text-probe.py` | authors §1.2's fixture; derived from `ink-pass-r92/make-clip-probe.py` |
| `shape-text-probe.xlsx` | the fixture as authored (`--from-col 0 --to-col 7 --to-row 40 --right-word ZQ --left-word LEFTZ`) |
| `clip-overreach.py` | the three-way pixel count of §2.2 and §2.4 |
| `overreach.tsv` | its output over the 74 renderings the clip moved |
| `cliptext-chars.tsv` | §1.4 — per-document alphanumeric counts on both legs, with the gate's band |

## 4. Left standing

- **O25 and O26 above.** Neither was attempted.
- **Why a chart's metafile sets a clip on some documents and not others.** `044_Cash_flow_forecast`
  is clipped at the block and `012_Contextures` is not, and both are chart OLEs. The replace/
  intersect distinction explains *how* a chart escapes; it does not predict *which* charts do.
  O26 needs that before it can be implemented without regressing the 6.55 of `|ink|%` the block
  clip is worth on `044` alone.
- **The `/Figure` versus `/Div` tagging** is 26.2.4.2's, not ours, and no source line for it was
  read. The census does not depend on which is which — it takes both — but a round that wants to
  separate charts from shapes in the corpus cannot use the tag until it has.
