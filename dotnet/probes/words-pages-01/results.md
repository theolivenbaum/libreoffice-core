# words-pages-01 — two one-page shortfalls, two unrelated causes, one of them shared

Round brief: `words/batch-004/doc/1447.doc` (3 pages against 4) and `words/batch-006/doc/003.doc`
(4 against 5), both `.doc`, both word-exact, both nominated as the cleanest instances of the
standing **under-paginate** class — "a vertical-budget error (line height, text-area height, or the
line-fits rule)".

**The brief's framing is wrong for both documents**, and that is the round's first result. Neither
is a vertical-budget error. One is a font substitution the gate's font column cannot see; the other
is an empty paragraph measured at the wrong size by the WW8 reader. They have nothing in common
beyond the symptom.

`prediction.md` was committed at `bacadc1f517` before any fix was written or any sweep run.

---

## 1. `003.doc` — an empty paragraph takes the CHPX that ends at its mark

### What was measured

Page 2 onwards is identical in pitch, line for line — 13.80, 27.60, 47.40, 19.80 pt in both
renderings — and differs only in where the page starts. The whole error is on page 1 and it is
**32.20 pt**, which is exactly three empty paragraphs measured at 12 pt where LibreOffice measures
them at 14, 36 and 14:

| empty paragraph | reference | ours | deficit |
|---|---|---|---|
| after "Tisková a informační služba" | 16.10 pt (14 pt) | 13.80 pt (12 pt) | 2.30 |
| after "INFOSERVIS" | 41.40 pt (36 pt) | 13.80 pt (12 pt) | 27.60 |
| after the empty Heading 2 | 16.10 pt (14 pt) | 13.80 pt (12 pt) | 2.30 |

32.20 pt is two 16 pt lines, which is what let two extra empty paragraphs fit at the foot of our
page 1; every page after it then carried two lines more than the reference's, and the seven trailing
empty paragraphs that give the reference a fifth (blank) page fitted on our fourth.

Two instruments made this attributable rather than inferred, and both are committed here:

* **`FlowProbe`** dumps every placed line with its box height and the paragraph it came from. An
  empty paragraph draws no ink, so `pdftotext -bbox` cannot see it on either side and the
  arithmetic above cannot be done from the PDFs alone.
* **`chpx.py`** reads the `.doc` itself — piece table, `PlcfBteChpx`, CHPX FKPs — and prints the
  grpprl in force at each paragraph mark.

### The rule

`chpx.py` says all three of those marks carry **no CHPX exception at all** and `istd` 0 (Normal), so
the paragraph style gives 12 pt, which is what we used. Each of them is the first paragraph after a
CHPX run that *did* carry a size. The FKP reads:

```
run 3: fc 1094..1150  CHps=1c00 (14pt)   ends at cp 63 = an empty paragraph's own mark
run 4: fc 1150..1170  (no exception)     cp 63..72, ten empty paragraphs
run 5: fc 1170..1192  CHps=4800 (36pt)   ends at cp 84
run 6: fc 1192..1194  (no exception)     cp 84
run 7: fc 1194..1196  CHps=1c00 (14pt)   cp 85
run 8: fc 1196......  (no exception)     cp 86 onwards
```

LibreOffice draws the empty paragraphs at cp 63, 84 and 86 at 14, 36 and 14 pt, and the ones at
cp 64-72 and cp 87 at the style's 12 pt. The discriminator is whether a CHPX exception **ends at the
mark**: LibreOffice's reader closes such an attribute at offset 0 of the node that mark has already
opened, and a zero-length hint on an empty node covers the whole node.

> **An empty paragraph whose own mark carries no CHPX exception takes the one in force at the
> position before it.** The paragraph style stays its own — what crosses the mark is a character
> attribute, not a style — and it never crosses a story boundary.

All seven points on the document agree, **including the four that must not inherit**. Our own ODF
path lays the same document out correctly (`soffice --convert-to fodt` then `FlowProbe`), so the
layout engine was never implicated: `Ww8DocumentReader.Describe` was.

`EmptyParagraphCharacterLayout`, `src/Paperless.WordProcessing/Ww8/Ww8DocumentReader.Layout.cs`.

### Reach

Four of the corpus's 66 `.doc` carry the pattern at all (`003`, `07-04`, `A320SimNotes`,
`150_5335_5a`); three renderings moved. `003.doc` goes 4 → **5 pages, `match`**. `A320SimNotes.doc`
goes 40 → 41 against 42, and `P200904290238_0238_51880.doc` re-flows without changing its count.

**No committable fixture exists for this.** Every `.doc` in `tests/corpus/features` is a LibreOffice
export, and LibreOffice's own DOC writer emits an explicit CHPX at every paragraph mark — scanned,
0 of 46. The corpus rules forbid committing a document from the web. The rule is therefore pinned by
this file and by `chpx.py`, which reproduces the FKP table above from the bytes.

---

## 2. `1447.doc` — the family class the document declares decides the substitute

### What was measured

The gate reported `fonts 5/5` and it is not the same five. We drew the body in Liberation Serif and
the reference in **DejaVu Serif**. The line advances name the faces outright: ours 13.80 pt, which
is Liberation Serif's `(1825 + 443 + 87) / 2048 x 12 pt`, and the reference's 14.00 pt, which is
DejaVu Serif's. Both wrap to the same 432.0 pt measure and the reference fits about **11% less text
on each line** — its first paragraph takes nine lines where ours took seven.

The body names the family **`Times`**. Ten authored one-paragraph documents rendered by the
installed 26.2.4.2 give the rule, and it is not about `Times`:

| declared | class | LibreOffice draws | `fc-match <name>` |
|---|---|---|---|
| Times | *(none)* | Liberation Serif | Liberation Serif |
| Times | roman | **DejaVu Serif** | Liberation Serif |
| Times | swiss | **DejaVu Sans** | Liberation Serif |
| Times | modern / script / decorative | Liberation Serif | Liberation Serif |
| Helvetica | *(none)* / swiss | Liberation Sans / **DejaVu Sans** | Liberation Sans |
| Albany | *(none)* / swiss | Liberation Sans / **DejaVu Sans** | Liberation Sans |
| Thorndale | *(none)* / roman | Liberation Serif / **DejaVu Serif** | Liberation Serif |
| Times New Roman / Arial / Calibri / Cambria / Courier New | roman / swiss / swiss / roman / modern | Liberation Serif / Liberation Sans / Carlito / Caladea / Liberation Mono | same |
| Symbol | *(none)* / roman | OpenSymbol / OpenSymbol | OpenSymbol |

`fc-match "Times,serif"` answers DejaVu Serif, and that is the mechanism exactly.
`FontConfigManager::Substitute` (`vcl/unx/generic/font/fontconfig.cxx`:1076-1086) adds the requested
name as `FC_FAMILY` and then **appends a second `FC_FAMILY`** — `"serif"` for `FAMILY_ROMAN`,
`"sans"` for `FAMILY_SWISS`, and nothing at all for any other family type, which is why `modern`
does not mean monospace however much it looks as though it should. It is the *pre-match*
substitution, so it runs **before** LibreOffice consults `VCL.xcu` — and that ordering is what our
resolver had backwards, its comment saying the table is consulted first and fontconfig only when the
chain names nothing installed.

> **When a document declares a Roman or Swiss family class for a font that is not installed, the
> generic family it implies decides the substitute, ahead of LibreOffice's own chain.** Two
> exceptions, both measured: a *strong* metric alias survives it — the test is whether an installed
> face declares itself the equivalent of the very name asked for, which is true of Liberation Sans
> for Arial and false of Liberation Sans for Helvetica — and a pi face is exempt, because every Word
> document declares `Symbol` roman and there is no roman equivalent of a font of arrows.

`SystemFontResolver.Resolve`, with `DeclaredFontFamily` on `FontRequest`; fed by `FFN.ff` in
`Ww8FontTable` and by `w:family` in `fontTable.xml` for DOCX.

### Reach, and the instrument that measures it properly

The word gate barely sees this. What it is *about* is which face is on the page, so the honest
measure is the symmetric difference between our face set and the reference's, over the 36 renderings
the two fixes changed:

| face-set distance to the reference | documents |
|---|---:|
| closer | **29** |
| unchanged | 5 |
| further | 2 |

`1447.doc` goes from 4 faces wrong to **0**; `Sample_SQMS_Program.docx`, `May 25 bulletin…docx`,
`ECSS-E-ST-50-16C…docx` and eight others go to an exact match. The two that went further are
`ABCD-FE-01-00` and `ABCD-WB-08-00`, from one author: both now draw *an extra* DejaVu Serif the
reference draws in nothing, from a `Times-Roman`/`Cambria Math` entry the reference never puts a
glyph in. Named as a lead, not chased.

### `1447.doc` is still 3 pages against 4, and the residue is 1 twip

The font fix landed and did not finish the document. **Every line break on page 1 now matches the
reference word for word**, which none of them did before — confirmed by a blind reading of the
page pair by a subagent that was shown nothing but the image and reported "the wrapped line-breaks
within shared sentences match word-for-word", "font, font size, justification and margins appear the
same", and located the whole difference in where the page breaks. That is the `page-vision` control
agreeing with the measurement from the other direction.

What is left is that our DejaVu Serif line is **13.95 pt and LibreOffice's is 14.00** — one twip. By
line 35 the accumulated 0.91 pt is exactly the margin by which our 37th line fits and the
reference's does not; with only one line of the next paragraph fitting, its orphan control (2) moves
the whole four-line paragraph to page 2, and the reference's page 1 ends three lines early.

**This was investigated and deliberately not changed.** See §4 — no rule fits.

---

## 3. Numbers

Baseline and both sweeps use `sweep.sh`, committed here: the gate's own three checks against the
**banked** 26.2.4.2 references, rendering only our half, with `SOURCE_DATE_EPOCH` set. It is
validated twice over — its baseline reproduces `words-e-01`'s recorded whole-track figures exactly
(155 match, 165 page-exact, 115 page error), and its verdicts on `batch-004`/`batch-006` reproduce
`batch-check.sh`'s document for document. It costs 3 minutes where `batch-check.sh` costs an hour,
which is what made a before-and-after reach figure affordable.

### The whole words track, 200 documents

| | baseline | after |
|---|---:|---:|
| match | 155 | **157** |
| page-exact | 165 | 166 |
| total absolute page error | 115 | 113 |
| renderings changed (byte for byte) | — | 36 |
| verdicts gained | — | 2 |
| **verdicts lost** | — | **0** |

Gained: `003.doc` (the WW8 rule) and `150_5335_5a.doc` (the family-class rule). Ten rows moved a page
or word count; the other 26 changed only their type, which is the class of improvement the word gate
is blind to and the face-set table above is not.

One page count went backwards and it is worth stating plainly:
`May 25 bulletin focus on carers in the workplace.docx` was 4/4 and is now 5/4. Its face set went
from 4 wrong to **exactly the reference's** — it had been page-exact with the wrong font, which is
the cancelling-errors shape `TODO.batches.md` warns about. Keeping the correct face and a wrong page
count is the right trade; the document fails the gate on words either way and always did.

### Batch validation, in the order the project requires

```
batch-check.sh … 'words/batch-00[46]'    TOTAL 20  MATCH 19  MISMATCH 1
batch-check.sh … 'words/batch-00[1-6]'   TOTAL 60  MATCH 59  MISMATCH 1
```

18/20 before, 19/20 after; the one remaining is `1447.doc` at 3/4. Batches 001-003 and 005 are
untouched — 58/60 before, 59/60 after.

### Tests

Every project run individually, counts read rather than colours.

| project | passed | failed | skipped | total |
|---|---:|---:|---:|---:|
| Paperless.Core.Tests | 313 | 0 | 0 | 313 |
| Paperless.Containers.Tests | 109 | 0 | 0 | 109 |
| Paperless.Markup.Tests | 259 | 0 | 0 | 259 |
| Paperless.OpenDocument.Tests | 125 | 0 | 0 | 125 |
| Paperless.Presentations.Tests | 631 | 0 | 0 | 631 |
| Paperless.Rendering.Tests | 148 | 0 | 1 | 149 |
| Paperless.Spreadsheets.Tests | 695 | 0 | 0 | 695 |
| Paperless.Text.Tests | 304 | 0 | 0 | 304 |
| Paperless.Vector.Tests | 295 | 0 | 0 | 295 |
| Paperless.WordProcessing.Tests | 801 | 0 | 0 | 801 |
| Paperless.Fidelity.Tests | 519 | **31** | 0 | 550 |

`Paperless.Fidelity.Tests` had 31 failures on this branch before the round and has exactly 31 after
— **none added**. The build is warning-free.

Fourteen tests are new: eleven in `FontResolutionTests` (the family-class rule, the strong-alias
exception, the pi-face carve-out and the four inert classes) and five in `DocFontFamilyClassTests`
(the `FFN.ff` field). `FontTableTests`'s summary said `fontTable.xml` is "read and reported rather
than acted on", which is no longer true of `w:family`, and has been corrected.

---

## 4. What was measured and deliberately not acted on: the line-height law

Chasing `1447.doc`'s last twip led to a finding worth more than the document, and to a decision not
to act on it.

Line pitch was measured for five installed faces at fifteen sizes by reading the `Td`/`TD`/`Tm`
operators out of LibreOffice's own PDFs (`baselines.py` here; `pdftotext -bbox` reports the *ink*
box, which moves with whichever glyphs a line holds and cannot settle a one-twip question). Our rule
— round `(ascender + descender + lineGap) / upem x size` once, to whole twips — is right on **70 of
75** points. The five it misses are each exactly +1 twip:

| face | size | exact (twips) | ours | LibreOffice |
|---|---:|---:|---:|---:|
| Carlito | 18 | 439.453 | 439 | **440** |
| DejaVu Serif / Sans | 12 | 279.375 | 279 | **280** |
| Liberation Sans | 13 | 298.975 | 299 | **300** |
| Liberation Sans | 16 | 367.969 | 368 | **369** |
| Liberation Serif | 10 | 229.980 | 230 | **231** |

**The rounding rule cannot be "sum then round", and this refutes it outright**: Liberation Serif
(1825 + 443 + 87) and Liberation Sans (1854 + 434 + 67) have the *identical* total of 2355 design
units, so the formula predicts the same pitch for both at every size — and they measurably differ at
10, 13 and 16 pt. The split decides, not the sum.

No candidate fits. Rounding ascent, descent and leading separately is worse (19 misses of 75);
ceiling is worse (13); `max` of the two is wrong on Liberation Serif at 10 pt; ascent-plus-leading
against descent is wrong on Liberation Serif at 12 pt. Every miss is +1 and never −1, so the true
value is `roundA` or `roundA + 1` and something not yet named picks between them.

Left alone on purpose. It is a change to the metric every line of every document in all three tracks
is measured with, on evidence that names no mechanism — which is the fudge-factor trap wearing a
different hat. The table above is the specification for whoever takes it: 75 points, an exact
instrument, and a refutation to start from.

## 5. Leads

* **The line-height law, above.** Worth a round of its own. Every document short by a line at the
  foot of a page is a candidate.
* **RTF and ODF declare the same family class and neither reader records it.** RTF's `\froman`
  family is not parsed at all; ODF's `style:font-family-generic` is present in every file
  LibreOffice writes and is not read. Both are reader work, not resolver work — the resolver takes
  `DeclaredFontFamily` from anyone.
* **Presentations and spreadsheets are untouched and should not stay that way.** OOXML carries the
  same datum as `pitchFamily` on `<a:latin>`. Nothing in this round changes a slide or a sheet,
  because no reader outside word processing sets `DeclaredFamily`.
* **`ABCD-FE-01-00` and `ABCD-WB-08-00`** now embed one face more than the reference. A
  `Times-Roman` or `Cambria Math` entry the reference puts no glyph in.
