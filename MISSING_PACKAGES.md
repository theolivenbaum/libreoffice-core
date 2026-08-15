# Packages this environment needs and did not have

Install these when provisioning a container for the Paperless parity work. Each one below was
found missing by measurement, not by guesswork, and each entry says what broke and how the
breakage presented — because in every case so far the symptom looked like something else.

```sh
apt-get update && apt-get install -y fonts-dejavu-core
```

That is the whole list at present. It is short and it is not trivial.

**Re-check it every session — the install does not survive.** This was installed and written
up as fixed, and a later session opened with `fc-match "DejaVu Sans"` answering
`wqy-zenhei.ttc` again and the package absent from `dpkg -l`. Every *other* font the reference
needs was still present, so nothing looks wrong until you check this one. The `apt-get update`
in the command above is load-bearing rather than habit: without it the container's stale index
answers `E: Package 'fonts-dejavu-core' has no installation candidate`, which reads as the
package having been withdrawn from the archive when it has not.

```sh
fc-match "DejaVu Sans"      # must say DejaVuSans.ttf, not wqy-zenhei.ttc
```

---

## `fonts-dejavu-core`

**Symptom without it:** two unit tests fail —
`Paperless.Spreadsheets.Tests.SheetColumnDigitsTests.ADigitWidthIsNeitherTruncatedNorRounded`
at `("DejaVu Sans", 11)` and `("DejaVu Sans", 12)` — and *nothing else in the suite reports a
problem*. Spreadsheets sits at 619/621 and the other nine projects are green.

**Why those two tests are the canary.** They are not incidental. The test pins DejaVu Sans at
1303/2048 of an em specifically because it straddles the rounding carry from the opposite side
to Carlito's 1038/2048, and its comment records that all four figures "were read out of the
`style:column-width` LibreOffice 24.2.7.2 wrote for a one-column probe workbook". So the
repository's own test suite is a statement that DejaVu was installed in the environment where
every stored figure on this project was measured. **The test suite is the specification of the
environment**, and this is the case that proves it.

**Why it matters far beyond two tests.** DejaVu sits ahead of WenQuanYi Zen Hei in fontconfig's
fallback chain. **267 of the 534 reference PDFs — half the corpus — fall back to
WenQuanYiZenHei when DejaVu is absent.**

Measured on the **reference half only**, by sweeping the whole corpus twice with LibreOffice held
constant at 26.2.4.2 and *only* the font set differing:

| track | documents | page counts changed | total \|Δpages\| | word counts changed | total \|Δwords\| |
|---|---:|---:|---:|---:|---:|
| words | 200 | **42** | 383 | 55 | 7029 |
| slides | 163 | **0** | 0 | 61 | 3676 |
| sheets | 171 | **11** | 43 | 36 | 3191 |
| **total** | **534** | **53** | **426** | **152** | **13 896** |

Slides moving zero pages is the expected structural result: a deck's page count is its slide
count, so only the text channel can move there, and it did (61 decks).

**Two corrections to the first reading of this table, each established by later measurement:**

- **The direction is not uniform.** This was first written up as "every page-count change is in
  the same direction — fewer pages with DejaVu, because the DejaVu fallback is narrower". That
  holds on words but is false on sheets, where **6 of the 11 gain pages with DejaVu and 5 lose
  them**. DejaVu is not uniformly narrower than the face it displaced, and it also restores a bold
  that had collapsed into WenQuanYi. A tidy directional story was reached for before the sign had
  been checked per document.
- **The font set is an input to *both* halves of the gate, not only the reference.** Paperless
  resolves faces through fontconfig as well, so our own column moves too: re-rendering the sheets
  track from the same source with DejaVu present moved **31 of 171** of our own documents. The
  practical rule is that a parity figure is valid only when **both** banks were rendered on the
  same font set. A mismatched pair is worse than a merely stale one, because it is silently
  internally inconsistent. One round withdrew its number as a mismatched pair and it was later
  shown to have been consistent after all — both halves had been rendered before the font landed,
  53 seconds ahead of the `dpkg` timestamp.

A missing font is therefore not a cosmetic gap. It moves glyph advances, and glyph advances
move wrapping, row heights, cropping and pagination — which is to say it moves the gate's first
two checks directly. Any parity figure measured without it is measuring the wrong environment.

A missing font is therefore not a cosmetic gap. It moves glyph advances, and glyph advances
move wrapping, row heights, cropping and pagination — which is to say it moves the gate's first
two checks directly. Any parity figure measured without it is measuring the wrong environment.

**The trap it set, recorded because it nearly worked.** This container also has LibreOffice
26.2.4.2 where the stored figures were taken against 24.2.7.2. It is very natural to attribute
all reference movement to the version bump and stop looking — a prior pass did exactly that, and
wrote up a whole-corpus movement table on that basis. **Two variables had changed, not one.**
Anything phrased as "the 24.2.7.2 → 26.2.4.2 effect" and measured before this font landed is
confounded and has to be re-taken.

The first census that looked for the problem also missed it: `grep -rl dejavu` over the corpus
returns **zero** documents, and grepping the reference PDFs for `DejaVu` or `WenQuanYi` returns
zero too, because PDF font names live inside compressed streams. Both readings say "no reach"
and both are wrong. `pdffonts` parses the file and gives the true answer — the right instrument
matters more than the thorough-looking sweep.

---

## Deliberately *not* installed

- **`ttf-mscorefonts-installer`** — LibreOffice suggests it, and it is correct that it is absent.
  The reference PDFs name `LiberationSans`/`LiberationSerif`/`LiberationMono` throughout and
  never `Arial`, `Times New Roman` or `Courier New`. Liberation is the metric-compatible
  substitute, so the substitution is already happening and is what every stored figure was
  measured against. Installing the real MS fonts would change the reference on a large fraction
  of the corpus and invalidate the baseline in the same way the missing DejaVu did.
- **`fonts-dejavu-extra`** — adds Condensed variants that would enter the fallback chain. Only
  `-core` (which pulls `-mono`) is evidenced by the test suite; adding more is an unforced
  change to the font environment.
- **`python3-pil` and `python3-numpy`** — these are now installed on this container, and that was
  **an unnecessary install, recorded so nobody repeats it**. They went in to build a one-off
  side-by-side review page. The project's own comparison tooling does **not** want them and says
  so in `pdf-image-diff.py`'s own header: poppler renders to PPM, which is a header and raw RGB,
  and PNG is zlib plus four chunks, so *"adding numpy or Pillow to read two rectangles of bytes
  would be a dependency for its own sake"*. It reads P6 directly with `struct` and diffs a page in
  about a tenth of a second.

  Nothing was broken by their absence and nothing is broken by their presence — they do not touch
  rendering. **Do not add them to a provisioning script**, and prefer `pdf-image-diff.py` to
  writing a second pixel comparator.
- **Anything shipping a `.otf`** — and this one is a genuine, measured gap that is still being
  left open on purpose. `fc-list | grep -c '\.otf'` is **0** here, so the single remaining skip
  in `Paperless.Rendering.Tests` never runs: it guards a poppler failure mode that once blanked
  **161 glyph runs**, and it needs a CFF-outline face to exercise. A guard that cannot run is
  worth roughly nothing, so this is a real loss.

  It is nonetheless the right call for now, because **installing a font is precisely what caused
  this project's worst confound**. Adding `fonts-dejavu-core` moved 53 of 534 reference page
  counts and 31 of 171 of our own sheets renderings; any new face may enter the fallback chain
  and invalidate the canonical reference bank, forcing a 534-document re-sweep and re-stating
  every scoreboard. **The cost is a re-baseline; the benefit is one test.** If it is ever taken,
  take it deliberately: install, re-sweep the reference, and diff the banks before believing any
  figure measured across the change.

## Already present, and required — do not remove

`fonts-liberation`, `fonts-crosextra-carlito`, `fonts-crosextra-caladea`, `fonts-opensymbol`,
plus the CJK faces `fonts-wqy-zenhei` and `fonts-ipafont-gothic`. A census of all 534 reference
renderings with `pdffonts` shows the corpus resolves to exactly these:

| face | reference PDFs |
|---|---:|
| LiberationSans / -Bold / -Italic / -BoldItalic | 338 / 282 / 115 / 76 |
| WenQuanYiZenHei | 268 |
| Carlito Regular / Bold / Italic / BoldItalic | 233 / 177 / 60 / 39 |
| OpenSymbol | 203 |
| LiberationSerif and variants | 153 / 109 / 59 / 29 |
| LiberationMono and variants | 41 / 11 / 6 / 2 |
| IPAGothic, IPAPGothic | 26 / 3 |
| Caladea Regular / Bold | 8 / 8 |

`Montserrat-Bold` (2) and `Verdana-Italic` (1) also appear; those are embedded by the documents
themselves and need nothing installed.

---

## How to check the environment before trusting a measurement

```sh
fc-match "DejaVu Sans"                  # must report DejaVuSans.ttf, not a fallback
cd dotnet && dotnet test tests/Paperless.Spreadsheets.Tests/Paperless.Spreadsheets.Tests.csproj
                                        # must be 621/621, 0 skipped
```

A fallback answer from `fc-match` is the tell. `fc-match` never fails — it always returns
*something*, and here it returned WenQuanYi Zen Hei for a request for DejaVu Sans, which reads
as success unless you look at what came back.


---

## `libreoffice-math` — not installed, and it silently changes the reference

Found 2026-08-14 while working the words `extra` group, and **not installed**, deliberately.

Without it **every reference in this container draws nothing for an OMML equation.** A
one-equation probe renders as `BEFOREEQUATION  AFTEREQUATION`, with the equation's space
reserved on the page and no ink in it.

**`ABCD-FE-01-00 Flight Envelope - v1 08.03.16.docx` is the corpus document this decides, and
it is not a defect of ours — it is this missing package.** Measured 2026-08-15: the document
holds **54 `m:oMath` elements** (the figure of 33 recorded here earlier counted `m:oMathPara`,
which is the wrapper and undercounts) carrying **121 whitespace-split tokens**. Against a word
delta of **+111**, and — the decisive test rather than the arithmetic — of the 45 *distinct*
equation strings, **we draw more occurrences than the reference for 37**, the other 8 being
strings that also occur in ordinary body text. So the reference is drawing essentially none of
them.

That makes it the same shape as a raster ceiling: **our output is the better one and the word
gate scores it as a failure.** It should be read as an environment ceiling for as long as
`libreoffice-math` is absent, and it is the one corpus document whose verdict would change if
the package were installed — which is the concrete argument for eventually taking that step, at
the cost of re-banking every equation-bearing reference.

This is the same class of problem as the missing `fonts-dejavu-core` above: **an input to the
gate that nothing in the harness declares.** The difference is that the font affected 267 of
534 references and this affects only equation-bearing documents — but the failure mode is
identical, and so is the way it hides. A document whose equations the reference cannot draw
looks like a document where we draw too much.

**It was not installed on the spot, and that was the right call.** Installing it changes the
reference for every equation-bearing document, and other agents were mid-round measuring
against the banked set at `/c/sandbox/workdir/refpdfs-26.2.4.2-fonts/`. Doing it properly
means: install, re-bank the affected references, and re-baseline — as a deliberate step, when
no measurement is in flight.

```sh
apt-get update && apt-get install -y --no-install-recommends libreoffice-math
```

Until that happens, treat any word-count gap on a document containing `m:oMath` as suspect,
and check whether the reference drew the equation at all before attributing the gap to us.
