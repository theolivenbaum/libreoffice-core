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
    /// <summary>
    /// The frame a <c>w:pict</c> or <c>w:object</c> reserves, or null when it reserves nothing.
    /// </summary>
    /// <param name="element">The <c>w:pict</c> or <c>w:object</c>.</param>
    /// <param name="anchorOffset">Where in the paragraph's text it sits.</param>
    /// <param name="pictures">How to resolve <c>v:imagedata</c> into bytes, or null for geometry only.</param>
    public static PageFrame? Read(XElement element, int anchorOffset, DocxPictures? pictures)
    {
        ArgumentNullException.ThrowIfNull(element);

        XElement? shape = element
            .Descendants(XName.Get("shape", OoxmlNamespaces.Vml))
            .FirstOrDefault();


        if (shape is null) return null;

        Dictionary<string, string> style = Style(shape);

        // Floating: the page places it, not the line. See the remarks.
        if (style.TryGetValue("position", out string? position)
            && position.Equals("absolute", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Length? width = style.TryGetValue("width", out string? w) ? Css(w) : null;
        Length? height = style.TryGetValue("height", out string? h) ? Css(h) : null;

        // `w:dxaOrig`/`w:dyaOrig` are the object's original size in twentieths of a point, and are what
        // a `w:object` carries when its shape's style states no box.
        width ??= Twips(element, "dxaOrig");
        height ??= Twips(element, "dyaOrig");

        if (width is not { } across || height is not { } down) return null;
        if (across <= Length.Zero || down <= Length.Zero) return null;

        FramePicture picture = pictures?.ReadVml(shape) ?? FramePicture.None;


        return new PageFrame
        {
            Size = new DocSize(across, down),
            Anchor = FrameAnchor.AsCharacter,
            AnchorOffset = anchorOffset,
            Wrap = TextWrap.Through,
            IsImage = true,
            Image = picture.Raster,
            Vector = picture.Vector,
        };
    }

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
