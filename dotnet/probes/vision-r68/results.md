# Five pages read blind, 2026-09-06

## Why this exists, and why the readings are worth something

`dotnet/CLAUDE.md` has said "look at the rendering, do not chase it through metrics alone"
for many rounds, and `.claude/skills/page-vision/SKILL.md` says the reading is only worth
something if it comes from **a reader who has never seen the document, has not read the
round's brief, and cannot grep the repository**. Six rounds in a row reported that they could
not do that — there is no vision-delegating tool inside a round agent's session, established
by testing rather than assumed (`create_session` spawns a sibling in its own container that
cannot open the PNGs; `SendMessage` answers "No agent named … is reachable").

**The parent session can.** These five readings were produced by five separate fresh
reviewers, each given one composed pair image and a brief that forbade reading any other
file and forbade running any command at all. Each was asked to describe the two halves
*separately, before comparing*, to give **direction** rather than existence, to say what
looked **identical**, and to flag what the resolution could not settle.

The documents were the five the whole-corpus mismatch classification
(`probes/mismatch-classify-01/`) had marked **"screened, not read"** or read only in one
line, plus one chart it had read briefly.

## Environment

    ours   = Paperless.Cli @ 0fc357beb
    ref    = /opt/libreoffice26.2/program/soffice, LibreOffice 26.2.4.2, the calibration target
    fonts  = system fontconfig; the 26.2 install's three bundled confounds moved aside
    pairs  = 110 dpi, composed by .claude/skills/page-vision/scripts/compose.py
    corpus = /home/user/sample-files, 947 documents

## What the readings found

### `014_Contextures_chart_sample_991ecfc5.xls` p1 — six symptoms on one Pareto chart

- primary value axis reads `$1 $1 … $0 $0` against the reference's `$800 $720 … $0`;
- **no secondary percentage axis**, where the reference carries `100.0%` down to `0.0%`;
- the `Cumulative %` series draws no visible line and its legend key is a hollow square
  rather than a line-and-marker;
- the bars **run off the top of the plot** and continue up over the chart title and the
  worksheet heading to the top edge of the page — about a full plot-height of overshoot;
- **18-20 phantom `#N/A` categories** after the seven real ones, which are compressed into
  the leftmost 30% of the axis;
- the legend sits outside the plot to the right, stacked; the reference's is inside it,
  upper-left, horizontal. The reference also draws an outer chart frame and we do not.

Identical: the worksheet heading, `Total of All Values: $793.0M`, the chart title, the
plot's pale-yellow fill and its gridlines, both series' colours, both legend strings, and
all seven category names including a two-line wrap.

**The reviewer could not tell whether the `$1`/`$0` ticks are a wrong axis maximum or
truncated labels, and said so.** The hypothesis handed to the round that owns this: the
`Cumulative %` series belongs on the secondary axis and runs 0-1, so plotting it on the
primary makes the primary's maximum ~1 and forces the `$800`-scale bars to overshoot by
exactly the amount observed — one cause for three symptoms.

### `055_Project_timeline_with_milestones…xlsx` p1 — a category axis where a date axis belongs

- we print the literal string **`[CELLRANGE]`** twice, above `Project start` and
  `Project end`; the reference prints nothing there. That is *extra* glyphs, and this
  document's gate surplus is +166;
- the reference's axis carries ~38 **date** labels with its markers crowded into the
  leftmost tenth and one outlier at the far right; ours carries 13 **milestone-name**
  labels evenly spaced — a category axis where the reference has a date axis;
- our data labels' second line is the milestone name; the reference's is a date;
- the reference draws a leader line from every label to its marker and we draw none.

### `029_Annual_budget…xlsx` p1 — per-point colour and chart text weight

- we draw both bars in **one flat slate-blue**; the reference draws bar 1 a saturated dark
  indigo with a slight gradient and bar 2 a very pale lavender;
- the reference's axis, data and category labels are **bold**; ours are regular weight;
- we label the categories `Income` and `Expenses`; **the reference labels them `1` and
  `2`** — ours is the better output, and the standing rule is not to chase a LibreOffice
  bug, so that one needs a ruling from the chart XML before anything is changed.

Identical: page geometry to within a pixel or two over the whole page, the summary block,
both tables and every value in them, the axis range and step, both data-label strings.

### `070_Equipment_inventory_list…xlsx` p1 — a second theme-colour witness, and a shape box

- the title `EQUIPMENT INVENTORY LIST`, the largest text on the page, is **olive/green in
  ours and steel/slate blue in the reference** — same glyphs, same size, same start and end
  x. This is the **second independent witness** for the theme-colour chain, after
  `053_Personal_asset_inventory`'s blue-where-the-reference-is-teal;
- the reference strokes a **thin green outline** around each of three slicer placeholder
  boxes and **clips their text to the box**; we stroke nothing and let the text overflow
  into its neighbour, producing an overprinted smear;
- the reference splits each notice into two paragraphs with a blank line; ours runs it
  continuous.

The three dates differing by three years are the known volatile-recalculation class
(`TODAY()` re-evaluated by the reference, read from the file's cache by us), and
`Current value` follows from them. Not a defect of this class.

### `048_Expense_trends_budget…xlsx` p1 — the reading the image could not settle

The reviewer reported ours showing a normal left margin and chopping mid-word at the right
edge, and the reference losing text at **both** ends; it inferred that our page might be
~1.5x narrower and **said plainly that it could not prove it**, naming the rival reading
(the pairing tool taking different windows) and calling its own conclusion "no more than a
lean".

**It is refuted, and refuting it took one command.** Both renderings are
`612 x 792 pts (letter)` with **14 pages**, and `pdftoppm` gives identical raster
dimensions page for page. Neither page is narrower than the other.

What the text layer says instead:

    p1 ours : TEMPLATE TIPS Is there an easy way to jump between the expense trends summary…
    p1 ref  : E TIPS ay to jump between the expense trends summary sheet and monthly expense…

The reference is missing `TEMPLAT` from `TEMPLATE` and `Is there an easy w` from `Is there
an easy way` — it is clipping at the **left**, where we clip only at the right. A block
wider than the page losing both ends is the signature of **centring**; losing only its right
end is the signature of **left-alignment**. So the live hypothesis is that a cell's text
which overflows its column is centred by the reference and left-aligned by us, and that is
what the +102 glyph surplus is made of.

## The methodological point, which is the reason to keep this file

**Two of the five readings contained a confident claim that measurement then overturned or
had to settle** — this one, and a claim in an earlier round that a footer string was missing
which `pdf-ops.py` then placed within 0.2 pt on both sides. Both reviewers flagged their own
uncertainty, which is what made them cheap to check.

That is the split to hold on to: **a blind reading is very good at direction and kind —
"the bars overshoot the plot upward", "the reference clips at the left" — and cannot
establish cause.** Every reading above was turned into a named hypothesis and handed to a
round to measure, and none of them was acted on as a diagnosis.
