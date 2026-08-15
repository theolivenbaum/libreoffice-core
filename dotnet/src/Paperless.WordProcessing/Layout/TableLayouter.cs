using Paperless.Core.Geometry;
using Paperless.Core.Units;
using Paperless.Text.Layout;

namespace Paperless.WordProcessing.Layout;

/// <summary>
/// Works out how tall a table's rows are, and where each of its cells goes.
/// </summary>
/// <remarks>
/// <para>
/// Two passes, because the answer is circular until it is not. A cell's height depends on its width, which
/// the grid states outright; a row's height is its tallest cell's; and a cell's <em>rectangle</em> depends
/// on the row heights. So the first pass lays every cell's text out at its own width and keeps the result,
/// and the second turns the heights into rectangles. Nothing is measured twice.
/// </para>
/// <para>
/// A cell spanning rows contributes to the last row it covers rather than to the first. That is what keeps
/// a tall merged cell from making its <em>first</em> row tall — the two-row merge in a real table is
/// usually beside two short ordinary cells, and charging its height to row one would leave row two empty
/// and the table the wrong shape. LibreOffice reaches the same answer by growing the merge's rows to fit;
/// charging the last row is the same result for the common case and cheaper than an iteration.
/// </para>
/// </remarks>
public static class TableLayouter
{
    /// <summary>
    /// Lays a table out at a stated origin, reporting the cells and the row heights.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="origin">
    /// Where the table's top-left corner goes, in page coordinates. Its left indent is applied here rather
    /// than by the caller, so a caller only has to know where the body area starts.
    /// </param>
    /// <param name="nesting">
    /// How many tables enclose this one, so that a file claiming absurd nesting stops rather than recursing.
    /// </param>
    /// <param name="available">
    /// How wide the area holding the table is — the body's text width, or the enclosing cell's inner width
    /// for a nested one. It changes nothing for a table that declares its grid, which is every table this
    /// engine has ever laid out; it is read only by one that left a column without a width and so has to be
    /// fitted to what it sits in. See <see cref="PageTable.ColumnFit"/>.
    /// </param>
    /// <param name="collapsesSpacing">
    /// Whether the paragraphs inside a cell collapse their spacing against one another rather than adding
    /// it — see <see cref="FlowLayouter.LayOut"/>. It decides a cell's height, and so a row's, so passing
    /// the document's answer matters as much here as it does in the body.
    /// </param>
    /// <param name="addsCellLineSpacing">
    /// Whether a cell grows by its last paragraph's proportional line spacing — Writer's
    /// <c>AddParaLineSpacingToTableCells</c>. See <see cref="CellLineSpacing"/>.
    /// </param>
    /// <returns>
    /// The cells with page-coordinate rectangles, and each row's height in order — the caller needs the
    /// heights to decide where the table ends and which rows fit on the page.
    /// </returns>
    public static (List<PlacedTableCell> Cells, List<Length> RowHeights) LayOut(
        PageTable table,
        DocPoint origin,
        int nesting = 0,
        Length? available = null,
        bool collapsesSpacing = false,
        bool addsCellLineSpacing = false)
    {
        ArgumentNullException.ThrowIfNull(table);

        List<Length> lefts = ColumnLefts(table.WidthsWithin(available ?? table.Width));
        int rows = Math.Min(table.Rows.Count, PageTable.MaxRows);

        // Pass one: every cell's text, laid out at its own width, with the row it charges its height to.
        List<Measured> measured = [];
        List<Length> heights = [.. Enumerable.Repeat(Length.Zero, rows)];

        for (int row = 0; row < rows; row++)
        {
            foreach (PageTableCell cell in table.Rows[row].Cells)
            {
                Length width = WidthOf(cell, lefts);
                if (width <= Length.Zero) continue;

                int last = Math.Min(row + Math.Max(1, cell.RowSpan), rows) - 1;

                // A turned cell breaks its text at the cell's *height*, which is exactly what this pass is
                // trying to work out — so it is not measured here at all. It does not have to be: it
                // contributes nothing to the row's height, so the circle never closes. See `Turned`.
                if (cell.IsTurned)
                {
                    measured.Add(new Measured(row, last, cell, width, null, Length.Zero));
                    continue;
                }

                Length inner = width - cell.Padding.Horizontal;
                PlacedFlow? content = inner > Length.Zero
                    ? FlowLayouter.LayOut(
                        cell.Blocks,
                        new DocRect(Length.Zero, Length.Zero, inner, Length.Zero),
                        Length.Zero,
                        nesting,
                        collapsesSpacing,
                        addsCellLineSpacing)
                    : null;

                // The advance rather than the ink: a cell is as tall as its content plus the space after
                // its last paragraph, which is what `AddParaSpacingToTableCells` — on for every Word
                // document — makes Writer do. See `PlacedFlow.Advance`.
                Length text = content is null ? Length.Zero : content.Advance;

                // And its last paragraph's proportional line spacing on top of that, which is the second
                // half of the same compatibility rule. See `CellLineSpacing`.
                if (addsCellLineSpacing) text += CellLineSpacing(cell.Blocks);

                measured.Add(new Measured(row, last, cell, width, content, text));

                // A merged cell charges only its last row, and only for what one row's worth of it needs.
                // Charging the whole height there would make that row as tall as the merge.
                if (last == row)
                {
                    heights[row] = Length.Max(heights[row], text + cell.Padding.Vertical);
                }
            }

            // The declared height, which is a floor unless the row says it is exact — in which case it is the
            // height, and content taller than it is clipped rather than growing the row. Applied per row
            // before the merge shortfall below, so that a merge spanning an exact row cannot stretch it.
            //
            // A border takes space, and a row owns *half* of each of the two grid lines it sits between — the
            // line runs through the border's centre, so the other half belongs to the neighbour. The two
            // outermost halves, which have no neighbour, are added to the last row once the rectangles are
            // built; see there.
            //
            // The `atLeast` floor sits *under* those borders rather than over them: it raises the content and
            // the borders are then added on top, so a row resting on its floor is one border taller than the
            // floor. Measured by sweeping the border width against a fixed w:trHeight — see
            // `dotnet/probes/words-pagination-01/row-min-height-border.py`, which reads 24.00 / 24.50 / 25.00 /
            // 26.00 / 27.00 pt out of the reference for w:sz 0 / 4 / 8 / 16 / 24 against a 24 pt floor, while
            // we read 24.00 throughout. That the gap tracks the border exactly is what rules out the other
            // reading of the same two corpus observations — both `ESPN-R - MCF - Manual` and the FAA Holdover
            // Tables draw a w:sz="4" grid, so a flat half point fits them just as well and is refuted here.
            //
            // `exact` is the other branch and measured the other way: at w:sz="16" both sides read 24.00, so a
            // clipped row's height really is the whole of it, borders included. Applying the border there too
            // would be the obvious symmetry and is wrong.
            heights[row] = table.Rows[row].HasExactHeight
                ? Length.Max(Length.Zero, table.Rows[row].MinHeight)
                : Length.Max(heights[row], table.Rows[row].MinHeight)
                  + BorderHeight(table.Rows[row]);
        }

        // A merged cell may still need more room than the rows it covers add up to, so the last row it
        // covers takes the difference. Done after every row has its own floor, since the sum is what
        // decides whether there is a shortfall at all.
        foreach (Measured cell in measured)
        {
            if (cell.LastRow == cell.Row) continue;

            Length covered = Length.Zero;
            for (int row = cell.Row; row <= cell.LastRow; row++) covered += heights[row];

            // An exact row does not grow, so a merge ending in one has nowhere to put its shortfall and its
            // content overflows. Skipping the row rather than growing it is the whole point of "exact".
            if (table.Rows[cell.LastRow].HasExactHeight) continue;

            Length needed = cell.TextHeight + cell.Cell.Padding.Vertical;
            if (needed > covered) heights[cell.LastRow] += needed - covered;
        }

        // Pass two: the heights are settled, so every cell has a rectangle.
        List<Length> tops = [];

        // The first grid line sits half a border *below* the table's top edge, because a grid line runs through
        // the centre of its border and the row heights already include half of it at each end. Measured: a
        // table whose top edge is at 70.2 pt draws its first border at 70.45 with a 0.5 pt border.
        Length top = rows > 0 ? BorderHeight(table.Rows[0]) / 2 : Length.Zero;

        for (int row = 0; row < rows; row++)
        {
            tops.Add(top);
            top += heights[row];
        }

        List<PlacedTableCell> placed = new(measured.Count);
        foreach (Measured cell in measured)
        {
            Length height = tops[cell.LastRow] + heights[cell.LastRow] - tops[cell.Row];

            DocRect area = new(
                origin.X + table.LeftWithin(available ?? table.Width) + lefts[cell.Cell.Column],
                origin.Y + tops[cell.Row],
                cell.Width,
                height);

            Length bandAbove = BorderHeight(table.Rows[cell.Row]) / 2;
            Length bandBelow = BorderHeight(table.Rows[cell.LastRow]) / 2;

            if (cell.Cell.IsTurned)
            {
                (PlacedFlow? turned, AffineTransform? onto) = Turned(
                    cell.Cell, area, bandAbove, bandBelow, nesting, collapsesSpacing, addsCellLineSpacing);

                placed.Add(new PlacedTableCell
                {
                    Cell = cell.Cell,
                    Area = area,
                    Content = turned,
                    ContentTransform = onto,
                    Row = cell.Row,
                });

                continue;
            }

            placed.Add(new PlacedTableCell
            {
                Cell = cell.Cell,
                Area = area,
                Content = Positioned(cell, area, bandAbove, bandBelow),
                Row = cell.Row,
            });
        }

        // The two half grid lines the rectangles do not cover: the one above the first row, which is why
        // `tops` starts half a border down, and the one below the last. Writer charges a cell for its
        // whole border and neighbouring rows share the line between them, so a table of n rows is n+1
        // borders tall rather than n — measured on a three-row fixture at 0, 1 and 2 pt, where each of the
        // three cases came out exactly one border taller than this engine made it. Charged to the last row
        // rather than split between the first and the last, because the paginator reconstructs a
        // continuation page's offset by adding up the heights it has already placed, and an allowance in
        // `heights[0]` that no rectangle carries would move every later row up by half a border.
        if (rows > 0)
        {
            heights[rows - 1] +=
                BorderHeight(table.Rows[0]) / 2 + BorderHeight(table.Rows[rows - 1]) / 2;
        }

        return (placed, heights);
    }

    /// <summary>
    /// One row's cells restricted to the part of the row that goes on a single page.
    /// </summary>
    /// <remarks>
    /// Positioned with the part's own top at nought and the table's left edge at nought, exactly as
    /// <see cref="LayOut"/> leaves a whole table, so the caller moves it onto the page with
    /// <see cref="Offset"/> and nothing here needs to know where the page is.
    /// </remarks>
    /// <param name="Cells">The cells, each holding only the lines that belong to this part.</param>
    /// <param name="Height">How tall the part is, borders included.</param>
    /// <param name="Cut">
    /// The row-relative depth everything above which is now drawn. Handed back to
    /// <see cref="SliceRow"/> for the next page's part, and equal to <see cref="Height"/> only by
    /// coincidence — the part is as tall as its tallest cell needs, and the cut is where the text stopped.
    /// </param>
    /// <param name="IsComplete">True when nothing of the row is left over.</param>
    public readonly record struct RowSlice(
        List<PlacedTableCell> Cells, Length Height, Length Cut, bool IsComplete);

    /// <summary>
    /// Takes the part of a row that fits in a given height, starting below what is already drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What lets a table row cross a page break, which Writer does through a <em>follow flow line</em>:
    /// <c>SwTabFrame::Split</c> keeps the rows that fit in the master table and hands the first one that
    /// does not to <c>lcl_InsertNewFollowFlowLine</c>, which builds a second frame for the same row on the
    /// next page and moves whatever text did not fit into it
    /// (<c>sw/source/core/layout/tabfrm.cxx</c>). This is that in one function: the lines above the cut
    /// stay, the rest are the next page's problem.
    /// </para>
    /// <para>
    /// The cut is one depth for the whole row rather than a per-cell allowance, which is the point rather
    /// than a simplification — a row has one bottom edge, and choosing per cell would let one cell's text
    /// run past the edge another cell's stopped at. So the candidate cuts are the line bottoms of every
    /// cell together, and the deepest one whose part still fits is taken.
    /// </para>
    /// <para>
    /// Returns null when the row must move whole: a cell holding a nested table (Writer's
    /// <c>bTableLayoutTooComplex</c>), a cell merged across this row, a row of a stated exact height, or a
    /// cut that would leave nothing on either side of it. The last is what stops a split that gains
    /// nothing — a page ending in an empty row followed by the same row again does not terminate.
    /// </para>
    /// </remarks>
    /// <param name="row">The row, for its borders and its height rule.</param>
    /// <param name="cells">Its cells as <see cref="LayOut"/> placed them.</param>
    /// <param name="drawn">How far into the row an earlier page already reached; nought at its first part.</param>
    /// <param name="room">How much height is left on this page.</param>
    public static RowSlice? SliceRow(
        PageTableRow row, IReadOnlyList<PlacedTableCell> cells, Length drawn, Length room)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(cells);

        if (cells.Count == 0 || row.HasExactHeight) return null;

        Length rowTop = cells[0].Area.Y;
        foreach (PlacedTableCell cell in cells)
        {
            // A cell covering more than this row cannot be cut here: its text belongs to a row further
            // down, and half of its rectangle would be drawn on each of two pages.
            if (Math.Max(1, cell.Cell.RowSpan) > 1) return null;
            if (cell.Content is { Tables.Count: > 0 }) return null;
            rowTop = Length.Min(rowTop, cell.Area.Y);
        }

        Length border = BorderHeight(row);
        Length above = rowTop + drawn;

        // Every line that is not yet drawn, as the depth its bottom sits at. These are the only places the
        // row may be cut, since a cut between two of them would draw half a line.
        List<Length> candidates = [];
        foreach (PlacedTableCell cell in cells)
        {
            // A turned cell offers no cuts: its lines run across the row rather than down it, so there is
            // no depth at which cutting one would divide its text. It rides on the row's first part.
            if (cell.ContentTransform is not null) continue;
            if (cell.Content is not { } flow) continue;

            foreach (PlacedLine line in flow.Lines)
            {
                Length bottom = flow.Area.Y + line.Top + line.Box.Height;
                if (bottom > above) candidates.Add(bottom);
            }
        }

        if (candidates.Count == 0) return null;

        candidates.Sort();

        // The deepest cut whose part still fits. The height is not decreasing in the cut, so the first
        // candidate that does not fit ends the search.
        Length? chosen = null;
        Length height = Length.Zero;

        foreach (Length candidate in candidates)
        {
            if (chosen is { } already && already == candidate) continue;

            Length needed = HeightAt(cells, rowTop, above, candidate, border);
            if (needed > room) break;

            chosen = candidate;
            height = needed;
        }

        if (chosen is not { } cut) return null;

        bool complete = candidates[^1] <= cut;

        // A part holding every remaining line is not a split at all; the caller places the whole row.
        if (complete && drawn <= Length.Zero) return null;

        return new RowSlice(Sliced(cells, rowTop, above, cut, height), height, cut - rowTop, complete);
    }

    /// <summary>
    /// How tall the row's part is when it is cut at a stated depth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same sum <see cref="LayOut"/> makes a whole row from — the tallest cell's text plus its
    /// padding, plus the row's borders — over the lines this part holds. The row's declared floor is
    /// deliberately <em>not</em> applied: it is a floor on the row and the parts add up to the row, so
    /// imposing it on each would make a split row twice as tall as an unsplit one. A row whose floor
    /// exceeds its text never reaches here anyway, since every line then fits in one part and
    /// <see cref="SliceRow"/> declines a split that leaves nothing over.
    /// </para>
    /// <para>
    /// The two ends of the run are measured as <see cref="LayOut"/> measures them rather than as ink, and
    /// that is what makes the two agree. A cell's flow does not begin at its first line — a first
    /// paragraph's space-before sits above it — and it does not end at its last, since
    /// <see cref="PlacedFlow.Advance"/> carries the last paragraph's space-after, which is exactly what
    /// a row's height is built from. So the row's own first part starts at the flow's top, and a part
    /// holding a cell's last line ends at its advance. Measuring both from the ink instead makes the sum
    /// of a row's parts shorter than the row, and then a row whose text fits the page but whose *height*
    /// does not is judged unbreakable by one measure and too tall by the other: it moves whole and leaves
    /// the difference blank. Measured on
    /// <c>f445896eb008d14c1746fc37d412dc22.docx</c>, where 205.8 pt of a page went empty because the row
    /// was 211.8 pt tall and its lines measured 202.5.
    /// </para>
    /// <para>
    /// A <em>follow</em> part starts at the top of the block its first line belongs to and not at the top
    /// of that line — see <see cref="UpperSpaceAbove"/>. That is one line of arithmetic and it is the
    /// difference between a split row's parts adding up to the row and adding up to a point or two less.
    /// </para>
    /// </remarks>
    private static Length HeightAt(
        IReadOnlyList<PlacedTableCell> cells, Length rowTop, Length above, Length cut, Length border)
    {
        // The row's own first part keeps the offset it was laid out with — see `Sliced` — so its cells
        // begin at the top of their flow and not at their first line.
        bool isFirst = above <= rowTop;
        Length text = Length.Zero;

        foreach (PlacedTableCell cell in cells)
        {
            // Charges the part nothing, exactly as it charged the row nothing. See `Turned`.
            if (cell.ContentTransform is not null) continue;
            if (cell.Content is not { } flow) continue;

            Length? top = null;
            Length bottom = Length.Zero;
            PlacedLine? following = null;

            foreach (PlacedLine line in flow.Lines)
            {
                Length end = flow.Area.Y + line.Top + line.Box.Height;
                if (end <= above) continue;

                if (end > cut)
                {
                    following ??= line;
                    continue;
                }

                top ??= isFirst
                    ? flow.Area.Y
                    : flow.Area.Y + line.Top - UpperSpaceAbove(flow, line);
                bottom = end;
            }

            if (top is not { } start) continue;

            if (following is { } next)
            {
                // A part ends where the next block begins, not where its own last line does — so a part
                // whose last line completes a paragraph is charged that paragraph's space-after, the same
                // way `Advance` charges the cell for its final one. Within a paragraph the next line's
                // block top *is* this line's bottom and this changes nothing.
                bottom = Length.Max(bottom, flow.Area.Y + next.Top - next.UpperSpace);
            }
            else if (flow.Lines.Count > 0
                     && flow.Area.Y + flow.Lines[^1].Top + flow.Lines[^1].Box.Height <= cut)
            {
                // Nothing of this cell is left over, so its part is as tall as the cell — the trailing
                // spacing included, which is what `LayOut` charged the row for.
                bottom = Length.Max(bottom, flow.Area.Y + flow.Advance);
            }

            text = Length.Max(text, bottom - start + cell.Cell.Padding.Vertical);
        }

        return text + border;
    }

    /// <summary>
    /// The upper spacing a follow part re-applies above its first line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A part of a split row that is not the row's first begins at the top of the <em>block</em> its first
    /// line belongs to, which is one paragraph's space-before above the line. So a paragraph cut in half
    /// by a page break pays its space-before twice — once on each part — and a paragraph that merely
    /// starts a follow part pays it there rather than losing it.
    /// </para>
    /// <para>
    /// Both halves of that are measured rather than reasoned, on 12 authored probes rendered through
    /// LibreOffice 26.2.4.2 (<c>dotnet/probes/words-regress-01/probe-rowsplit-spacing.py</c> and
    /// <c>probe-rowsplit-paras.py</c>). Sweeping <c>w:spacing w:before</c> and <c>w:after</c>
    /// independently over a row cut across a page, the reference's follow part is
    /// <c>before + remaining lines + after + border</c> in every combination, and the sum of a split
    /// row's two parts therefore exceeds the unsplit row by exactly <c>before</c>. This is Writer's
    /// <c>AddParaSpacingToTableCells</c> compatibility setting seen from the other end:
    /// <see cref="PlacedFlow.Advance"/> already charges a cell for its last paragraph's space-after
    /// because of it, and <c>SwFlowFrame::CalcUpperSpace</c> is why a follow keeps its upper space in a
    /// table cell where an ordinary body paragraph would drop it.
    /// </para>
    /// <para>
    /// Only a follow part is affected: the row's own first part starts at the flow's top, which already
    /// holds the first paragraph's space-before.
    /// </para>
    /// </remarks>
    /// <param name="flow">The cell's flow, for the paragraph the line belongs to.</param>
    /// <param name="line">The first line the part holds.</param>
    private static Length UpperSpaceAbove(PlacedFlow flow, PlacedLine line)
    {
        // Only a paragraph's first line carries it, so a line cut out of the middle of one has to be
        // traced back to the line that does.
        if (line.StartsParagraph) return line.UpperSpace;

        foreach (PlacedLine other in flow.Lines)
        {
            if (other.ParagraphIndex != line.ParagraphIndex || !other.StartsParagraph) continue;
            return other.UpperSpace;
        }

        return Length.Zero;
    }

    /// <summary>The cells of one part, holding its lines and positioned from the part's own top.</summary>
    private static List<PlacedTableCell> Sliced(
        IReadOnlyList<PlacedTableCell> cells, Length rowTop, Length above, Length cut, Length height)
    {
        List<PlacedTableCell> sliced = new(cells.Count);

        foreach (PlacedTableCell cell in cells)
        {
            // A turned cell goes whole onto the row's first part and is absent from every later one. It
            // cannot be divided — see `SliceRow` — and drawing it twice would print the label on both
            // pages. Its own rectangle still becomes the part's, so the borders round it are right.
            if (cell.ContentTransform is { } onto)
            {
                bool isFirstPart = above <= rowTop;

                sliced.Add(cell with
                {
                    Area = new DocRect(cell.Area.X, Length.Zero, cell.Area.Width, height),
                    Content = isFirstPart ? cell.Content : null,
                    ContentTransform = isFirstPart
                        ? onto with { F = onto.F - rowTop.Emu }
                        : null,
                });

                continue;
            }

            List<PlacedLine> kept = [];
            Length? first = null;

            if (cell.Content is { } flow)
            {
                foreach (PlacedLine line in flow.Lines)
                {
                    Length end = flow.Area.Y + line.Top + line.Box.Height;
                    if (end <= above || end > cut) continue;

                    // Less the space the part is charged for above it, so the text lands where the height
                    // `HeightAt` reported puts it. See `UpperSpaceAbove`.
                    first ??= line.Top - UpperSpaceAbove(flow, line);
                    kept.Add(line);
                }
            }

            DocRect area = new(cell.Area.X, Length.Zero, cell.Area.Width, height);
            PlacedFlow? content = null;

            if (cell.Content is { } text && kept.Count > 0)
            {
                // The remaining text starts at the top of the part rather than where it was measured, which
                // is what a follow flow line is: the cell begins again on the next page. The first part
                // keeps the offset it was laid out with, so a short cell beside a long one stays where its
                // vertical alignment put it.
                Length top = above <= rowTop
                    ? text.Area.Y - rowTop
                    : cell.Cell.Padding.Top - first!.Value;

                content = text with
                {
                    Area = new DocRect(text.Area.X, top, text.Area.Width, height),
                    Lines = kept,
                };
            }

            sliced.Add(cell with { Area = area, Content = content });
        }

        return sliced;
    }

    /// <summary>
    /// The same cells, shifted.
    /// </summary>
    /// <remarks>
    /// What lets a table be laid out once and placed many times: a cell's lines are positioned relative to
    /// its content rectangle, so moving the rectangle takes the text with it and nothing needs measuring
    /// again. Used by the paginator both to move a table onto a page and to draw a repeated heading row
    /// part way down one.
    /// </remarks>
    /// <param name="cells">The cells to shift.</param>
    /// <param name="dx">How far right.</param>
    /// <param name="dy">How far down.</param>
    public static List<PlacedTableCell> Offset(
        IEnumerable<PlacedTableCell> cells, Length dx, Length dy)
    {
        ArgumentNullException.ThrowIfNull(cells);

        List<PlacedTableCell> moved = [];
        foreach (PlacedTableCell cell in cells)
        {
            // A turned cell's content is in the cell's own coordinates, so moving the cell moves its
            // *transform* and must not touch the flow: shifting both would move the text twice.
            if (cell.ContentTransform is { } onto)
            {
                moved.Add(cell with
                {
                    Area = Shift(cell.Area, dx, dy),
                    ContentTransform = onto with { E = onto.E + dx.Emu, F = onto.F + dy.Emu },
                });

                continue;
            }

            moved.Add(cell with
            {
                Area = Shift(cell.Area, dx, dy),
                Content = ShiftFlow(cell.Content, dx, dy),
            });
        }

        return moved;
    }

    /// <summary>
    /// Moves a cell's whole content: its rectangle, and any table nested inside it.
    /// </summary>
    /// <remarks>
    /// The lines need no attention — they are positioned relative to the flow's rectangle, so moving the
    /// rectangle takes them along. A nested table is the exception and the reason this exists: its cells
    /// carry page coordinates rather than flow-relative ones, so they have to be moved by the same amount
    /// explicitly. Missing that leaves a nested table wherever the pre-layout pass put it — which is near
    /// the page's top-left corner, since a table is laid out once at the origin and placed later.
    /// </remarks>
    private static PlacedFlow? ShiftFlow(PlacedFlow? flow, Length dx, Length dy)
    {
        if (flow is null) return null;
        if (dx == Length.Zero && dy == Length.Zero) return flow;

        return flow with
        {
            Area = Shift(flow.Area, dx, dy),
            Tables = [.. flow.Tables.Select(table => table with
            {
                Area = Shift(table.Area, dx, dy),
                Cells = Offset(table.Cells, dx, dy),
            })],
        };
    }

    /// <summary>
    /// How much of a row's height its borders take: half of its thickest top and half of its thickest bottom.
    /// </summary>
    /// <remarks>
    /// The thickest rather than each cell's own, because the row has one height and one grid line above it: two
    /// cells disagreeing about their top border share the thicker one's line, which is what the drawing does too
    /// when it consolidates. Measured: a row 18.95 pt tall without borders is 19.4 pt with 0.5 pt ones, and half
    /// of each of two borders is 0.5 pt — right to within a twip of rounding.
    /// </remarks>
    private static Length BorderHeight(PageTableRow row)
    {
        Length top = Length.Zero;
        Length bottom = Length.Zero;

        foreach (PageTableCell cell in row.Cells)
        {
            top = Length.Max(top, cell.Borders.Top.Width);
            bottom = Length.Max(bottom, cell.Borders.Bottom.Width);
        }

        return (top + bottom) / 2;
    }

    /// <summary>
    /// How much a cell grows for the proportional line spacing on its last paragraph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Writer's <c>AddParaLineSpacingToTableCells</c>, which <c>WriterFilter.cxx</c>:314 switches on for
    /// every document the OOXML, DOC and RTF importers read and which is off for a native ODF one. It is
    /// the companion of <c>AddParaSpacingToTableCells</c> — that one charges the cell for its last
    /// paragraph's space-after, this one for the space its line spacing would have handed to a paragraph
    /// below it. <c>SwFlowFrame::CalcAddLowerSpaceAsLastInTableCell</c>
    /// (<c>sw/source/core/layout/flowfrm.cxx</c>:1946) adds both, and only for the last flow frame in the
    /// cell, which is why this is measured from the last block rather than from every paragraph.
    /// </para>
    /// <para>
    /// The amount is not the leading the layout engine computes. <c>SwBorderAttrs::CalcLineSpacing_</c>
    /// (<c>sw/source/core/layout/frmtool.cxx</c>:2681) is
    /// <c>nFontSize × (prop − 100) × 1.15 / 100</c> in twips — the paragraph's <em>font size</em> rather
    /// than its measured line height, with a 1.15 fudge factor added for tdf#125300 that stands in for the
    /// ratio between the two. Reproduced here to the digit, including the truncation and the order of
    /// operations: the integer product is formed first and only then multiplied by a binary <c>1.15</c>
    /// slightly below the decimal one, which is what makes 125% come out at 68 twips rather than 69.
    /// Measured against LibreOffice on a one-cell fixture at 110, 115, 125, 150, 200 and 250 per cent,
    /// where every one of the six matched exactly.
    /// </para>
    /// <para>
    /// A cell ending in a nested table gets nothing: the attribute set consulted there is the table's,
    /// which carries no line spacing item at all.
    /// </para>
    /// </remarks>
    /// <param name="blocks">The cell's blocks.</param>
    private static Length CellLineSpacing(IReadOnlyList<PageBlock> blocks)
    {
        if (blocks.Count == 0 || blocks[^1] is not PageParagraph last) return Length.Zero;

        LineSpacingRule spacing = last.Format.LineSpacing;
        if (spacing.Mode != LineSpacingMode.Proportional) return Length.Zero;

        long percent = (long)Math.Round(spacing.Proportion * 100.0, MidpointRounding.AwayFromZero);
        if (percent <= 100) return Length.Zero;

        return Length.FromTwips((long)(last.EmSize.Twips * (percent - 100) * 1.15 / 100.0));
    }

    private static DocRect Shift(DocRect area, Length dx, Length dy)
        => new(area.X + dx, area.Y + dy, area.Width, area.Height);

    /// <summary>
    /// A cell's text moved from the origin into its own rectangle, and aligned inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lines themselves do not move: they are positioned relative to the flow's area, so shifting the
    /// area takes them with it. What does change is the vertical alignment, which is only decidable now —
    /// it depends on the row's final height, and the row's height depended on this cell.
    /// </para>
    /// <para>
    /// A border is not only a line, it is a band the text may not enter: Writer insets a cell's content by
    /// the whole border width on top of the padding, so a 1 pt border moves the first line down 1 pt and
    /// everything after the table down 2. The rectangle here starts half a grid line above the content —
    /// the line is drawn through its middle — so what the content owes is the other half at each end.
    /// Measured against LibreOffice on a one-column fixture at 0, 1 and 2 pt borders, where the step was
    /// exactly half the border and did not depend on the number of rows.
    /// </para>
    /// </remarks>
    /// <param name="cell">The measured cell.</param>
    /// <param name="area">Its rectangle.</param>
    /// <param name="bandAbove">Half the grid line above the cell's first row.</param>
    /// <param name="bandBelow">Half the grid line below the cell's last row.</param>
    private static PlacedFlow? Positioned(
        Measured cell, DocRect area, Length bandAbove, Length bandBelow)
    {
        if (cell.Content is null) return null;

        CellPadding padding = cell.Cell.Padding;
        Length height = area.Height - padding.Vertical - bandAbove - bandBelow;
        Length spare = height - cell.TextHeight;

        Length offset = spare <= Length.Zero
            ? Length.Zero
            : cell.Cell.VerticalAlignment switch
            {
                CellVerticalAlignment.Middle => spare / 2,
                CellVerticalAlignment.Bottom => spare,
                _ => Length.Zero,
            };

        DocRect placed = new(
            area.X + padding.Left,
            area.Y + bandAbove + padding.Top + offset,
            area.Width - padding.Horizontal,
            height > Length.Zero ? height : Length.Zero);

        // From the origin the flow was laid out at to where the cell actually is.
        PlacedFlow moved = ShiftFlow(
            cell.Content,
            placed.X - cell.Content.Area.X,
            placed.Y - cell.Content.Area.Y)!;

        // The height comes from the cell rather than from the content, since the row may be taller.
        return moved with { Area = placed };
    }

    /// <summary>
    /// Lays out a turned cell's text and works out the quarter turn that puts it on the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything here was measured against the installed LibreOffice 26.2.4.2 on generated probes, read
    /// out of the PDF's own operators. Four facts, each of which the obvious implementation gets wrong:
    /// </para>
    /// <para>
    /// <strong>The line breaks at the cell's inner height, and it is the same inner box an upright cell
    /// uses</strong> — the height less the two half grid lines and less the <em>vertical</em> padding.
    /// Horizontal padding does not shorten the line. Pinned by a five-twip sweep of <c>w:trHeight</c>: the
    /// four-glyph to five-glyph boundary sits at exactly 500 twips in all three of {0.5 pt borders, no
    /// borders, 10 pt top and bottom cell margin}, whose frames are 25.5, 25.0 and 45.5 pt tall. Reading
    /// the turn as "swap the padding too" would have moved that boundary in two of the three.
    /// </para>
    /// <para>
    /// <strong>A turned cell contributes nothing to its row's height.</strong> Not one line's worth,
    /// nothing: a row holding only turned cells collapses to zero and LibreOffice draws neither its text
    /// nor its borders. That is what makes the apparent circularity — the line length is the cell height,
    /// the cell height is the tallest cell's line count — not a circle at all, and it is why this runs in
    /// the second pass with the row heights already settled.
    /// </para>
    /// <para>
    /// <strong>A line that starts outside the cell is not drawn at all.</strong> Dropped rather than
    /// clipped: there is no text-showing operator for it in the reference's PDF, so it is absent from the
    /// text layer as well as from the ink. A 50 pt column whose inner width is 38.7 pt draws four lines at
    /// 11.55 pt each — the fourth overhanging — and not the fifth, which would start past the edge.
    /// </para>
    /// <para>
    /// <strong><c>w:vAlign</c> places the line stack across the cell's width.</strong> Top puts the first
    /// line against the left edge, bottom the last against the right, centre in the middle — measured at
    /// 71.20, 148.80 and 110.00 pt on one fixture. It is the same axis in the cell's own frame, which is
    /// why it is still called a vertical alignment.
    /// </para>
    /// <para>
    /// What this deliberately does not do is measure the cell's <em>width</em> from the text. A turned
    /// label wants the column to be one line thick and the document says how thick; nothing in Writer
    /// widens a column to fit turned text either.
    /// </para>
    /// </remarks>
    /// <param name="cell">The cell.</param>
    /// <param name="area">Its rectangle on the page.</param>
    /// <param name="bandAbove">Half the grid line above it.</param>
    /// <param name="bandBelow">Half the grid line below it.</param>
    /// <param name="nesting">How many tables enclose the one this cell is in.</param>
    /// <param name="collapsesSpacing">Whether paragraphs collapse their spacing — see <see cref="LayOut"/>.</param>
    /// <param name="addsCellLineSpacing">Whether a cell grows by its last line spacing — see <see cref="LayOut"/>.</param>
    private static (PlacedFlow? Content, AffineTransform? Onto) Turned(
        PageTableCell cell,
        DocRect area,
        Length bandAbove,
        Length bandBelow,
        int nesting,
        bool collapsesSpacing,
        bool addsCellLineSpacing)
    {
        CellPadding padding = cell.Padding;

        // Along the text: the cell's inner height, which is what the lines break at.
        Length along = area.Height - padding.Vertical - bandAbove - bandBelow;

        // Across the stack: the cell's inner width, which is how many lines can be drawn.
        Length across = area.Width - padding.Horizontal;

        if (along <= Length.Zero || across <= Length.Zero) return (null, null);

        PlacedFlow? flow = FlowLayouter.LayOut(
            cell.Blocks,
            new DocRect(Length.Zero, Length.Zero, along, Length.Zero),
            Length.Zero,
            nesting,
            collapsesSpacing,
            addsCellLineSpacing);

        if (flow is null) return (null, null);

        // The lines that begin inside the cell. A line starting past the edge is not drawn — see above —
        // and the ones before it are kept whole even where they overhang, which is what the reference does.
        List<PlacedLine> kept = [];
        foreach (PlacedLine line in flow.Lines)
        {
            if (line.Top < across) kept.Add(line);
        }

        if (kept.Count == 0) return (null, null);

        // How much of the width the stack actually uses, for the alignment below. The last line's box
        // rather than the flow's advance: a turned cell has no space-after to charge across its width.
        Length used = kept[^1].Top + kept[^1].Box.Height;
        Length spare = across - used;

        Length offset = spare <= Length.Zero
            ? Length.Zero
            : cell.VerticalAlignment switch
            {
                CellVerticalAlignment.Middle => spare / 2,
                CellVerticalAlignment.Bottom => spare,
                _ => Length.Zero,
            };

        // Where the flow's own origin lands. The text starts at whichever end of the cell it runs *from*:
        // the bottom for a cell turned anticlockwise, the top for one turned clockwise.
        Length left = area.X + padding.Left;
        Length right = area.Right - padding.Right;
        Length top = area.Y + bandAbove + padding.Top;
        Length bottom = area.Bottom - bandBelow - padding.Bottom;

        (double radians, Length x, Length y) = cell.TextDirection switch
        {
            CellTextDirection.TopToBottomRightToLeft => (Math.PI / 2, right - offset, top),
            _ => (-Math.PI / 2, left + offset, bottom),
        };

        AffineTransform onto = AffineTransform.Concat(
            AffineTransform.Rotation(radians),
            AffineTransform.Translation(x.Emu, y.Emu));

        // The flow keeps its own coordinates — the transform is what moves it — but it is trimmed to the
        // lines that are drawn, and its rectangle is the whole inner box so that anything measuring the
        // flow sees the room it had rather than the room it used.
        return (
            flow with
            {
                Lines = kept,
                Area = new DocRect(Length.Zero, Length.Zero, along, across),
            },
            onto);
    }

    /// <summary>Where each grid column starts, measured from the table's left edge.</summary>
    /// <remarks>
    /// One entry per column plus a final one past the last, so a cell's width is the difference between
    /// two lookups whatever it spans — including a cell whose span runs off the end of the declared grid,
    /// which real documents contain.
    /// </remarks>
    private static List<Length> ColumnLefts(IReadOnlyList<Length> widths)
    {
        int columns = Math.Min(widths.Count, PageTable.MaxColumns);
        List<Length> lefts = new(columns + 1);

        Length at = Length.Zero;
        for (int column = 0; column < columns; column++)
        {
            lefts.Add(at);
            at += widths[column];
        }

        lefts.Add(at);
        return lefts;
    }

    /// <summary>How wide a cell is: the columns it spans, added up.</summary>
    /// <remarks>
    /// Clamped to the declared grid at both ends. A cell starting past the last column has no width and is
    /// dropped; one spanning past it stops at the edge, which is what LibreOffice's own importers do with a
    /// row whose cells overrun the grid rather than widening the table.
    /// </remarks>
    private static Length WidthOf(PageTableCell cell, List<Length> lefts)
    {
        int columns = lefts.Count - 1;
        if (cell.Column < 0 || cell.Column >= columns) return Length.Zero;

        int end = Math.Clamp(cell.ColumnEnd, cell.Column + 1, columns);
        return lefts[end] - lefts[cell.Column];
    }

    /// <summary>
    /// A cell after pass one: its text laid out, and which rows it charges.
    /// </summary>
    /// <param name="Row">The row it starts in.</param>
    /// <param name="LastRow">The last row it covers, which is where its height is charged.</param>
    /// <param name="Cell">The cell itself.</param>
    /// <param name="Width">Its outer width, padding included.</param>
    /// <param name="Content">Its text at the origin, to be moved once the rectangle is known.</param>
    /// <param name="TextHeight">How tall that text is.</param>
    private readonly record struct Measured(
        int Row,
        int LastRow,
        PageTableCell Cell,
        Length Width,
        PlacedFlow? Content,
        Length TextHeight);
}
