# indexwrap-r169 — a floating obstacle, and what wraps beside it

**Reference: `/opt/libreoffice26.2/program/soffice`, LibreOffice 26.2.4.2
(`0229ac93fcf0d7cbc6376066c6f35021cef002dc`).** Ours is this tree's own CLI. Nothing in
`dotnet/src` was changed by this round.

## Headline: the fixture asks a question this tree cannot reach, and that is the finding

`probes/pages-r164` §3 proposed that `absrc-pac-01-info-note-en.doc`'s missing page is a Writer
**section frame not being wrapped beside a fly** — the reference puts its `text:table-of-content`
below a left-anchored table and we wrap the index's paragraphs beside it — and asked for two
fixtures, an obstacle followed by a plain paragraph and the same obstacle followed by a table of
contents.

Built as `w:tblpPr` positioned tables, those fixtures **never reach the question**: this tree does
not float such a table at all when there is room beside it, so nothing is wrapped and both arms
answer the same. Four arms, in DOCX and again after the reference's own conversion to DOC:

| arm | what | 26.2.4.2 | ours |
|---|---|---|---|
| `plain-after` | the prose after the table | p1 x **229.70** | p1 x **72.00** |
| `toc-after` | the index after the table | p1 x **229.70** | p1 x **72.00** |
| `heading-then-plain` | the heading, then the prose | p1 x 229.70 | p1 x 72.00 |
| `heading-then-toc` | the heading, then the index | p1 x 229.70 | p1 x 72.00 |

And vertically: the reference draws `PROSE line 1` at y 708.80, level with `QUICK LINK 1` at
708.25, while we draw it at **611.20** — below the whole table. So the table is in our flow and
beside the reference's.

## 1. It is a documented decision, not an oversight

`[src]` `Paginator.PlaceFloatedTable` (`Layout/Paginator.cs`:3602-3645) reads a `w:tblpPr` table's
position, and then declines it:

> A fly with room beside it is one this cannot place: Writer wraps the flow into that room and
> nothing here can. A fly that fills the column has no such room, and Writer puts the flow
> *under* it — which is a position rather than a wrap, and is reproducible.

`FillsTheColumn` is that test. The fixture's table is 3084 twips in a 9360-twip column, so it
leaves 6276 twips of room and stays in the flow. **A positioned table is never an obstacle in this
tree**, however it is written — the reader's `PageTable.IsPositioned` reaches
`FlowLayouter`'s float and `Paginator`'s placement, and neither registers it with
`FrameObstacles`.

**Reach if anyone takes it: 50 `w:tblpPr` in 42 of the corpus DOCX**, of which the ones that
change are those whose table does not fill its column. Text wrapping beside a floating table is a
feature rather than a bug fix, and this fixture is a starting point for it.

## 2. So `absrc`'s index question is still open, and the next fixture needs a different obstacle

`FrameObstacles` does wrap text beside a **frame**, which is why `absrc` shows the behaviour at all
and this fixture does not: whatever the WW8 reader makes of that document's quick-links table, it
is not a `PageTable { IsPositioned: true }`. The next cut has to use an object the obstacle path
actually sees — an anchored shape or text box with square wrapping — and put a plain paragraph
after it in one arm and a `TOC` field in the other.

**One inference in `pages-r164` §3 should be re-checked before it is built on.** That round read
`INFORMATION HIGHLIGHTS` at x 311.00 in the reference and 310.86 here as *both engines wrapping it
beside the obstacle, out of document order*. On a 612 pt page with 72 pt margins, 311 is also
roughly where a centred or indented heading of that length starts, and the round did not separate
the two. Measure the heading's own alignment before taking the agreement as evidence about
obstacles.
