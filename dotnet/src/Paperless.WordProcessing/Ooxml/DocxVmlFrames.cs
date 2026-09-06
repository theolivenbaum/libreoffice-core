using System.Globalization;
using System.Xml.Linq;
using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.Ooxml;
using Paperless.Text.Fonts;
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
    /// <param name="typeface">
    /// How to resolve a font family into a face, or null to draw no WordArt. Only a
    /// <c>v:textpath</c> needs it: its text is an attribute rather than a run, so it never reaches
    /// the layout that resolves every other face in the document.
    /// </param>
    public static List<PageFrame> ReadAll(
        XElement element,
        int anchorOffset,
        DocxPictures? pictures,
        Func<XElement, IReadOnlyList<PageBlock>>? content = null,
        Func<string?, OpenTypeFace?>? typeface = null)
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

            if (One(top, element, anchorOffset, pictures, content, typeface) is { } frame)
            {
                frames.Add(frame);
            }
        }

        return frames;
    }

    /// <summary>The <c>v:shapetype</c> a shape's <c>type="#id"</c> names, or null.</summary>
    /// <remarks>
    /// Word writes the definition once per <c>w:pict</c> and the shape refers to it, so the number
    /// that says which WordArt shape this is — <c>o:spt</c>, and the <c>136</c> in the id — lives on
    /// the sibling rather than on the shape. Searched from the whole <c>w:pict</c> because a
    /// <c>v:group</c>'s members refer to a definition written outside the group.
    /// </remarks>
    private static XElement? ShapeTypeOf(XElement shape, XElement scope)
    {
        if (shape.Attribute("type")?.Value is not { Length: > 1 } reference) return null;
        if (reference[0] != '#') return null;

        string id = reference[1..];
        foreach (XElement candidate in scope.Descendants(XName.Get("shapetype", OoxmlNamespaces.Vml)))
        {
            if (candidate.Attribute("id")?.Value == id) return candidate;
        }

        return null;
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
    /// coordinate space — nested groups included.
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
    /// <para>
    /// <strong>A nested group resolves to a rectangle by exactly that arithmetic, and is then the
    /// origin and extent its own members are measured against.</strong> This used to skip one —
    /// <c>if (member.Name.LocalName is "group") continue;</c> — and with it the whole subtree beneath
    /// it. Measured on <c>068_Work_Breakdown_Structure_Template_Green_Theme</c>, which draws its 41
    /// boxes as a root, five phase groups each holding a connector and a nested group, and 35 task
    /// boxes inside those five nested groups: we drew the root and the five phase labels and nothing
    /// deeper, 19 words against the reference's 86, and <b>the words inside the nested groups number
    /// exactly 67</b>. A blind reviewer given only the rendered page, and knowing nothing about the
    /// markup, reported the same shape from the other side — <em>"the surviving items are exactly the
    /// top two levels of the tree, in tree order, not a random scatter and not a partial-column
    /// truncation; there is no piling up and no overlapping, the failure is omission"</em> — and an
    /// earlier reviewer proposed the discriminator itself: if every rendered item is at nesting depth
    /// ≤ 1 and every missing one is ≥ 2, it is a recursion limit and not a fill problem.
    /// </para>
    /// <para>
    /// One words document in 337 holds a nested <c>v:group</c> this reader reaches. Six others —
    /// <c>056</c>, <c>057</c>, <c>025</c>, <c>030</c>, <c>008</c>, <c>071</c> — hold 19 to 40 of them
    /// each, and every one is inside an <c>mc:Fallback</c> whose <c>mc:Choice</c> DrawingML is what we
    /// read. Deleting <c>056</c>'s entire <c>mc:Fallback</c> leaves its rendering's word count
    /// unchanged, which is what establishes that rather than the markup's shape.
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

        Flatten(
            group,
            new DocRect(originX, originY, groupWidth, groupHeight),
            HorizontalOriginOf(style),
            VerticalOriginOf(style),
            LayerOf(style),
            depth: 0,
            frames,
            anchorOffset,
            pictures,
            content);

        return frames;
    }

    /// <summary>How deep a <c>v:group</c> may nest before the walk gives up.</summary>
    /// <remarks>
    /// A bound against a file that says something absurd, not a modelled limit — the corpus's deepest
    /// is two. The same number <c>DocxFrames</c> uses for the DrawingML side.
    /// </remarks>
    private const int MaxGroupNesting = 8;

    /// <summary>
    /// One group's members, resolved against the rectangle the group itself occupies.
    /// </summary>
    /// <param name="group">The <c>v:group</c> whose members are being walked.</param>
    /// <param name="area">Where that group sits and how big it is, in real units.</param>
    /// <param name="horizontal">What a member's horizontal offset is measured from.</param>
    /// <param name="vertical">What a member's vertical offset is measured from.</param>
    /// <param name="layer">Which layer the group paints on, inherited by every member.</param>
    /// <param name="depth">How many groups deep this one is.</param>
    /// <param name="frames">The frames collected so far, appended to.</param>
    /// <param name="anchorOffset">Where in the paragraph's text the drawing sits.</param>
    /// <param name="pictures">How to resolve <c>v:imagedata</c> into bytes, or null for geometry only.</param>
    /// <param name="content">How to read a <c>w:txbxContent</c> into blocks, or null to skip its text.</param>
    private static void Flatten(
        XElement group,
        DocRect area,
        FrameHorizontalOrigin horizontal,
        FrameVerticalOrigin vertical,
        VmlLayer layer,
        int depth,
        List<PageFrame> frames,
        int anchorOffset,
        DocxPictures? pictures,
        Func<XElement, IReadOnlyList<PageBlock>>? content)
    {
        if (depth > MaxGroupNesting) return;

        (double spaceX, double spaceY) = Pair(group.Attribute("coordsize")?.Value) ?? (0, 0);
        (double baseX, double baseY) = Pair(group.Attribute("coordorigin")?.Value) ?? (0, 0);
        if (spaceX <= 0 || spaceY <= 0) return;

        foreach (XElement member in group.Elements().Where(child => IsShape(child) is not null))
        {
            Dictionary<string, string> box = Style(member);
            if (Number(box.GetValueOrDefault("left", "")) is not { } left
                || Number(box.GetValueOrDefault("top", "")) is not { } top
                || Number(box.GetValueOrDefault("width", "")) is not { } wide
                || Number(box.GetValueOrDefault("height", "")) is not { } tall)
            {
                continue;
            }

            // A straight connector is how VML writes a rule, and a vertical one is
            // `width:0;height:7035`. Every other shape with no area has nothing to draw.
            bool rule = IsStraightConnector(member);
            if (wide < 0 || tall < 0) continue;
            if (!rule && (wide <= 0 || tall <= 0)) continue;

            DocRect placed = new(
                area.X + (area.Width * ((left - baseX) / spaceX)),
                area.Y + (area.Height * ((top - baseY) / spaceY)),
                area.Width * (wide / spaceX),
                area.Height * (tall / spaceY));

            if (member.Name.LocalName is "group")
            {
                Flatten(
                    member, placed, horizontal, vertical, layer, depth + 1,
                    frames, anchorOffset, pictures, content);
                continue;
            }

            XElement? text = TextBox(member);
            FramePicture picture = text is null && pictures is not null
                ? pictures.ReadVml(member)
                : FramePicture.None;

            VmlPaint paint = PaintOf(member, box);

            frames.Add(new PageFrame
            {
                Size = new DocSize(placed.Width, placed.Height),
                Anchor = FrameAnchor.Paragraph,
                AnchorOffset = anchorOffset,
                Wrap = TextWrap.Through,
                HorizontalOrigin = horizontal,
                HorizontalOffset = placed.X,
                VerticalOrigin = vertical,
                VerticalOffset = placed.Y,
                BehindText = layer.BehindText,
                ZOrder = layer.ZOrder,
                IsImage = text is null,
                Image = picture.Raster,
                Crop = picture.Crop,
                Vector = picture.Vector,
                Fill = paint.Fill,
                BorderColour = paint.Line,
                BorderWidth = paint.Width,
                IsLine = paint.IsLine,
                IsLineMirrored = paint.IsLineMirrored,
                Blocks = text is not null && content is not null ? content(text) : [],
            });
        }
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
        Func<XElement, IReadOnlyList<PageBlock>>? content,
        Func<string?, OpenTypeFace?>? typeface = null)
    {
        Dictionary<string, string> style = Style(shape);

        // Floating: the page places it, not the line. It still gets drawn — see the remarks.
        if (style.TryGetValue("position", out string? position)
            && position.Equals("absolute", StringComparison.OrdinalIgnoreCase))
        {
            return Floating(shape, style, element, anchorOffset, pictures, content, typeface);
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

        VmlPaint paint = PaintOf(shape, style);
        VmlFontwork warp = DocxVmlFontwork.Read(
            shape, ShapeTypeOf(shape, element), new DocSize(across, down), typeface);

        if (warp.Outline is not null)
        {
            (across, down) = (warp.Box.Width, warp.Box.Height);
            paint = FontworkPaint(shape, style);
        }

        return new PageFrame
        {
            Size = new DocSize(across, down),
            Anchor = FrameAnchor.AsCharacter,
            AnchorOffset = anchorOffset,
            Wrap = TextWrap.Through,
            FillOutline = warp.Outline,
            StrokeOutline = warp.Outline,
            RotationDegrees = Rotation(style),
            IsImage = box is null && warp.Outline is null,
            Image = picture.Raster,
            Crop = picture.Crop,
            Vector = picture.Vector,
            Fill = paint.Fill,
            BorderColour = paint.Line,
            BorderWidth = paint.Width,
            IsLine = paint.IsLine,
            IsLineMirrored = paint.IsLineMirrored,
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
        XElement element,
        int anchorOffset,
        DocxPictures? pictures,
        Func<XElement, IReadOnlyList<PageBlock>>? content,
        Func<string?, OpenTypeFace?>? typeface = null)
    {
        if ((style.TryGetValue("width", out string? w) ? Css(w) : null) is not { } across) return null;
        if ((style.TryGetValue("height", out string? h) ? Css(h) : null) is not { } down) return null;

        // A straight connector states one extent as zero — `width:0;height:12.75pt` is how VML
        // writes a vertical rule — and is the one shape with no area that still draws something.
        bool rule = IsStraightConnector(shape);
        if (across < Length.Zero || down < Length.Zero) return null;
        if (!rule && (across <= Length.Zero || down <= Length.Zero)) return null;

        XElement? box = TextBox(shape);
        FramePicture picture = box is null && pictures is not null
            ? pictures.ReadVml(shape)
            : FramePicture.None;

        Length x = (style.TryGetValue("margin-left", out string? ml) ? Css(ml) : null) ?? Length.Zero;
        Length y = (style.TryGetValue("margin-top", out string? mt) ? Css(mt) : null) ?? Length.Zero;

        VmlPaint paint = PaintOf(shape, style);
        VmlLayer layer = LayerOf(style);
        VmlFontwork warp = DocxVmlFontwork.Read(
            shape, ShapeTypeOf(shape, element), new DocSize(across, down), typeface);

        if (warp.Outline is not null)
        {
            (across, down) = (warp.Box.Width, warp.Box.Height);
            paint = FontworkPaint(shape, style);
        }

        return new PageFrame
        {
            Size = new DocSize(across, down),
            Anchor = FrameAnchor.Paragraph,
            AnchorOffset = anchorOffset,

            // No line makes room for a floating shape. See the remarks; this is the half of the rule
            // the reverted attempt got right.
            Wrap = TextWrap.Through,

            HorizontalOrigin = HorizontalOriginOf(style),
            HorizontalAlignment = HorizontalAlignmentOf(style),
            HorizontalOffset = x,
            VerticalOrigin = VerticalOriginOf(style),
            VerticalAlignment = VerticalAlignmentOf(style),
            VerticalOffset = y,
            BehindText = layer.BehindText,
            ZOrder = layer.ZOrder,
            FillOutline = warp.Outline,
            StrokeOutline = warp.Outline,
            RotationDegrees = Rotation(style),
            IsImage = box is null && warp.Outline is null,
            Image = picture.Raster,
            Crop = picture.Crop,
            Vector = picture.Vector,
            Fill = paint.Fill,
            BorderColour = paint.Line,
            BorderWidth = paint.Width,
            IsLine = paint.IsLine,
            IsLineMirrored = paint.IsLineMirrored,
            Blocks = box is not null && content is not null ? content(box) : [],
            Padding = box is null ? default : default,
        };
    }

    /// <summary>How a VML shape is painted: its area, its outline, and what shape that outline is.</summary>
    /// <param name="Fill">The area colour, or null to paint none.</param>
    /// <param name="Line">The outline colour, or null to stroke nothing.</param>
    /// <param name="Width">How thick the outline is.</param>
    /// <param name="IsLine">Whether the outline is the rectangle's diagonal rather than its four sides.</param>
    /// <param name="IsLineMirrored">Which diagonal, when it is one.</param>
    private readonly record struct VmlPaint(
        Colour? Fill, Colour? Line, Length Width, bool IsLine, bool IsLineMirrored)
    {
        public static readonly VmlPaint None = new(null, null, Length.Zero, false, false);
    }

    /// <summary>
    /// The fill and outline a VML shape states, or nothing when it states none this draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>We drew neither on any VML shape until this existed</strong>, which is a defect no
    /// column of the gate can see. Measured on the five Work Breakdown Structure templates before
    /// the change: <c>068</c>'s reference emits 41 <c>f*</c> fills and 36 strokes and ours emits
    /// <b>zero of each</b>, while placing all 41 labels in the right places — and a blind reviewer
    /// given only the rendered pair, knowing nothing about the markup, reported exactly that:
    /// <em>"the reference draws pale-green filled boxes with green borders around every label;
    /// ours draws nothing — bare text on white."</em>
    /// </para>
    /// <para>
    /// <strong>A theme-indexed VML colour resolves to the literal RGB beside the index, and the
    /// index is never consulted.</strong> <c>fillcolor="#e2efd9 [665]"</c> is <c>#E2EFD9</c>:
    /// <c>ConversionHelper::decodeColor</c> splits the value at its space and returns on a
    /// seven-character <c>#RRGGBB</c> (<c>oox/source/vml/vmlformatting.cxx:252-257</c>) long
    /// before the palette branch at line 282. Confirmed twice in the reference's own content
    /// stream — <c>068</c> draws 41 fills at <c>#E2EFD9</c>, <c>069</c> draws 22 at
    /// <c>#F2F2F2</c> from <c>fillcolor="#f2f2f2 [3052]"</c>.
    /// </para>
    /// <para>
    /// <strong>Nothing is defaulted.</strong> LibreOffice gives an unstated fill white and an
    /// unstated stroke black (<c>FillModel::pushToPropMap</c>, <c>StrokeModel::pushToPropMap</c>,
    /// the same file). Reproducing that here would put a white fill and a black box around all 37
    /// <c>#_x0000_t75</c> picture shapes in the words corpus, none of which states either — so
    /// this reads what the shape states and never invents ink, which is the rule
    /// <see cref="DocxFrames"/> already applies to a DrawingML <c>a:fillRef</c>.
    /// </para>
    /// <para>
    /// <strong>Only two geometries are painted here, because only two of them are the rectangle we
    /// would draw.</strong> A <c>v:rect</c> and a <c>v:roundrect</c> are; a straight connector is
    /// its box's diagonal, which is what <see cref="Layout.PageFrame.IsLine"/> already means. A
    /// <c>#_x0000_t136</c> WordArt states a <c>fillcolor</c> that fills glyph outlines and a
    /// <c>#_x0000_t15</c> a pentagon, and filling their rectangles would be a confident wrong
    /// answer; the corpus holds 15 and 3 of them.
    /// </para>
    /// <para>
    /// <strong>The WordArt half of that has been answered rather than reversed.</strong> The rule
    /// stands — a rectangle is still not painted for one — and the fifteen <c>#_x0000_t136</c>
    /// shapes now draw the thing they actually are: <see cref="DocxVmlFontwork"/> builds their glyph
    /// outlines and <see cref="FontworkPaint"/> paints <em>those</em> with the <c>fillcolor</c>. A
    /// <c>#_x0000_t15</c> pentagon is still unpainted, and still for the same reason.
    /// </para>
    /// <para>
    /// <strong><c>strokeweight</c> is honoured and its absence is a hairline</strong>, read off
    /// the 300 dpi reference raster rather than off the <c>w</c> operator — LibreOffice's export
    /// writes a single <c>0.1 w</c> for the whole page, which is not the drawn width. A
    /// <c>v:rect</c> border stating no weight comes out one device pixel; a connector stating
    /// <c>strokeweight="1pt"</c> comes out 4 px at 300 dpi, which is 0.96 pt.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A <c>v:fill/@opacity</c> as a fraction of one, or null when the element states none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three spellings and all three are in the wild: <c>26214f</c> is VML's 16.16 fixed point, so
    /// 26214/65536 = 0.4; <c>40%</c> is a percentage; <c>.4</c> is a plain fraction.
    /// <c>ConversionHelper::decodePercent</c> reads all three, and the <c>f</c> suffix is the one a
    /// reader misses — the corpus's 48 opacity attributes are every one of them in that form, so
    /// reading it as a plain number gives 26214 rather than 0.4.
    /// </para>
    /// <para>
    /// <strong>This was read by nothing at all, and it is not only a matter of how the box is
    /// painted.</strong> It is the term that decides whether the text inside the box comes out
    /// black or white — see <see cref="Layout.AutomaticColour"/>, and
    /// <c>069_Work_Breakdown_Structure_Template_Professional_Format</c>, whose
    /// <c>fillcolor="#8496b0"</c> is dark and whose <c>opacity="26214f"</c> makes it bright.
    /// </para>
    /// </remarks>
    /// <param name="value">The attribute's text, or null.</param>
    private static double? Opacity(string? value)
    {
        if (value is not { Length: > 0 }) return null;

        string text = value.Trim();
        double scale = 1.0;

        if (text.EndsWith('f'))
        {
            text = text[..^1];
            scale = 1.0 / 65536.0;
        }
        else if (text.EndsWith('%'))
        {
            text = text[..^1];
            scale = 1.0 / 100.0;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw))
            return null;

        double fraction = raw * scale;
        return double.IsFinite(fraction) ? Math.Clamp(fraction, 0.0, 1.0) : null;
    }

    private static VmlPaint PaintOf(XElement shape, Dictionary<string, string> style)
    {
        bool box = shape.Name.LocalName is "rect" or "roundrect";
        bool rule = IsStraightConnector(shape);
        if (!box && !rule) return VmlPaint.None;

        XElement? fillElement = shape.Element(XName.Get("fill", OoxmlNamespaces.Vml));
        XElement? strokeElement = shape.Element(XName.Get("stroke", OoxmlNamespaces.Vml));

        Colour? fill = box && On(shape.Attribute("filled")?.Value)
                       && On(fillElement?.Attribute("on")?.Value)
            ? VmlColour(shape.Attribute("fillcolor")?.Value ?? fillElement?.Attribute("color")?.Value)
            : null;

        if (fill is { } opaque && Opacity(fillElement?.Attribute("opacity")?.Value) is { } opacity)
        {
            fill = opaque.WithAlpha((byte)Math.Clamp(Math.Floor((opacity * 255.0) + 0.5), 0.0, 255.0));
        }

        Colour? line = On(shape.Attribute("stroked")?.Value)
                       && On(strokeElement?.Attribute("on")?.Value)
            ? VmlColour(
                shape.Attribute("strokecolor")?.Value ?? strokeElement?.Attribute("color")?.Value)
            : null;

        if (line is null) return new VmlPaint(fill, null, Length.Zero, false, false);

        Length width =
            Css(shape.Attribute("strokeweight")?.Value
                ?? strokeElement?.Attribute("weight")?.Value
                ?? string.Empty)
            ?? Hairline;

        return new VmlPaint(
            fill, line, width <= Length.Zero ? Hairline : width, rule, IsMirrored(style));
    }

    /// <summary>The thinnest line LibreOffice's PDF export writes, which is what it draws a VML
    /// outline stating no <c>strokeweight</c> as.</summary>
    private static readonly Length Hairline = Length.FromPoints(0.1);

    /// <summary>
    /// True for the straight connector VML writes a rule as.
    /// </summary>
    /// <remarks>
    /// <c>o:connectortype</c>, or a <c>type</c> naming a <c>v:shapetype</c> whose <c>o:spt</c> is
    /// 32 — the preset whose path is the box's own diagonal, <c>m,l21600,21600e</c>. This is the
    /// one shape allowed through the zero-extent check, because a vertical rule is written
    /// <c>width:0;height:12.75pt</c> and there are 87 of them in the words corpus.
    /// </remarks>
    private static bool IsStraightConnector(XElement shape)
    {
        if (shape.Name.LocalName is not "shape") return false;
        if (shape.Attribute(XName.Get("connectortype", OoxmlNamespaces.VmlOffice)) is not null) return true;

        string? type = shape.Attribute("type")?.Value?.TrimStart('#');
        if (string.IsNullOrEmpty(type)) return false;

        if (type is "_x0000_t32") return true;

        return shape.Document?.Descendants(XName.Get("shapetype", OoxmlNamespaces.Vml))
            .FirstOrDefault(candidate => candidate.Attribute("id")?.Value == type)
            ?.Attribute(XName.Get("spt", OoxmlNamespaces.VmlOffice))?.Value is "32";
    }

    /// <summary>
    /// The fill and stroke of a VML WordArt shape, which are its glyphs' own ink.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="PaintOf"/> because the rule is the opposite one. That method paints
    /// only a <c>v:rect</c> and a <c>v:roundrect</c> and defaults nothing, so that a picture shape
    /// stating neither is not given a white box and a black border it never asked for. A Fontwork
    /// has no box: the fill and the stroke <em>are</em> the letters, and LibreOffice's own defaults
    /// therefore apply — <c>FillModel::pushToPropMap</c> gives an unstated fill white and
    /// <c>StrokeModel::pushToPropMap</c> gives an unstated stroke black
    /// (<c>oox/source/vml/vmlformatting.cxx</c>).
    /// </para>
    /// <para>
    /// Nothing in the corpus measures those defaults: all 15 <c>#_x0000_t136</c> shapes state
    /// <c>fillcolor</c> and <c>stroked="f"</c> explicitly, and 14 of them state
    /// <c>&lt;v:fill opacity=".5"/&gt;</c> beside it.
    /// </para>
    /// </remarks>
    private static VmlPaint FontworkPaint(XElement shape, Dictionary<string, string> style)
    {
        XElement? fillElement = shape.Element(XName.Get("fill", OoxmlNamespaces.Vml));
        XElement? strokeElement = shape.Element(XName.Get("stroke", OoxmlNamespaces.Vml));

        Colour? fill = On(shape.Attribute("filled")?.Value)
                       && On(fillElement?.Attribute("on")?.Value)
            ? VmlColour(shape.Attribute("fillcolor")?.Value ?? fillElement?.Attribute("color")?.Value)
              ?? Colour.FromRgb(0xFFFFFF)
            : null;

        if (fill is { } opaque && Opacity(fillElement?.Attribute("opacity")?.Value) is { } opacity)
        {
            fill = opaque.WithAlpha((byte)Math.Clamp(Math.Floor((opacity * 255.0) + 0.5), 0.0, 255.0));
        }

        Colour? line = On(shape.Attribute("stroked")?.Value)
                       && On(strokeElement?.Attribute("on")?.Value)
            ? VmlColour(shape.Attribute("strokecolor")?.Value ?? strokeElement?.Attribute("color")?.Value)
              ?? Colour.FromRgb(0x000000)
            : null;

        if (line is null) return new VmlPaint(fill, null, Length.Zero, false, false);

        Length width =
            Css(shape.Attribute("strokeweight")?.Value
                ?? strokeElement?.Attribute("weight")?.Value
                ?? string.Empty)
            ?? Hairline;

        return new VmlPaint(fill, line, width <= Length.Zero ? Hairline : width, false, IsMirrored(style));
    }

    /// <summary>
    /// How far a floating VML shape is turned about its own centre, clockwise, in degrees.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ConversionHelper::decodeRotation</c> (<c>oox/source/vml/vmlformatting.cxx</c>): a bare
    /// number is degrees and an <c>fd</c> suffix is 1/65536 of one, which is how Word writes an
    /// angle a user dragged rather than typed.
    /// </para>
    /// <para>
    /// <strong>Read for a WordArt shape and for nothing else, deliberately.</strong> The corpus's
    /// words track states <c>rotation</c> on <b>347</b> VML shapes across <b>34</b> documents —
    /// genogram connectors, unit-circle labels, storyboard arrows — and turning all of them is a
    /// change with its own reach and its own regression risk. Fifteen of the 347 are the
    /// <c>#_x0000_t136</c> watermarks, every one of them <c>rotation:315</c>, and a watermark drawn
    /// flat instead of diagonally is not the shape the reference draws at all. So the rest wait for
    /// a round that measures them.
    /// </para>
    /// </remarks>
    private static double Rotation(Dictionary<string, string> style)
    {
        if (!style.TryGetValue("rotation", out string? stated)) return 0;

        string text = stated.Trim();
        double scale = 1.0;

        if (text.EndsWith("fd", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^2];
            scale = 1.0 / 65536.0;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double degrees)
            ? degrees * scale
            : 0;
    }

    /// <summary>Where <c>mso-position-horizontal</c> puts the shape, or an offset when it says nothing.</summary>
    /// <remarks>
    /// <c>lcl_SetAnchorType</c>, <c>oox/source/vml/vmlshape.cxx:661-680</c>. <c>inside</c> and
    /// <c>outside</c> also set a page toggle, which this does not carry; they appear on no words
    /// document.
    /// </remarks>
    private static FrameHorizontalAlignment HorizontalAlignmentOf(Dictionary<string, string> style)
        => style.GetValueOrDefault("mso-position-horizontal") switch
        {
            "center" => FrameHorizontalAlignment.Centre,
            "left" => FrameHorizontalAlignment.Left,
            "right" => FrameHorizontalAlignment.Right,
            "inside" => FrameHorizontalAlignment.Left,
            "outside" => FrameHorizontalAlignment.Right,
            _ => FrameHorizontalAlignment.Offset,
        };

    /// <summary>Where <c>mso-position-vertical</c> puts the shape.</summary>
    /// <remarks><c>oox/source/vml/vmlshape.cxx:700-710</c>.</remarks>
    private static FrameVerticalAlignment VerticalAlignmentOf(Dictionary<string, string> style)
        => style.GetValueOrDefault("mso-position-vertical") switch
        {
            "center" => FrameVerticalAlignment.Middle,
            "top" or "inside" => FrameVerticalAlignment.Top,
            "bottom" or "outside" => FrameVerticalAlignment.Bottom,
            _ => FrameVerticalAlignment.Offset,
        };

    /// <summary>Which diagonal a connector runs along.</summary>
    /// <remarks>
    /// The preset's path runs from the box's top-left to its bottom-right. <c>flip:x</c> and
    /// <c>flip:y</c> each swap that for the other diagonal, and stating both swaps it back.
    /// </remarks>
    private static bool IsMirrored(Dictionary<string, string> style)
    {
        if (!style.TryGetValue("flip", out string? flip)) return false;

        bool x = flip.Contains('x', StringComparison.OrdinalIgnoreCase);
        bool y = flip.Contains('y', StringComparison.OrdinalIgnoreCase);
        return x ^ y;
    }

    /// <summary>A VML boolean, whose absence means on.</summary>
    /// <remarks><c>filled="f"</c> and <c>stroked="f"</c> are how a shape says no.</remarks>
    private static bool On(string? value)
        => value is null
           || !(value.Equals("f", StringComparison.OrdinalIgnoreCase)
                || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value == "0");

    /// <summary>
    /// A VML colour value, or null when it names nothing this understands.
    /// </summary>
    /// <remarks>
    /// The order is <c>ConversionHelper::decodeColor</c>'s: split the value at its first space so
    /// that a trailing <c>[3209]</c> palette index is set aside, then <c>#RRGGBB</c>, then the
    /// three-digit <c>#RGB</c>, then a preset name. A bare palette index with no colour beside it
    /// is not resolved — that would need the theme's palette, and no words document writes one.
    /// An unrecognised name yields null rather than black, so an unknown value draws nothing
    /// instead of drawing the wrong thing.
    /// </remarks>
    private static Colour? VmlColour(string? value)
    {
        if (value is null) return null;

        string text = value.Trim();
        int space = text.IndexOf(' ');
        if (space > 0) text = text[..space];
        if (text.Length == 0) return null;

        if (text[0] == '#')
        {
            ReadOnlySpan<char> digits = text.AsSpan(1);
            if (digits.Length == 6
                && uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                 out uint rgb))
            {
                return Colour.FromRgb(rgb);
            }

            if (digits.Length == 3
                && uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                 out uint packed))
            {
                uint r = (packed >> 8) & 0xF;
                uint g = (packed >> 4) & 0xF;
                uint b = packed & 0xF;
                return Colour.FromRgb((r * 0x11 << 16) | (g * 0x11 << 8) | (b * 0x11));
            }

            return null;
        }

        return Presets.TryGetValue(text, out uint preset) ? Colour.FromRgb(preset) : null;
    }

    /// <summary>
    /// The VML preset colour names, from <c>Color::getVmlPresetColor</c>'s table.
    /// </summary>
    /// <remarks>
    /// The sixteen HTML 4 names plus the four VML spells differently. Only <c>black</c>,
    /// <c>white</c> and <c>red</c> appear in this corpus — 138, 55 and 78 times — and the rest are
    /// here because the table is small and a missing name draws nothing at all, which is the one
    /// failure mode that is invisible.
    /// </remarks>
    private static readonly Dictionary<string, uint> Presets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = 0x000000, ["silver"] = 0xC0C0C0, ["gray"] = 0x808080, ["grey"] = 0x808080,
            ["white"] = 0xFFFFFF, ["maroon"] = 0x800000, ["red"] = 0xFF0000, ["purple"] = 0x800080,
            ["fuchsia"] = 0xFF00FF, ["green"] = 0x008000, ["lime"] = 0x00FF00, ["olive"] = 0x808000,
            ["yellow"] = 0xFFFF00, ["navy"] = 0x000080, ["blue"] = 0x0000FF, ["teal"] = 0x008080,
            ["aqua"] = 0x00FFFF, ["cyan"] = 0x00FFFF, ["magenta"] = 0xFF00FF, ["orange"] = 0xFFA500,
        };

    /// <summary>The <c>w:txbxContent</c> a VML shape carries, or null when it carries none.</summary>
    private static XElement? TextBox(XElement shape)
        => shape.Descendants(Word.Name("txbxContent")).FirstOrDefault();

    /// <summary>Which layer a floating VML shape paints on, and where in that layer's stack.</summary>
    /// <param name="BehindText">True for the hell layer, painted before the text.</param>
    /// <param name="ZOrder">Where in the layer it sits, low to high.</param>
    private readonly record struct VmlLayer(bool BehindText, long ZOrder);

    /// <summary>
    /// The layer and stacking position a VML shape's <c>z-index</c> asks for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The sign is the layer and the magnitude is the order, and nothing read either.</strong>
    /// <c>oox/source/vml/vmlshapecontext.cxx:536</c> keeps the declaration verbatim and
    /// <c>vmlshape.cxx:408</c> hands it to Writer as <c>VML-Z-ORDER</c>; the mapper then sets the
    /// shape opaque exactly when it is not negative —
    /// <c>xShapePropertySet-&gt;setPropertyValue(PROP_OPAQUE, uno::Any(zOrder &gt;= 0))</c>,
    /// <c>sw/source/writerfilter/dmapper/DomainMapper_Impl.cxx:5157</c> and <c>:5203</c> — so a
    /// negative <c>z-index</c> is the hell layer and that is how Word writes a watermark.
    /// </para>
    /// <para>
    /// <strong>A shape that declares a <c>z-index</c> outranks every <c>relativeHeight</c>, whatever
    /// the two numbers are.</strong> <c>GraphicZOrderHelper::adjustRelativeHeight</c>
    /// (<c>sw/source/writerfilter/dmapper/GraphicHelpers.cxx:279-330</c>) says it in as many words —
    /// "in general, all z-index-defined shapes appear on top of relativeHeight graphics regardless of
    /// the value" — and implements it by pushing every DrawingML anchor below zero
    /// (<c>GraphicImport.cxx:695</c>) while leaving a <c>z-index</c> alone. The two ranges are
    /// separated here the other way about, by lifting a <c>z-index</c> clear of the whole unsigned
    /// 32-bit range that <c>relativeHeight</c> occupies, which leaves every stored DrawingML order
    /// exactly as it was.
    /// </para>
    /// <para>
    /// A shape that declares no <c>z-index</c> keeps document order: zero, in front of the text, which
    /// is what every VML shape did before this existed.
    /// </para>
    /// </remarks>
    private static VmlLayer LayerOf(Dictionary<string, string> style)
    {
        if (!style.TryGetValue("z-index", out string? text)) return default;

        if (!long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                           out long z))
        {
            return default;
        }

        return new VmlLayer(z < 0, VmlZOrderBase + z);
    }

    /// <summary>
    /// What a VML <c>z-index</c> is lifted by so that it clears every DrawingML <c>relativeHeight</c>.
    /// </summary>
    /// <remarks>
    /// 2^32, one past the top of <c>ST_RelativeHeight</c>'s unsigned 32-bit range. A negative
    /// <c>z-index</c> still lands above the range — the corpus's are around −251 million — which is
    /// right: it shares the hell layer with the <c>behindDoc</c> anchors and LibreOffice puts it above
    /// them there too (<c>GraphicHelpers.cxx:305-315</c> pushes a behind-text <c>relativeHeight</c>
    /// down a further level and leaves a negative <c>z-index</c> where it is).
    /// </remarks>
    private const long VmlZOrderBase = 4294967296L;

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
