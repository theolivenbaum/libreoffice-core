# sheets-vision-02 — four failing pages, read blind by agents who had never seen them

Round subject: use vision on the sheets track's current failures, with the reading delegated
to fresh subagents rather than done by the agent that chose the pages.

**Environment.** LibreOffice 26.2.4.2; references are the banked
`/c/sandbox/workdir/refpdfs-26.2.4.2-fonts/sheets/`. `fonts-dejavu-core` was **missing again**
at session start (`fc-match "DejaVu Sans"` → `wqy-zenhei.ttc`) despite being installed and
written up last session — reinstalled before any measurement, and recorded in
`MISSING_PACKAGES.md`. Every figure below is post-reinstall.

## Method, and why it is different from previous vision rounds

Previous rounds had the agent that picked the page also read it. That is not a control, and
the reason is measured rather than asserted: while calibrating the new `page-vision` skill I
read one page at 150 dpi, then 72, then 36. The 36 dpi read came out fluent and detailed and
was **worthless** — by then I was reciting the page. You cannot un-see a page.

So here: four failing documents, one page each, each composed into a single self-labelling
image by `page-vision/scripts/pair.sh`, each handed to a **separate subagent** that had never
seen the document, was forbidden to read any repo file or run any command, and — importantly
— **was not told the gate numbers**. Telling a reviewer "945 words against 431" buys a report
about word counts.

The scoreboard the pages were drawn from: joining `sd-sheets-d`'s `gate-after-all.tsv` against
`gate-ref-all.tsv` under the gate's own rule gives **146 of 171 sheets passing, 25 failing**.

## What came back

Four readings, four documents, and **two of the four independently named the same mechanism**
without any contact with each other.

### 1. An overflowing cell's text is painted again on the following page strip

`essd-16-3433-2024-t02.xlsx`, the track's most lopsided word failure (945 against 431).

The reviewer, given only the image, reported that our page 2 is a full page of prose whose
every line **begins mid-word** (`tadtm`, `horeline`, `ransect's`) and is clipped at both edges,
while the reference's page 2 is blank; and inferred that the run's origin cell lies on a page
to the left and that we let it bleed across the page break.

Measured afterwards, per page:

| page | ours | reference |
|---:|---:|---:|
| 1 | 439 | **439** |
| 2 | 315 | **0** |
| 3 | 153 | **0** |
| 4 | 49 | **0** |

**Page 1 agrees to the word.** The entire 514-word failure is text painted onto three pages
the reference leaves empty.

The important negative: **page counts already match, 4 against 4**, and on page 1 both
renderers push text past the right page edge by almost exactly the same amount (rightmost text
`xMax` 617.0 pt ours, 617.7 pt reference, on a 612 pt page). So `SheetTextOverflow`'s
print-area extension is **correct** and is not the defect — LibreOffice extends the area the
same way, producing the same four pages, and then simply does not paint the run again on the
strips after the one holding its anchor cell.

**The defect is in painting, not pagination:** the overflow run is drawn on every page its
extended width covers, instead of once on the anchor cell's page, clipped there.

Not yet fixed — it is a change to the sheet painter's clip, and wants its own round.

### 2. A rich-text run's underline and strikethrough were never read — **fixed**

`Infotabelle_WLAN im Flugzeug.xlsx`. The reviewer reported that the reference draws
`Innereuropäische Flüge:` **underlined** with a bold `kostenlos` under it and we draw the first
line un-underlined; and — this is the part that made it cheap to act on — noted that **bold
sub-runs render correctly everywhere else on the page** (`3 €`, `2,90 €`, `13,58 €`), so
"rich text is broken" was already ruled out before anyone opened a file.

The source cell is exactly that:

```xml
<r><rPr><u/><sz val="10"/>…</rPr><t>Innereuropäische Flüge</t></r>
<r><rPr><sz val="10"/>…</rPr><t xml:space="preserve">:      Chatten: </t></r>
<r><rPr><b/><sz val="10"/>…</rPr><t>kostenlos</t></r>
```

`XlsxRunFont` carried `Family, Points, Bold, Italic, Colour` and nothing else. An `rPr` is a
full `CT_Font` by schema, so `<u>` and `<strike>` were parsed, discarded, and drawn plain —
while the *cell* path read both (`XlsxCellFormats.UnderlineOf`) and `SheetTextLayout` already
drew both. Only the run path was missing, which is why nothing failed and nothing looked wrong.

**Reach**, over the 109 corpus workbooks that have a shared string table: **13 state `<u>` on a
run, 6 state `<strike>`.** An upper bound, per the standing rule about declaration counts — a
shared string no cell references is still in the table. Two of the 13 are in the current
failing list.

**It moves no words, so no gate column can see it.** That is the class this round existed to
find.

Fixed: `Underline` and `StruckThrough` added to `XlsxRunFont`, read in `XlsxRichRuns.ReadFont`,
applied in `XlsxCellFormats.Apply`. Absent stays **null rather than `None`**, because absent
means "keep what the run inherits" and an explicit `val="none"` turns an inherited line off;
collapsing the two would have left every underlined run plain, which is the original bug
wearing a different hat. Eight tests in `XlsxRunDecorationTests`; Spreadsheets 684/684, 0
skipped; solution builds 0/0.

### 3. A token too wide for its cell is not broken; LibreOffice breaks it mid-token

`Published_Issuances_2024.xlsx`, 457 words against 479, one page, page-exact.

The reviewer measured this off the image rather than eyeballing it. Every row's `LINK` cell
holds a URL. The reference wraps each onto **two lines, breaking mid-token**:

```
https://www.bsp.gov.ph/Regulations/Published%2
0Issuances/Images/M-2024-039.pdf
```

We draw **one line, clipped at the cell border**, with *zero ink* on the second line — in all
22 rows, including rows with ~35 px of unused vertical space. So the filename tail of every URL
is missing, which is the word deficit.

What the same reviewer measured as **identical**, which is what makes this specific: all six
column widths to the pixel, all 22 row heights, row count and content, body font metrics to the
pixel — and **the DESCRIPTION column wraps line-for-line identically in both**, including a
4-line cell. So this is not a wrap-width, measurement, autofit or font difference. It is
specifically **the absence of a character-level break fallback for a token containing no
break opportunity**.

Not fixed. Candidate causes the image cannot separate, and the probe that would: put a long
space-free token and a long spaced token into wrap-enabled cells, one plain and one a
hyperlink. If only the space-free hyperlink fails, the run is being treated as atomic; if every
space-free token fails, the character-break fallback is simply missing.

### 4. Borders: we draw rules the reference does not, and draw them doubled

Same document, same reviewer, measured in pixels:

- a **vertical rule between `ISSUANCE` and `DATE ISSUED`** that the reference does not draw
  anywhere — ours is dark on 587 of 605 body scanlines, the reference's on 36 (incidental glyph
  pixels);
- **two horizontal rules** the reference omits, at exactly the `1194/1193` and `1189/1188` row
  boundaries — ours 100% dark across the table width, reference 0%. The *adjacent* `1188/1187`
  boundary is drawn by both, so it is not a whole-region effect and the source almost certainly
  distinguishes those two pairs;
- roughly half our interior rules are **2 px where the reference's are 1 px**, and ours
  consistently occupies the reference's pixel *plus the one to its left*.

This corroborates, from an independent direction and on a different document, the standing
unassigned finding that **cell borders are not coalesced** (19 drawn strokes against
LibreOffice's 103 on the sampled page, with doubled hairlines at joins). Worth treating as one
item when it is picked up.

### 5. Chart defects on a page nobody had looked at

`Template Pilot Logbook JAR-FCL V3.0.xls`, page 17 — one embedded chart on an otherwise empty
page. Five differences, of which the cheapest to act on:

- **Y axis tick labels are `1200` where the reference draws `1200.0`** — every tick, one decimal
  place. This is precisely the failure mode `Core/Numbers` was moved down into Core to prevent
  ("every tick was written in its shortest round-trip form"). The move happened; the axis is
  still not applying the source's number format.
- **We draw a legend the reference does not** (and ours is clipped off the page edge).
- **The reference sets the title and axis titles bold; we set them regular**, and its axis title
  is larger.
- **We draw 2 X-axis labels where the reference draws ~37**, and our two label *values* match
  none of the reference's — so this may be date interpretation rather than label thinning.
- The series mark is in a different place and a different shape.

Ruled out by the same reading: plot-area geometry, position on the page, axis scale and
gridline positions, and the grey/magenta colours are all identical, so chart framing, axis
auto-scaling and theme colour resolution are **not** implicated.

## What this round says about method

- **Two of four reviewers independently named overflow clipping**, on two unrelated documents
  (§1 and the right-aligned `kein WLAN` spill in `Infotabelle`). Convergence across independent
  blind readings is worth much more than the same reader finding a class three times.
- **The "what looks identical" section carried most of the value.** In §3 it is what reduced a
  vague "text is missing" to "character-level break fallback", by ruling out six other causes
  before anyone read a line of code.
- **Not briefing the reviewers with the gate numbers worked.** None of the four reports is
  about word counts, and three of the five findings move no words at all.

## Not done

- §1 (overflow repainted on later strips), §3 (character break fallback) and §4 (border
  coalescing) are diagnosed and unfixed.
- §5's chart items are diagnosed from one page only; reach across the corpus is unmeasured.
- Only 4 of the 25 current sheets failures were read. The other 21 are untouched.
- The `essd` finding was checked for breadth on three other documents and the "reference leaves
  the page blank" signature appeared on only one of them (`NorwegianXPension…`, 1 blank page, 6
  words). So §1 as stated is a **narrow** variant; the broader "we do not clip at a boundary"
  mechanism needs a detector of its own rather than the blank-page proxy.
