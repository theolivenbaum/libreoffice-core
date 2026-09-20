# Deliberate divergences from 26.2.4.2, and what they cost the gate

The gate scores this tree against LibreOffice 26.2.4.2's own rendering, so every difference from it
normally counts against us. **These ones do not, because they are places where 26.2.4.2 is wrong
about the document and Word is right, and this project wants Word parity.** Each is measured, each
has a switch, and each costs a gate row that no later round should spend time on.

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

**This tree**: honours the paragraph. `DocxTocStyles` implements the reference's rule and
`PAPERLESS_LIBREOFFICE_TOC_STYLES=1` turns it on for a run that wants to agree with LibreOffice
instead. Nine arms and three controls are in
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
`PAPERLESS_LIBREOFFICE_TOC_STYLES=1`.
