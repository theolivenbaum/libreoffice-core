using System.Globalization;
using System.Xml.Linq;
using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.Ooxml;
using Paperless.WordProcessing.Layout;

namespace Paperless.WordProcessing.Ooxml;

/// <content>
/// Reading a <c>wp:anchor</c> — a floating drawing — into the frame the layout engine takes.
/// </content>
/// <remarks>
/// <para>
/// OOXML's spelling is the friendliest of the four for one reason: every measurement is already in
/// <strong>EMUs</strong>, which is Paperless's own unit, so nothing needs converting or rounding. The
/// extent, the two offsets and the four wrap distances are all plain integers in the unit the rest of the
/// engine works in.
/// </para>
/// <para>
/// The trap is the <em>names</em>. OOXML's <c>wp:wrapNone</c> and ODF's <c>style:wrap="none"</c> are
/// opposites: OOXML means "do not wrap — let the text run through", and ODF means "do not put text beside
/// it — push the text below". ODF's sense is OOXML's <c>wp:wrapTopAndBottom</c>. A reader mapping the two
/// by name draws a watermark that shoves the page apart and an inset picture that the text runs over.
/// </para>
/// </remarks>
public sealed partial class DocxLayoutSource
{
    /// <summary>Turns the floating drawings found in a paragraph into frames.</summary>
    private List<PageFrame> FramesOf(List<DocxFrameAnchor> anchored)
    {
        List<PageFrame> frames = [];

        foreach (DocxFrameAnchor found in anchored)
        {
            XElement anchor = found.Element;

            DocSize size = new(
                Emu(Drawing(anchor, "extent"), "cx"), Emu(Drawing(anchor, "extent"), "cy"));

            if (size.IsEmpty) continue;

            frames.Add(new PageFrame
            {
                Offset = new DocPoint(Offset(anchor, "positionH"), Offset(anchor, "positionV")),
                Size = size,
                Anchor = AnchorOf(anchor),
                Wrap = WrapOf(anchor),
                Margins = new CellPadding(
                    Emu(anchor, "distL"), Emu(anchor, "distR"),
                    Emu(anchor, "distT"), Emu(anchor, "distB")),
                Padding = PaddingOf(anchor),
                Background = BackgroundOf(anchor),
                Borders = BordersOf(anchor),

                // An OOXML text box is a DrawingML shape, and a shape's outline is centred on its edge rather
                // than drawn inside it. Measured on one document exported both ways: LibreOffice strokes the
                // ODF frame's left border at 57.7 pt and the DOCX shape's at 56.65, for a frame whose left
                // edge is 56.7.
                BorderStraddlesTheEdge = true,
                HorizontalAlignment = AlignmentOf(Drawing(anchor, "positionH")),
                VerticalAlignment = AlignmentOf(Drawing(anchor, "positionV")),
                HorizontalRelativeTo = ReferenceOf(Drawing(anchor, "positionH")),
                VerticalRelativeTo = ReferenceOf(Drawing(anchor, "positionV")),
                Blocks = ContentOf(anchor),
            });
        }

        return frames;
    }

    /// <summary>
    /// The gap between the shape's edges and its own text, from <c>a:bodyPr</c>'s four insets.
    /// </summary>
    /// <remarks>
    /// DrawingML's names, not WordprocessingML's: <c>lIns</c>, <c>rIns</c>, <c>tIns</c> and <c>bIns</c>, in
    /// EMUs, on the shape's body properties rather than on the anchor. Their <em>defaults</em> are not zero
    /// and are not symmetrical — 91440 EMUs (0.1 inch) at the sides and 45720 (0.05 inch) top and bottom —
    /// so a shape stating none still insets its text, and taking the absence as nothing lays the first line
    /// against the shape's edge.
    /// </remarks>
    private static CellPadding PaddingOf(XElement anchor)
    {
        // By local name rather than by namespace, because which namespace it is in depends on what kind of
        // shape holds it: a Word shape writes `wps:bodyPr` and a DrawingML one `a:bodyPr`, and the element is
        // the same element either way. Looking only in DrawingML's namespace finds nothing for a text box and
        // falls back to the defaults, which puts the first line 2.95 pt to the right of where it belongs.
        XElement? body = anchor
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "bodyPr");

        return new CellPadding(
            Inset(body, "lIns", DefaultSideInset),
            Inset(body, "rIns", DefaultSideInset),
            Inset(body, "tIns", DefaultEndInset),
            Inset(body, "bIns", DefaultEndInset));

        static Length Inset(XElement? body, string name, Length fallback)
            => Plain(body, name) is { } stated ? Emu(stated) : fallback;
    }

    /// <summary>
    /// The shape's fill colour, or null when it has none.
    /// </summary>
    /// <remarks>
    /// DrawingML rather than WordprocessingML: the colour is <c>a:solidFill/a:srgbClr</c> on the shape's
    /// <c>spPr</c>, as a six-digit hex string, and <c>a:noFill</c> is how a shape says it is transparent. Only
    /// the solid case is read; a gradient, a pattern or a picture fill contributes nothing rather than its
    /// first stop, since a wrong flat colour behind text is more visible than no colour at all.
    /// </remarks>
    private static Colour? BackgroundOf(XElement anchor)
    {
        XElement? properties = ShapeProperties(anchor);
        if (properties is null) return null;

        return Drawn(properties, "noFill") is not null
            ? null
            : SolidColour(Drawn(properties, "solidFill"));
    }

    /// <summary>
    /// The shape's outline, applied to all four sides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One outline, not four: DrawingML gives a shape a single <c>a:ln</c> and every side takes it, which is
    /// the difference from ODF and from a table cell. Its width is <c>@w</c> in EMUs — 25400 for the two points
    /// LibreOffice's export writes for a 2 pt border — and its colour is the same <c>a:solidFill</c> nesting
    /// the fill uses.
    /// </para>
    /// <para>
    /// A shape with no <c>a:ln</c> at all gets no border. That is not what Word does — DrawingML's real default
    /// comes from the theme's line style, which nothing here reads — but it is what every file LibreOffice
    /// writes means, since its export always states the element.
    /// </para>
    /// </remarks>
    private static CellBorders BordersOf(XElement anchor)
    {
        XElement? line = Drawn(ShapeProperties(anchor), "ln");
        if (line is null || Drawn(line, "noFill") is not null) return default;

        Length width = Emu(line, "w");
        if (width <= Length.Zero) return default;

        TableBorder border = new(width, SolidColour(Drawn(line, "solidFill")) ?? Colour.Black);

        return new CellBorders(border, border, border, border);
    }

    /// <summary>
    /// The shape's <c>spPr</c>, found by local name.
    /// </summary>
    /// <remarks>
    /// By local name for the same reason <see cref="PaddingOf"/> finds <c>bodyPr</c> that way: a Word shape
    /// writes <c>wps:spPr</c> and a DrawingML picture <c>pic:spPr</c>, and the element is the same either way.
    /// </remarks>
    private static XElement? ShapeProperties(XElement anchor)
        => anchor.Descendants().FirstOrDefault(element => element.Name.LocalName == "spPr");

    /// <summary>An <c>a:solidFill</c>'s colour, or null when it is not a plain RGB one.</summary>
    /// <remarks>
    /// <c>a:srgbClr</c> only. A theme colour — <c>a:schemeClr</c> — needs the theme's palette and the
    /// <c>lumMod</c>/<c>shade</c>/<c>tint</c> chain resolved, which is a body of work of its own and is
    /// recorded as such in this library's TODO.
    /// </remarks>
    private static Colour? SolidColour(XElement? fill)
    {
        string? value = Plain(Drawn(fill, "srgbClr"), "val");

        return value is not null
               && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)
            ? Colour.FromRgb(rgb)
            : null;
    }

    private static XElement? Drawn(XElement? parent, string name)
        => parent?.Element(XName.Get(name, OoxmlNamespaces.DrawingML));

    /// <summary>DrawingML's default left and right text inset: a tenth of an inch.</summary>
    private static readonly Length DefaultSideInset = Length.FromEmu(91440);

    /// <summary>Its default top and bottom inset: a twentieth of an inch.</summary>
    private static readonly Length DefaultEndInset = Length.FromEmu(45720);

    /// <summary>
    /// The blocks inside the shape's text box, or empty when it holds none.
    /// </summary>
    /// <remarks>
    /// <c>wps:txbx/w:txbxContent</c>, whose children are ordinary <c>w:p</c> and <c>w:tbl</c> elements — the
    /// same things a table cell holds, read through the same walk. A shape with no text box holds a picture or
    /// nothing, and either way contributes no lines.
    /// </remarks>
    private List<PageBlock> ContentOf(XElement anchor)
    {
        XElement? content = anchor
            .Descendants(XName.Get("txbxContent", OoxmlNamespaces.WordprocessingML))
            .FirstOrDefault();

        return content is null ? [] : ReadCell(content);
    }

    /// <summary>
    /// One of the two position offsets, in EMUs.
    /// </summary>
    /// <remarks>
    /// <c>wp:posOffset</c> is a measure and <c>wp:align</c> a named position; a position stating the second has
    /// no offset to read, and its alignment comes from <see cref="AlignmentOf"/> instead.
    /// </remarks>
    private static Length Offset(XElement anchor, string name)
    {
        XElement? position = Drawing(anchor, name);

        return position is null ? Length.Zero : Emu(Drawing(position, "posOffset")?.Value);
    }

    /// <summary>
    /// How one axis is stated, from <c>wp:align</c>.
    /// </summary>
    /// <remarks>
    /// An <em>element</em> rather than an attribute, and the alternative to <c>wp:posOffset</c> rather than a
    /// modifier of it — a position states one or the other. Its vocabulary is shared between the two axes and
    /// so overlaps confusingly: <c>center</c> means both centres, while <c>left</c> and <c>top</c> are the same
    /// case seen twice. <c>inside</c> and <c>outside</c> are the binding and outer edges of a two-sided
    /// document and are read as start and end, since nothing here knows which side a page is.
    /// </remarks>
    /// <param name="position">The <c>wp:positionH</c> or <c>wp:positionV</c>.</param>
    private static FrameAlignment AlignmentOf(XElement? position)
        => Drawing(position, "align")?.Value?.Trim() switch
        {
            "left" or "top" or "inside" => FrameAlignment.Start,
            "center" => FrameAlignment.Centre,
            "right" or "bottom" or "outside" => FrameAlignment.End,
            _ => FrameAlignment.Offset,
        };

    /// <summary>
    /// Which rectangle the position is measured against, from <c>relativeFrom</c>.
    /// </summary>
    /// <remarks>
    /// <c>page</c> is the whole sheet and <c>margin</c> — with its four one-sided spellings — is the text area,
    /// which is the distinction that matters: reading them as the same thing puts a page-centred picture off by
    /// half the difference between the two margins. <c>column</c> is the text column, which for a single-column
    /// page is the text area again, and is what LibreOffice's own export writes; treating it as the paragraph
    /// instead is right only while the paragraph has no indent.
    /// </remarks>
    /// <param name="position">The <c>wp:positionH</c> or <c>wp:positionV</c>.</param>
    private static FrameReference ReferenceOf(XElement? position)
        => Plain(position, "relativeFrom") switch
        {
            "page" => FrameReference.Page,
            "margin" or "leftMargin" or "rightMargin" or "topMargin" or "bottomMargin"
                or "insideMargin" or "outsideMargin" or "column" => FrameReference.TextArea,
            _ => FrameReference.Paragraph,
        };

    /// <summary>What the offsets are measured from.</summary>
    /// <remarks>
    /// <c>relativeFrom</c> on the vertical position, which is the one that decides whether the frame moves
    /// with its paragraph. <c>column</c> and <c>margin</c> and the rest are treated as the paragraph's own
    /// reference, which is right for the vertical <c>paragraph</c> and an approximation otherwise.
    /// </remarks>
    private static FrameAnchor AnchorOf(XElement anchor)
        => Plain(Drawing(anchor, "positionV"), "relativeFrom") switch
        {
            "page" or "topMargin" or "bottomMargin" => FrameAnchor.Page,
            "character" or "char" => FrameAnchor.Character,
            _ => FrameAnchor.Paragraph,
        };

    /// <summary>
    /// How text treats the frame, from whichever wrap element the anchor carries.
    /// </summary>
    /// <remarks>
    /// The mode is an element name rather than an attribute, and <c>wp:wrapSquare</c>'s
    /// <c>wrapText</c> says which sides — <c>bothSides</c>, <c>left</c>, <c>right</c> or <c>largest</c>,
    /// where "largest" is ODF's <c>dynamic</c>. <c>wp:wrapTight</c> and <c>wp:wrapThrough</c> follow the
    /// picture's outline rather than its box; they are read as square wraps, which keeps the text beside the
    /// picture rather than through it and is wrong only in the notch a contour would have cut.
    /// </remarks>
    private static TextWrap WrapOf(XElement anchor)
    {
        foreach (XElement child in anchor.Elements())
        {
            if (child.Name.Namespace != OoxmlNamespaces.DrawingMLWordprocessing) continue;

            switch (child.Name.LocalName)
            {
                // Not a mistake: OOXML's "none" is ODF's "run-through".
                case "wrapNone":
                    return TextWrap.Through;

                case "wrapTopAndBottom":
                    return TextWrap.None;

                case "wrapSquare" or "wrapTight" or "wrapThrough":
                    return Plain(child, "wrapText") switch
                    {
                        "left" => TextWrap.Left,
                        "right" => TextWrap.Right,
                        "largest" => TextWrap.Dynamic,
                        _ => TextWrap.Parallel,
                    };

                default:
                    break;
            }
        }

        // An anchor with no wrap element at all. Word treats that as square, which is also the safer guess:
        // a frame that should have moved the text and did not is more visible than the reverse.
        return TextWrap.Parallel;
    }

    private static XElement? Drawing(XElement? parent, string name)
        => parent?.Element(XName.Get(name, OoxmlNamespaces.DrawingMLWordprocessing));

    /// <summary>
    /// An <em>unprefixed</em> attribute's value, which is what DrawingML writes.
    /// </summary>
    /// <remarks>
    /// The trap that costs an afternoon: a <c>wp:</c> element's attributes are in <strong>no</strong>
    /// namespace. <c>cx</c>, <c>distL</c>, <c>wrapText</c> and <c>relativeFrom</c> are all plain names, so
    /// <c>Word.Attribute</c> — which prepends the WordprocessingML namespace, correctly, for everything in
    /// <c>w:</c> — returns null for every one of them. The symptom is a frame of no size, which is silently
    /// dropped, so the document lays out as though it had no frames at all.
    /// </remarks>
    private static string? Plain(XElement? element, string name)
        => element?.Attribute(name)?.Value;

    private static Length Emu(XElement? element, string attribute)
        => Emu(Plain(element, attribute));

    /// <summary>An EMU count as a length, which needs no conversion at all.</summary>
    private static Length Emu(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long emu)
            ? Length.FromEmu(emu)
            : Length.Zero;
}

/// <summary>One floating drawing found while walking a paragraph.</summary>
/// <param name="Offset">Where in the paragraph's text it was declared.</param>
/// <param name="Element">The <c>wp:anchor</c> itself.</param>
internal readonly record struct DocxFrameAnchor(int Offset, XElement Element);
