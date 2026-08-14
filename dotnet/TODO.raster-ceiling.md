# Pages where the reference rasterises and the word gate cannot be won

**Check this list before working any word-count failure.** Several agents have each spent part
of a round re-deriving that some page belongs to this class.

LibreOffice sometimes **rasterises** an embedded object instead of playing it, so its PDF holds a
picture where ours holds real, searchable glyph runs. The rendered pages look alike —
`pdf-image-diff.py` scores them near-identical — but `pdftotext` reads our text and finds nothing
in theirs. **Our output is the better one**, and the word gate scores it as a failure. Driving
those numbers down would mean drawing less text, which is the wrong direction.

Regenerate with:

```sh
.claude/skills/corpus-batches/scripts/raster-ceiling-pages.py /c/sandbox/workdir/sample-files out
.claude/skills/corpus-batches/scripts/raster-ceiling-pages.py /c/sandbox/workdir/sample-files out --documents-only
```

Machine-readable copy: `dotnet/raster-ceiling-pages.tsv`.

## The threshold is a bar, and pages sit just under it

`8_P-Pavese`'s **page 6 carries the same 692×240 raster as its page 5** and is absent from the
table below only because +44 words on 180 is 24.4% against the 25% bar. That is a property of
my threshold, not of the document.

So the list under-counts, and by an unknown amount. Treat a document with one flagged page as
likely to have neighbours just under the bar, and re-measure rather than assuming the table is
exhaustive. Raising the bar is not the fix — it would start excusing real defects, which this
file has already done once.

## How a page earns its flag

Three conditions, on a document whose page count already agrees:

1. The reference draws a raster on that page.
2. **We do not draw that same raster.** Matched on dimensions.
3. We extract materially more words there than the reference does — at least 8 more and at
   least 25% more, about two-thirds of a line of prose.

**Condition 2 was missing until an agent disproved four of this file's own rows.** Without it,
the first condition is satisfied just as well by a logo *both* renderers draw: four pages of
`UG.CAO.00133` were flagged on a 162×109 JPEG of the EU flag in the footer, identical on both
sides, while the document's real surplus was a header block drawn on 13 of its 18 pages. The
signature misfires on any document that puts a small picture in its page furniture and has a
furniture defect elsewhere. Adding it removed **16 of 53 pages — nearly a third of the list.**

Matching on dimensions rather than on content is deliberate: a rasterised metafile and a logo
differ in size by orders of magnitude, and decoding every image to compare pixels would cost more
than the whole scan.

### The 25% threshold is excluding pages of this list's own class

Measured in round seventeen on `slides/batch-008/…/8_P-Pavese…pptx`. `pdfimages -list` shows the
reference drawing the **same 692x240 JPEG with a soft mask on pages 5 and 6**, and us drawing
neither; page 5 is on the list at +44 on 70 and page 6 is not, at +44 on **180 — 24.4%**. Page 16
of the same deck sits at +23% and is a different defect entirely (see below). So condition 3's
"at least 25% more" is not separating the class from anything here; it is dropping half of one
document's instances of it.

Either lower it, or say in this file that the list is a deliberate under-count and that a page
just under the bar should be checked with `pdfimages` before being treated as a defect. Note the
consequence for that document: it fails the word gate at 2240 against 2108, and **excusing only
its listed page leaves it at 2152, still outside the 2% band** — so a reader working from this
list alone would conclude the residue is ours when two-thirds of it is not.

## The numbers

| | |
|---|---|
| pages flagged | **37** across 21 documents |
| by track | 28 slides, 8 words, 1 sheets |
| flagged pages whose document embeds a metafile | 21 |
| flagged pages whose document embeds **none** | 16 |
| excess words accounted for | **2706** |
| documents embedding a metafile at all | 100 of 534 |
| documents that cannot be judged yet | 83 |

An embedded metafile is the commonest cause and not the only one. `W3_Case_Study…ppt` holds none
and its page 10 is squarely this class — the reference draws there the same 845×572 object it
draws on `Thailand17.ppt`'s page 8. **The flag keys on the observable signature; the metafile
count rides along as an attribution.** An earlier version filtered the page scan down to metafile
carriers and hid nearly half the list that way.

The scan also could not originally see a metafile in a binary document at all: a `.ppt` keeps its
pictures zlib-compressed inside Escher blip records, so a raw signature search finds nothing in a
file that plainly contains one. Inflating every plausible stream took the carrier count from 76
to 100.

## Two boundaries worth stating

**A flagged page does not excuse its document, and the two can point opposite ways.** Re-measure
before subtracting. This file's own worked example inverted once already — `UG.CAO.00133` was
recorded as 225 words short overall and later measured +245 over — before turning out to be a
false positive entirely.

**Eighty-three documents cannot be judged.** A per-page comparison is meaningless while the page
counts disagree, so those are an honest **unknown** rather than a pass. Fix their pagination
first, then re-run.

## The flagged pages

| Document | Page | ours | ref | excess | metafile |
|---|---|---|---|---|---|
| `words/batch-016/…/AFS-050-004-F2_0i.docx` | 3 | 419 | 53 | +366 | — |
| `words/batch-013/…/FO.FCTOA_.000129 Application for activities re` | 5 | 429 | 162 | +267 | 2/0 |
| `slides/batch-012/…/OnTrac_StarCertificationProgram-3Day.pptx` | 10 | 281 | 30 | +251 | 2/0 |
| `slides/batch-014/…/N2_E_Maestroni_Swarm_COP.pptx` | 7 | 307 | 102 | +205 | — |
| `words/batch-020/…/EHEST-SMS-Safety-Management-Manual-V2.docx` | 18 | 418 | 224 | +194 | 6/0 |
| `words/batch-020/…/EHEST-SMS-Safety-Management-Manual-V2.docx` | 43 | 396 | 229 | +167 | 6/0 |
| `slides/batch-016/…/16 - UTM - (NASA).pptx` | 29 | 109 | 1 | +108 | 2/0 |
| `slides/batch-016/…/16 - UTM - (NASA).pptx` | 7 | 261 | 158 | +103 | 2/0 |
| `slides/batch-010/…/W3_Case_Study_of_a_Tsunami_Warning_Simulation_` | 10 | 102 | 9 | +93 | — |
| `slides/batch-014/…/Thailand17.ppt` | 8 | 102 | 9 | +93 | 6/0 |
| `words/batch-013/…/FO.FCTOA_.000129 Application for activities re` | 2 | 254 | 187 | +67 | 2/0 |
| `slides/batch-014/…/WiGr_2021W_1_Angebot-Nachfrage-Elastizität-211` | 21 | 78 | 23 | +55 | 0/1 |
| `slides/batch-010/…/Fundamentals_Module_1_basics.ppt` | 6 | 70 | 20 | +50 | 1/0 |
| `words/batch-011/…/f2_registro_de_aprovacao_com_pbcs_EN.docx` | 1 | 230 | 181 | +49 | 0/3 |
| `slides/batch-014/…/WiGr_2021W_1_Angebot-Nachfrage-Elastizität-211` | 28 | 53 | 5 | +48 | 0/1 |
| `words/batch-020/…/EHEST-SMS-Safety-Management-Manual-V2.docx` | 76 | 97 | 51 | +46 | 6/0 |
| `slides/batch-012/…/OnTrac_StarCertificationProgram-3Day.pptx` | 9 | 96 | 50 | +46 | 2/0 |
| `slides/batch-014/…/WiGr_2021W_1_Angebot-Nachfrage-Elastizität-211` | 45 | 50 | 5 | +45 | 0/1 |
| `slides/batch-008/…/8_P-Pavese_AIRBUS-ATB-journee-CRATB.pptx` | 5 | 114 | 70 | +44 | 3/2 |
| `slides/batch-017/…/Demick_JetBlue.pptx` | 5 | 93 | 54 | +39 | — |
| `words/batch-015/…/approvals-and-standardisation-organisation-app` | 6 | 161 | 123 | +38 | — |
| `sheets/batch-010/…/TOGAF9-Tool-ConfReqts-CSQ.xls` | 21 | 69 | 31 | +38 | — |
| `slides/batch-014/…/Structural Testing.pptx` | 19 | 37 | 5 | +32 | 2/0 |
| `slides/batch-014/…/WiGr_2021W_1_Angebot-Nachfrage-Elastizität-211` | 44 | 34 | 4 | +30 | 0/1 |
| `slides/batch-014/…/WiGr_2021W_1_Angebot-Nachfrage-Elastizität-211` | 46 | 35 | 5 | +30 | 0/1 |
| `slides/batch-017/…/Demick_JetBlue.pptx` | 7 | 94 | 64 | +30 | — |
| `slides/batch-017/…/Demick_JetBlue.pptx` | 4 | 108 | 79 | +29 | — |
| `slides/batch-016/…/FAAAIandtheArtandScienceofV&Vfinal.pptx` | 14 | 119 | 91 | +28 | 1/1 |
| `slides/batch-014/…/Intersil_Italy_CAN_Bus_Transceiver_Presentatio` | 30 | 130 | 103 | +27 | 6/0 |
| `slides/batch-007/…/introduction_to_bea_tuxedo.ppt` | 38 | 84 | 63 | +21 | — |
| `slides/batch-007/…/introduction_to_bea_tuxedo.ppt` | 2 | 38 | 27 | +11 | — |
| `slides/batch-007/…/introduction_to_bea_tuxedo.ppt` | 13 | 41 | 31 | +10 | — |
| `slides/batch-007/…/introduction_to_bea_tuxedo.ppt` | 14 | 46 | 36 | +10 | — |
| `slides/batch-009/…/NWD-GLA-Community-Outreach-Day-Oct-2025.pptx` | 5 | 15 | 5 | +10 | — |
| `slides/batch-004/…/ws_prod-g-doc-Events-industrymeeting18112004-E` | 9 | 38 | 29 | +9 | — |
| `slides/batch-007/…/introduction_to_bea_tuxedo.ppt` | 26 | 30 | 21 | +9 | — |
| `slides/batch-007/…/introduction_to_bea_tuxedo.ppt` | 39 | 38 | 30 | +8 | — |

## What is known about the mechanism, and what is not

**Established.** The rasterisation happens **upstream of PDF export**. The raster is not in the
file — two `.ppt`s were scanned through every inflated zlib stream, not just their raw bytes —
and it is not the PDF writer, since `implWriteBitmapEx` downsamples only under
`ReduceImageResolution` and the 300 dpi `FLOATTRANSPARENT` branch cannot yield the observed
66–265 dpi. `8_P-Pavese…pptx` slide 5 is a bare `p:pic` over an EMF with 791 `EXTTEXTOUTW`
records, no EMF+, no alpha, no raster-op and no bitmap, and the reference draws a 692×240 raster
with a soft mask.

**Not established.** Which LibreOffice path does it, and whether the metafile-carrying and
metafile-free cases share one. EMF+ is ruled out as the trigger by counter-example — `2014BSA`
slide 5's EMF *does* carry EMF+ and renders as text. `SELECTCLIPPATH` is the standout structural
difference between the two, but that is a correlation on two documents and is **unverified**.

Naming that path would let the flag become a rule rather than a list. Until then this is the
record.

## A second ceiling, with a different mechanism and a named cause

Rasterisation is not the only way the reference draws less than we do. The slides track's
largest single ink figure turned out to be half this, and unlike the rasterisation class the
mechanism is **named and verified** rather than open.

`slides/batch-012/pptx/NAS-Infrastructure-Roadmaps-v16.0.pptx` puts each of its data tables in
a `p:graphicFrame` wrapped in `mc:AlternateContent`:

```xml
<mc:Choice xmlns:v="urn:schemas-microsoft-com:vml" Requires="v">
  <p:oleObj r:id="rId3" progId="Excel.Sheet.12"><p:link/></p:oleObj>
</mc:Choice>
<mc:Fallback>
  <p:oleObj …><p:link/><p:pic>…<a:blip r:embed="rId4"/>…</p:pic></p:oleObj>
</mc:Fallback>
```

`rId3` is an *external* relationship to a SharePoint workbook. `rId4` is `image14.emf`, sitting
in the package, and it is a picture of the table's data.

`oox/source/core/contexthandler2.cxx:238-249` lists the namespaces LibreOffice will take a
`mc:Choice` for, and **`v` is on it** — so LibreOffice takes the Choice, gets a linked OLE
object with no local replacement picture, cannot reach the link, and draws nothing. We do not
claim VML, take the Fallback, and draw the EMF. Ours is the better output by any reading, and
the spec's rule — take the first Choice whose namespaces you understand — is on our side, since
we have no VML reader at all.

Measured, splitting the document's per-page ink by whether the page carries one:

| | pages | ink | major |
|---|---|---|---|
| carrying a `Requires="v"` `p:oleObj` | 24 | **152.12** | 24 |
| everything else | 113 | 73.21 | 42 |

The 152.12 did not move under either of this round's fixes — it is the same figure before and
after, which is what says it is a property of those pages rather than noise.

**Re-derived independently in round fourteen and it reproduces to the digit**: 152.12 on the 24
pages carrying one and 73.28 on the other 113, against the 152.12 and 73.21 recorded here. That
makes this one of the few claims on this track to survive an independent check with its *sentence*
intact as well as its number — the usual result is the reverse.

Round fourteen also took the other half apart, and it is **not** a second discrete defect waiting
to be found. Its worst pages carry none of `p:graphicFrame`, `a:tbl`, `a:blipFill`,
`a:pattFill`, `a:gradFill`, `dgm:relIds`, `a:prstTxWarp` or `a:outerShdw` in any
concentration, and the diff
report calls 40-50% of each one *"marks displaced or reshaped"*: a reflow spread thin over 113
pages at about 0.65 each, worst page 4.27. So the splitting method paid here once and has now been
run to the end on this document; the next instrument for what is left is the extraction
comparison, not more pixels.

Corpus-wide the pattern is small: ten decks have a slide with a `Requires="v"` choice around a
`p:oleObj`, and only NAS has it on more than four slides. So this is one document's ceiling
rather than a class to build a tool around — but it is 10% of the track's ink and it had been
recorded twice as "linked Excel OLE, known" without the number being split, which is what let
its other 216.29 sit unexamined for two rounds. **Split a big document's ink before believing
its attribution.**

## Sheets is nearly untouched by this

One flagged page on the whole track. That track's image problem is the opposite one, and note
that its headline example was also wrong: `apron-area.xls` was recorded as drawing 0 images
against the reference's 1670, and the census that produced it was counting placements of EMFs
that draw as vector content. The document was a full match all along, page-1 ink 1.09%. Treat the
rest of that census as suspect.

## The slides track's ink, ranked with both ceilings subtracted

**Read this before ranking anything by `|ink|%`.** Measured at `2ced17655` over the whole
163-document slides track, from the sweep's own kept comparison reports and its two sets of
rendered PDFs — no re-rendering, so it can be reproduced from any sweep's output:

```sh
python3 dotnet/probes/slides-r22/alternate-content-oleobj-census.py /c/sandbox/workdir/sample-files/slides > altcontent.tsv
python3 dotnet/probes/slides-r22/raster-pages-from-renderings.py <sweepdir> > raster-pages.tsv
python3 dotnet/probes/slides-r22/slides-ink-ranking.py            # reads both, beside sweep-base/
```

| | ink | pages | major pages |
|---|---:|---:|---:|
| the track, as swept | **1233.03** | 4199 | 415 |
| on a page at one of the two ceilings | 201.27 | 63 | 38 |
| **residual — what a fix can still win** | **1031.76** | 4136 | 377 |

The full ranking is `dotnet/probes/slides-ink-ranking.tsv`, sorted by the residual column.

**The two ceilings are different mechanisms and only one of them is visible to `pdfimages`.**

1. *The reference rasterises.* The signature this file's table already uses, applied per page:
   the reference draws a raster we do not draw, and we extract at least 8 and at least 25% more
   words there. **27 pages, 40.74 of ink.**
2. *The reference draws nothing at all,* because it takes an `mc:Choice` it claims and finds an
   unreachable external link, where we take the `mc:Fallback` and draw the replacement picture —
   the mechanism named in the section above. Censused from the packages rather than inferred:
   **38 slides across 10 documents, 165.46 of ink, 28 major pages.**

The two overlap on two pages of `16 - UTM - (NASA).pptx`, which is why the totals are 63 rather
than 65.

### What the subtraction changes, which is the point of doing it

`NAS-Infrastructure-Roadmaps-v16.0.pptx` has been quoted at **224.77, 18% of the track**. That
over-attributes it by more than two to one: **152.14 of the 224.77 is on the 24 slides carrying a
`Requires="v"` `p:oleObj`** and **72.63 is not** — which reproduces the 152.12/73.21 recorded in
the section above, independently and from a different instrument. NAS stays first on the residual
ranking and is worth a fifth of what its headline says.

Two documents rise past it in share of what is left, and **neither has ever been taken apart**:

| document | ink | residual | ceiling pages |
|---|---:|---:|---:|
| `Wildlife for REDAC September 11.pptx` | 54.89 | **54.78** | 1 |
| `Reporting_responsibilities_matrix.pptx` (268 pages) | 54.27 | **54.27** | 0 |
| `Thailand17.ppt` | 48.80 | 47.96 | 1 |
| `ITE106-Chapter 4.ppt` | 27.98 | 27.98 | 0 |

`Demick_JetBlue.pptx` moves the other way — 36.40 to **16.19**, three of its ten pages being the
rasterisation ceiling — so the automatic-series-colour work it motivates is worth less than its
headline suggested.

**Regenerate this after any round that moves the track.** The table above is `2ced17655`; the
round that produced it then took the track to `|ink|%` 1185.07 over ten documents, so the residual
is about 984 and the ranking's top rows have moved. The three scripts read a finished sweep's own
output and cost nothing but the reading.

**The ceiling subtraction is a floor on the ceiling, not a measurement of it.** Mechanism 1's
threshold is this file's own, and the section above records that it under-counts; mechanism 2 is
exact for `pptx` and blind to `.ppt`, which has no `mc:AlternateContent` to census. So 1031.76 is
an upper bound on what is winnable, and the ranking is the part to trust rather than the total.

---

# The same ceiling with the sign reversed: the reference's count is inflated

Everything above is about pages where **we** extract more words than the reference and are right
to. The mirror case exists, was measured on 2026-08-14, and belongs in the same file because a
round working a word-count failure has to rule out both.

**`2017-04-27-Lease-Transition-Records-Checklist-FINAL-1.xlsx` and its
`2020-01-29` twin** — page-exact at 5/5, **2323 words against the reference's 2498**, on every
page. It looks exactly like 175 words of lost content. It is not:

- Strip all whitespace from both extractions and the two character streams are **identical apart
  from one transposed word** — 13 858 characters each side, two diff blocks.
- The reference carries **310 single-letter tokens against our 154**.

LibreOffice writes intra-word positioning adjustments, and `pdftotext` reads each reposition as a
word boundary: `L icense`, `M aintenance`, `CM R`. The words are all there and in the right place
on both sides; only the reference's tokenisation is shattered.

This is the `Tj`-granularity artefact the render-comparison skill records, seen from the other
end. There it was *ours* fragmenting (`http://www.` counted 48 times); here it is the
reference's, and the gate reads it as our deficit.

**So when a page-exact document's word count is short, compare the whitespace-stripped character
streams before believing content is missing.** If they match, the defect is in tokenisation and
the gate cannot be won on that document — chasing it would mean making our text layer *worse* to
match poppler's misreading of theirs.

Note these two documents were worked anyway and the work was not wasted: they also had a genuine
font defect (a declared `family="1"` we ignored, so we set Bell MT in DejaVu Sans where the
reference sets DejaVu Serif). That is now fixed and the faces match. **The visible page improved;
the gate column did not move, and never will.**

## A third shape: the reference splits its own words

`slides/batch-004/pptx/solog_orientation_august_2019.pptx` — page-exact at 15/15, **670 words
against the reference's 685**, which reads as fifteen words lost.

Nothing is lost. Extracted with the same `pdftotext` and compared per page, character by
character with whitespace removed: **4758 non-space characters in the reference against our
4756**, every page's character multiset identical apart from two hyphens — and those are
`pdftotext` de-hyphenating *our* soft line breaks, with the hyphens confirmed present in our PDF
by `-bbox`.

All fifteen tokens are the reference splitting words it drew whole:

- **8 of 15** — LibreOffice writes **one show operator per character** on the footer lock-up
  ("19 glyphs in 18 shows"), and rounded advances leave a 1.26 pt gap after each `M`, so
  `pdftotext` reads `MIAMI` as `M` `IAM` `I` on pages 1, 2, 3 and 15. Ink spans 171.20–213.43 pt
  against our 171.09–214.26 — the same width, drawn in the same place.
- **7 of 15** — LibreOffice fills a line and breaks an over-long URL **mid-token**; we move the
  whole token to the next line first. A real fidelity difference, worth fixing on its own merits,
  but it moves zero words in either direction.

### The same shape at forty times the size: `architecture6.ppt`

`slides/batch-007/ppt/architecture6.ppt` — page-exact at 31/31, **1926 words against the
reference's 2544**. A 618-word deficit, a quarter of the document, and the largest word gap left
on the slides track. It is the same shape as the fifteen words above, and nothing is missing.

Whitespace-stripped character streams: **11048 characters ours against the reference's 11038**.
We draw *ten more* than the reference. Every opcode of the difference is one of four things, and
none of them is content:

- the bullet PUA code point — `U+E47A`/`U+E46F` ours against `U+F0B2`/`U+F0A7` — because
  LibreOffice keeps the symbol slot at `0xF000 | code` in its `ToUnicode` while we map it to the
  OpenSymbol glyph we actually draw. Both are unreadable Private Use Area noise in the text layer
  and neither counts as a word: the character is glued to the word after it in both;
- reading order on the five table pages, where `pdftotext` visits the label column and the footer
  at different points;
- three hyphens `pdftotext` de-hyphenates out of the **reference's** own line breaks
  (`lowest-level`, `multi-level`, `batch-`), which we do not break there;
- **two words the reference loses.** Its page 13 table overruns the page: the last row's
  `each layer.` is drawn past the bottom edge and its body text runs straight through the footer.
  We fit the row and draw the words. The deficit is 618 in the reference's favour *despite* this.

All 618 are on five pages — 10, 14, 21, 24 and 27, the pattern-table slides — and all of them are
the reference positioning table text glyph by glyph. Page 10's description cell is **"65 glyphs in
64 show(s)"** in the reference against **"74 glyphs in 11 show(s)"** in ours.

**Why LibreOffice does it here is worth recording, because it looks like a metric bug of ours and
is not.** The reference's `TJ` arrays carry per-glyph corrections of −12 to −164 thousandths of an
em, all negative, all widening. Solving for the advances they imply gives, on `Description` at
14 pt: 831, 679, 594, 594, 493, 344, 719, 477, 344, 688 — which is **DejaVu Sans Bold**
(830, 678, 595, 595, 493, 343, 716, 478, 343, 687), while the glyphs drawn and the `/Widths`
written are **Liberation Sans Bold** (722, 556, 556, 556, 389, 278, 611, 333, 278, 611).
LibreOffice measured that text with one face and drew it with another, which inflates the line by
15% and is what overruns the page. Our own pen is not implicated: the 24 pt title on the same page
is **157.39 pt ours against 157.28 pt reference**, 0.07% apart.

So the document is unwinnable twice over — the tokenisation is the reference's, and closing the
gap would mean adopting per-glyph positioning we have no reason to want. A blind reviewer sent the
page pair with no numbers reported the mechanism independently: *"the bottom half's text is WIDER
than the top's for identical strings … a wide, splayed m"*, and *"the table overruns the bottom of
the page … the footer text is overlapped by the table's body text"*.

**What the round found instead is in `probes/slides-arch-01/results.md`:** every bullet on every
binary `.ppt` was drawn hard black, on 26 of the corpus's `.ppt` decks and 935 glyphs. The word
column cannot see it and did not move.

So this document joins the list from a third direction. The three shapes now recorded here:

| shape | who over-counts | cause |
|---|---|---|
| the reference rasterises an embedded object | ours | we draw real text where it draws a picture |
| the reference's tokenisation shatters | the reference | intra-word positioning read as word breaks |
| the reference splits its own words | the reference | one show per character, plus mid-token URL breaks |

**The common test is the same in all three: compare the whitespace-stripped character streams
before believing a word count.** If they match, the gate cannot be won on that document and the
number is about `pdftotext`, not about the renderer.

---

# A fourth shape, and a different kind: **the reference is not deterministic**

Everything above assumes the reference is a fixed answer we are trying to match. On at least one
document it is not.

**`sheets/batch-005/xlsx/fse_identification_form.xlsx`** — page-exact at 3/3, and its gate row
has been 440 words against 427 all day. Converted five times by the same `soffice` 26.2.4.2, the
same file, the same session:

```
run 1: 430   run 2: 430   run 3: 430   run 4: 430   run 5: 443
```

(raw `pdftotext` counts; the gate's letter-or-digit counts are the same swing, 427 against 440)

The 13-word difference is one sentence, and it is always the same one:

> *The serial number of the FSE assigned by the Original Equipment Manufacturer (OEM).*

**LibreOffice draws that cell in about one run in five and omits it in the others. We draw it
every time.** So the direction of the "defect" is the opposite of how the gate reads it: the text
is in the document, we render it, and the banked reference happens to be one of the runs that
dropped it.

A blind reviewer had already reported "we draw a sentence the reference leaves blank" and, quite
properly, listed *"the reference genuinely drops it — least likely given it is the declared ground
truth"* as its fourth candidate cause. That candidate was the right one. It is worth remembering
how reasonable it was to rank it last.

## What this costs, and what to do about it

- **This document cannot be scored against a freshly rendered reference.** Its verdict is decided
  by which run you happen to take. Anyone working it must use the banked PDF and know that the
  banked PDF is *one sample*, not the answer.
- **`.claude/skills/README.md`'s "the same input converted twice gives identical output" is
  qualified by this** and has been annotated. That claim was verified — on the documents it was
  verified on. It does not hold universally.
- **Before believing any single-document reference figure, render the reference more than once.**
  It costs one extra conversion. Two rounds have now spent effort on this document's 13 words:
  one attributing them to a paint clip, one to a dropped cell.

Whether other documents in the corpus share this is **unmeasured**. The honest position is that
we have one confirmed case and no idea of the rate. A cheap sweep — convert every document twice
and diff the extracted text — would settle it and has not been run.
