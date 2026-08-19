---
name: page-vision
description: Use your own vision on a rendered page — how to get a page in front of a reader at a resolution where the defect survives, how to delegate the reading to an uncontaminated reviewer, and what a visual reading can and cannot establish. Use when looking at a failing page, reviewing a rendering against the LibreOffice reference, or when a metric says a page is wrong and you need to know *what* is wrong. Covers the viewer's pixel budget, when to crop, pairing two renderings into one reviewable image, and the contamination traps that make a fluent reading worthless.
---

# Looking at a page

`render-comparison` says *look at the page* and is right. This skill is the mechanics of
actually doing it: getting the pixels in front of a reader at a size where the thing you are
hunting is still visible, and keeping the reading honest enough to act on.

Three separate failures this addresses, all of which produce a confident and useless report:

- the page arrives too small to show the defect, and the reviewer reports the resolution;
- the reviewer already knows what is supposed to be wrong, and finds it;
- the reading is treated as a diagnosis rather than as an observation.

## Delegate the reading. It is the only real control.

**Send the page to a fresh subagent and let it describe the page before you look at it
yourself.** Everything else in this file is secondary to that.

The reason is measured, not stylistic. While calibrating this skill I read one page at 150
dpi, then at 72, then at 36. The 36 dpi read came out fluent and detailed — and it was
worthless, because by then I was reciting the page rather than reading it. **You cannot
un-see a page.** Any second look at a page you have already seen is recall wearing the
costume of observation, and it will agree with whatever you already believed.

A subagent that has never seen the document, has not read the round's brief, and is
forbidden from grepping the repo is the only reader in the loop whose agreement means
anything.

```bash
export PAPERLESS_CLI=<the tree you mean to measure>/dotnet/tools/…/Paperless.Cli
.claude/skills/page-vision/scripts/pair.sh "<doc>__xlsx" --worst --outdir /abs/pairs
```

That prints one path. Hand the path to a subagent with a brief that:

- names which half is which **only as a fallback** — the image labels itself, see below;
- **forbids** reading project documentation, source, `results.md`, or running any command.
  A reviewer that greps for the answer is no longer a control;
- asks for the two halves to be described **separately, before** they are compared;
- demands **direction** — "the reference breaks lines earlier", not "line breaks differ";
- asks explicitly for what looks *identical*, which is what rules causes out;
- asks for the candidate causes the image **cannot decide between**, and what measurement
  would separate them.

Run several in parallel, one page each. They are cheap and independent, and a class that
shows up in three unrelated readings is worth more than one that shows up in three pages a
single reader looked at in sequence.

### Do not brief a reviewer with the numbers

Telling a reviewer "this page has 945 words against the reference's 431" buys a report about
word counts. The gate numbers are what you are trying to *explain*; feeding them in gets you
a story that fits them. Give the reviewer the image and nothing else, and compare their
reading against the numbers afterwards — when a blind reading and an independent measurement
land on the same mechanism, that agreement is evidence. When you supplied the answer, it is not.

## The viewer has a fixed pixel budget: about 2000 px on the long edge

This is the mechanical fact everything else follows from, and it is measured. A page rendered
at 600 dpi — 4961×7016 — came back annotated:

```
[Image: original 4961x7016, displayed at 1414x2000. Multiply coordinates by 3.51 …]
```

So an image larger than the budget is **downscaled before anyone sees it**, and the viewer
says so when it happens. Two consequences:

- **For a full A4 portrait page, anything above ~170 dpi is thrown away.** 600 dpi costs 40×
  the pixels of 150 dpi and shows you exactly what 150 dpi shows you.
- **If you see that "displayed at" line, you wasted the render.** It is a receipt for pixels
  you paid for and did not receive.

Landscape 16:9 slides have a shorter long edge relative to their content, so the same budget
buys them ~150 dpi.

## What resolution do you actually need? Work in pixels per em

The variable that decides legibility is not dpi, it is **how many pixels tall one em is**:

```
px_per_em = point_size × dpi / 72
```

Measured on a real 10.00 pt table (blind reads, scored token-for-token against `pdftotext`):

| dpi | px/em | page pixels | blind read against ground truth |
|---:|---:|---|---|
| 150 | 20.8 | 1241×1754 | **221/221 tokens**, 3 differences, all line-join artefacts |
| 72 | 10.0 | 596×842 | fully legible |
| 60 | 8.3 | 497×702 | **249/249 tokens, 0 missed, 0 invented** — on a page never seen before |
| 36 | 5.0 | 298×421 | structure only; and see the contamination warning above |

**About 8 px per em reads reliably.** So the dpi you need is `576 / point_size` — 58 dpi for
10 pt text, 96 dpi for 6 pt.

Put that beside the 2000 px budget and the useful conclusion is: **a full page at the budget
resolves text far smaller than any document actually uses.** Legibility is therefore almost
never the reason to crop, and "I could not read it" almost always means the image was built
wrongly rather than that the page needed more pixels.

## What cropping *is* for: ink finer than a glyph

Crop when the question is about geometry below the level of a letter:

- hairline width; **doubled or overlapping strokes** where table borders meet;
- glyph shape and ligatures;
- antialiasing, colour at edges, gradient banding;
- whether two nearly-equal lengths are actually equal.

Measured: at 150 dpi the word "Aircraft" is simply the word "Aircraft". In a 600 dpi crop it
is visibly set with an **`ft` ligature**, and the cell separators resolve into distinct
hairlines. Neither fact is recoverable from the full page at any dpi, because the full page
cannot exceed the budget.

`pdftoppm` crops in device pixels at the dpi you ask for, which is exactly what you want:

```bash
pdftoppm -r 600 -f 5 -l 5 -png -x 600 -y 780 -W 1900 -H 700 ref.pdf crop
```

Keep `-W`/`-H` inside the budget or the crop is downscaled too and you are back where you
started.

## Pairing two renderings

**By default, do not composite at all.** Two separate images each get their own 2000 px
budget; one composite splits a single budget between both halves. Reading them as two images
is strictly higher resolution.

Composite when the question is **alignment** — is this block displaced, does this column
start in the same place, has the whole page drifted — because that is the one question two
separate images answer badly.

```bash
.claude/skills/page-vision/scripts/compose.py ours.png ref.png -o pair.png
```

`compose.py` does three things worth knowing:

- **It picks the arrangement that costs the least resolution.** Two portrait pages side by
  side have a long edge of 2×595 pt; stacked, 2×842. So side-by-side is the higher-resolution
  arrangement for portrait pages, and stacking is higher for 16:9 slides. `--layout auto`
  computes this. Note this cuts against the older advice to always stack — stacking is right
  when you need the same region to land under itself, and it costs resolution to get that.
- **It labels the halves inside the image** — blue band "OURS (PAPERLESS)" on the left/top,
  orange "REFERENCE (LIBREOFFICE)" on the right/bottom. A reviewer told which side is which
  only in the prompt will occasionally report them the wrong way round, and a swapped reading
  inverts the sign of every conclusion in it. The label travels with the pixels.
- **It warns when the two halves differ in size.** Two halves rendered at different dpi
  compose into an image whose most striking feature is the scale difference, and the reviewer
  duly reports that our text is tiny. That defect lives in the compositor. If both halves
  *were* rendered at the same dpi, then the page geometry genuinely differs and that is the
  finding — which is why this warns rather than refuses.

It reduces by taking the **darkest pixel of each block**, and the reason is a scar.

It used to reduce with nearest-neighbour, with a comment claiming that preserved a
one-pixel hairline. It does not: nearest *samples* one source pixel per destination pixel,
so a rule thinner than the sampling step falls between samples and disappears. Composing at
80% silently dropped a real underline — a 245 px solid black run at y=802 in the 150 dpi
render, present as a fill in the PDF itself — and two independent reviewers then correctly
reported that the composed image had no underline in it. That was relayed onward as a
renderer defect. It was a compositor defect.

Averaging is not the fix either: it turns a doubled hairline — a defect this project *is*
hunting — into one grey smudge indistinguishable from a single line. Darkest-of-block keeps
any hairline at full strength, keeps two of them distinguishable while a pale row separates
them, and never invents ink. It biases towards showing marks, which is the right bias when
the question is "is this drawn or not".

**The general rule, which this file should have taken from `render-comparison` and did
not:** before believing that a reviewer's "it is absent" is a fact about the document, check
it is not a fact about the pipeline that built their image. Confirm absence in the PDF's own
operators — `pdf-ops.py dump`, or a thin-wide-fill scan — not in a downscaled raster. A
missing mark and a mark your instrument threw away look identical to the reader.

`pair.sh` chains `look.py` and `compose.py` so the two halves cannot be rendered at
mismatched dpi by hand. About 20 s per document.

## What a reading establishes, and what it does not

**Reliable from an image:** presence and absence; position and displacement; relative size;
colour; the *direction* of a difference; the *kind* of element involved.

**Not reliable — measure it:** exact values; counts of many similar items; whether two nearly
equal lengths are equal; font identity; and anything that requires knowing what *should* be
there.

**Never available from an image: cause.** An image cannot separate a picture bullet from a
character bullet in a substituted symbol font from an autonumber. The discipline is to *name
the candidates the image cannot decide between* and then go and measure. A reading that
promotes itself into a diagnosis is worse than no reading, because it is cited later as though
it were one.

### `pdftotext -bbox` reports an ink box, not a baseline

Its `yMin` is derived from the **font descriptor**, not from the text-positioning operator. So two
renderings whose baselines are identical to the hundredth of a point can report a constant `yMin`
offset, and it looks exactly like a vertical layout defect.

Measured: every Caladea heading in `exhibit-06---technical-architecture-template.docx` showed a
flat **2.1 pt** offset against the reference — which is precisely `usWinAscent − sTypoAscent` for
that face. The `Td` baselines were the same on both sides. A round nearly filed a font-metric bug
on it.

**When the question is vertical position, read the baseline out of the content stream.** Use
`-bbox` for *which* words are on a page and roughly where; use the operators for where a line
actually sits.

### Ink and the text layer are different things, and may legitimately disagree

You read ink. `pdftotext` reads the text layer. Measured on the calibration page: the rendered
line ends `L410 UVP-E-` and continues `LW`, with a visible hyphen — and the text layer contains
no hyphen at all, joining it as `UVP-ELW`. Neither is wrong.

So when your reading and `pdftotext` disagree, that is not automatically a misreading. It can
be a finding *about the text layer* — which is the half of the output the word-count gate
actually scores.

## Order of work

1. **Render both sides at the same dpi.** `pair.sh` if you want one image, `look.py` if two.
2. **Get it read by someone who has not seen it** — a subagent, in parallel across pages.
3. **Collect direction and kind**, plus what looked identical.
4. **Name the candidate causes** the image could not decide between.
5. **Only now consult the record** — `results.md`, the TODOs, the source. Reading blind first
   and looking up second is a control on the reading, and it works: a gradient description
   produced that way matched a diagnosis made a week earlier from source with no chance of
   having been led to it.
6. **Measure the named cause.** The reading told you where to point the instrument; it is not
   the instrument.

## Look at pages that PASS

Inherited from `render-comparison` and repeated because it is the highest-yield habit here:
the failing set is picked over, and the gate — page count, words within max(2%, 3), unembedded
fonts — is blind to most real defects. A track can be 163 of 163 page-exact while three
passing pages opened at random yield three findings. Rank the *passing* documents by `|ink|%`
and send the worst to a reviewer.
