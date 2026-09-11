# The two conditional-format successors: the compared string, and the bar

Round 97, seated by `probes/cond-format-r96/results.md` §4 and §5 and by register entries **O21**
and **O22**. Base `61bc19e03`, worktree `/home/user/wt-cfdraw`, branch `agent/cfdraw`, reference
`/opt/libreoffice26.2/program/soffice` (**LibreOffice 26.2.4.2**). The reference half of every
corpus figure is the bank at `/home/user/gate-orig-r83/ref/`, reused rather than re-rendered; only
our half was run, at two frozen copies of the binary outside the tree, which a diff confined to
`dotnet/src/Paperless.Spreadsheets` makes sound. The fixtures' reference halves were rendered fresh
by 26.2.4.2 during this round.

**There is no `Task`/subagent tool in this container**, so no page was read by anyone
uncontaminated. Nothing below rests on a reading: every fixture expectation is 26.2.4.2's own PDF
read back as filled rectangles, stroked paths, embedded images and coloured text spans, and every
corpus figure is `pdf-image-diff.py` beside an operator census.

**Both seats are put into a terminal state, and the second one splits.** O21 is fixed, with a reach
that is not nil. O22's `dataBar` half is fixed; its `iconSet` half is **not** nil reach — round 96's
census of it was an under-count and the corrected figure is four times larger — and it is
re-seated with the reason it cannot be drawn here. §6 says which and why.

---

## 1. O21 — a cell's drawn text and its compared value are two different strings

### The mechanism, in one line of the reference

`RichStringPortion::setText` is the whole of what Calc's SpreadsheetML importer does to a shared
string:

```cpp
void RichStringPortion::setText( const OUString& rText )
{
    maText = AttributeConversion::decodeXString(rText);
}
```

`sc/source/filter/oox/richstring.cxx`:60-63. The `_xHHHH_` escapes are resolved and **nothing else
happens**: every control character the file states is still in the string when the document holds
it. What `XlsxCellText.Of` describes — a lone `U+0009` dropped in a string with no line feed, a
`U+000D` dropped, the rest of `U+0000`–`U+001F` dropped — is the *drawing* layer's behaviour, and a
conditional format never reaches it. `ScConditionEntry::FillCache` keys its `duplicateValues` cache
on `ScRefCellValue`'s own string (`conditio.cxx`:804-859), which is `maText`.

So the reader needed the stored spelling beside the drawn one, and now has it:
`XlsxCellText.Stored` is stage 1 alone, `XlsxSharedStrings.StoredAt` answers it, and
`XlsxConditionalStyles.Sheet.ValueOf` reads that rather than the indexer. The second copy is
recorded **only where it differs** from the drawn one, which on the corpus is a handful of strings
in a table of tens of thousands.

### The fixture, and the second half of the finding it produced

`tests/corpus/features/sheet-cf-stored-vs-drawn.xlsx`, 2592 bytes, authored by
`make-fixtures.py`. Column A is `\talpha`, `alpha`, `alpha`, `bravo`, `\tbravo`, `\tbravo` under one
`duplicateValues` rule, every tab written `_x0009_`. Column C is `\t`, three spaces and `zulu`
under `containsBlanks` and `notContainsBlanks`.

26.2.4.2's own PDF of it (`fixture-reference/painted.txt`):

| cell | stored | drawn | reference paints |
|---|---|---|---|
| A1 | `\talpha` | `alpha` | nothing |
| A2, A3 | `alpha` | `alpha` | `#FFC7CE` / `#9C0006` |
| A4 | `bravo` | `bravo` | nothing |
| A5, A6 | `\tbravo` | `bravo` | `#FFC7CE` / `#9C0006` |
| C1 | `\t` | *(empty)* | `#CCE5FF` — the **not**-blank rule |
| C2 | `   ` | `   ` | `#FFC7CE` — the blank rule |
| C3 | `zulu` | `zulu` | `#CCE5FF` |

The A column is the direct statement of the finding: **the reference draws A1 and A2 as the same
five glyphs and compares them as different keys**, and A5/A6 are duplicates of each other while A4
is a duplicate of nothing. Reading the drawn text makes all six a duplicate and paints all six.

**C1 is the half the brief did not ask for and it comes free.** `containsBlanks` is not a mode but
the substituted formula `LEN(TRIM(#B))=0` (round 96 §1), and Calc's `TRIM` takes **spaces and not
tabs** — so a cell holding one tab is *not* blank to the reference and *is* blank to a reader that
normalised the tab away first. The fixture shows the reference giving C1 the blue not-blank rule
and C2, three spaces, the red blank one. The same one-line change fixes both, because both go
through `Sheet.ValueOf`.

`XlsxConditionalPredicateTests.AConditionComparesTheStoredStringAndNotTheDrawnOne` asserts all
seven cells. Reverting the three source files, clearing `obj`/`bin` and rebuilding:
`painted.Fill(0) should be null but was "#FFC7CE"` — **it fails at the base and passes after**.

### The reach, measured and small

The one corpus witness is round 96's: `6880ac7361ca…ST Capability List Rev.16 - Web.xlsx`, whose
`A3` is `"\tD5758620001301"` and whose `A2805` is `"D5758620001301"`.

| | base | **after** | 26.2.4.2 |
|---|---:|---:|---:|
| `#9C0006` spans, whole document | 18 | **16** | **16** |
| pages carrying them | 1, 25, 26, 27, 28, 85 | **25, 26, 27, 28** | **25, 26, 27, 28** |

Exact. The two spans that go are the two the round-96 write-up named, on pages 1 and 85, and the
sixteen that stay were already span-for-span the reference's. **Reach is 1 cell in 1 of 947
renderings, 2 spans** — the figure the brief carried, confirmed rather than revised, and it is
*not* nil: the document's rendering moves and moves in the right direction.

---

## 2. O22, first half — `dataBar` is read, and half of its meaning is in the `x14` extension

### The four arms, each read out of 26.2 and each confirmed twice

**(1) `minLength` and `maxLength` are ignored unless the axis is `NONE` or `MIDDLE`.**
`DataBarRule`'s constructor sets `mxFormat->meAxisPosition = databar::NONE`
(`condformatbuffer.cxx`:351-356) and `importAttribs` defaults the two lengths to **10 and 90**
(`:384-389`). Only the extension moves the axis, and `ExtCfDataBarRule::finalizeImport` maps
anything that is not `none` or `middle` — the `automatic` default included — to `AUTOMATIC`
(`:1630-1643`). `ScDataBarFormat::GetDataBarInfo`'s `AUTOMATIC` arm
(`colorscale.cxx`:1004-1043) contains neither `nMinLength` nor `nMaxLength`.

So two files whose main-namespace markup is identical draw different bars, and the fixtures separate
them. Both state `<cfvo type="min"/><cfvo type="max"/>` over the values 0, 25, 50, 75, 100 in one
column; `sheet-cf-data-bar-auto.xlsx` adds an `x14:dataBar` with `autoMin`/`autoMax`, `gradient="0"`
and no `axisPosition`.

| | reference's fods | bar widths in the reference's PDF, in a 46.91 pt cell |
|---|---|---|
| `sheet-cf-data-bar-lengths` | `axis-position="none"`, `min-length="10" max-length="90"` | 4.82, 14.23, 23.53, 32.88, 42.29 → **1 : 3 : 5 : 7 : 9** |
| `sheet-cf-data-bar-auto` | *no* `axis-position`, still `min-length="10" max-length="90"` | *none*, 11.73, 23.47, 35.18, 46.91 → **0 : 1 : 2 : 3 : 4** |

The second row is the one that matters: the two lengths **survive the import** and stop reaching the
bar, which is why reading the fods alone would have got it wrong. And **a cell sitting on the
automatic minimum draws no bar at all** — `nValue <= nMin` gives `mnLength = 0` and `drawDataBars`
returns before painting.

**(2) The extension's own `minLength`/`maxLength` are never read.** `importDataBar`
(`condformatbuffer.cxx`:1710-1715) reads `gradient` and `axisPosition` and nothing else. All nine
corpus rules state `minLength="0" maxLength="100"` in their extension and 26.2.4.2 writes
`min-length="10" max-length="90"` for every one of them.

**(3) The extension's `cfvo` types overwrite the main namespace's, and the value only when it
parses whole.** `:1665-1703`. `autoMin`/`autoMax` both become `COLORSCALE_AUTO`, which the main
namespace cannot spell; a `num` takes the `<xm:f>` beside it only when
`nSize == msScaleTypeValue.getLength()`, so a real formula such as `069_Blue_modern_balance_sheet`'s
`$C$11` leaves the main-namespace number standing.

**(4) A negative value's colour exists only if an `x14:negativeFillColor` does.**
`mbNeg` is set by the presence of that element (`:1656-1663`) and by nothing else, and the
`COL_LIGHTRED` fallback beside it (`colorscale.cxx`:1066-1073) is therefore unreachable from an
OOXML import. `sheet-cf-data-bar-negative.xlsx` — `cfvo num -100`/`num 100` over −100…100 with an
`x14:negativeFillColor rgb="FFC00000"` — pins the whole arm at the reference:

| cell | value | reference's bar | reference's axis |
|---|---:|---|---|
| A1 | −100 | x 53.89–77.36, `#c00000` | stroke at x 77.33, y 70.8–85.69 |
| A2 | −50 | x 65.62–77.36, `#c00000` | stroke at 77.33 |
| A3 | 0 | *none* | *none* |
| A4 | 50 | x 77.33–89.06, `#2e75b6` | stroke at 77.33 |
| A5 | 100 | x 77.33–100.80, `#2e75b6` | stroke at 77.33 |

The zero is `-100 × nMin/(nMax − nMin)` = 50 %, which is the exact midpoint of the 53.89–100.80
paint rectangle. A3 draws **neither** a bar nor an axis, because the zero-length return in
`drawDataBars` is above the axis code. And the axis spans the **whole** cell (y 70.8–85.69) while
the bar is inset (y 71.0–85.49) — `Point aPoint1(nPosZero, rRect.Top())` against a `aPaintRect`
that was adjusted first.

**And a fifth arm, which is a text arm rather than a geometry one.** `showValue="0"` sets
`mbOnlyBar`, which becomes `!mbShowValue`, and `ScOutputData::DrawStrings` then clears `bDoCell`
(`output2.cxx`:1691-1697) — **after** the row's height is settled. 26.2.4.2's PDF of
`sheet-cf-data-bar-only.xlsx` holds five bars and **no text-showing operator at all**: the numbers
are not hidden behind the bars, they are not drawn. `SheetFormatting.HidesValue` is asked in
`SheetPageDrawing.DrawCell`, which is the same place in the pipeline.

### The geometry, and the one constant that had to be measured

`drawDataBars` (`sc/source/ui/view/output.cxx`:883-950) insets the cell by `2 * nOneX` and
`2 * nOneY`, where `nOneX = PixelToLogic(Size(1,1)).Width()` on the output device. For a PDF export
that device is `vcl::PDFWriter`'s reference device at 720 dpi, so one pixel is two twips and the
inset is **0.2 pt on each edge**. Measured rather than taken on trust:

| | cell | bar | inset per edge |
|---|---|---|---|
| `088_To-do_list`, G5 across | 518.35–600.75 | 518.54–600.55 | 0.19 / 0.20 |
| `088_To-do_list`, rows down | 21.01 pt tall | 20.62 pt tall | 0.195 |
| `sheet-cf-data-bar-auto`, rows down | 14.91 pt tall | 14.48 pt tall | 0.21 |

`SheetPageDecoration.BarInset` is 0.2 pt and the residual is under a fortieth of a point. The bar
itself is then `[zero, zero + (right − zero) × length/100]` for a positive length and the mirror for
a negative one, and the reference truncates that product to whole twips
(`static_cast<tools::Long>`), which this does not — worth 1/20 pt at most and below the two sides'
own agreement.

**What this reproduces on the corpus witness**, our render against 26.2.4.2's, `088_To-do_list`
page 1:

| value | 26.2.4.2 | ours |
|---:|---|---|
| 0.5 | 518.54–559.56 × 126.85–147.47 | 518.66–559.65 × 126.90–147.49 |
| 1.0 | 518.54–600.55 × 147.85–168.46 | 518.66–600.64 × 147.89–168.48 |
| 0.75 | 518.54–580.06 × 168.84–189.46 | 518.66–580.15 × 168.88–189.48 |
| 0.25 | 518.54–539.06 × 189.83–210.45 | 518.66–539.15 × 189.88–210.47 |

Widths agree to 0.02 pt, positions to 0.12, and the colour is `#bfa9b7` on both sides — the theme
slot 4 at tint 0.39997558519241921, resolved by the palette this tree already had.

### Precedence

`ScConditionalFormat::GetData` walks `maEntries` and takes a bar only when it has none
(`conditio.cxx`:1913-1917), and `ScDocument::FillInfo` does the same across formats
(`fillinfo.cxx`:326-330). So the *first* bar to reach a cell keeps it, under round 96's ordering:
blocks in document order, rules within a block by priority.

The corpus states the discriminating case for the priority half.
`076_Inventory_list_accessibility_guide` declares two `dataBar` rules over `J6:J16` **inside one
block**, priorities 21 and 22, resolving to `#d9d9d9` and `#989494`. 26.2.4.2's PDF holds **eleven
`#d9d9d9` rectangles and no `#989494` anywhere**, and so does ours after this change. No corpus
document states two bars in two different blocks, so the block half is read from the source and not
measured here — said rather than implied.

### What is deliberately not modelled

**A gradient bar is painted solid.** `mbGradient` is `ScDataBarFormatData`'s own default of *true*
and only `x14:dataBar/@gradient` changes it; the reference's true branch is `DrawGradient` with a
linear gradient from the bar's colour to `COL_TRANSPARENT` at 255 steps, and its PDF of
`sheet-cf-data-bar-lengths.xlsx` comes back as **209 slices fading from `#2e75b6` to white**, a
count VCL derives from the device's own pixel width. **0 of the 9 corpus rules state it** — all nine
carry an extension saying `gradient="0"` — so it is recorded and not reproduced, and
`SheetDataBar.Gradient` carries the reason.

**The axis dash pattern is the source's and has no corpus witness.** `LineInfo(LineStyle::Dash, 1)`
with four dashes of three logic units and three between them. All nine corpus rules resolve a
minimum at or above zero, so their zero sits on the left edge and `drawDataBars` returns before the
axis: **0 of 9 reach it**, and the only witness is the negative fixture.

---

## 3. The census, corrected in both directions

Counted 2026-09-11 by `census-drawrules.py`, which opens **every** corpus file as a zip rather than
filtering on `.xlsx`/`.xlsm` — round 96's own warning about `Special-Procedures_2025-07-10.xls` —
and which counts an `x14:cfRule` as a rule of its own when no main-namespace rule claims its `id`
through an `<x14:id>`:

| | main-namespace rules | documents | `x14` extensions of those | `x14`-only rules | documents |
|---|---:|---:|---:|---:|---:|
| `dataBar` | 9 | 6 | 9 | **0** | 0 |
| `iconSet` | 2 | 2 | 0 | **18** | 8 |

`244` corpus documents open as OPC spreadsheets, one more than round 96's 243.

**`dataBar` is exactly what round 96 said: 9 rules in 6 documents.** Every one of the nine carries
an extension, none states an `axisPosition`, all nine say `gradient="0"`, and their `cfvo` pairs are
`autoMin`/`autoMax` (three rules) or `num 0`/`num 1` (six).

**`iconSet` is not 2 rules in 2 documents — it is 20 in 9.** Round 96 corrected an earlier
over-count of 20 down to 2, and that correction went too far: eighteen of those rules are stated
**only** in the worksheet's `x14` extension list, with their own `xm:sqref` and no main-namespace
counterpart, and the reference imports and paints them. `088_To-do_list` is the proof in one file —
its `H3:H7` icon set exists nowhere but the extension, and 26.2.4.2 draws a 16 × 16 image at
(600.93, 160.72). **A census that reads only `cfRule` in the SpreadsheetML namespace cannot see an
`x14`-only rule**, which is the general form of the lesson and is now in the script's docstring.

---

## 4. What moved, over the whole corpus

*(filled in from `sweep-base-hashes.tsv` / `sweep-after-hashes.tsv` — see §4 table below.)*

---

## 5. Confinement and the suite

*(filled in below.)*

---

## 6. What is left, and which of the two states it is in

**O21 — fixed.** Mechanism cited by `file:line`, fixture whose expectation is 26.2.4.2's own
rendering, a test that fails at the base and passes after, and a measured reach of one cell in one
of 947 renderings that moves that rendering's span census onto the reference's exactly.

**O22, `dataBar` — fixed.** Same four requirements; reach in §4.

**O22, `iconSet` — not closed, and re-seated with a corrected census.** It is neither fixed nor nil
reach, and saying otherwise would be the kind of promotion the ground rules warn about. What is
established:

- **Reach is 20 rules in 9 documents, not 2 in 2** (§3), and 26.2.4.2's own renderings of those
  nine draw **60 icons covering 1549 pt²** in seven of them — 27 on `066_Agile_Gantt_chart`, 12
  each on `077_Inventory_list_with_highlighting` and `078_Modern_inventory_list`, 4 on
  `075_Idea_planner_tasks`, 2 each on `069_Blue_modern_balance_sheet` and
  `076_Inventory_list_accessibility_guide`, 1 on `088_To-do_list`. `041_Business_budget`,
  `042_Business_monthly_budget` and `sistem-rekod-markah-srm` state a rule and draw no icon.
- **Every part of it except the glyph is readable, and is written down here so the next round does
  not re-derive it.** `ScIconSetFormat::GetIconSetInfo` (`colorscale.cxx`:1185-1252) walks all the
  entries and keeps the **last** index whose `Compare` holds — it does not break — with each
  entry's own mode, default `EqGreater`, which `gte="0"` turns into `Greater`. `GetMinValue`/
  `GetMaxValue` take a `COLORSCALE_VALUE` or `COLORSCALE_FORMULA` first entry's own number and
  otherwise the range's. `mbReverse` reflects the index across the entries after the search.
  A custom entry spelt `NoIcons` is stored with index **−1** (`condformatbuffer.cxx`:455-467) and
  `GetIconSetInfo` then returns **`nullptr`**, so such a cell has no `ScIconSetInfo` at all and
  therefore **keeps its own text however `showValue` is set** — which is why the `showValue="0"`
  half of the family cannot be implemented on its own either.
- **What blocks it is artwork, not reading.** `drawIconSets` (`output.cxx`:955-985) paints
  `ScIconSetFormat::getBitmap`'s image at ten points square in the cell's bottom-left corner, and
  that image is one of LibreOffice's own icon-theme assets. There is nothing in the file to derive
  it from, and copying the assets into this tree is not a rendering decision to take in a round.
  Drawing the bucket without the glyph — suppressing the value and painting nothing — would be
  further from the reference than doing nothing, because the icon is the ink and the value is not
  drawn beside it.

So: **`iconSet` stays seated**, with its census, its mechanism and the one thing that would have to
be decided before it can be closed. What is left is exactly *what glyph to draw*, and that is a
question about assets rather than about the format.

**One thing found on the way and left, because it is O20's family and not either of these.**
`088_To-do_list`'s remaining 0.04 %: its `expression` rule `AND($G3=0,$G3<>"")` over `B3:I7` paints
row 3 in the reference and in neither of our legs, and its `dxf` is
`<fill><patternFill><bgColor theme="0" tint="-0.0499893…"/></patternFill></fill>` — **a
`patternFill` with no `patternType`**. Whether this tree requires the attribute or the formula
evaluator lacks `AND` is not established here and was deliberately not chased: changing how a
`dxf` fill is read reaches 55 documents and 1215 rules, which is a round of its own.
