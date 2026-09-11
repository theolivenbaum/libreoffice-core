# `rtf-style-r97` — O24: the other route into *Standard*, and what the intermediates really add

Measured 2026-09-11 in `/home/user/wt-rtfstyle` (branch `agent/rtfstyle`, base `61bc19e03`).
Reference **`/opt/libreoffice26.2/program/soffice` — LibreOffice 26.2.4.2**. Every reference
number below comes from that binary; nothing here is read off a page by eye, and there is no
`Task`/subagent tool in this container to delegate a blind reading to.

`words-close-r95` §2 closed O9 — `Body Text` and `caption` inherit the document's own `Normal`,
because `PoolFormattingOf` answered a constant and *Standard* is a **reference**. It left six
names out, for the stated reason that they reach `COLL_STANDARD` only through an *intermediate*
pool style whose own properties the import does not reset. This round measures those
intermediates, and the answer is not the one the brief expected.

---

## 1. The two routes, and the census of the second

`StyleSheetTable::ApplyStyleSheets` converts the entry's name and then asks
`xStyles->hasByName` (`sw/source/writerfilter/dmapper/StyleSheetTable.cxx`:1099-1101 — r95's
citation, verified). **Both halves of that sentence decide the question, and only the first had
been censused.**

For a name `ConvertStyleName`'s map does not hold, the function returns it **unchanged**
(`:2083-2113`) — unless the name collides with one of the map's own *values*, when it gains a
` (WW)` suffix and then matches nothing. So the second lookup is on the name as the file wrote
it, against Writer's paragraph style names in **either** spelling: `SwXStyleFamily::hasByName`
(`sw/source/core/unocore/unostyle.cxx`:1030-1039) maps a programmatic name to a UI name and looks
the UI name up.

`writer-names.tsv` is that set, generated from the reference's own two tables — the
`STR_POOLCOLL_*_ARY` order in `DocumentStylePoolManager.cxx`:290-460 against `sw/inc/strings.hrc`
for the UI names, and `SwStyleNameMapper`'s `Get*ProgNameArray` for the programmatic ones.
**126 paragraph pool styles, 252 spellings.**

`hasbyname-census.py` runs both rules over the 338 converted `.rtf`, on r88's selection rule
(a style counts as *used* only when a `\sN` outside `{\stylesheet}` names it) and r95's
condition (no resolvable `\sbasedon`, because that is when the pool parent decides anything):

| | |
|---|---:|
| `.rtf` scanned | **338** |
| distinct names applied without a resolvable `\sbasedon` | **84** |
| of those, names that reach an existing Writer paragraph style | **17** |
| of the 17, names that reach it by `hasByName` rather than the map | **3** |

The three are **`Figure`, `Heading` and `Text`**, one document each. **The brief is wrong about
`Figure`**: it is in `ConvertStyleName`'s map for neither spelling, so it is in exactly the same
class as the bare `Heading` the brief separated it from. `Text` is a name nobody had named at
all — it is `COLL_LABEL_FRAME`, and `bulletin.rtf` applies it to 17 paragraphs.

**Every Writer paragraph pool style has `COLL_STANDARD` at the root of its chain.** That falls
out of transcribing `GetPoolParent` (`sw/source/core/doc/poolfmt.cxx`:169-296) group by group,
which the census does: `COLL_HTML_BITS` answers Standard outright, `COLL_DOC_BITS` and
`COLL_REGISTER_BITS` and `COLL_LISTS_BITS` funnel through one base style each, and in
`COLL_EXTRA_BITS` and `COLL_TEXT_BITS` every branch terminates there too. So *"does the walk
reach Standard"* is never the question. *"What does the intermediate contribute"* is the whole
of it.

The control family is unchanged and now has a second member. `Quote`, `List Paragraph` and
`Normal (Web)` are in the map with an **empty** Writer name, so nothing is reused; `Marginalia`
and `Text body indent` are Writer names that are also map *values*, so the ` (WW)` suffix takes
them out of reach. Measured: all five keep `\pard\plain`'s twelve points at 26.2.4.2.

## 2. What the intermediates contribute, and the one line that explains it

`genpool2.py` and `genpool3.py` write 116 one-name probes — a style named *X* with a forward
`\sbasedon` that cannot resolve, applied to one paragraph between two `\fs20` controls, each
arm × the two `Normal` sizes that tell inheritance from a constant. `readfodt.py` resolves the
`style:parent-style-name` chain out of `soffice --convert-to fodt`, which is ~0.15 s a document
when a whole directory goes to one process, against ~8 s for a PDF.

| intermediate | names it is under | what it adds to the paragraph |
|---|---|---|
| `COLL_HEADERFOOTER` — *Header and Footer* | `header`, `footer` | **nothing** |
| `COLL_REGISTER_BASE` — *Index* | `toc 1`…`toc 9`, `Index 1`…`Index 3` | **nothing** |
| none — a bare `Heading` **is** `COLL_HEADLINE_BASE` | `Heading` | **nothing** |
| `COLL_LABEL` — *Caption* | `Figure`, `Illustration`, `Table`, `Drawing`, `Text` | **italic, 12 pt, 6 pt above and below** |
| `COLL_TEXT` — *Text body* | `List Indent`, `First line indent`, … | 7 pt below and 115 % line spacing — **not modelled**, §5 |

**The `COLL_HEADERFOOTER` row is a measurement, not a reading**, and it had to be: the block at
`DocumentStylePoolManager.cxx`:914-937 gives *Header and Footer* two tab stops and switches line
numbering off, and after an RTF import it has neither. `intermediates.py` is the control that
separates *the pool style has none* from *the export does not write them* — the same binary and
the same filter over a natively-imported document, where both tab stops are present
(`intermediates.txt`):

```
style                     after an RTF import              after a native ODF import
Header_20_and_20_Footer   parent=Standard                  parent=Standard tabs=3.3465in:center,6.6929in:right number-lines=false
Caption                   parent=Standard font-size=12pt   parent=Standard font-size=12pt font-style=italic …
                          font-style=italic …
```

**The line that decides it is `DomainMapper.cxx`:141**, *"Don't load the default style
definitions to avoid weird mix"* — `SetDocumentSettingsProperty("StylesNoDefault", true)` at the
start of the import, put back to false at `:259` once importing is finished. That flag is
`bNoDefault` in `DocumentStylePoolManager::GetTextCollFromPool` (`:676`), and it skips the whole
per-style `switch` that would have given the style its properties. So the rule is **when the pool
style was created**, not which one it is:

* `COLL_HEADLINE_BASE`, `COLL_NUMBER_BULLET_BASE`, `COLL_LABEL` and `COLL_REGISTER_BASE` are
  created by `SwDocShell::InitNew` (`sw/source/uibase/app/docshini.cxx`:224-289), before the
  filter runs, and keep everything;
* `COLL_HEADERFOOTER`, `COLL_HEADER` and `COLL_FOOTER` are created *during* the import, when the
  entry's name is looked up, and get nothing.

That same `InitNew` loop is where *Caption*'s **12 pt** comes from, and it is not the `PT_10` of
`DocumentStylePoolManager.cxx`:964: the loop overwrites each of those four styles' size with the
configured default — `FONTSIZE_DEFAULT` 240 for `FONT_CAPTION`, `FONTSIZE_OUTLINE` 280 for
`FONT_OUTLINE` (`sw/source/uibase/inc/fontcfg.hxx`:51,54). The 280 is the 14 pt this tree has
been giving `HeadingPool` since round 87 from the other citation; the two agree.

## 3. *Caption* is a reference too, and the corpus takes that arm

`SetPropertiesToDefault` (`StyleSheetTable.cxx`:305-331, called at `:1111`) resets the style the
entry **matched**, and then the entry's own properties are written back over it. A document that
declares a `caption` of its own therefore *replaces* the intermediate — and both corpus documents
that apply a `COLL_LABEL_*` name do exactly that:

```
{\s344\sbasedon0\snext0\sb120\sa60\keepn\rtlch\ab\ltrch\f4\fs20\b caption;}   231164_SystemDesignDocument
{\s593\sbasedon0\snext0\rtlch\afs20\ab\ltrch\f0\fs20\b caption;}              bulletin
```

so the italic never arrives and what `Figure` and `Text` take is that entry's **bold 10 pt**.
`genpool3.py` measures both arms, fourteen names × two `Normal` sizes: `plain` gives 12 pt italic
with 6/6, `withcap` gives 10 pt bold with nothing.

The `withcap` arm is also the reason this is not a constant in the model. `RtfPoolParent.Caption`
answers the document's own `caption` entry when there is one and Writer's pool `Caption` when
there is not, exactly as `RtfPoolParent.Standard` answers the document's own `Normal`.

## 4. Both sides, 116 probes

`measurepool.py` renders every probe through 26.2.4.2 and through this tree and reads HEAD's
size, face and the baseline gaps either side off the raster.

| | agree | differ |
|---|---:|---:|
| `genpool2.py` — 10 names × 3 arms × 2 `Normal` sizes | **60** | 0 |
| `genpool3.py` — 14 names × 2 arms × 2 `Normal` sizes | **52** | 4 |
| total | **112** | **4** |

The four are `List Indent` and are §5. Two rows worth reading, because they are where the two
rules meet:

| probe | 26.2.4.2 | this tree |
|---|---|---|
| `p_headingbare_plain_20` / `_28` | 10 pt / 14 pt, no spacing, no keep | the same |
| `p_figure_own_20` | 12 pt **italic**, 6 pt above and below | the same |
| `p_figure_withcap_20` | 10 pt **bold**, upright, no added spacing | the same |
| `p_quote_*` (control) | 12 pt at both `Normal` sizes | the same |

`p_headingbare_*` is the trap in the brief's list: a bare `Heading` answers *Standard*, **not**
the heading constants, because `Heading` *is* `COLL_HEADLINE_BASE` and the import resets the very
style that would have supplied them. `p_figure_own_*` is round 87's rule meeting this one — the
entry states its own `\fs28`, `getDefaultSPRM` writes the reset back over it, and the italic and
the spacing arrive anyway because they come from an *ancestor*.

## 5. What is not modelled, and why

`List Indent` is `COLL_CONFRONTATION`, whose pool parent is `COLL_TEXT` — *Text body*, which
states 7 pt below and 115 % proportional line spacing (`DocumentStylePoolManager.cxx`:694-700)
and which `InitNew` creates, so it keeps them. 26.2.4.2 answers the document's own `Normal` size
with about 8.7 pt more below; this tree answers `\pard\plain`'s twelve points. It is left open
for two reasons: `RtfStyleFormatting` carries no proportional line spacing, and the corpus reach
is **nil** — 0 of 338 apply any `COLL_TEXT`-parented name. `AnIntermediateBelowTextBodyIsNotModelledYet`
pins it as a test, in r88's idiom, and the four probes are reported as differing rather than
quietly dropped.

Not modelled either, and for the same nil-reach reason: the full 439-entry `ConvertStyleName` map
and the full 252 Writer spellings. `PoolParentOf` holds the names this round measured plus the
numeric siblings of measured families. The census is what says that costs nothing — **outside
`Normal` itself, the 338 apply no other name that reaches a Writer style at all.**

