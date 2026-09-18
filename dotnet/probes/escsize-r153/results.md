# Round 153 — O78 seat A: an escaped size is truncated to a twip, not rounded

**Written incrementally.** Each section was written as its measurement finished.

## 0. What this round is

Round 152 (`probes/faa-r152/results.md` §7) named two seats for **O78** and said seat A must ship
first: `Escapement.SizeOf` quantises a superscript's size differently from 26.2.4.2, and on
`FAA 2025-26 Holdover Tables.docx` that one tenth of a point is the whole of a 7.05 pt wrap, a
5.00 pt displacement at the foot of page 82 and a blank page 83.

It also named the rule to put in its place — *round to the nearest tenth of a point* — and this
round **refutes that** and ships truncation to a twip instead. The two agree at the one base size
round 152 could discriminate at, which is why its conclusion survived its own controls.

### Environment

| | |
|---|---|
| tree | `/home/user/libreoffice-core`, base `a3159ce0d` *(A split table row is ruled once at the cut (O83))* |
| ours | `dotnet/tools/…/Paperless.Cli`, rebuilt 2026-09-18 12:18:43 UTC for the `head` leg |
| reference | `/opt/libreoffice26.2/program/soffice` — **LibreOffice 26.2.4.2 `0229ac93fcf0d7cbc6376066c6f35021cef002dc`** |
| C++ read | this checkout, `configure.ac` **27.2.0.0.alpha0+** — *not* the reference binary's source |
| date | 2026-09-18 |

---

## 1. [bin] The instrument round 152 used cannot see this rule, and the reason is one line of the PDF writer

`esc-score.py` in that round reads `span['size']` out of the rendered PDF, which is the `Tf`
operator. Rendering `data/adv.docx` — one superscript run at each of 33 base sizes — and reading
both channels off the *same* PDF:

| base | what the `Tf` says | what the ADVANCE says |
|---|--:|--:|
| 5.5 pt | 3.2 pt = **64 tw** | **62.977 tw** |
| 6.0 pt | 3.5 pt = **70 tw** | **69.001 tw** |
| 8.0 pt | 4.6 pt = **92 tw** | **91.971 tw** |
| 11.0 pt | 6.4 pt = **128 tw** | **126.989 tw** |
| 18.0 pt | 10.4 pt = **208 tw** | **207.992 tw** |

**Every one of the 33 `Tf` values is a whole tenth of a point**, and at 21 of the 33 it is not the
size the layout used. So "26.2.4.2 rounds an escaped size to a tenth of a point" is a statement
about `pdfwriter_impl`'s number formatting, and the tenth-of-a-point rule was fitted to it.
`data/adv-ref.txt`.

## 2. [bin] The rule, measured through an advance: truncate to a whole twip, 81 of 81

The channel is CLAUDE.md's own prescription for an advance — difference two **right-aligned** lines
so that every fixed term cancels. Each arm is a right-aligned line holding a label and then N
copies of one digit in a superscript run; two arms per cell differ only in N (2 and 42), so the
difference of the digit span's left edge is exactly 40 advances of one glyph at whatever size the
layout used, with the label, the margin and the side bearing all cancelled. The glyph's `hmtx`
advance is read from the face itself, so the size follows with no free parameter.

| fixture | what varies | arms | `trunc` | `round` to a twip | `round` to a tenth |
|---|---|--:|--:|--:|--:|
| `make-adv.py` (`.docx`, `w:vertAlign`, proportion 58) | 33 base sizes, 4.0–20.0 pt | 33 | **33** | 21 | 18 |
| — *of those, where the three rules disagree* | | 21 | **21** | 9 | 6 |
| `make-prop2.py` (`.fodt`, `style:text-position`) | proportions 25–99 at 11 and 13 pt | 48 | **48** | 24 | 22 |
| — *of those, where the three rules disagree* | | 38 | **38** | 14 | 12 |

**Truncation is right at 81 of 81 and at 59 of 59 discriminating arms; no other candidate is.**
`data/adv-ref.txt`, `data/prop2-ref.txt`.

Two notes on the second fixture. The 40 pt arms are dropped — 42 digits at that size overrun the
measure — and a strongly raised small run lands on a PyMuPDF *line* of its own, so `prop-score2.py`
pairs a label with its digits geometrically (the digits begin where the label ends, on the same
row) rather than by being on one line. The first cut of that pairing matched on x alone and
produced a table of plausible nonsense; requiring the y overlap as well is what fixed it.

## 3. [src] And that is what the C++ does, in two places

`SwSubFont::SetSize` (`sw/source/core/inc/swfont.hxx`:772-783) sets the shrunk font to

```cpp
Font::SetFontSize( Size( m_aSize.Width()  * GetPropr() / 100,
                         m_aSize.Height() * GetPropr() / 100 ) );
```

— integer division, in the layout's own unit, which for Writer is the twip.
`SvxFont::SetPhysFont` (`editeng/source/items/svxfont.cxx`:348-364) does the same for an EditEngine
text. Neither rounds, and neither knows about a tenth of a point.

This is the [src] half; §2 is the [bin] half and stands on its own.

## 4. The change

`dotnet/src/Paperless.WordProcessing/Layout/Escapement.cs`:

```csharp
-    => Proportion is 0 or 100 ? emSize : Twips(emSize.Twips * Proportion / 100.0);   // Math.Round
+    => Proportion is 0 or 100 ? emSize : Length.FromTwips(emSize.Twips * Proportion / 100);
```

`SizeOf` is the single seat all four word readers use — `DocxLayoutSource.cs`, `OdtLayoutSource.cs`,
`RtfReader.cs`, `DocReader.cs` — so this reaches DOCX, ODT, RTF and WW8 at once. The two rules part
company at **29 of the 57 half-point sizes** between two and thirty point at proportion 58, which is
why the round is swept whole-corpus rather than censused.

The file's own remarks carried the refuted claim — *"58% of eleven point is 127.6 twips, and
LibreOffice draws the citation at 128 — 6.4 pt, not 6.38"* — which is §1's `Tf` read of exactly this
document. It is replaced by the measurement and by a warning not to re-measure it that way.

## 5. The fixture, the tests and the pin

`dotnet/tests/corpus/features/words-escapement-size.fodt` — seven paragraphs, all drawing the same
word at eleven point, differing in one attribute: no escapement, then proportions 58, 49, 53 (as a
subscript), 25 and 100, then a paragraph holding an unescaped run and an escaped span. A width
ratio is therefore a size ratio exactly.

Confirmed at the reference before it was asserted: 26.2.4.2 draws the 58, 49 and 25 per cent arms
at **127.12, 107.12 and 55.03** twips of the unescaped arm's width against this tree's 127, 107 and
55, where the rounded answers would be 128, 108 and 55. `data/fixture-ratios.txt`.

`EscapementSizeTests` is 24 assertions: the arithmetic at eight base sizes where the rules differ,
at seven proportions, the two no-op proportions, the five drawn arms and the span. **Mutation-pinned**
(`mutate.sh`) against four rival rules, each built and run in turn, with the harness grepping the
build output for compiler errors so that a non-compiling arm cannot read as a passing one:

| arm | result |
|---|---|
| round to a twip | **18 of 24 failed** |
| round to a tenth of a point | **14 of 24 failed** |
| no quantisation at all | **18 of 24 failed** |
| truncate to a tenth of a point | **14 of 24 failed** |
| base (truncate to a twip) | 24 passed |

## 6. Reach

`census.py`, `data/census.txt` — the denominator, not the reach:

| | states an escapement |
|---|---|
| corpus `.docx`/`.docm` (`w:vertAlign` super/subscript) | **57 of 271** |
| converted `.odt` (`style:text-position` with a proportion below 100) | **84 of 337** |
| converted `.rtf` (`\super`/`\sub`) | **84 of 337** |
| corpus `.doc` | not censusable statically |

**Every proportion the corpus states is 58** — 417 `super 58%` and 60 `sub 58%` over the 96 `.odt`
that state a text position at all, and nothing else below 100 — so the other proportions in §2 are
authored, and `style:text-position`'s rise is written by LibreOffice as the keyword `super`/`sub`
rather than as a percentage. A census matching only the numeric spelling finds **none** of them;
the first cut of `census.py` did exactly that and reported 0 of 337.

## 7. [bin] O78's witness: the document lands where round 152 said it would, to the hundredth

`FAA 2025-26 Holdover Tables.docx`, this leg's own render against the banked 26.2.4.2 rendering in
`/home/user/refpdfs-words-26.2.4.2/1/` (rendered 2026-09-17, after the Narrow-font move, so it is
not one of the suspect ones).

| | 26.2.4.2 | base (round 152's measurement) | **head** |
|---|--:|--:|--:|
| pages | 167 | 167 | **166** |
| last grid line on page 82 | 544.70 | 549.70 | **542.65** |
| displacement at the foot of page 82 | — | **+5.00** | **−2.05** |
| Σ of the 33 row deltas | — | +5.05 | **−2.00** |
| page 83 | 1751 alphanumerics | **74 — blank** | **correct** |
| pages whose orientation differs | — | 10 | **2** |

Round 152 simulated this fix by re-spelling the document's superscript as an explicit `w:sz` and
measured **−2.05**, Σ **−2.00**, 166 pages, page 83 correct, orientation mismatches 2. The shipped
fix reproduces its simulation **to the hundredth of a point on both figures**, which is as clean a
confirmation as that method offers: the simulation changed the document and this changed the
renderer, and they agree.

**Seat B is still open and is what the remaining 2.05 pt is** — a `w:vMerge` continuation cell's
stated top rule, which `DocxLayoutSource.Tables.Resolved` drops before `OwnTopRule` can see it.
Round 152 measured both together at **−0.05**.

**And the two pages that still differ are the defect round 152 predicted this would expose.** From
page 127 our stream runs one page *ahead*: 26.2.4.2 emits a near-empty page 127 — 160 alphanumerics,
its header and footer and nothing else — and we do not. That page was invisible while the
document's two faults cancelled in the page stream; it is now the whole of the remaining gap and it
is not this seat.
