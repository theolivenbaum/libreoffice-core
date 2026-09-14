# Round 123 — a double underline is two thinner lines, and none of the three rules was where the reference puts it

Seat `agent/dblunder`, based on `d98c8be24`. The register's **O68**, and out of it two more: **O69**,
seated as nil reach, and the offset half of O64, which is folded into this round rather than seated.

Every arm below is confirmed twice, once in the 27.2 tree and once against the installed 26.2.4.2's
own output, per `dotnet/CLAUDE.md`'s rule 1.

---

## 0. The headline

1. **The corpus reach of a double underline is one document.** 1 of 947, and it is a `.doc`;
   0 of 302 presentations, and a `.ppt` cannot state one at all. Base rate: 345 of 947 documents
   state an underline, 8080 statements between them, of which **2 are double**.
2. **The slides half of O68 is dead twice over** and is seated as O69 rather than implemented.
3. **The word-processing half is implemented**, through all four spellings, and the sweep's Writer
   double rows go from 0 of 90 to 90 of 90.
4. **The register's own reading of the gap is refuted for the branch that matters.** *"Both lines
   sit at that thickness with a gap of one thickness between them"* is true on the HarfBuzz branch
   and false on the descent branch, where the gap is floored at **five device pixels** — and the
   three Liberation faces are on the descent branch.
5. **The reference's two lines are three thicknesses apart, not two**, because
   `drawStraightTextLine` centres the first line and then centres the second *and adds a whole
   further thickness*. `SheetTextLayout` drew them two apart, so **O64's "the Calc path is exact"
   was a statement about the thickness and about nothing else**.
6. **And no rule of any of the three kinds was where the reference puts it.** Round 120 quantised
   the thickness onto the device and left the offset a fraction of the em. Measured over 126
   authored rules per module, a single underline was **0.067 pt** out on average and **0.139 pt** at
   worst. The same device chain answers the offsets, and with it this tree reproduces
   **252 of 252** authored rules and **15 062 of 15 103** of the reference's own rules over the whole
   corpus, thickness and position together, with no free parameter.

---

## 1. The census, before any code

The register said the corpus reach was not censused. It is now, three ways, and the three agree.

### 1.1 Markup over the 947 documents

`census-ooxml.py MANIFEST.tsv /home/user/sample-files > census-corpus.tsv`. Spellings scanned:
`w:u w:val="double"|"wavyDouble"` (ST_Underline's only two two-line values, ECMA-376 17.18.99),
`a:u="dbl"|"wavyDbl"` (ST_TextUnderlineType, 20.1.10.82), SpreadsheetML `<u val="double"/>`
(CT_UnderlineProperty, 18.4.13) and ODF `style:text-underline-type="double"`.

| family | ext | docs | scannable | with a double | occurrences | with any underline | underline statements |
|---|---|---:|---:|---:|---:|---:|---:|
| sheets | xls | 64 | 0 | — | — | — | — |
| sheets | xlsm | 2 | 2 | 0 | 0 | 2 | 3 |
| sheets | xlsx | 241 | 241 | **1** | **2** | 125 | 3206 |
| slides | ppt | 51 | 0 | — | — | — | — |
| slides | pptx | 251 | 251 | **0** | **0** | 63 | 1110 |
| words | doc | 66 | 0 | — | — | — | — |
| words | docx | 272 | 272 | **0** | **0** | 155 | 3761 |
| | | 947 | 766 | **1** | **2** | 345 | 8080 |

**C9's base rate: 345 of 947 documents state an underline at all and 2 of their 8080 statements are
double** — 0.025 % of the statements, 0.29 % of the documents that have any underline, 0.106 % of
the corpus.

The single hit is `sheets/done-011/xlsx/Application_Compliance_Checklist_5_Apr_2021.xlsx`, and it is
smaller than the count suggests. One of the two is `<font><u val="double"/><sz val="10"/><name
val="Arial"/></font>` at index 19 of `styles.xml`, and **no `xf` in the workbook references font 19**
— a dead style. The other is a rich-text run inside shared string 251 whose text is **a single
space**, used in cells `E238` of sheet2 and `E581` of sheet3. So the whole live spreadsheet reach is
two spaces.

### 1.2 The converted ODF and RTF corpus

181 of the 947 are binary and no markup grep reaches them. `/home/user/corpus-odf` is 26.2.4.2's own
`--convert-to` of the whole corpus — odt 338, rtf 338, ods 307, odp 302 — so it is what the
reference *understood*, which is the only thing that can be drawn. `census-odf.py`,
`census-odf.tsv`.

| ext | files | with a double | occurrences | with any underline |
|---|---:|---:|---:|---:|
| .odt | 338 | **1** | 2 | 204 |
| .rtf | 338 | **1** | 162 | 204 |
| .ods | 307 | **1** | 1 | 307 |
| .odp | 302 | **0** | **0** | 136 |

Two documents, and one of them is the spreadsheet above. The other is
`words/done-004/odt/RobertQ_Service.odt`, whose original is `words/done-004/doc/RobertQ_Service.doc`.
The `.rtf` column's 162 is the same document: RTF restates a character property per run.

**A control on the instrument.** LibreOffice's ODF exporter writes `style:text-underline-type` only
when it is `double` — over all 947 converted files the attribute occurs **3 times and every one says
`double`**, against 2581 `style:text-underline-style="solid"`. So a zero in the `.odp` column is a
zero and not a spelling artefact, which is the check CLAUDE.md's namespace rule asks for.

### 1.3 The `.doc` bytes, independent of `soffice`

`census-ww8.py` scans each `.doc` for WW8's `sprmCKul`, opcode `0x2A3E` little-endian, whose operand
3 is double (`sw/source/filter/ww8/ww8par6.cxx`:3605). The scan is over the whole file rather than a
parsed FKP, so it over-counts and cannot under-count — a zero is evidence.

**1 of 66 `.doc` carries operand 3, exactly twice, and it is `RobertQ_Service.doc`** — the same
document and the same count as its ODT conversion, from a different instrument. Operand histogram
over the 66: single 599, none 53, **DOUBLE 2**, by-word 3, dot-dot-dash 1, dash 1.
`census-ww8.tsv`.

### 1.4 A `.ppt` cannot state one

`filter/source/msfilter/svdfppt.cxx`:5575-5576 imports `PPT_CharAttr_Underline` as
`nVal != 0 ? LINESTYLE_SINGLE : LINESTYLE_NONE` — one bit — and `LINESTYLE_DOUBLE` appears nowhere in
the `.ppt` reader. So 51 of the 302 presentations are structurally incapable of carrying a double
underline, and §1.2 says the other 251 empirically do not.

### 1.5 What the reference actually draws

`RobertQ_Service.doc`'s own banked reference rendering (`/home/user/gate-r122/ref`) draws **three
double underlines, six rules, on pages 1 and 4 of 4** — the 16 pt Liberation Serif Bold titles
*APPLICATION FOR EMPLOYMENT* and *PLEASE READ CAREFULLY* and the 14 pt *SERVICE DEPARTMENT*
subtitle. They are headings rather than incidental text, which is why the seat is worth closing at a
reach of one.

**So: words 1 of 338 (0.30 %), slides 0 of 302, sheets 1 of 307 whose live ink is two spaces.** With
the conversions, three renderings of one document out of 947 + 1285.

---

## 2. `ImplInitTextLineSize`, both branches, and what the register got wrong

`FontMetricData::ImplInitTextLineSize` (`vcl/source/font/fontmetric.cxx`:200-352) answers in whole
720 dpi device pixels — that is O64 — and it answers **an offset per line as well as a size**.

**HarfBuzz branch** (`:200-260`), taken unless the family is in
`Office::Common::Misc::FontsDontUseUnderlineMetrics`, which in the installed 26.2.4.2's
`share/registry/main.xcd` is exactly *Liberation Serif, Liberation Sans, Liberation Mono*:

```
nSize  = post.underlineThickness x scale        nOffset = -post.underlineOffset x scale
mnDUnderlineSize    = ceil(nSize x 2/3)                                        :236
mnDUnderlineOffset1 = ceil(nOffset - nSize/2)                             :234, :237
mnDUnderlineOffset2 = mnDUnderlineOffset1 + mnDUnderlineSize x 2               :238
```

**Descent branch** (`:261-352`):

```
n2LineHeight = ((descent x 16) + 50) / 100, min 1                              :297
n2LineDY     = max(n2LineHeight, 1 + DPIY/150)                             :299-306
mnDUnderlineOffset1 = nUnderlineOffset - n2LineDY/2 - n2LineHeight             :318
mnDUnderlineOffset2 = mnDUnderlineOffset1 + n2LineDY + n2LineHeight            :319
```

### 2.1 The register's "gap of one thickness" is true on one branch and false on the other

On the HarfBuzz branch the two tops are `2 x size` apart, so the gap between the lines is one
thickness and the row is right. On the descent branch they are `n2LineDY + n2LineHeight` apart, and
`n2LineDY` is **floored at `1 + DPIY/150`** — #117909's *"add some pixels to minimum double line
distance on higher resolution devices"*. **The PDF writer's device is 720 dpi, so that floor is five
pixels**, and it binds wherever the double underline is thinner than five pixels — which is every
Liberation face below about 12 pt. Since the three Liberation faces are precisely the ones on the
descent branch, the branch the register describes correctly is the branch a corpus set in Arial,
Times New Roman and Courier New never takes.

### 2.2 Two descents, and they are different numbers

The thicknesses take the #i55341-**clamped** descent; `nUnderlineOffset` takes the raw `mnDescent`
(`:315`). A face whose clamp fires — Liberation Mono at every size in this corpus — has its rules
*sized* off one number and *placed* off another. Reading one descent for both is wrong on Mono and
right everywhere else, which is the kind of error a small probe misses.

### 2.3 The second line carries an extra whole thickness, and it is the reference's

`PDFWriterImpl::drawStraightTextLine` (`vcl/source/pdf/pdfwriter_impl.cxx`:6740-6861):

```
nOffset   = nLineHeight / 2                     integer, tdf#154235          :6741
nLineHeight = HCONV(nLineHeight)                                             :6751
nLinePos    = HCONV(nLinePos  + nOffset)                                     :6752
nLinePos2   = HCONV(nLinePos2 + nOffset)                                     :6753
line 1 at  -nLinePos                                                    :6845-6850
line 2 at  -nLinePos2 - nLineHeight                                     :6853-6858
```

tdf#154235 added `nOffset` to *both* offsets so that each states the middle of its own line; the
second then keeps a whole further `nLineHeight` when it is emitted. The two centres therefore come
out **three thicknesses apart** where the metric put them two apart, and the edge gap is two
thicknesses. It is asymmetric and it is almost certainly a leftover. **It is recorded and reproduced
because it is what 26.2.4.2 draws, not because it is what tdf#154235 intended.**

---

## 3. Against the binary: 252 of 252

### 3.1 The position probe

Round 120's sweep scored a **median thickness** and nothing else — `read-rules.py` never reports
where a rule is — so nothing in its bank could distinguish a second line one thickness below the
first from one three below. `make-posprobe.py` authors the same shape of fixture at a reduced grid
and `read-pos.py` reads each rule's own offset from its own baseline:

- six faces (three on each branch, and Liberation Mono's clamp fires at every size),
- **seven sizes reaching down to 6 pt**, because the five-pixel floor binds only below about 12 pt
  and a probe of large text cannot see the term at all,
- three kinds,
- two modules — Writer in twips and Calc in hundredths of a millimetre.

No Impress leg, deliberately: §1 closes the slides half as nil reach, so authoring a fixture for it
would be measuring a path no document takes.

```sh
python3 make-posprobe.py $OUT
for leg in ref1 ref2; do for e in fodt fods; do
  /opt/libreoffice26.2/program/soffice -env:UserInstallation=file://$OUT/$leg/profile \
      --headless --norestore --convert-to pdf --outdir $OUT/$leg $OUT/pos-$e.$e
done; done
python3 read-pos.py $OUT/ref1/pos-$e.pdf pos-manifest.tsv > ref-pos-$e.tsv
python3 predict-pos.py pos-manifest.tsv ref-pos-$e.tsv $e
```

**C11:** each module was rendered twice into separate profiles and the two readings are
byte-identical, so nothing here rests on a single run.

### 3.2 The result

`predict-pos.py` implements the whole chain — both branches, both descents, the five-pixel floor,
`HCONV`, tdf#154235's half-thickness and the second line's extra one — with no free parameter.

| leg | rules | exact on thickness **and** position |
|---|---:|---:|
| Writer (`pos-fodt.fodt`, whole twips) | 126 | **126** |
| Calc (`pos-fods.fods`, whole 1/100 mm) | 126 | **126** |

Tolerance 0.0015 pt, which is the channel's own floor: `appendMappedLength` writes thousandths of a
point.

### 3.3 And on the corpus's own faces, with no new render at all

`/home/user/gate-r122/ref` holds one banked 26.2.4.2 rendering per corpus document. `corpus-rules.py`
walks all 947, finds every thin horizontal stroke that sits in an em-scaled window of a span's
baseline **and matches that span's x-extent at both ends** — a text line is drawn across the advance
of its own run, where a border spans its cell — and scores it.

Two exclusions, both stated rather than quietly dropped:

- **Scaled pages.** A slide, a shrunk cell or a sheet printed to a scale is drawn through a
  transform, so the page states the reference's number multiplied by something this instrument
  cannot see. O64 is what separates them: an unscaled rule's thickness is a whole logical unit and a
  scaled one is not. **1184 rules.** A half-point test on the *size* does not do it — a scale of
  0.975 turns a 5.13 pt em into a clean 5.00, which is how the first cut of this scan produced 1116
  spurious misses.
- **Document-embedded faces**, whose metrics are the file's rather than the machine's: **151 rules**
  across Roboto, Montserrat, Rubik, Verdana, Arial Narrow, Alegreya Sans, Play and Noto Sans
  Armenian. Plus 84 rules on faces that resolve to a file this reader cannot parse.

| kind | scored | exact |
|---|---:|---:|
| single underline | 9125 | **9084** |
| strikethrough | 5978 | **5978** |
| | 15 103 | **15 062** (99.73 %) |

By track: docx 2394 of 2410, doc 613 of 621, pptx 661 of 661, ppt 246 of 260, xlsx 7590 of 7593,
xls 3526 of 3526, xlsm 15 of 15.

**All 41 misses are accounted for and none of them wants a constant** (`corpus-rules-misses.txt`):

| n | what |
|---:|---|
| 14 | `ws_prod-…-European-Safety-Strategy-Initiative.ppt`: a shadowed run draws its rule twice, and this scanner matches both to both spans |
| 6 | `RobertQ_Service.doc`'s own **double** underlines, which the scanner classifies as two singles — the model predicts them exactly (§3.4) |
| 6 | a **bold** underline, `LINESTYLE_BOLD`, on `License App Instructions 2-22.docx` and `OM template for non-complex NCC operators.docx`. `nBLineHeight = ((descent x 50) + 50) / 100` reproduces both to the digit — Liberation Serif at 12 pt gives 13 px = 1.3 pt against a drawn 1.3, Liberation Sans Bold at 17 pt gives 18 px = 1.8 pt against a drawn 1.8. **This engine has no state for a bold underline and draws it at the single thickness.** Reach: 6 rules in 2 of 947 documents |
| 6 | a glyph-fallback face: the rule's metric is the run's own face and this scanner reads the face the *span* was subset under, which for a fallback run is DejaVu Sans or FreeSerif |
| 9 | a paragraph or cell border that happens to underrun a full-width run, plus two rows whose thickness lands on a whole unit by luck on a scaled page |

### 3.4 The corpus's one double underline, before the probe existed

The chain was settled on the banked reference before a single new render, which is worth recording
because it means the numbers do not depend on the probe. `RobertQ_Service__doc.pdf` draws its rules
at **1.2 and 3.0 pt below the baseline at 0.6 thick** and **1.1 and 2.6 at 0.5** — separations of
exactly 3 x thickness. Hand-computing the chain for 16 pt Liberation Serif Bold: em 160 px, descent
35, `n2LineHeight` 6, `n2LineDY` max(6, 5) = 6, offsets 9 and 21, `nOffset` 3, so `HCONV(12)` = 24
twips = **1.2 pt** and `HCONV(24) + HCONV(6)` = 48 + 12 twips = **3.0 pt**. Four of four to the
digit, on a document nobody authored for the purpose.

---

## 4. What changed

### 4.1 The state

`TextUnderline { None, SingleLine, DoubleLine }` in `Paperless.Text.Fonts` — the shape
`SheetUnderline` already had, spelled the same way because CA1720 rejects `Single` and `Double`. It
replaces the `bool` on `PageRun`, `WordCharacterFormat`, `WordTextStyle`, `OdfTextFormat`,
`OdfTextStyle`, `RtfLayoutRun`, `RtfState`, `RtfStyleFormatting`, `Ww8LayoutFormat` and
`Ww8LayoutRun`; each keeps an `IsUnderlined` that reads `!= None`, so the extraction side is
untouched.

| format | spelling | mapped by |
|---|---|---|
| WordprocessingML | `w:u w:val="double"`, `"wavyDouble"` | `WordCharacterFormat.UnderlineOf` |
| RTF | `\uldb`, `\ululdbwave` | `RtfDocumentReader`'s control-word switch |
| WW8 | `sprmCKul` operands **3** and **43** | `Ww8DocumentReader.UnderlineOf` |
| ODF | `style:text-underline-type="double"` | `OdfTextFormat.UnderlineIn` |

The two `wavy` forms are `LINESTYLE_DOUBLEWAVE` and come out as a double line, because this engine
draws no wave.

**The RTF prefix is a trap and the switch is written to avoid it.** `\uld`, `\uldash`, `\uldashd` and
`\uldashdd` all begin with the letters of `\uldb`, and the converted corpus holds all four; a
control word matched by prefix double-underlines every one of them.

### 4.2 The ODF half is CLAUDE.md's namespace trap from the other side

`style:text-underline-style` and `style:text-underline-type` are **two attributes of one item**:
both map to `CharUnderline` with `MID_FLAG_MERGE_PROPERTY` (`xmloff/source/text/txtprmap.cxx`:177-179)
and their handlers merge into whatever the *same element* has already contributed
(`xmloff/source/style/undlihdl.cxx`:114-160). So they cannot be resolved one at a time through the
cascade — an outer style's type would be merged with an inner style's style. That is the
`style:font-name` / `fo:font-family` rule inverted: there two spellings say the same thing, here two
attributes each say part of one thing, and both need the **level decided before the attribute**.

`OdfStyles.ResolveTogether` picks the innermost level stating *either* and reads both from there.

**The witness proves it rather than illustrating it.** `RobertQ_Service`'s `Subtitle` style states
both attributes; **four** of its automatic children restate `style:text-underline-style="solid"`
alone and **three** restate neither. 26.2.4.2 draws the four with one line and the three with two.
Resolving the type independently would have double-underlined all seven.

### 4.3 The geometry

`LineSpacing.RuleWidths` gains four members — `UnderlineOffset`, `StrikeoutOffset`,
`DoubleUnderlineFirst`, `DoubleUnderlineSecond` — all top edges on the grid, all from the chain in
§2. The three drawing sites take them and no longer scale a design unit:

| site | grid | what it used to do |
|---|---|---|
| `PageDrawing.Rules` | `MetricGrid.WriterTextLine` | one line at the single thickness for a double; design-unit offsets |
| `SheetTextLayout.Rules` | `MetricGrid.TextLine` | right thickness, **wrong offsets** — the single underline's for line 1 and twice the thickness below it for line 2 |
| `SlideTextLayout.Rules` | `MetricGrid.TextLine` | design-unit offsets; no double state, and none added (O69) |

### 4.4 O64's row overstates itself, and this is the correction

O64 closed on **810 of 810**, and that figure is a *median thickness*: `read-rules.py` takes the
median of the rules it finds in a window and reports no position at all, so the sweep it was scored
on **structurally could not see** where a rule sat. Two consequences:

- *"`SheetUnderline` has the state and the Calc path is exact"* was true of the thickness and false
  of the position. Calc's second line was one thickness below the first where the reference puts it
  three.
- The register's C16 row carries *"the two sides' top edges within **0.02 pt** on 7449 rules of
  `TK-Syllabus`"*, and round 120 cited it as the reason not to touch position. Those 7449 are
  **strikethroughs on one document**, and a strikethrough is the best case: over the 126-rule probe
  a strikethrough is 0.030 pt out on average and a single underline **0.067 pt, worst 0.139**. The
  figure was right about its own rules and not representative of the class.

---

## 5. Before and after

### 5.1 Round 120's own sweep, all 810 rules

<!--SWEEP810-->

### 5.2 The position probe, ours against the reference

| | Writer | Calc |
|---|---|---|
| single underline, before | 7 of 42 | 0 of 42 |
| strikethrough, before | 5 of 42 | 4 of 42 |
| double underline, before | 0 of 42 (one line drawn) | 0 of 42 (two lines, wrong offsets) |
| **all three, after** | **126 of 126** | **126 of 126** |

Mean absolute offset error before: single 0.0633 pt (Writer) / 0.0670 (Calc), strikethrough 0.0307 /
0.0302, worst 0.1387. After: 0.0000 / 0.0003.

### 5.3 Reach

<!--REACH-->

---

## 6. Tests

<!--TESTS-->

---

## 7. What this round did not do

- **The slides double underline.** Seated as **O69**, nil reach, with both legs.
- **A bold underline** (`w:u w:val="thick"`, `\ulth`, `sprmCKul` operand 6, ODF
  `style:text-underline-width="bold"`), which the reference draws at
  `((descent x 50) + 50) / 100` — about twice the single thickness — and this engine draws at the
  single thickness. Measured at **6 rules in 2 of 947 documents** in §3.3, and the arithmetic that
  would close it is already written down there. Not seated as its own row: it is a line of
  `ResolveRuleWidths` and a fourth enum member whenever a document asks for it.
- **A wave, a dot or a dash.** Every pattern is still drawn solid, which is unchanged and is what
  the four readers' comments already said.
- **The `above` variants** (`ImplInitAboveTextLineSize`), which nothing in this engine draws.
