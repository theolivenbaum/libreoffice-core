using System.Globalization;
using System.Xml.Linq;
using Paperless.Core.Geometry;
using Paperless.Core.Units;
using Paperless.Ooxml;
using Paperless.WordProcessing.Layout;

namespace Paperless.WordProcessing.Ooxml;

/// <summary>
/// The room a <c>w:pict</c> or <c>w:object</c> takes on the line it sits on.
/// </summary>
/// <remarks>
/// <para>
/// A <c>w:drawing</c> states its size in a <c>wp:extent</c> and VML does not: a <c>v:shape</c> carries a
/// CSS <c>style</c>, and a <c>w:object</c> additionally carries <c>w:dxaOrig</c>/<c>w:dyaOrig</c>. Until
/// this existed the anchor character was the whole of the reserved height, so a figure occupied one text
/// line and every page after it was wrong.
/// </para>
/// <para>
/// <strong>Only an inline shape reserves anything, and that is the whole of why an earlier attempt at
/// this was reverted.</strong> A shape whose style says <c>position:absolute</c> is floating: it is
/// placed against the page or the paragraph and the text does not make room for it on a line.
/// Reserving for those as well is what added seven pages to <c>33004.docx</c>, which holds five
/// floating shapes and one inline. Measured over the corpus, the two are not a rare split — 161 of the
/// 224 sized VML shapes in the words track are floating.
/// </para>
/// <para>
/// <strong>The size is the one the style declares.</strong> Read off the reference's own content stream
/// on <c>EHEST-SMS-Safety-Management-Manual-V2.docx</c> page 18, whose Visio object declares
/// <c>style="width:425pt;height:190pt"</c>: LibreOffice draws the replacement image with a
/// <c>425.00 0 0 190.00 … cm</c>, exactly the declared box.
/// </para>
/// <para>
/// <strong>Take the <c>v:shape</c>, not the first VML child.</strong> A <c>w:object</c> Word writes
/// opens with a <c>v:shapetype</c> — the reusable geometry definition — and only then the <c>v:shape</c>
/// that uses it. The <c>v:shapetype</c> carries no <c>style</c>, so a reader that takes the first VML
/// element finds no size and silently reserves nothing. That is the difference between this and the
/// three probes the earlier attempt passed, which wrote a bare <c>v:shape</c>.
/// </para>
/// </remarks>
internal static class DocxVmlFrames
{
    /// <summary>Every frame a <c>w:pict</c> or <c>w:object</c> contributes.</summary>
    /// <remarks>
    /// A list rather than one frame because a single <c>w:pict</c> routinely holds several shapes —
    /// the Work Breakdown Structure templates put 49 across 13 of them — and taking the first was how
    /// the other 48 went missing.
    /// </remarks>
    /// <param name="element">The <c>w:pict</c> or <c>w:object</c>.</param>
    /// <param name="anchorOffset">Where in the paragraph's text it sits.</param>
    /// <param name="pictures">How to resolve <c>v:imagedata</c> into bytes, or null for geometry only.</param>
    /// <param name="content">How to read a <c>w:txbxContent</c> into blocks, or null to skip its text.</param>
    public static List<PageFrame> ReadAll(
        XElement element,
        int anchorOffset,
        DocxPictures? pictures,
        Func<XElement, IReadOnlyList<PageBlock>>? content = null)
    {
        ArgumentNullException.ThrowIfNull(element);

        List<PageFrame> frames = [];
        foreach (XElement top in TopLevel(element))
        {
            if (top.Name.LocalName is "group")
            {
                frames.AddRange(Group(top, anchorOffset, pictures, content));
                continue;
            }

            if (One(top, element, anchorOffset, pictures, content) is { } frame) frames.Add(frame);
        }

        return frames;
    }

    /// <summary>The VML shapes and groups of a <c>w:pict</c> that are not inside another group.</summary>
    /// <remarks>
    /// <para>
    /// <c>v:shapetype</c> is excluded deliberately — it is the reusable geometry definition Word writes
    /// ahead of the shape that uses it, it carries no <c>style</c>, and a reader that takes the first
    /// VML element finds no size and silently reserves nothing.
    /// </para>
    /// <para>
    /// Members of a <c>v:group</c> are excluded here and reached through <see cref="Group"/> instead,
    /// because their coordinates are in the group's space and mean nothing on their own. Measured on
    /// the Work Breakdown Structure templates: 18 of their 24 text boxes sit inside a group, so a
    /// reader that walked every descendant placed three quarters of the document's text at the origin.
    /// </para>
    /// </remarks>
    private static IEnumerable<XElement> TopLevel(XElement element)
        => element.Descendants().Where(child =>
            IsShape(child) is not false
            && !child.Ancestors()
                .TakeWhile(ancestor => ancestor != element)
                .Any(ancestor => ancestor.Name == XName.Get("group", OoxmlNamespaces.Vml)));

    /// <summary>True for a VML shape or group, false for anything else.</summary>
    private static bool? IsShape(XElement child)
        => child.Name.Namespace == OoxmlNamespaces.Vml
           && child.Name.LocalName is "shape" or "rect" or "roundrect" or "oval" or "group"
            ? true
            : null;

    /// <summary>
    /// A <c>v:group</c> flattened into one frame per member, each mapped out of the group's own
    /// coordinate space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A group states where it sits and how big it is the way any floating shape does — in its
    /// <c>style</c>, in real units — and then declares a <em>child</em> coordinate space with
    /// <c>coordsize</c> (and <c>coordorigin</c>, which defaults to <c>0,0</c>). Its members'
    /// <c>left</c>, <c>top</c>, <c>width</c> and <c>height</c> are bare numbers in that space, so a
    /// member at <c>left:5400</c> in a group whose <c>coordsize</c> is <c>21600,21600</c> sits a
    /// quarter of the way across the group, whatever the group's width in points happens to be.
    /// </para>
    /// <para>
    /// Reading those bare numbers as points is the failure this exists to avoid: they are routinely
    /// in the thousands, so every member lands metres off the page and its text is clipped away.
    /// </para>
    /// </remarks>
    private static List<PageFrame> Group(
        XElement group,
        int anchorOffset,
        DocxPictures? pictures,
        Func<XElement, IReadOnlyList<PageBlock>>? content)
    {
        List<PageFrame> frames = [];

        Dictionary<string, string> style = Style(group);
        if ((style.TryGetValue("width", out string? gw) ? Css(gw) : null) is not { } groupWidth
            || (style.TryGetValue("height", out string? gh) ? Css(gh) : null) is not { } groupHeight
            || groupWidth <= Length.Zero
            || groupHeight <= Length.Zero)
        {
            return frames;
        }

        Length originX = (style.TryGetValue("margin-left", out string? ml) ? Css(ml) : null)
                         ?? Length.Zero;
        Length originY = (style.TryGetValue("margin-top", out string? mt) ? Css(mt) : null)
                         ?? Length.Zero;

        (double spaceX, double spaceY) = Pair(group.Attribute("coordsize")?.Value) ?? (0, 0);
        (double baseX, double baseY) = Pair(group.Attribute("coordorigin")?.Value) ?? (0, 0);
        if (spaceX <= 0 || spaceY <= 0) return frames;

        FrameHorizontalOrigin horizontal = HorizontalOriginOf(style);
        FrameVerticalOrigin vertical = VerticalOriginOf(style);

        foreach (XElement member in group.Elements().Where(child => IsShape(child) is not null))
        {
            if (member.Name.LocalName is "group") continue;   // nested groups: not measured, not guessed

            Dictionary<string, string> box = Style(member);
            if (Number(box.GetValueOrDefault("left", "")) is not { } left
                || Number(box.GetValueOrDefault("top", "")) is not { } top
                || Number(box.GetValueOrDefault("width", "")) is not { } wide
                || Number(box.GetValueOrDefault("height", "")) is not { } tall)
            {
                continue;
            }

            if (wide <= 0 || tall <= 0) continue;

            XElement? text = TextBox(member);
            FramePicture picture = text is null && pictures is not null
                ? pictures.ReadVml(member)
                : FramePicture.None;

            frames.Add(new PageFrame
            {
                Size = new DocSize(
                    groupWidth * (wide / spaceX), groupHeight * (tall / spaceY)),
                Anchor = FrameAnchor.Paragraph,
                AnchorOffset = anchorOffset,
                Wrap = TextWrap.Through,
                HorizontalOrigin = horizontal,
                HorizontalOffset = originX + (groupWidth * ((left - baseX) / spaceX)),
                VerticalOrigin = vertical,
                VerticalOffset = originY + (groupHeight * ((top - baseY) / spaceY)),
                IsImage = text is null,
                Image = picture.Raster,
                Vector = picture.Vector,
                Blocks = text is not null && content is not null ? content(text) : [],
            });
        }

        return frames;
    }

    /// <summary>A VML <c>x,y</c> attribute pair, or null when it is not one.</summary>
    private static (double X, double Y)? Pair(string? text)
    {
        if (text is null) return null;

        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && Number(parts[0]) is { } x && Number(parts[1]) is { } y
            ? (x, y)
            : null;
    }

    private static PageFrame? One(
        XElement shape,
        XElement element,
        int anchorOffset,
        DocxPictures? pictures,
        Func<XElement, IReadOnlyList<PageBlock>>? content)
    {
        Dictionary<string, string> style = Style(shape);

        // Floating: the page places it, not the line. It still gets drawn — see the remarks.
        if (style.TryGetValue("position", out string? position)
            && position.Equals("absolute", StringComparison.OrdinalIgnoreCase))
        {
            return Floating(shape, style, anchorOffset, pictures, content);
        }

        Length? width = style.TryGetValue("width", out string? w) ? Css(w) : null;
        Length? height = style.TryGetValue("height", out string? h) ? Css(h) : null;

        // `w:dxaOrig`/`w:dyaOrig` are the object's original size in twentieths of a point, and are what
        // a `w:object` carries when its shape's style states no box.
        width ??= Twips(element, "dxaOrig");
        height ??= Twips(element, "dyaOrig");

        if (width is not { } across || height is not { } down) return null;
        if (across <= Length.Zero || down <= Length.Zero) return null;

        XElement? box = TextBox(shape);
        FramePicture picture = box is null && pictures is not null
            ? pictures.ReadVml(shape)
            : FramePicture.None;

        return new PageFrame
        {
            Size = new DocSize(across, down),
            Anchor = FrameAnchor.AsCharacter,
            AnchorOffset = anchorOffset,
            Wrap = TextWrap.Through,
            IsImage = box is null,
            Image = picture.Raster,
            Vector = picture.Vector,
            Blocks = box is not null && content is not null ? content(box) : [],
        };
    }

    /// <summary>
    /// A floating VML shape: placed against its own origin, drawn, and reserving no line room.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves have to be kept apart. <strong>Reserving room for a floating shape is wrong</strong>
    /// — that is what added seven pages to <c>33004.docx</c> and got the first attempt reverted — but
    /// <strong>not drawing it is equally wrong</strong>, and cost seven documents their entire text.
    /// <c>TextWrap.Through</c> is what says both at once: the frame is placed and painted, and no line
    /// makes room for it.
    /// </para>
    /// <para>
    /// A shape with no size is skipped rather than drawn at nothing: VML writes <c>width:0</c> for the
    /// bare connector lines these templates use as leader rules, and a zero-area frame has no text to
    /// draw and nothing to paint.
    /// </para>
    /// </remarks>
    private static PageFrame? Floating(
        XElement shape,
        Dictionary<string, string> style,
        int anchorOffset,
        DocxPictures? pictures,
        Func<XElement, IReadOnlyList<PageBlock>>? content)
    {
        if ((style.TryGetValue("width", out string? w) ? Css(w) : null) is not { } across) return null;
        if ((style.TryGetValue("height", out string? h) ? Css(h) : null) is not { } down) return null;
        if (across <= Length.Zero || down <= Length.Zero) return null;

        XElement? box = TextBox(shape);
        FramePicture picture = box is null && pictures is not null
            ? pictures.ReadVml(shape)
            : FramePicture.None;

        Length x = (style.TryGetValue("margin-left", out string? ml) ? Css(ml) : null) ?? Length.Zero;
        Length y = (style.TryGetValue("margin-top", out string? mt) ? Css(mt) : null) ?? Length.Zero;

        return new PageFrame
        {
            Size = new DocSize(across, down),
            Anchor = FrameAnchor.Paragraph,
            AnchorOffset = anchorOffset,

            // No line makes room for a floating shape. See the remarks; this is the half of the rule
            // the reverted attempt got right.
            Wrap = TextWrap.Through,

            HorizontalOrigin = HorizontalOriginOf(style),
            HorizontalOffset = x,
            VerticalOrigin = VerticalOriginOf(style),
            VerticalOffset = y,
            IsImage = box is null,
            Image = picture.Raster,
            Vector = picture.Vector,
            Blocks = box is not null && content is not null ? content(box) : [],
            Padding = box is null ? default : default,
        };
    }

    /// <summary>The <c>w:txbxContent</c> a VML shape carries, or null when it carries none.</summary>
    private static XElement? TextBox(XElement shape)
        => shape.Descendants(Word.Name("txbxContent")).FirstOrDefault();

    /// <summary>What <c>margin-left</c> is measured from.</summary>
    /// <remarks>
    /// <c>mso-position-horizontal-relative</c>, whose default is <c>text</c> — the column the anchor
    /// sits in — and not the page. <c>char</c> is the anchor character's own position, which the column
    /// is the nearest origin this model has.
    /// </remarks>
    private static FrameHorizontalOrigin HorizontalOriginOf(Dictionary<string, string> style)
        => style.GetValueOrDefault("mso-position-horizontal-relative") switch
        {
            "page" => FrameHorizontalOrigin.Page,
            "margin" => FrameHorizontalOrigin.PageMargin,
            _ => FrameHorizontalOrigin.Column,
        };

    /// <summary>What <c>margin-top</c> is measured from.</summary>
    /// <remarks>
    /// <c>mso-position-vertical-relative</c>. Its default is <c>text</c>, which is the anchor
    /// paragraph — the origin a negative <c>margin-top</c> is measured up from, and these templates
    /// use negative ones freely.
    /// </remarks>
    private static FrameVerticalOrigin VerticalOriginOf(Dictionary<string, string> style)
        => style.GetValueOrDefault("mso-position-vertical-relative") switch
        {
            "page" => FrameVerticalOrigin.Page,
            "margin" => FrameVerticalOrigin.PageMargin,
            _ => FrameVerticalOrigin.Paragraph,
        };

    /// <summary>The declarations of a VML shape's <c>style</c> attribute, lower-cased and trimmed.</summary>
    private static Dictionary<string, string> Style(XElement shape)
    {
        Dictionary<string, string> declarations = new(StringComparer.OrdinalIgnoreCase);

        if (shape.Attribute("style")?.Value is not { } text) return declarations;

        foreach (string declaration in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = declaration.IndexOf(':');
            if (colon <= 0) continue;

            declarations[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
        }

        return declarations;
    }

    /// <summary>A CSS length in the units VML writes, or null when it is none of them.</summary>
    /// <remarks>
    /// VML's own unit set, from the same table <c>oox/source/vml/vmlformatting.cxx</c>'s
    /// <c>ConversionHelper::decodeMeasureToEmu</c> reads. A bare number is points, which is what Word
    /// writes when it writes any; <c>px</c> is 1/96 inch, the CSS reference pixel, and not a device one.
    /// </remarks>
    private static Length? Css(string text)
    {
        string value = text.Trim();
        if (value.Length == 0) return null;

        (string suffix, double perUnit)[] units =
        [
            ("pt", 12700.0),
            ("in", 914400.0),
            ("cm", 360000.0),
            ("mm", 36000.0),
            ("pc", 152400.0),
            ("pi", 152400.0),
            ("px", 914400.0 / 96.0),
            ("em", 152400.0),
            ("ex", 76200.0),
        ];

        foreach ((string suffix, double perUnit) in units)
        {
            if (!value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

            return Number(value[..^suffix.Length]) is { } scaled
                ? Length.FromEmu((long)Math.Round(scaled * perUnit))
                : null;
        }

        return Number(value) is { } points ? Length.FromPoints(points) : null;
    }

    private static double? Number(string text)
        => double.TryParse(
            text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;

    /// <summary>One of <c>w:object</c>'s original-size attributes, in twentieths of a point.</summary>
    private static Length? Twips(XElement element, string name)
        => Word.Attribute(element, name) is { } text && Word.Integer(text, out int twips) && twips > 0
            ? Length.FromTwips(twips)
            : null;
}
