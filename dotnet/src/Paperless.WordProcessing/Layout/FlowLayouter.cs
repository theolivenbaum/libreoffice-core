using Paperless.Core.Geometry;
using Paperless.Core.Units;
using Paperless.Text.Layout;

namespace Paperless.WordProcessing.Layout;

/// <summary>
/// Lays a run of blocks out into one rectangle, as a header, a footer or a table cell.
/// </summary>
/// <remarks>
/// <para>
/// The operation all three share, and the reason <see cref="PlacedFlow"/> is one type: stack the blocks at
/// the rectangle's width, with their spacing between them, and report where each line and each table
/// landed. What none of the three does is <em>flow</em> — nothing here splits across a page, because a
/// header is furniture and a cell belongs to its row. The body's own blocks go through
/// <see cref="Paginator"/> instead, which is the same stacking plus everything that makes a page end.
/// </para>
/// <para>
/// Height is discovered rather than given, which is what a table needs: a row is as tall as its tallest
/// cell, so the cells have to be laid out before the row's height is known, and laying one out cannot
/// require it.
/// </para>
/// </remarks>
public static class FlowLayouter
{
    /// <summary>
    /// How deeply tables may nest inside one another before the innermost is dropped.
    /// </summary>
    /// <remarks>
    /// A guard on untrusted input, and one that matters more here than most: a cell holds a flow, a flow
    /// holds a table, and that table's cells hold flows — so a file claiming a hundred levels of nesting
    /// would recurse a hundred deep for every cell of every level. Real documents nest two or three.
    /// </remarks>
    public const int MaxNesting = 16;

    /// <summary>
    /// Lays blocks out into a rectangle, or returns null when there is nothing to place.
    /// </summary>
    /// <param name="blocks">The paragraphs and tables, in order.</param>
    /// <param name="area">The rectangle to fill, whose width decides the line breaks.</param>
    /// <param name="offsetFromTop">
    /// Where the first line goes: zero to grow downwards from the area's top, a value to start that far
    /// below it, and null to <em>bottom-align</em> the whole block so that its last line rests on the
    /// area's bottom. Null is what a Word footer does; see <see cref="Model.PageGeometry.FooterOffset"/>.
    /// </param>
    /// <param name="nesting">How many tables enclose this flow, for the recursion guard.</param>
    /// <remarks>
    /// Nothing is clipped and nothing overflows into a second rectangle: content taller than the area is
    /// placed anyway and runs past its bottom, which is what Writer does with a fixed-height header whose
    /// text does not fit. A stated offset is honoured even then, so an overflowing footer grows downwards
    /// rather than climbing into the body.
    /// </remarks>
    public static PlacedFlow? LayOut(
        IReadOnlyList<PageBlock> blocks, DocRect area, Length? offsetFromTop, int nesting = 0)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        if (blocks.Count == 0 || area.Width <= Length.Zero) return null;

        List<PlacedLine> placed = [];
        List<PlacedTable> tables = [];
        List<PlacedFrame> frames = [];
        Length top = Length.Zero;

        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is PageTable nested)
            {
                if (nesting >= MaxNesting) continue;

                top += nested.SpaceBefore;

                (List<PlacedTableCell> cells, List<Length> rowHeights) = TableLayouter.LayOut(
                    nested,
                    new DocPoint(area.X, area.Y + top),
                    nesting + 1);

                Length height = Length.Zero;
                foreach (Length row in rowHeights) height += row;

                tables.Add(new PlacedTable
                {
                    Table = nested,
                    Area = new DocRect(
                        area.X + nested.LeftIndent, area.Y + top, nested.Width, height),
                    Cells = cells,
                    FirstRow = 0,
                    RowEnd = rowHeights.Count,
                });

                top += height + nested.SpaceAfter;
                continue;
            }

            PageParagraph paragraph = (PageParagraph)blocks[i];
            ParagraphLayouter layouter = new(paragraph.Face);
            ParagraphFormat? previous = i > 0 && blocks[i - 1] is PageParagraph before
                ? before.Format
                : null;

            // Where this paragraph's first line will sit, worked out *before* laying it out, because a frame
            // anchored here is positioned relative to it and the lines' room depends on the answer.
            Length paragraphTop =
                top + ParagraphLayouter.SpaceBetween(previous, paragraph.Format);

            // A frame anchored to this paragraph joins the obstructions before the paragraph is laid out, so
            // it affects its own paragraph's lines as well as everything below. One anchored further down
            // does not reach back up, which is what Writer does for anything but a negative offset.
            foreach (PageFrame frame in paragraph.Frames)
            {
                // The flow's own rectangle stands in for the column and the page alike: a cell, a running head
                // or another frame has neither, and no format can state a page-relative frame inside one.
                FrameSpace space = FrameSpace.In(
                    area,
                    new DocPoint(area.X + paragraph.Format.StartIndent, area.Y + paragraphTop),
                    ParagraphWidth(area, paragraph.Format));

                frames.Add(new PlacedFrame(
                    frame,
                    frame.BoundsIn(space),
                    frame.RegionIn(space),

                    // The frame's own text, at the frame's width — the same flow a table cell's content is,
                    // and nested one deeper so that a frame inside a frame cannot recurse forever.
                    nesting < MaxNesting
                        ? LayOut(frame.Blocks, frame.ContentAreaIn(space), Length.Zero, nesting + 1)
                        : null));
            }

            LineRoom? room = Room(frames, area);

            LaidOutParagraph layout = paragraph.HasRuns
                ? layouter.Layout(
                    MeasuredParagraph.Measure(
                        paragraph.Text,
                        [.. paragraph.Runs.Select(run => run.ToFormattedRun())]),
                    paragraph.Format,
                    area.Width,
                    paragraph.Language,
                    previous,
                    Shifted(room, area.Y + paragraphTop))
                : layouter.Layout(
                    paragraph.Text,
                    paragraph.Format,
                    paragraph.EmSize,
                    area.Width,
                    paragraph.Language,
                    previous,
                    paragraph.Shaping,
                    Shifted(room, area.Y + paragraphTop));

            top += layout.SpaceBefore;
            Length paragraphBase = top;
            Length dropped = Length.Zero;

            for (int line = 0; line < layout.Lines.Count; line++)
            {
                LineBox box = layout.Lines[line];

                // The first line loses the leading above its text, exactly as the first line of a page's
                // body does: the space belongs to the paragraph's upper margin and is dropped at the top
                // of a frame, and each of these three is a frame. The amount comes off every line below it
                // as well, since their own tops were computed with the leading still there.
                if (placed.Count == 0 && tables.Count == 0)
                {
                    dropped = box.SpaceAbove;
                    box = box.WithoutSpaceAbove();
                }

                // From the box's own top rather than by adding up heights, because the two differ once a
                // frame has pushed a line past itself: the gap that push left belongs to the paragraph and
                // summing heights would close it up again.
                Length at = paragraphBase + box.Top - (line == 0 ? Length.Zero : dropped);
                placed.Add(new PlacedLine(i, line, box, at));
                top = at + box.Height;
            }

            top += layout.SpaceAfter;
        }

        if (placed.Count == 0 && tables.Count == 0) return null;

        // Where the block as a whole goes. A bottom-aligned one only shifts when there is room to shift
        // into; a stated offset is taken as given even when the content is taller than the area.
        Length shift = offsetFromTop ?? (top < area.Height ? area.Height - top : Length.Zero);

        if (shift != Length.Zero)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                placed[i] = placed[i] with { Top = placed[i].Top + shift };
            }

            // A table's cells carry page coordinates rather than flow-relative ones, so they move with the
            // rectangle rather than with the line tops. Forgetting this leaves a bottom-aligned footer's
            // table where the flow would have been had it not moved.
            for (int i = 0; i < tables.Count; i++)
            {
                tables[i] = tables[i] with
                {
                    Area = Shift(tables[i].Area, shift),
                    Cells = TableLayouter.Offset(tables[i].Cells, Length.Zero, shift),
                };
            }
        }

        return new PlacedFlow
        {
            Blocks = blocks,
            Lines = placed,
            Tables = tables,
            Frames = frames,
            Area = area,
        };
    }

    /// <summary>
    /// The room each line has, given the frames placed so far — or null when none of them is in the way.
    /// </summary>
    /// <remarks>
    /// Null rather than a callback that always answers "all of it", so that a document without frames — very
    /// nearly all of them — takes exactly the path it took before any of this existed. The callback is asked
    /// once per line and walks the frames each time, which is the right trade at the counts involved: a page
    /// has a handful of frames and a few dozen lines.
    /// </remarks>
    private static LineRoom? Room(List<PlacedFrame> frames, DocRect area)
    {
        if (frames.Count == 0 || !frames.Exists(frame => frame.Frame.Obstructs)) return null;

        return (top, height) => FreeSpace(frames, area, top, top + height);
    }

    /// <summary>Turns a paragraph-relative room callback into the page-relative one the frames need.</summary>
    /// <remarks>
    /// The layouter measures a line's top from its <em>paragraph's</em> top and a frame's region is in page
    /// coordinates, so one of the two has to be translated. Doing it here keeps the layouter from having to
    /// know where on the page it is.
    /// </remarks>
    private static LineRoom? Shifted(LineRoom? room, Length paragraphTop)
        => room is null
            ? null
            : (top, height) => room(paragraphTop + top, height).RelativeTo(paragraphTop);

    /// <summary>
    /// The widest run of a line's span that no frame obstructs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <em>widest</em>, not the first: text goes down whichever side of a frame has more room, which is
    /// what makes a frame at the left margin push text to its right and a frame at the right margin leave
    /// text where it was. Computed by cutting the area's span at every overlapping frame and keeping the
    /// largest piece, which handles two frames side by side without a special case.
    /// </para>
    /// <para>
    /// A frame is in the way when its region overlaps the line's box <em>inclusively</em> — a line whose
    /// bottom edge exactly meets a frame's top counts as obstructed. That is measured rather than chosen:
    /// Writer tests with <c>SwRect::Overlaps</c>, whose comparisons are <c>&lt;=</c> and <c>&gt;=</c>, and a
    /// corpus document that put a line's bottom exactly on a frame's top showed the line wrapped.
    /// </para>
    /// <para>
    /// A <see cref="TextWrap.None"/> frame is not handled here and cannot be: it does not narrow a line, it
    /// pushes it below the frame, which is a vertical decision and belongs where the tops are assigned.
    /// </para>
    /// </remarks>
    /// <summary>
    /// How wide a paragraph is in its area, which is what a frame aligned to it is aligned within.
    /// </summary>
    /// <remarks>
    /// Its indents taken off, both of them, because ODF's <c>paragraph</c> reference means the paragraph's own
    /// box rather than the column's — so a frame centred in an indented paragraph is not centred in the column.
    /// The first line's indent is deliberately not part of it: it moves one line, not the block.
    /// </remarks>
    /// <param name="area">The flow's rectangle.</param>
    /// <param name="format">The paragraph's format, for its indents.</param>
    public static Length ParagraphWidth(DocRect area, ParagraphFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        return Length.Max(Length.Zero, area.Width - format.StartIndent - format.EndIndent);
    }

    /// <param name="frames">The frames already placed, in page coordinates.</param>
    /// <param name="area">The area the line is being laid out in.</param>
    /// <param name="lineTop">The line box's top, in page coordinates.</param>
    /// <param name="lineBottom">Its bottom.</param>
    public static LineSpace FreeSpace(
        List<PlacedFrame> frames, DocRect area, Length lineTop, Length lineBottom)
    {
        Length left = Length.Zero;
        Length right = area.Width;
        Length? moveTo = null;

        foreach (PlacedFrame placed in frames)
        {
            if (!placed.Frame.Obstructs) continue;

            DocRect region = placed.Region;
            if (region.Bottom < lineTop || region.Y > lineBottom) continue;

            // In the area's own coordinates, since that is what a LineSpace is measured in.
            Length from = region.X - area.X;
            Length to = region.Right - area.X;

            // Whichever side the mode allows. `None` allows neither, so the line goes below the frame; the
            // one-sided modes allow one, and if that side has no room the line goes below it just the same —
            // measured, since a left-wrapped frame flush against the margin pushes text down rather than
            // squeezing it into nothing.
            bool mayUseBefore = placed.Frame.Wrap is not (TextWrap.None or TextWrap.Right);
            bool mayUseAfter = placed.Frame.Wrap is not (TextWrap.None or TextWrap.Left);

            Length before = mayUseBefore ? from - left : Length.Zero;
            Length after = mayUseAfter ? right - to : Length.Zero;

            if (before <= Length.Zero && after <= Length.Zero)
            {
                // Nothing usable beside this frame. The line's top moves to the region's bottom, and the
                // largest such push wins: two frames side by side each forbidding text push past both.
                moveTo = moveTo is { } already
                    ? Length.Max(already, region.Bottom)
                    : region.Bottom;
                continue;
            }

            if (to <= left || from >= right) continue;

            // The wider allowed side survives and the other is given up. Two frames narrowing the same line
            // therefore compose, because the survivor is cut again by the next.
            if (before >= after) right = Length.Max(left, from);
            else left = Length.Min(right, to);
        }

        return new LineSpace(left, Length.Max(Length.Zero, right - left), moveTo);
    }

    /// <summary>
    /// How tall the blocks are at a given width, without placing them anywhere.
    /// </summary>
    /// <remarks>
    /// What a table's rows are sized from: a cell's height is its content's, and the row's is the tallest
    /// cell's. Measured by laying the flow out into a rectangle of unbounded height and asking where it
    /// ended, because that is the only answer that agrees with where the lines will actually be drawn —
    /// summing estimated line heights instead would drift from the real result exactly where it matters.
    /// </remarks>
    public static Length HeightOf(IReadOnlyList<PageBlock> blocks, Length width, int nesting = 0)
    {
        PlacedFlow? flow = LayOut(
            blocks, new DocRect(Length.Zero, Length.Zero, width, Length.Zero), Length.Zero, nesting);

        return flow is null ? Length.Zero : Extent(flow);
    }

    /// <summary>
    /// How far down a flow's content reaches, measured from its area's top.
    /// </summary>
    /// <remarks>
    /// The lower of the two edges, because a flow can end with either: a header of two paragraphs ends at
    /// its last line, and one that ends with a table ends at the table's bottom. Taking only the lines
    /// would size a cell whose last block is a table as though the table were not there.
    /// </remarks>
    public static Length Extent(PlacedFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);

        Length bottom = flow.Lines.Count == 0
            ? Length.Zero
            : flow.Lines[^1].Top + flow.Lines[^1].Box.Height;

        foreach (PlacedTable table in flow.Tables)
        {
            Length reach = table.Area.Bottom - flow.Area.Y;
            bottom = Length.Max(bottom, reach);
        }

        return bottom;
    }

    private static DocRect Shift(DocRect area, Length dy)
        => new(area.X, area.Y + dy, area.Width, area.Height);
}
