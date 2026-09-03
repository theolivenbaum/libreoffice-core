using System.Globalization;
using System.Xml.Linq;
using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.Ooxml.DrawingML;
using Paperless.WordProcessing.Layout;

namespace Paperless.WordProcessing.Ooxml;

/// <summary>
/// Reads a <c>w:drawing</c> — OOXML's floating frame — into the layout engine's own model.
/// </summary>
/// <remarks>
/// <para>
/// A drawing is either a <c>wp:inline</c>, which is a frame set in the text like a very large character,
/// or a <c>wp:anchor</c>, which floats. Only the anchored one has a position and a wrap, and it states
/// both in the same shape as ODF does but with a different vocabulary: <c>wp:positionH</c> pairs a
/// <c>relativeFrom</c> with either a <c>wp:posOffset</c> in EMUs or a <c>wp:align</c> naming an edge.
/// </para>
/// <para>
/// The wrap is an <em>element</em> rather than an attribute, which is the first thing to get right:
/// <c>wp:wrapNone</c>, <c>wp:wrapSquare</c>, <c>wp:wrapTight</c>, <c>wp:wrapThrough</c> and
/// <c>wp:wrapTopAndBottom</c> are five siblings and exactly one appears. And the names lie in the same
/// direction ODF's do, in the opposite place: <c>wp:wrapNone</c> means the text runs <em>through</em> the
/// frame, while <c>wp:wrapTopAndBottom</c> is what ODF calls <c>none</c>. Only <c>wp:wrapSquare</c>
/// carries the side, in <c>wrapText</c>.
/// </para>
/// <para>
/// <c>wp:wrapTight</c> and <c>wp:wrapThrough</c> ask for a contour wrap, which is a later item. They are
/// read as the square wrap their <c>wrapText</c> names, which is the same hole with straight sides — a
/// visible approximation rather than a wrong one, and much closer than ignoring the frame.
/// </para>
/// </remarks>
internal static class DocxFrames
{
    /// <summary>
    /// Reads the frame a <c>w:drawing</c> holds, or null when it holds nothing placeable.
    /// </summary>
    /// <param name="drawing">The <c>w:drawing</c> element.</param>
    /// <param name="content">How to read a text frame's own paragraphs, or null to skip them.</param>
    /// <param name="anchorOffset">Where in the paragraph's text the drawing sits.</param>
    /// <param name="pictures">
    /// How to resolve an <c>a:blip</c>'s <c>r:embed</c> into bytes, or null to record the frame's
    /// geometry without them — which is all the wrap ever needed.
    /// </param>
    public static PageFrame? Read(
        XElement drawing,
        Func<XElement, IReadOnlyList<PageBlock>>? content,
        int anchorOffset,
        DocxPictures? pictures = null)
        => ReadAll(drawing, content, anchorOffset, pictures) is [PageFrame first, ..] ? first : null;

    /// <summary>
    /// Reads every frame a <c>w:drawing</c> holds: one, or one per member of a shape group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A <c>wpg:wgp</c> is many shapes in one drawing, and each of them can hold text.</strong>
    /// A letterhead written in Word is routinely a group of a dozen text boxes and a logo, and reading
    /// only the first text box in the drawing — the first <c>txbxContent</c> under the anchor — draws one
    /// of them and silently loses the rest. Measured on
    /// <c>Press release_EUREKA labels ITEA 3 Cluster.docx</c>: nineteen shapes, eighteen of them text
    /// boxes, of which one drew.
    /// </para>
    /// <para>
    /// LibreOffice imports a group as a <c>SdrObjGroup</c> and keeps the nesting
    /// (<c>oox/source/drawingml/shapegroupcontext.cxx</c>); this flattens it instead, because the layout
    /// engine places one rectangle per frame and a member's rectangle is fully determined once the
    /// group's is. The flattening is what <see cref="PageFrame.GroupSize"/> and
    /// <see cref="PageFrame.GroupOffset"/> carry.
    /// </para>
    /// <para>
    /// The first frame returned is always the group's own envelope, which keeps the anchor's wrap so the
    /// hole in the text is the group's rather than one per member.
    /// </para>
    /// </remarks>
    /// <param name="drawing">The <c>w:drawing</c> element.</param>
    /// <param name="content">How to read a text frame's own paragraphs, or null to skip them.</param>
    /// <param name="anchorOffset">Where in the paragraph's text the drawing sits.</param>
    /// <param name="pictures">How to resolve an <c>a:blip</c>'s <c>r:embed</c> into bytes, or null.</param>
    /// <param name="context">
    /// What the drawing's surroundings decide about it — the theme its colours resolve against, whether
    /// it sits in a header or a footer, and the file's compatibility mode. Default for a caller that has
    /// none of it, which costs a scheme-coloured fill and the header rule and nothing else.
    /// </param>
    public static IReadOnlyList<PageFrame> ReadAll(
        XElement drawing,
        Func<XElement, IReadOnlyList<PageBlock>>? content,
        int anchorOffset,
        DocxPictures? pictures = null,
        DocxFrameContext context = default)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        XElement? anchor = Child(drawing, "anchor");
        XElement? inline = anchor is null ? Child(drawing, "inline") : null;
        XElement? placed = anchor ?? inline;
        if (placed is null) return [];

        XElement? extent = Child(placed, "extent");
        if (extent is null) return [];

        Length width = Emu(extent.Attribute("cx")?.Value);
        Length height = Emu(extent.Attribute("cy")?.Value);
        if (width <= Length.Zero || height <= Length.Zero) return [];

        if (Group(placed) is { } group)
        {
            return Members(group, placed, anchor, new DocSize(width, height), content, anchorOffset,
                           pictures, context);
        }

        PageFrame? single = One(placed, anchor, new DocSize(width, height), content, anchorOffset,
                                pictures, context);
        return single is null ? [] : [single];
    }

    /// <summary>
    /// Whether a <c>w:drawing</c> floats — a <c>wp:anchor</c> rather than a <c>wp:inline</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked by the reader that builds the paragraph, not by this one, and it is the difference between
    /// a run that takes room on its line and a run that takes none. A <c>wp:inline</c> is laid out as a
    /// character; a <c>wp:anchor</c> becomes a fly, and Writer's own import leaves the paragraph it was
    /// written in **empty** — which is why an anchor character standing for one must not make the
    /// paragraph count as having text.
    /// </para>
    /// <para>
    /// Measured on <c>088_Printable_Graph_Paper_Template_Quality_layout</c>, whose last paragraph is a
    /// 2 pt mark holding one anchored logo 8.45 pt above the bottom margin. Read as text-bearing it takes
    /// the 11 pt body size, overflows, and costs the document a whole second page; read as empty it takes
    /// the mark's 2 pt and fits, which is what 26.2.4.2 does. Eleven authored variants of that document
    /// are in <c>dotnet/probes/words-r50-chartset/</c>: deleting the drawing run fixes it and no property
    /// of the frame — offset, extent, <c>behindDoc</c>, wrap mode or anchor origin — changes anything.
    /// </para>
    /// </remarks>
    /// <param name="drawing">A <c>w:drawing</c> element.</param>
    /// <returns>True when it carries a <c>wp:anchor</c>.</returns>
    public static bool IsFloating(XElement drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        return Child(drawing, "anchor") is not null;
    }

    /// <summary>
    /// Whether an anchored drawing belongs on the layer Writer paints before the text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>m_bOpaque</c> in <c>sw/source/writerfilter/dmapper/GraphicImport.cxx</c>, reproduced in the
    /// order that file assigns it, because the order is the rule:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///   it starts as <c>!IsInHeaderFooter()</c> (:342), so <strong>every drawing in a header or a
    ///   footer is behind the text</strong> whether or not it says so;
    ///   </description></item>
    ///   <item><description><c>behindDoc="1"</c> clears it (:698-702);</description></item>
    ///   <item><description>
    ///   and for <c>wrapSquare</c>, <c>wrapThrough</c>, <c>wrapTight</c> and <c>wrapTopAndBottom</c>, a
    ///   file whose <c>compatibilityMode</c> is 15 or more puts it back (:1589, :1697) — tdf#137850,
    ///   "Word >= 2013 seems to ignore bBehindDoc except for wrapNone, but older versions honour it".
    ///   </description></item>
    /// </list>
    /// <para>
    /// The resulting item is <c>SvxOpaqueItem</c>, and false is the hell layer.
    /// </para>
    /// <para>
    /// Only an anchored drawing is asked. A <c>wp:inline</c> takes room on its line rather than floating
    /// over anything, so its layer decides nothing that is visible; LibreOffice does still push it to the
    /// bottom of the z-order (:242-246), and following that here would move as-character pictures in
    /// every header in the corpus to buy nothing measurable.
    /// </para>
    /// </remarks>
    private static bool BehindText(XElement? anchor, DocxFrameContext context)
    {
        if (anchor is null) return false;

        bool opaque = !context.InHeaderFooter;

        if (anchor.Attribute("behindDoc")?.Value is not ("1" or "true" or "on")) return !opaque;

        opaque = false;
        if (context.CompatibilityMode >= 15 && WrapsAside(anchor)) opaque = !context.InHeaderFooter;

        return !opaque;
    }

    /// <summary>The anchor's declared place in the z order, or zero when it declares none.</summary>
    /// <remarks>
    /// <c>relativeHeight</c> is a <c>ST_RelativeHeight</c>, an unsigned 32-bit value, and real files
    /// use the top of that range — the corpus templates sit around 251 660 000, which overflows a
    /// signed <c>int</c> only above 2^31 but is parsed as <c>uint</c> here so that the whole declared
    /// range round-trips rather than most of it. An unparseable or absent value is zero, which sorts
    /// below every anchor that declares one and leaves document order untouched.
    /// </remarks>
    private static uint ZOrder(XElement? anchor) =>
        anchor?.Attribute("relativeHeight")?.Value is { } text
        && uint.TryParse(text, System.Globalization.NumberStyles.Integer,
                         System.Globalization.CultureInfo.InvariantCulture, out uint z)
            ? z
            : 0u;

    /// <summary>Whether the anchor asks for one of the four wraps that leave a hole in the text.</summary>
    private static bool WrapsAside(XElement anchor)
    {
        foreach (XElement child in anchor.Elements())
        {
            if (child.Name.LocalName
                is "wrapSquare" or "wrapThrough" or "wrapTight" or "wrapTopAndBottom")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The one frame an ordinary drawing holds.</summary>
    private static PageFrame? One(
        XElement placed,
        XElement? anchor,
        DocSize size,
        Func<XElement, IReadOnlyList<PageBlock>>? content,
        int anchorOffset,
        DocxPictures? pictures,
        DocxFrameContext context)
    {
        Length width = size.Width;
        Length height = size.Height;

        XElement? box = Descendant(placed, "txbxContent");
        FramePicture picture = box is null && pictures is not null ? pictures.Read(placed) : FramePicture.None;

        // A chart is a graphic frame rather than a picture, so it names its part through a different
        // relationship and is asked for separately. Only where the drawing holds no text box, which is
        // the one thing it can be that is neither.
        DocxChart chart = box is null && pictures is not null ? pictures.Chart(placed) : default;

        (Length x, FrameHorizontalOrigin horigin, FrameHorizontalAlignment halign) = Horizontal(anchor);
        (Length y, FrameVerticalOrigin vorigin, FrameVerticalAlignment valign) = Vertical(anchor);
        XElement? shapeProperties = ShapeProperties(placed);
        (Colour? fill, Colour? line, Length lineWidth) = Appearance(shapeProperties, context.Theme);
        (bool isLine, bool isLineMirrored) = LineGeometry(shapeProperties);
        (string? preset, IReadOnlyDictionary<string, double>? adjustments) =
            PresetGeometry(shapeProperties);

        return new PageFrame
        {
            Size = new DocSize(width, height),
            Fill = fill,
            BorderColour = line,
            BorderWidth = lineWidth,
            BehindText = BehindText(anchor, context),
            ZOrder = ZOrder(anchor),
            Preset = preset,
            Adjustments = adjustments,
            IsLine = isLine,
            IsLineMirrored = isLineMirrored,
            Anchor = anchor is null ? FrameAnchor.AsCharacter : FrameAnchor.Paragraph,
            AnchorOffset = anchorOffset,
            Wrap = anchor is null ? TextWrap.Through : WrapOf(anchor),
            HorizontalOrigin = horigin,
            HorizontalAlignment = halign,
            HorizontalOffset = x,
            VerticalOrigin = vorigin,
            VerticalAlignment = valign,
            VerticalOffset = y,
            Spacing = Spacing(placed),
            IsImage = box is null && chart.Plot is null,
            Image = picture.Raster,
            Crop = picture.Crop,
            Vector = picture.Vector,
            Chart = chart.Plot,
            ChartFontFamily = chart.Family,
            Name = Child(placed, "docPr")?.Attribute("name")?.Value,
            Blocks = box is not null && content is not null ? content(box) : [],
            Padding = box is null ? default : Insets(placed),
            HasFixedHeight = box is not null && !GrowsWithText(placed),
        };
    }

    /// <summary>The <c>wpg:wgp</c> a drawing's graphic data holds, or null when it holds something else.</summary>
    /// <remarks>
    /// <c>wpg:wpc</c> — a drawing <em>canvas</em> — is the same shape with a different name and is taken
    /// too: Word writes one whenever a user draws several shapes on a canvas rather than grouping them,
    /// and the members are laid out by the same transform.
    /// </remarks>
    private static XElement? Group(XElement placed)
    {
        XElement? data = Child(Child(placed, "graphic") ?? placed, "graphicData");
        if (data is null) return null;

        foreach (XElement child in data.Elements())
        {
            if (child.Name.LocalName is "wgp" or "wpc") return child;
        }

        return null;
    }

    /// <summary>
    /// A group flattened into its envelope and one frame per leaf shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transform is the one every DrawingML group carries: a child stated at <c>a:off</c> in the
    /// group's own child coordinate space — the space <c>a:chOff</c> and <c>a:chExt</c> describe — maps
    /// to <c>(off − chOff) × ext ÷ chExt</c> inside the group's rectangle. Nested groups compose, which
    /// is why this recurses with the transform rather than with the element.
    /// </para>
    /// <para>
    /// A group with no <c>a:chExt</c> — which a canvas usually has — is read as one-to-one, since the
    /// child coordinates are then the group's own.
    /// </para>
    /// </remarks>
    private static List<PageFrame> Members(
        XElement group,
        XElement placed,
        XElement? anchor,
        DocSize size,
        Func<XElement, IReadOnlyList<PageBlock>>? content,
        int anchorOffset,
        DocxPictures? pictures,
        DocxFrameContext context)
    {
        (Length x, FrameHorizontalOrigin horigin, FrameHorizontalAlignment halign) = Horizontal(anchor);
        (Length y, FrameVerticalOrigin vorigin, FrameVerticalAlignment valign) = Vertical(anchor);

        // The envelope of a group paints nothing of its own — an `SdrObjGroup` has no fill and no line —
        // so it takes no appearance here, only the paint order its members inherit.
        PageFrame envelope = new()
        {
            Size = size,
            BehindText = BehindText(anchor, context),
            ZOrder = ZOrder(anchor),
            Anchor = anchor is null ? FrameAnchor.AsCharacter : FrameAnchor.Paragraph,
            AnchorOffset = anchorOffset,
            Wrap = anchor is null ? TextWrap.Through : WrapOf(anchor),
            HorizontalOrigin = horigin,
            HorizontalAlignment = halign,
            HorizontalOffset = x,
            VerticalOrigin = vorigin,
            VerticalAlignment = valign,
            VerticalOffset = y,
            Spacing = Spacing(placed),
            IsImage = false,
            Name = Child(placed, "docPr")?.Attribute("name")?.Value,
        };

        List<PageFrame> frames = [envelope];

        Walk(group, TransformOf(group, size), 0);
        return frames;

        void Walk(XElement container, GroupTransform transform, int depth)
        {
            // Real files nest a group inside a group and stop; the bound is against a file that says
            // otherwise, since the walk is the only thing keeping it finite.
            if (depth > MaxGroupNesting) return;

            foreach (XElement child in container.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "grpSp" or "wgp" or "wpc":
                        Walk(child, transform.Around(child, TransformOf(child, size)), depth + 1);
                        break;

                    case "wsp" or "pic" or "sp":
                    {
                        if (Leaf(child, transform, envelope, size, content, anchorOffset, pictures,
                                 context)
                            is { } leaf)
                        {
                            frames.Add(leaf);
                        }

                        break;
                    }

                    default:
                        continue;
                }
            }
        }
    }

    /// <summary>How deep a group may nest before the walk gives up.</summary>
    private const int MaxGroupNesting = 8;

    /// <summary>One leaf shape of a group, placed inside the group's rectangle.</summary>
    /// <remarks>
    /// A shape with no <c>a:xfrm</c> of its own has no rectangle to be placed at and is skipped rather
    /// than drawn at the group's origin, where it would sit on top of the member that is really there.
    /// </remarks>
    private static PageFrame? Leaf(
        XElement shape,
        GroupTransform transform,
        PageFrame envelope,
        DocSize size,
        Func<XElement, IReadOnlyList<PageBlock>>? content,
        int anchorOffset,
        DocxPictures? pictures,
        DocxFrameContext context)
    {
        XElement? properties = shape.Elements()
            .FirstOrDefault(child => child.Name.LocalName is "spPr");
        XElement? transformation = properties is null ? null : Child(properties, "xfrm");
        if (transformation is null) return null;

        XElement? offset = Child(transformation, "off");
        XElement? extent = Child(transformation, "ext");
        if (offset is null || extent is null) return null;

        DocRect within = transform.Map(
            Raw(offset, "x"), Raw(offset, "y"), Raw(extent, "cx"), Raw(extent, "cy"));

        if (within.Width <= Length.Zero || within.Height <= Length.Zero) return null;

        XElement? box = Descendant(shape, "txbxContent");
        FramePicture picture = box is null && pictures is not null
            ? pictures.Read(shape)
            : FramePicture.None;

        (Colour? fill, Colour? line, Length lineWidth) = Appearance(properties, context.Theme);
        (bool isLine, bool isLineMirrored) = LineGeometry(properties);

        return envelope with
        {
            Size = new DocSize(within.Width, within.Height),
            Fill = fill,
            BorderColour = line,
            BorderWidth = lineWidth,
            GroupSize = size,
            GroupOffset = new DocPoint(within.X, within.Y),
            IsLine = isLine,
            IsLineMirrored = isLineMirrored,

            // The envelope keeps the anchor's wrap; a member must not punch a hole of its own, or a
            // nineteen-shape letterhead would narrow the text nineteen times over.
            Wrap = TextWrap.Through,
            Spacing = default,
            IsImage = box is null,
            Image = picture.Raster,
            Crop = picture.Crop,
            Vector = picture.Vector,
            Chart = null,
            ChartFontFamily = null,
            AnchorOffset = anchorOffset,
            Name = Descendant(shape, "cNvPr")?.Attribute("name")?.Value,
            Blocks = box is not null && content is not null ? content(box) : [],
            Padding = box is null ? default : Insets(shape),
            HasFixedHeight = box is not null && !GrowsWithText(shape),
        };
    }

    /// <summary>
    /// A group's child-coordinate to group-rectangle mapping.
    /// </summary>
    /// <param name="OriginX">The child space's origin, <c>a:chOff/@x</c>.</param>
    /// <param name="OriginY">The child space's origin, <c>a:chOff/@y</c>.</param>
    /// <param name="ScaleX">Group width divided by <c>a:chExt/@cx</c>.</param>
    /// <param name="ScaleY">Group height divided by <c>a:chExt/@cy</c>.</param>
    /// <param name="ShiftX">Where the mapped rectangle starts inside the group, in EMUs.</param>
    /// <param name="ShiftY">The same, vertically.</param>
    private readonly record struct GroupTransform(
        double OriginX, double OriginY, double ScaleX, double ScaleY, double ShiftX, double ShiftY)
    {
        /// <summary>The identity, for a group that states no child space of its own.</summary>
        public static GroupTransform Identity => new(0, 0, 1, 1, 0, 0);

        /// <summary>
        /// A nested group's transform, composed inside this one — <em>including where the nested group
        /// itself sits</em>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This used to be <c>Composed(inner)</c>, which added <c>inner.ShiftX</c> — and
        /// <see cref="TransformOf"/> never sets a shift, so it added nought. A nested
        /// <c>a:grpSpPr/a:xfrm/a:off</c>, which is where the nested group sits inside its parent, was
        /// therefore **dropped**, and every nested group's members were laid out as though the group
        /// were at the parent's own origin. The scale composed correctly throughout, so the members
        /// came out the right size in the wrong place, which is the hardest kind of this defect to see.
        /// </para>
        /// <para>
        /// Measured on <c>056_Organogram_Template_Square_Theme</c>, whose <c>wpg:wgp</c> holds five
        /// text-bearing <c>a:grpSp</c> at <c>a:off/@x</c> 141890, 1623848, 3200400, 4761186 and
        /// 6258911 EMU. Every one has <c>chOff="0,0"</c> and a <c>chExt</c> equal to its <c>ext</c>, so
        /// all five resolved to the same rectangle: their twenty <c>Text here</c> boxes landed on top of
        /// one another at the drawing's own left edge, and the PDF's text layer holds **four** of the
        /// twenty. 26.2.4.2 draws them as a 5 × 5 lattice. A blind reviewer given only the image, and
        /// told nothing, reported *"the surviving leaves are piled into the left edge of the page, a
        /// single vertical stack … the remaining 20 leaves are absent as boxes"*.
        /// </para>
        /// <para>
        /// The composition is the ordinary one. The nested group's own <c>off</c> is a point in
        /// <em>this</em> group's child space, so it maps through this transform exactly as a leaf's
        /// does; the mapped point is where the nested group's own child space starts.
        /// </para>
        /// </remarks>
        /// <param name="group">The nested group, for its own <c>a:off</c>.</param>
        /// <param name="inner">The nested group's own child-space transform.</param>
        public GroupTransform Around(XElement group, GroupTransform inner)
        {
            XElement? properties = group.Elements()
                .FirstOrDefault(child => child.Name.LocalName is "grpSpPr" or "spPr");
            XElement? transformation = properties is null ? null : Child(properties, "xfrm");
            XElement? offset = transformation is null ? null : Child(transformation, "off");

            double x = offset is null ? OriginX : Raw(offset, "x");
            double y = offset is null ? OriginY : Raw(offset, "y");

            return new GroupTransform(
                inner.OriginX, inner.OriginY,
                inner.ScaleX * ScaleX, inner.ScaleY * ScaleY,
                ShiftX + ((x - OriginX) * ScaleX), ShiftY + ((y - OriginY) * ScaleY));
        }

        /// <summary>A child rectangle mapped into the group's own.</summary>
        public DocRect Map(double x, double y, double cx, double cy)
            => new(
                Round(ShiftX + ((x - OriginX) * ScaleX)),
                Round(ShiftY + ((y - OriginY) * ScaleY)),
                Round(cx * ScaleX),
                Round(cy * ScaleY));

        private static Length Round(double emu)
            => Length.FromTwips(Length.FromEmu((long)Math.Round(emu)).Twips);
    }

    /// <summary>The transform a group's own <c>a:xfrm</c> describes.</summary>
    private static GroupTransform TransformOf(XElement group, DocSize size)
    {
        XElement? properties = group.Elements()
            .FirstOrDefault(child => child.Name.LocalName is "grpSpPr" or "spPr");
        XElement? transformation = properties is null ? null : Child(properties, "xfrm");
        if (transformation is null) return GroupTransform.Identity;

        XElement? childOffset = Child(transformation, "chOff");
        XElement? childExtent = Child(transformation, "chExt");
        XElement? extent = Child(transformation, "ext");

        // The group's own extent when it states one, and the anchor's otherwise: `wp:extent` is what the
        // document says the whole drawing is, and the two agree in every file that states both.
        double width = extent is not null && Raw(extent, "cx") > 0 ? Raw(extent, "cx") : size.Width.Emu;
        double height = extent is not null && Raw(extent, "cy") > 0 ? Raw(extent, "cy") : size.Height.Emu;

        double spanX = childExtent is null ? 0 : Raw(childExtent, "cx");
        double spanY = childExtent is null ? 0 : Raw(childExtent, "cy");

        return new GroupTransform(
            childOffset is null ? 0 : Raw(childOffset, "x"),
            childOffset is null ? 0 : Raw(childOffset, "y"),
            spanX > 0 ? width / spanX : 1,
            spanY > 0 ? height / spanY : 1,
            0,
            0);
    }

    /// <summary>One attribute as the number the file wrote, before any unit is assumed.</summary>
    /// <remarks>
    /// A group's child coordinates are in a space of the file's own choosing — the corpus letterhead
    /// counts in twips — so they must not be read as EMUs on the way in. Only the mapped result is a
    /// length.
    /// </remarks>
    private static double Raw(XElement element, string name)
        => element.Attribute(name)?.Value is { } value
           && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0;

    /// <summary>
    /// How far text must stay clear, from the four <c>dist*</c> attributes.
    /// </summary>
    /// <remarks>
    /// On the anchor itself rather than in the wrap element, even though it is the wrap that uses it —
    /// <c>wp:wrapSquare</c> can restate the same four and usually does not.
    /// <para>
    /// The <c>wp:effectExtent</c> beside them is the room a shadow or a glow needs, and LibreOffice does
    /// fold it into the wrap margins (<c>GraphicImport.cxx</c>, the <c>WrapTextMode_PARALLEL</c> branch:
    /// <c>m_nRightMargin += aMSOBaseLeftTop.X + aMSOBaseSize.Width - (aLOBoundRect.X + aLOBoundRect.Width)</c>,
    /// which for an unrotated shape comes to the effect extent). It is deliberately <em>not</em> read, and
    /// the reason is a measurement rather than a principle: adding it horizontally moves the wrapped lines
    /// the right way by a twip, and adding it vertically raises the hole's top edge by a twip too — which
    /// on the corpus document makes the line above the frame touch it and narrows one line more than
    /// LibreOffice does. A whole line in the wrong place is a worse error than a twip, so neither is added
    /// until the asymmetry is understood. See the note in <c>Paperless.WordProcessing/TODO.md</c>.
    /// </para>
    /// </remarks>
    private static Margins Spacing(XElement anchor)
        => new(
            Emu(anchor.Attribute("distL")?.Value),
            Emu(anchor.Attribute("distT")?.Value),
            Emu(anchor.Attribute("distR")?.Value),
            Emu(anchor.Attribute("distB")?.Value));

    /// <summary>
    /// The wrap, which is which of five sibling elements is present.
    /// </summary>
    /// <remarks>
    /// <c>wp:wrapNone</c> is the one whose name means the opposite of what it says: it is the mode in
    /// which the text ignores the frame entirely, ODF's <c>run-through</c>. Word's own UI calls it
    /// "behind text" or "in front of text" depending on the anchor's <c>behindDoc</c>, which changes the
    /// paint order and not the layout.
    /// </remarks>
    private static TextWrap WrapOf(XElement anchor)
    {
        foreach (XElement child in anchor.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "wrapNone":
                    return TextWrap.Through;

                case "wrapTopAndBottom":
                    return TextWrap.TopAndBottom;

                case "wrapSquare" or "wrapTight" or "wrapThrough":
                    return child.Attribute("wrapText")?.Value switch
                    {
                        "left" => TextWrap.Left,
                        "right" => TextWrap.Right,
                        "largest" => TextWrap.Optimal,
                        _ => TextWrap.Both,
                    };

                default:
                    continue;
            }
        }

        return TextWrap.Through;
    }

    /// <summary>
    /// The horizontal position: an origin, and either an offset or an edge to align against.
    /// </summary>
    /// <remarks>
    /// <c>wp:align</c> and <c>wp:posOffset</c> are alternatives, not a pair — a frame stating the former
    /// has no coordinate at all, and reading a missing offset as zero would put every centred frame at
    /// the start margin.
    /// </remarks>
    private static (Length Offset, FrameHorizontalOrigin Origin, FrameHorizontalAlignment Alignment)
        Horizontal(XElement? anchor)
    {
        XElement? position = anchor is null ? null : Child(anchor, "positionH");
        if (position is null)
        {
            return (Length.Zero, FrameHorizontalOrigin.Column, FrameHorizontalAlignment.Left);
        }

        FrameHorizontalOrigin origin = position.Attribute("relativeFrom")?.Value switch
        {
            "page" => FrameHorizontalOrigin.Page,
            "margin" => FrameHorizontalOrigin.PageMargin,
            "character" => FrameHorizontalOrigin.Character,
            "leftMargin" or "rightMargin" or "insideMargin" or "outsideMargin" =>
                FrameHorizontalOrigin.Page,
            _ => FrameHorizontalOrigin.Column,
        };

        if (Child(position, "align")?.Value is { } align)
        {
            return (Length.Zero, origin, align switch
            {
                "left" => FrameHorizontalAlignment.Left,
                "center" => FrameHorizontalAlignment.Centre,
                "right" => FrameHorizontalAlignment.Right,
                "inside" => FrameHorizontalAlignment.Inside,
                "outside" => FrameHorizontalAlignment.Outside,
                _ => FrameHorizontalAlignment.Left,
            });
        }

        return (
            Emu(Child(position, "posOffset")?.Value), origin, FrameHorizontalAlignment.Offset);
    }

    /// <summary>The vertical position, the same shape as the horizontal one.</summary>
    private static (Length Offset, FrameVerticalOrigin Origin, FrameVerticalAlignment Alignment)
        Vertical(XElement? anchor)
    {
        XElement? position = anchor is null ? null : Child(anchor, "positionV");
        if (position is null)
        {
            return (Length.Zero, FrameVerticalOrigin.Paragraph, FrameVerticalAlignment.Top);
        }

        FrameVerticalOrigin origin = position.Attribute("relativeFrom")?.Value switch
        {
            "page" => FrameVerticalOrigin.Page,
            "margin" or "topMargin" or "bottomMargin" => FrameVerticalOrigin.PageMargin,
            "line" => FrameVerticalOrigin.Line,
            _ => FrameVerticalOrigin.Paragraph,
        };

        if (Child(position, "align")?.Value is { } align)
        {
            return (Length.Zero, origin, align switch
            {
                "top" => FrameVerticalAlignment.Top,
                "center" => FrameVerticalAlignment.Middle,
                "bottom" => FrameVerticalAlignment.Bottom,
                _ => FrameVerticalAlignment.Top,
            });
        }

        return (Emu(Child(position, "posOffset")?.Value), origin, FrameVerticalAlignment.Offset);
    }

    /// <summary>
    /// One EMU measurement, rounded to Writer's whole-twip grid.
    /// </summary>
    /// <remarks>
    /// Rounded here rather than kept exact, because every other measurement in the engine already is and
    /// a frame edge half a twip from a line's is the sort of difference that decides whether the line is
    /// narrowed at all — see the touching rule in <see cref="Layout.FrameObstacles"/>.
    /// </remarks>
    private static Length Emu(string? value)
        => value is not null && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long emu)
            ? Length.FromTwips(Length.FromEmu(emu).Twips)
            : Length.Zero;

    /// <summary>
    /// The <c>wps:bodyPr</c> governing a shape's text, or null when it states none.
    /// </summary>
    /// <remarks>
    /// Searched as a descendant rather than as a child because the element sits beside
    /// <c>wps:txbx</c> inside <c>wps:wsp</c>, which is itself three levels below the anchor — and a
    /// group's leaf carries its own.
    /// </remarks>
    private static XElement? BodyProperties(XElement shape) => Descendant(shape, "bodyPr");

    /// <summary>
    /// The distance between a text box's edge and its text, from <c>wps:bodyPr</c>.
    /// </summary>
    /// <remarks>
    /// The defaults are ECMA-376 §20.1.2.2.9's, and they are not zero: 91440 EMU (0.1 in) left and
    /// right, 45720 (0.05 in) top and bottom. Reading them matters twice over — they narrow the lines,
    /// and they are what <see cref="Layout.PageFrame.HasFixedHeight"/> measures the fit against, so a
    /// box 15 pt tall holds 7.8 pt of text rather than 15.
    /// </remarks>
    private static Margins Insets(XElement shape)
    {
        XElement? body = BodyProperties(shape);
        return new Margins(
            Inset(body, "lIns", DefaultHorizontalInsetEmu),
            Inset(body, "tIns", DefaultVerticalInsetEmu),
            Inset(body, "rIns", DefaultHorizontalInsetEmu),
            Inset(body, "bIns", DefaultVerticalInsetEmu));

        static Length Inset(XElement? body, string name, long fallback)
            => Emu(body?.Attribute(name)?.Value ?? fallback.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>ECMA-376 §20.1.2.2.9's default left and right text inset, in EMUs.</summary>
    private const long DefaultHorizontalInsetEmu = 91440;

    /// <summary>Its default top and bottom inset.</summary>
    private const long DefaultVerticalInsetEmu = 45720;

    /// <summary>
    /// Whether a shape's text body grows to fit its text rather than keeping the stated height.
    /// </summary>
    /// <remarks>
    /// Only <c>a:spAutoFit</c> does. <c>a:noAutofit</c>, <c>a:normAutofit</c> and a body stating
    /// nothing all keep the height — measured, not assumed: LibreOffice truncates a
    /// <c>normAutofit</c> box exactly as it truncates a <c>noAutofit</c> one rather than shrinking the
    /// text to fit. See <see cref="Layout.PageFrame.HasFixedHeight"/>.
    /// </remarks>
    private static bool GrowsWithText(XElement shape)
        => BodyProperties(shape) is { } body && Child(body, "spAutoFit") is not null;

    /// <summary>A child by local name, in whichever namespace it was written.</summary>
    /// <remarks>
    /// By local name because a drawing spans four namespaces — <c>wp:</c> for the anchor, <c>a:</c> for
    /// the graphic, <c>wps:</c> for the shape, <c>w:</c> for the text inside it — and matching the
    /// namespace of each would be four constants standing in for one distinction the file never makes.
    /// </remarks>
    private static XElement? Child(XElement parent, string name)
        => parent.Elements().FirstOrDefault(child => child.Name.LocalName == name);

    private static XElement? Descendant(XElement parent, string name)
        => parent.Descendants().FirstOrDefault(child => child.Name.LocalName == name);

    /// <summary>
    /// How a shape is painted: its fill, its outline colour, and how thick that outline is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>We drew neither of these on a DrawingML shape until this existed</strong>, which is a
    /// defect no column of the gate can see and two rounds of blind reviewers found by looking. On
    /// <c>ABCD-WB-08-00</c> and <c>ABCD-SDE-23-00</c> the reference draws a grey header panel, a
    /// bordered box round a logo placeholder and a solid grey bar behind "Document reference:"; we drew
    /// none of them, so that bar's white text landed on white paper and was invisible. Confirmed in the
    /// reference's operators rather than in a raster — its page 2 carries nine transparency-group
    /// XObjects and six strokes against our none and four — because the fills carry
    /// <c>a:alpha</c> and LibreOffice's PDF export writes an alpha fill as a transparency group rather
    /// than as a plain <c>re f</c>, so grepping the content stream for a fill finds nothing.
    /// </para>
    /// <para>
    /// Only <c>a:solidFill</c> is read, on both the area and the line. A gradient, a pattern or a
    /// picture fill is a real fill this cannot yet draw, and painting its first stop as a flat colour
    /// would be a confident wrong answer rather than an absent one; each leaves the frame as it was.
    /// </para>
    /// <para>
    /// A shape stating no fill element at all is left unfilled rather than given the theme's default.
    /// DrawingML says a shape with no <c>a:*Fill</c> takes the fill its <c>wps:style/a:fillRef</c>
    /// names out of the theme's format scheme, which is a whole style matrix and is what
    /// <c>oox/source/drawingml/shape.cxx</c> implements; reading only what the shape itself states is
    /// the conservative half of that and never invents ink. <c>a:noFill</c> is honoured explicitly, so
    /// "stated none" and "said nothing" already differ here for the case the corpus exercises.
    /// </para>
    /// </remarks>
    /// <param name="properties">The shape's own <c>spPr</c>, or null.</param>
    /// <param name="theme">The theme its colours resolve against, or null.</param>
    private static (Colour? Fill, Colour? Line, Length Width) Appearance(
        XElement? properties, DrawingTheme? theme)
    {
        if (properties is null) return (null, null, Length.Zero);

        Colour? fill = Solid(Child(properties, "solidFill"), theme);

        XElement? line = Child(properties, "ln");
        if (line is null) return (fill, null, Length.Zero);

        Colour? stroke = Solid(Child(line, "solidFill"), theme);
        return stroke is null
            ? (fill, null, Length.Zero)
            : (fill, stroke, Emu(line.Attribute("w")?.Value));

        static Colour? Solid(XElement? solidFill, DrawingTheme? palette)
            => solidFill is null
                ? null
                : DrawingColour.Read(solidFill.Elements().FirstOrDefault())?.Resolve(palette);
    }

    /// <summary>
    /// The <c>spPr</c> of the shape a drawing holds, or null when it holds none.
    /// </summary>
    /// <remarks>
    /// A descendant rather than a child, for the reason <see cref="BodyProperties"/> is: the element
    /// sits three levels below the anchor, under <c>wps:wsp</c> or <c>pic:pic</c>. The first in document
    /// order is the outermost shape's — a <c>wps:spPr</c> precedes the <c>wps:txbx</c> that could hold a
    /// nested drawing of its own — so this reads the shape asked about and not one inside its text.
    /// </remarks>
    private static XElement? ShapeProperties(XElement placed) => Descendant(placed, "spPr");

    /// <summary>The shape's preset name and stated adjustments, or nulls when it declares none.</summary>
    /// <remarks>
    /// <para>
    /// <c>rect</c> is returned as null on purpose. It is the bounding box the drawing code already
    /// paints, so resolving it through the preset catalogue would build a four-point path to
    /// arrive exactly where not asking arrives — and it is by far the commonest preset in the
    /// corpus, 64 of the 148 uses across the six templates that showed this.
    /// </para>
    /// <para>
    /// <c>line</c> and <c>straightConnector1</c> are left to <see cref="LineGeometry"/>, which
    /// already draws the diagonal they mean: their preset outline is the box, so taking it here
    /// would put three sides on the page that are not in the file.
    /// </para>
    /// </remarks>
    private static (string? Preset, IReadOnlyDictionary<string, double>? Adjustments)
        PresetGeometry(XElement? properties)
    {
        if (properties is null) return (null, null);

        XElement? geometry = Child(properties, "prstGeom");
        if (geometry?.Attribute("prst")?.Value is not { Length: > 0 } preset) return (null, null);
        if (preset is "rect" or "line" or "straightConnector1") return (null, null);

        Dictionary<string, double>? adjustments = null;
        XElement? values = Child(geometry, "avLst");
        foreach (XElement guide in
                 values?.Elements().Where(e => e.Name.LocalName == "gd") ?? [])
        {
            if (guide.Attribute("name")?.Value is not { Length: > 0 } name) continue;

            // `a:avLst` states a formula, and for an adjustment it is `val <n>` -- a literal. The
            // other forms are computed guides and belong to the preset rather than to the shape,
            // so anything that is not a plain value is left for the catalogue's own default.
            string? formula = guide.Attribute("fmla")?.Value;
            if (formula is null || !formula.StartsWith("val ", StringComparison.Ordinal)) continue;

            if (double.TryParse(
                    formula.AsSpan(4), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double value))
            {
                (adjustments ??= [])[name] = value;
            }
        }

        return (preset, adjustments);
    }

    /// <summary>
    /// Whether a shape's outline is its box's diagonal rather than its four sides, and which
    /// diagonal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>a:prstGeom prst="line"</c> and <c>prst="straightConnector1"</c> are the two presets
    /// LibreOffice turns into a straight connector — <c>Shape::createAndInsert</c> maps
    /// <c>XML_line</c> and <c>XML_straightConnector1</c> to <c>ConnectorType_LINE</c>
    /// (<c>oox/source/drawingml/shape.cxx</c>:2124-2127) — which is drawn corner to opposite
    /// corner of the shape's own rectangle. Word writes a flowchart's arrows exactly this way, as
    /// a <c>wps:wsp</c> whose non-visual properties are a <c>wps:cNvCnPr</c> and whose extent is
    /// often zero in one dimension.
    /// </para>
    /// <para>
    /// Drawing the box instead puts three sides on the page that are not in the file, which is
    /// what this path did: measured on an authored one-line document, LibreOffice drew a red
    /// diagonal and Paperless a red rectangle. The VML and Escher front ends already model it —
    /// see <see cref="Layout.PageFrame.IsLine"/> — so only the DrawingML reading was missing.
    /// </para>
    /// <para>
    /// The flips choose the diagonal, and the pair of them cancels: a shape flipped in both
    /// directions is the same line it started as, rotated by half a turn.
    /// </para>
    /// </remarks>
    private static (bool IsLine, bool IsMirrored) LineGeometry(XElement? properties)
    {
        if (properties is null) return (false, false);

        string? preset = Child(properties, "prstGeom")?.Attribute("prst")?.Value;
        if (preset is not ("line" or "straightConnector1")) return (false, false);

        XElement? transform = Child(properties, "xfrm");
        bool flipH = IsTrue(transform?.Attribute("flipH")?.Value);
        bool flipV = IsTrue(transform?.Attribute("flipV")?.Value);

        return (true, flipH ^ flipV);

        // DrawingML's booleans, which are "1"/"true" and their negatives rather than w:val's.
        static bool IsTrue(string? value) => value is "1" or "true" or "on";
    }
}

/// <summary>
/// What a drawing's surroundings decide about it, which the drawing itself does not state.
/// </summary>
/// <remarks>
/// A parameter object rather than three arguments because all three are inherited context: they are the
/// same for every frame in a walk and are threaded through the group recursion unchanged. The default —
/// no theme, not in a header, compatibility mode 0 — is what a caller that has none of it gets, and it
/// reproduces the behaviour every caller had before the type existed.
/// </remarks>
/// <param name="Theme">The theme a <c>a:schemeClr</c> resolves against, or null when there is none.</param>
/// <param name="InHeaderFooter">
/// Whether the drawing is anchored in a header or a footer, which decides its paint order on its own —
/// see <see cref="Layout.PageFrame.BehindText"/>.
/// </param>
/// <param name="CompatibilityMode">
/// The <c>compatibilityMode</c> compatibility setting, or 0 when the file states none. 15 and above is
/// Word 2013 and later, which changes what <c>behindDoc</c> means.
/// </param>
internal readonly record struct DocxFrameContext(
    DrawingTheme? Theme = null,
    bool InHeaderFooter = false,
    int CompatibilityMode = 0);
