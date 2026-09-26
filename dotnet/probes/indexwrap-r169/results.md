# indexwrap-r169 — is a table of contents wrapped beside a floating obstacle?

**Reference: `/opt/libreoffice26.2/program/soffice`, LibreOffice 26.2.4.2
(`0229ac93fcf0d7cbc6376066c6f35021cef002dc`).** Ours is this tree's own CLI. Nothing in
`dotnet/src` was changed by this round.

## Headline: **no** — the hypothesis is refuted, at the reference

`probes/pages-r164` §3 proposed that `absrc-pac-01-info-note-en.doc`'s missing page is a Writer
**section frame not being wrapped beside a fly**: the reference puts that document's
`text:table-of-content` below a left-anchored obstacle and we wrap the index's paragraphs beside
it, 179.9 pt right and 340.9 pt high. It named `FrameLayout.FrameObstacles.SpaceFor` as the seat
and a per-flow "descend past the obstacle" flag as the patch, conditional on a fixture nobody had
built.

**Built, it says the opposite.** 26.2.4.2 wraps a table of contents beside an anchored text box
exactly as it wraps a paragraph, and exactly as this tree does — to within 0.13 pt on six arms:

| arm | what | 26.2.4.2 | ours |
|---|---|---|---:|
| `box-plain-after` | the prose after the box | x 235.20 | x 235.10 |
| `box-toc-after` | a `TOC` field's entries | x 235.20 | x 235.10 |
| `box-toc-tabbed` | the same, each entry carrying the full-measure dot-leader right stop a real Word entry has | x 235.20 | x 235.10 |
| `box-plain-tabbed` | plain paragraphs with that same stop (control) | x 235.20 | x 235.10 |
| `box-heading-then-toc` | a centred heading, then the index | x 307.85 / 235.20 | x 307.72 / 235.10 |
| `box-heading-then-plain` | the same heading, then prose (control) | x 307.85 / 235.20 | x 307.72 / 235.10 |

So a section frame is **not** exempt from wrapping, the tabbed arm rules out "the entry is as wide
as the measure so it cannot fit", and **the proposed patch would have broken six arms that are
currently right.** `absrc`'s cause is still unknown; it is something about that document rather
than about indexes and flies.

## 1. A positioned table is never an obstacle here, which the first cut of this fixture hit

The first four arms use a `w:tblpPr` table as the obstacle, and **never reach the question**: this
tree does not float such a table when there is room beside it, so nothing wraps and every arm
answers the same. In DOCX and again after the reference's own conversion to DOC, the reference
draws the prose level with the table's first row (y 708.80 against `QUICK LINK 1`'s 708.25) and we
draw it at **611.20**, below the whole table.

`[src]` That is `Paginator.PlaceFloatedTable`'s own documented decision rather than an oversight
(`Layout/Paginator.cs`:3602-3645):

> A fly with room beside it is one this cannot place: Writer wraps the flow into that room and
> nothing here can. A fly that fills the column has no such room, and Writer puts the flow
> *under* it — which is a position rather than a wrap, and is reproducible.

`FillsTheColumn` is that test, and the fixture's 3084-twip table in a 9360-twip column leaves
6276 twips of room. A positioned table reaches `FlowLayouter`'s float and `Paginator`'s placement
and never reaches `FrameObstacles`. **Text wrapping beside a floating table is a feature rather
than a bug fix; the census that would scope it is 50 `w:tblpPr` in 42 of the corpus DOCX**, of
which the ones that would change are those whose table does not fill its column. The four
`*-after` arms here are a starting point for it.

## 2. What `absrc` actually shows, and one correction to how it was read

`[bin]` On the reference's page 1, in draw order by height: `QUICK LINKS` at y 505.60,
`INFORMATION HIGHLIGHTS` at **y 448.10 x 311.00**, the obstacle's rows at 436.15 → 171.65, and the
first index entry at **y 82.70 x 76.60**. Ours agrees on the first three to 0.15 pt and puts the
index at y 423.60 x 256.50 — beside the obstacle, where the reference has it below.

**The heading's x 311 is evidence of wrapping after all, and an earlier note in this file saying
it might be centring is withdrawn.** `P30` states `fo:text-align="center"`, and on this page
centring in the *full* measure (72 → 540) would start an 11 pt bold `INFORMATION HIGHLIGHTS`
around x 236. Centring in the measure *left over beside the obstacle* (220.8 → 540) starts it at
**310.4**, which is what both engines draw. So both wrap the heading, and only the index differs —
which is exactly what `pages-r164` reported.

What it is not is the rule above. Two things to try next, neither of which this round has
measured: whether the obstacle's own wrap mode in that WW8 file differs from the square wrap the
fixture uses, and where the reference puts the eight empty paragraphs between the heading and the
index — a 365 pt gap with nothing visible in it.
