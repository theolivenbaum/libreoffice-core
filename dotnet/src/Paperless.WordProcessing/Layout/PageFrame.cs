using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;

namespace Paperless.WordProcessing.Layout;

/// <summary>What a floating frame's position is measured from.</summary>
/// <remarks>
/// Only the anchor kinds that <em>float</em> are here. An as-character frame is not one of them: it sits in
/// the line like a very large glyph, takes part in line breaking, and is already modelled as an anchor
/// character in the paragraph's text. Giving it a case would invite it to be laid out twice.
/// </remarks>
public enum FrameAnchor
{
    /// <summary>To the paragraph it is declared in, which is what most documents use.</summary>
    Paragraph,

    /// <summary>To a character position within that paragraph.</summary>
    Character,

    /// <summary>To the page the anchoring paragraph lands on.</summary>
    Page,
}

/// <summary>How a frame's position is stated along one axis.</summary>
/// <remarks>
/// Every format spells this as a small vocabulary rather than as a number, because "centred" cannot be written
/// as an offset: it depends on the frame's own size and on the width of whatever it is centred in. So the
/// alignment and the thing it is relative to are read separately and resolved together, at the point where both
/// rectangles are known.
/// </remarks>
public enum FrameAlignment
{
    /// <summary>A measured distance from the reference's start edge, which is what most frames state.</summary>
    Offset,

    /// <summary>Against the reference's start edge, ignoring the stated offset.</summary>
    Start,

    /// <summary>Centred in the reference.</summary>
    Centre,

    /// <summary>Against the reference's end edge.</summary>
    End,
}

/// <summary>What a frame's position is measured against.</summary>
/// <remarks>
/// Three rather than the dozen each format names, because those dozen collapse: ODF's
/// <c>page-start-margin</c> and OOXML's <c>leftMargin</c> both mean an edge of the text area, and every
/// paragraph-ish value means the paragraph. What matters to placement is only <em>which rectangle</em>, and
/// there are three of those.
/// </remarks>
public enum FrameReference
{
    /// <summary>The anchoring paragraph, including its indents.</summary>
    Paragraph,

    /// <summary>The text area — the page less its margins, or the column within it.</summary>
    TextArea,

    /// <summary>The whole page, margins included.</summary>
    Page,
}

/// <summary>
/// The rectangles a frame's position can be measured against, resolved for one placement.
/// </summary>
/// <remarks>
/// Handed in rather than reachable from the frame, because only layout knows any of it: where the anchoring
/// paragraph ended up, how wide it is after its indents, and which page it landed on. A frame in a table cell
/// or a running head has no page of its own, and there the flow's own rectangle stands in for all three —
/// which is right, since a page-relative frame inside a cell is not a thing any of the four formats can state.
/// </remarks>
/// <param name="Anchor">The anchoring paragraph's top-left, in page coordinates.</param>
/// <param name="ParagraphWidth">How wide that paragraph is, after its indents.</param>
/// <param name="TextArea">The column the paragraph is in.</param>
/// <param name="Page">The whole page.</param>
public readonly record struct FrameSpace(
    DocPoint Anchor, Length ParagraphWidth, DocRect TextArea, DocRect Page)
{
    /// <summary>A space with no page of its own: a cell's, a header's, or another frame's.</summary>
    /// <param name="area">The flow's rectangle, which stands in for all three.</param>
    /// <param name="anchor">The anchoring paragraph's top-left.</param>
    /// <param name="paragraphWidth">How wide that paragraph is.</param>
    public static FrameSpace In(DocRect area, DocPoint anchor, Length paragraphWidth)
        => new(anchor, paragraphWidth, area, area);
}

/// <summary>How text behaves where a frame is in its way.</summary>
/// <remarks>
/// The names are ODF's, and the other three formats' spellings map onto them: DOCX's <c>w:wrap</c> values,
/// RTF's <c>\wraptext</c> family, and WW8's <c>wr</c> field in the anchor record. What matters to layout is
/// only which sides a line may use, so five values cover all four formats.
/// </remarks>
public enum TextWrap
{
    /// <summary>
    /// No text beside the frame at all: a line that would meet it is pushed below it.
    /// </summary>
    /// <remarks>
    /// ODF's <c>style:wrap="none"</c>, and the one value whose name reads backwards — it means "do not wrap
    /// text <em>around</em> it", not "do not let it affect the text".
    /// </remarks>
    None,

    /// <summary>Text runs down both sides of the frame, whichever has room.</summary>
    Parallel,

    /// <summary>Text keeps only the room to the frame's left.</summary>
    Left,

    /// <summary>Text keeps only the room to the frame's right.</summary>
    Right,

    /// <summary>
    /// Text takes whichever side has more room, and neither when both are too narrow.
    /// </summary>
    /// <remarks>
    /// ODF's <c>dynamic</c>, which LibreOffice's user interface calls "optimal". Modelled as
    /// <see cref="Parallel"/> for now, since the side with more room is what the free-interval arithmetic
    /// picks anyway; what is not modelled is the threshold below which Writer gives up and pushes the line
    /// down.
    /// </remarks>
    Dynamic,

    /// <summary>
    /// The frame does not affect the text at all, which runs straight through underneath or over it.
    /// </summary>
    /// <remarks>
    /// ODF's <c>run-through</c>. A watermark is the usual reason.
    /// </remarks>
    Through,
}

/// <summary>
/// A floating frame: a rectangle beside or behind the text, and how the text treats it.
/// </summary>
/// <remarks>
/// <para>
/// Its <see cref="Offset"/> is relative to whatever <see cref="Anchor"/> names rather than to the page,
/// because that is what every format states and because the resolution needs something only layout knows —
/// where the anchoring paragraph ended up. A frame anchored to a paragraph that moves to the next page moves
/// with it.
/// </para>
/// <para>
/// Its content is a flow of its own — a frame can hold anything a body can, and it is laid out at the frame's
/// width by the same <see cref="FlowLayouter"/> a table cell's content goes through. Empty for a frame whose
/// content is not text, an image above all, which is placed and drawn but has nothing to break into lines.
/// </para>
/// </remarks>
public sealed record PageFrame
{
    /// <summary>Where the frame's top-left sits, relative to its anchor.</summary>
    public required DocPoint Offset { get; init; }

    /// <summary>How big it is.</summary>
    public required DocSize Size { get; init; }

    /// <summary>What the offset is measured from.</summary>
    public FrameAnchor Anchor { get; init; } = FrameAnchor.Paragraph;

    /// <summary>How text behaves where the frame is in its way.</summary>
    public TextWrap Wrap { get; init; } = TextWrap.Parallel;

    /// <summary>
    /// The gap kept between the frame and the text beside it, which widens the region text avoids.
    /// </summary>
    /// <remarks>
    /// Part of the wrap region rather than of the frame: the frame is drawn at
    /// <see cref="Size"/> and the text stays this much further away. Measured on the corpus document — a
    /// 5 cm frame at the left margin with a 0.2 cm right margin pushes text to 204.1 pt, which is
    /// 56.7 + 141.73 + 5.67.
    /// </remarks>
    public CellPadding Margins { get; init; }

    /// <summary>
    /// The blocks inside the frame, in order, or empty when it holds no text.
    /// </summary>
    /// <remarks>
    /// Blocks rather than paragraphs, for the same reason a cell's content is: a frame can hold a table, and
    /// it goes through the same layout path either way.
    /// </remarks>
    public IReadOnlyList<PageBlock> Blocks { get; init; } = [];

    /// <summary>
    /// The gap between the frame's own edges and its text, which comes out of the width its lines break at.
    /// </summary>
    /// <remarks>
    /// The frame's <c>fo:padding</c>, and not to be confused with <see cref="Margins"/>: padding is inside
    /// the frame and margin is outside it. Conflating the two puts the frame's own text where the body text
    /// beside it should be.
    /// </remarks>
    public CellPadding Padding { get; init; }

    /// <summary>
    /// The colour filling the frame, or null when it is transparent.
    /// </summary>
    /// <remarks>
    /// Drawn over the frame's whole bounds rather than over its content area: the padding is inside the fill,
    /// which is what makes a padded coloured frame look like a box with a margin round its text rather than
    /// like a coloured word.
    /// </remarks>
    public Colour? Background { get; init; }

    /// <summary>
    /// The frame's own four edges.
    /// </summary>
    /// <remarks>
    /// Drawn <em>inside</em> the bounds by half each side's width, which is measured rather than assumed:
    /// LibreOffice puts a 2 pt border on a frame whose left edge is at 56.7 pt at 57.7, and the stroke spans
    /// the whole side rather than stopping short at the corners. So the frame's outer edge is where the
    /// document said the frame is, and the border grows inwards from it — unlike a table's grid line, which
    /// straddles the boundary it sits on.
    /// </remarks>
    public CellBorders Borders { get; init; }

    /// <summary>
    /// True when the border is centred on the frame's edge rather than drawn inside it.
    /// </summary>
    /// <remarks>
    /// The difference between a <em>frame</em> and a <em>shape</em>, and it is a whole border width across.
    /// LibreOffice renders an ODF text frame's border inside the frame's own edge; the same box arriving as a
    /// DrawingML shape — which is what an OOXML text box is, and what an ODF frame with no parent graphic
    /// style becomes — has its outline centred on the edge, so half of it falls outside the box. Measured on
    /// one document exported both ways: the ODF render strokes the left edge at 57.7 pt and the DOCX render
    /// strokes it at 56.65, where the frame's edge is 56.7.
    /// </remarks>
    public bool BorderStraddlesTheEdge { get; init; }

    /// <summary>True when the frame takes room from the text rather than being ignored by it.</summary>
    public bool Obstructs => Wrap != TextWrap.Through;

    /// <summary>Where the frame's position is measured from along the horizontal.</summary>
    public FrameAlignment HorizontalAlignment { get; init; } = FrameAlignment.Offset;

    /// <summary>And along the vertical.</summary>
    public FrameAlignment VerticalAlignment { get; init; } = FrameAlignment.Offset;

    /// <summary>Which rectangle the horizontal position is measured against.</summary>
    public FrameReference HorizontalRelativeTo { get; init; } = FrameReference.Paragraph;

    /// <summary>And which the vertical is.</summary>
    public FrameReference VerticalRelativeTo { get; init; } = FrameReference.Paragraph;

    /// <summary>
    /// The region text keeps clear of, given the rectangles the position resolves against.
    /// </summary>
    /// <param name="space">Where the anchor, its paragraph, its column and its page are.</param>
    public DocRect RegionIn(FrameSpace space)
    {
        DocRect bounds = BoundsIn(space);

        return new DocRect(
            bounds.X - Margins.Left,
            bounds.Y - Margins.Top,
            bounds.Width + Margins.Horizontal,
            bounds.Height + Margins.Vertical);
    }

    /// <summary>
    /// The frame itself — what would be drawn.
    /// </summary>
    /// <remarks>
    /// The two axes are resolved independently, because the formats state them independently: a picture
    /// centred on the page and two centimetres below its paragraph is ordinary, and it is two different
    /// rectangles that answer the two halves.
    /// </remarks>
    /// <param name="space">Where the anchor, its paragraph, its column and its page are.</param>
    public DocRect BoundsIn(FrameSpace space) => new(
        Placed(
            HorizontalAlignment,
            Offset.X,
            Size.Width,
            Horizontally(space, HorizontalRelativeTo)),
        Placed(
            VerticalAlignment,
            Offset.Y,
            Size.Height,
            Vertically(space, VerticalRelativeTo)),
        Size.Width,
        Size.Height);

    /// <summary>The rectangle the frame's own text is laid out in: its bounds less its padding.</summary>
    /// <param name="space">Where the anchor, its paragraph, its column and its page are.</param>
    public DocRect ContentAreaIn(FrameSpace space)
    {
        DocRect bounds = BoundsIn(space);

        return new DocRect(
            bounds.X + Padding.Left,
            bounds.Y + Padding.Top,
            Length.Max(Length.Zero, bounds.Width - Padding.Horizontal),
            Length.Max(Length.Zero, bounds.Height - Padding.Vertical));
    }

    /// <summary>
    /// One axis resolved: an offset, or an alignment within the reference's extent.
    /// </summary>
    /// <remarks>
    /// An extent of nothing falls back to the offset whatever the alignment says, which is the paragraph's
    /// case vertically: a paragraph's height is not known when its frames are placed — the frame is what
    /// changes it — so "centred in the paragraph" cannot be answered and "at the paragraph's top" is the
    /// nearest honest one. Centring against zero would put half the frame above the paragraph.
    /// </remarks>
    private static Length Placed(
        FrameAlignment alignment, Length offset, Length size, (Length Start, Length Extent) reference)
    {
        if (reference.Extent <= Length.Zero) return reference.Start + offset;

        return alignment switch
        {
            FrameAlignment.Start => reference.Start,
            FrameAlignment.Centre => reference.Start + ((reference.Extent - size) / 2),
            FrameAlignment.End => reference.Start + reference.Extent - size,
            _ => reference.Start + offset,
        };
    }

    private static (Length Start, Length Extent) Horizontally(
        FrameSpace space, FrameReference reference) => reference switch
        {
            FrameReference.TextArea => (space.TextArea.X, space.TextArea.Width),
            FrameReference.Page => (space.Page.X, space.Page.Width),
            _ => (space.Anchor.X, space.ParagraphWidth),
        };

    /// <summary>
    /// The vertical reference, whose paragraph case has a start and no extent.
    /// </summary>
    /// <remarks>
    /// Deliberately zero rather than a guess: see <see cref="Placed"/>. Every other reference has a real
    /// height, which is what lets a watermark be centred on the page.
    /// </remarks>
    private static (Length Start, Length Extent) Vertically(
        FrameSpace space, FrameReference reference) => reference switch
        {
            FrameReference.TextArea => (space.TextArea.Y, space.TextArea.Height),
            FrameReference.Page => (space.Page.Y, space.Page.Height),
            _ => (space.Anchor.Y, Length.Zero),
        };
}
