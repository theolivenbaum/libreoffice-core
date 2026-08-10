# Words round 46 — the prediction, committed before anything is rendered post-change

Baseline reproduced exactly at `ee87a6d0e` (`baseline.tsv`, 200 rows, no duplicate paths):
157/200 matching, |page error| 75, 167 exactly-correct page counts, |word error| 6602.

## The change

`WordParagraphFormats` gives a DOCX paragraph `OrphanLines = WidowLines = 2` when
`word/styles.xml` carries a `w:docDefaults/w:pPrDefault` element — **empty or not** — and nothing
in the chain (that element's own `w:pPr`, the style chain, the paragraph) turns `w:widowControl`
off. Today `IsOn` returns false for an absent element, so a DOCX paragraph has no widow or orphan
control unless something switches it on, which almost nothing does.

Established against the installed 24.2.7.2 by nine authored variants at five straddle positions
(`widow-orphan-default.py`), with `para-off` measuring the room at the foot of the page so a
variant that keeps fewer lines there is one with the control on:

| variant | behaves like |
|---|---|
| `no-docDefaults`, `no-pPrDefault` | control **off** |
| `empty-pPrDefault`, `pPrDefault-with-pPr` | control **on**, identical to `para-on` |
| `pPrDefault-widow-off` (docDefaults state `w:val="0"`) | control **off** |
| `pPrDefault-para-off` (paragraph states `w:val="0"`) | control **off** |
| `settings-on` (`w:settings/w:widowControl`) | control **off** — the document-level flag does nothing |

## What the census counts, and what it cannot see

`widow-orphan-census.py` over `words/batch-*`: **130 of 134 DOCX** carry a `w:pPrDefault`; four
have one that turns the flag off; none lack it. The remaining **66 documents are `.doc`**, which
this change does not touch at all — `Ww8LayoutFormat` already reads `HasWidowControl ?? true`, so
the binary half has had widow and orphan control all along. That asymmetry between the two readers
is what raised the question.

**The census counts documents whose paragraphs gain the property, not documents whose output
moves.** It cannot see:

- whether any paragraph in a document ever *straddles* a page break — a one-line paragraph, a
  heading, a list item and a table cell that never splits all pay nothing;
- the ODF reader (`fo:orphans`/`fo:widows`, already read) or the RTF reader (document-level
  `\widowctrl`, unchanged), neither of which is in this track's corpus in any number;
- a paragraph whose split is already forbidden by `w:keepLines` or by a table row rule, where
  adding an orphan count changes nothing;
- the interaction with the round-45 line-spacing rule, which changed where 11 documents' lines
  land and therefore which of them straddle at all.

So 130 is a ceiling on the population and says nothing about the reach.

## The numbers predicted

| | baseline `ee87a6d0e` | predicted |
|---|---:|---|
| documents matching | 157 | **154–161** |
| absolute page error | 75 | **62–82** |
| exactly-correct page counts | 167 | **162–173** |
| absolute word error | 6602 | 6450–6750 |
| renderings changed | — | **55–100 of 200** |

The bands are wide on purpose and the reason is the sign. Widow and orphan control only ever
*moves content down*, so our page counts can only rise. The baseline's page-delta histogram is
14 documents at −1, 7 at +1, 2 at −2, 3 at +2 and a tail: the change should help roughly twice as
many documents as it hurts, and a net gain of two or three verdicts is the central expectation.
A net **loss** is a real possibility and would not by itself refute the rule — the probes establish
what the reference does, and a document that goes red because a compensating error is exposed is
the round-45 `gpp` case again, to be judged on page error and exact-page count rather than on the
verdict.

The one document the rule was found on, `gpp-pr-top-7-office-markets-4q-2023.docx`, is predicted
to go 3/4 → **4/4** and back to `match`: mutating its "In the year 2023 …" paragraph to
`w:widowControl w:val="0"` puts that line back on page 1 in the reference, which is the causal
test.

## What would refute the rule

- A sweep in which the DOCX half of the corpus gets *worse* on both page error and exact-page
  count while the `.doc` half is untouched. The probes would still stand, and the reading "the
  default is 2" would have to be wrong in some way the nine variants do not reach — most likely
  in *how* Writer applies the counts rather than in whether it has them.
- Any authored variant where an empty `w:pPrDefault` fails to turn the control on. None of the
  five straddle positions did.
