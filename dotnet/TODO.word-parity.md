# Deliberate divergences from 26.2.4.2, and what they cost the gate

The gate scores this tree against LibreOffice 26.2.4.2's own rendering, so every difference from it
normally counts against us. **These ones do not, because they are places where 26.2.4.2 is wrong
about the document and Word is right, and this project wants Word parity.** Each is measured, each
has a switch, and each costs a gate row that no later round should spend time on.

**One switch turns all of them back on**: `PAPERLESS_LIBREOFFICE_QUIRKS=1`
(`Paperless.WordProcessing.WordParity`). It is one switch rather than one per rule because it is a
statement about which application the output should resemble, and half of each answer is neither.

**Check this file before working a words-track row that will not close.** It is the sibling of
`TODO.raster-ceiling.md`, which lists the rows the gate cannot win for a different reason: there
the reference rasterises text and our better output scores as the failure; here our output is
better because we read the file the way the application that wrote it does.

---

## A `TOC \t` switch voids a built-in heading's direct paragraph formatting

**26.2.4.2**: a `TOC` field whose `\t` template switch names a built-in `heading N` style makes
every paragraph in that style — anywhere in the document, before the field as well as after it —
lose its direct paragraph formatting. `w:spacing`, `w:jc`, `w:ind`, `w:shd`, `w:numPr` and
`w:keepNext` are all discarded; only `w:pageBreakBefore` and the runs' own `w:rPr` survive. The
`\t` branch of `DomainMapper_Impl::handleToc`
(`sw/source/writerfilter/dmapper/DomainMapper_Impl.cxx`:7663-7707) is where the difference is
introduced; the seat that does the discarding is not located.

**Word**: honours the paragraph.

**This tree**: honours the paragraph. `DocxTocStyles` implements the reference's rule and the
switch above turns it on for a run that wants to agree with LibreOffice instead. Nine arms and
three controls are in
`probes/tocstyle-r160/results.md`, and both halves are pinned by
`DocxTocTemplateStyleTests`.

**Reach**: 21 of the 271 corpus DOCX state a `\t` switch and **7** hold a paragraph it voids, 129
paragraphs in all — `SPA-11_mcar_part-11_v2.9` 87, `OM template for non-complex NCC operators` 18,
`hdss-bulletin-issue-285-25-june-2025` 9, the two FAA Holdover Tables 6 each,
`SPA-06_mcar_part-6_and_IS_v2.9` 2, `report-template` 1.

**What it costs**, measured by rendering all 271 with the rule on and with it off:

| document | 26.2.4.2 | this tree | why |
|---|---|---|---|
| **`words/pagination-001/docx/24-25_FAA_Holdover_Tables.docx`** | **155 pages** | **154** | the one gate row this costs |
| `SPA-11_mcar_part-11_v2.9.docx` | 49p, 70 763 chars | 49p, 70 763 — *exact* | ours is closer with the rule **off** |
| `SPA-06_mcar_part-6_and_IS_v2.9.docx` | 85p, 142 938 | 85p, 142 938 — *exact* | likewise |
| the other four | — | unmoved on pages and characters | — |

So the rule is worth exactly one page on one document, and switching it off puts two other
documents back on the reference's character count exactly.

### Why `24-25_FAA_Holdover_Tables.docx` is 154 against 155, in one paragraph

Its `TABLE 50` caption states `<w:pStyle w:val="Heading3"/><w:spacing w:after="0"/>` over a style
stating `w:after="120"`. We draw the nought the author asked for and 26.2.4.2 draws the style's
6 pt. On that page the table therefore ends at **539.75** here and **545.80** there, against a body
bottom of 558 — and the otherwise-empty `<w:br w:type="page"/>` paragraph that follows the table is
about 14.9 pt of 13 pt Arial. It fits for us and not for the reference, so the reference puts it on
a page of its own, blank, and we do not. Page 72 carries the same 1775 alphanumeric characters on
both sides and no row moves; the whole difference is that one empty page.

**Do not chase this row.** The document is otherwise within **51 characters of the reference over
155 pages** (299 532 against 299 583), and it is page-exact with
the switch set.


---

## A `REF` field is recomputed on load

**26.2.4.2**: recomputes every `REF` naming a bookmark when the document is opened, from the
bookmark's own text — a `SwGetRefField` with `ReferenceFieldSource::BOOKMARK`
(`dmapper/DomainMapper_Impl.cxx`:8541-8635) whose `UpdateField` reads `FindAnchor`'s range
(`reffld.cxx`:1559-1591) through `FilterText` (`:461-489`).

**Word**: draws the cached result. A `REF` is not updated when a document is opened or printed;
only F9, or *update fields before printing*, changes what the reader sees. So the two do **not**
disagree about what a `REF` evaluates to — they disagree about *when*.

**This tree**: draws the cache. `DocxReferenceFields` implements the recomputation behind the
switch; `DocxReferenceFieldTests` pins Word's answer and the reference's side by side, over the
twelve arms of `tests/corpus/features/words-reference-field.docx`. `probes/docxref-r158/results.md`
has the measurements.

**Reach**: 7 of the 271 corpus DOCX hold a resolvable `REF`; **2** state one whose bookmark says
something other than the cache.

**What it costs**:

| document | 26.2.4.2 | this tree | why |
|---|---|---|---|
| **`words/pagination-001/docx/FAA 2025-26 Holdover Tables.docx`** | **167 pages** | **166** | the gate row this costs |
| `Agile_Arc_SysDes.docx` | 20p, 33 905 chars | 20p, 33 897 | unchanged either way |

The reference's own reading of that document's page 7 is the argument for following Word rather
than against it:

```
26.2.4.2   The list of fluids (Tables Table 55, Table 56, Table 57 and Table 58) has been updated …
Word, us   The list of fluids (Tables 55, 56, 57 and 58) has been updated …
```

The four bookmarks cover the word `Table` as well as the number, so recomputing produces that
doubling — and it is what any application would produce on F9, including Word. The difference is
that Word does not press F9 on the reader's behalf. Elsewhere in the same document one note's
`REF` expands to a whole table caption, and the extra wrapped line is the page this costs.

---

## Still LibreOffice's reading, and why

These are places where this tree recomputes a field that Word would leave at its cache, and they
are **not** switched: each computes something the file cannot state correctly, so the cache is
stale by construction rather than merely unupdated.

- **`PAGE`, `NUMPAGES`, `SECTIONPAGES`.** Pagination decides them and Word maintains them too.
- **`FILENAME`, `TITLE`.** The cache is a statement about a file that no longer exists; the
  measurement that put them in is in `ConstantFields`' own remarks (351 of one document's 363-word
  gap). Word would show the cache here, so this is the closest of the three to being switched —
  and it is left because the value it draws is right about the document in hand where the cache is
  right about a previous one.
- **`STYLEREF`.** Page-dependent by nature, like `PAGE`: it quotes the heading in force, which
  Word recomputes as the layout moves. `DocxLayoutSource.StyleReferenceText` already declines
  inside a running head, where this tree cannot answer per page.

**Not yet decided: the RTF bookmark rotation (round 88).** `RtfBookmarkRotation` reproduces a
*writerfilter bug* — RTF sends a bookmark half's name before its id and the mapper is written for
the opposite order, so every name after the first lands one bookmark early. Word has no such bug.
Under this file's rule it should be off; what stops that being a one-line change is that the
corpus holds no `.rtf` at all, so the only RTF evidence is `/home/user/corpus-odf`'s 338 files —
**written by LibreOffice itself**, where reproducing LibreOffice's own read of its own output is
not obviously wrong. Measure the reach on real Word-written RTF before switching it.
