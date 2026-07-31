using System.Globalization;
using System.Xml.Linq;
using Paperless.Core.Geometry;
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
    /// <c>wp:posOffset</c> is a measure and <c>wp:align</c> a named position — <c>left</c>, <c>center</c>,
    /// <c>right</c> and their vertical counterparts. Only the measure is read; a named position comes back as
    /// zero, which puts the frame at its reference's start. That is the honest answer until the reference
    /// rectangle each <c>relativeFrom</c> names is resolved, since "centre" is meaningless without it.
    /// </remarks>
    private static Length Offset(XElement anchor, string name)
    {
        XElement? position = Drawing(anchor, name);

        return position is null ? Length.Zero : Emu(Drawing(position, "posOffset")?.Value);
    }

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
