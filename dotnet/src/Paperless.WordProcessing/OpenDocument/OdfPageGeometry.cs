using System.Xml.Linq;
using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.OpenDocument;
using Paperless.OpenDocument.Styles;
using Paperless.WordProcessing.Model;

namespace Paperless.WordProcessing.OpenDocument;

/// <summary>
/// Reads an ODF master page's geometry.
/// </summary>
/// <remarks>
/// <para>
/// ODF splits what the other three formats state in one place. A <c>style:master-page</c> pairs a
/// <em>name</em> and its header and footer content with a <c>style:page-layout</c> holding the
/// geometry, and a paragraph reaches its master page through its paragraph style's
/// <c>style:master-page-name</c>. So finding a section's page setup means resolving a style chain
/// first — which is why this takes the resolved <see cref="OdfStyles"/> rather than an element.
/// </para>
/// <para>
/// The header and footer are stated differently too. Word gives a distance from the page edge; ODF
/// gives the header its own height and a margin between it and the body, both inside the page's top
/// margin. The conversion is the page's top margin minus the header's own extent, which is what makes
/// <see cref="PageGeometry.HeaderDistance"/> comparable across all four formats.
/// </para>
/// <para>
/// ODF is also the one format that states its lengths with units — <c>2cm</c>, <c>0.79in</c>,
/// <c>1134twip</c> — so the parsing goes through <see cref="OdfValue.ParseLength"/> rather than
/// assuming a scale.
/// </para>
/// </remarks>
internal static class OdfPageGeometry
{
    /// <summary>A page dimension beyond this is treated as a producer error.</summary>
    private const double MaxDimensionMillimetres = 22 * 25.4;

    /// <summary>
    /// Reads a section from a master page and the styles it resolves through.
    /// </summary>
    /// <param name="styles">The document's styles, for the page layout the master names.</param>
    /// <param name="master">The master page, or null to get the defaults.</param>
    internal static WritingSection Read(OdfStyles styles, OdfMasterPage? master)
    {
        ArgumentNullException.ThrowIfNull(styles);

        OdfStyle? layout = styles.FindPageLayout(master?.PageLayoutName);
        OdfPropertySet? properties = layout?.Properties(OdfPropertyKind.PageLayout);

        // ODF's fo:margin-top is the distance from the page edge to the top of the *header*, not to
        // the body — so it is Word's w:header, and the body's own top margin is this plus whatever
        // the header occupies. Reading it as the body's margin puts every line of text too high by
        // the height of the header.
        // The page border, which ODF states as the *page style's own* box rather than as a distance
        // like Word does — so it has to be read before the margins, which it moves.
        PageBorders? borders = Borders(properties);

        Length headerDistance =
            (Length(properties, "margin-top") ?? PageMargins.Default.Top) + BorderBand(borders?.Top, properties, "top");
        Length footerDistance =
            (Length(properties, "margin-bottom") ?? PageMargins.Default.Bottom) + BorderBand(borders?.Bottom, properties, "bottom");

        // Measuring the furniture needs its own style, which is a child of the page layout rather
        // than a style in its own right. A master page with no header contributes nothing, which is
        // right: there is no header area to leave room for.
        Length headerHeight = master?.HasHeader() == true
            ? FurnitureExtent(layout?.HeaderProperties)
            : Core.Units.Length.Zero;
        Length footerHeight = master?.HasFooter() == true
            ? FurnitureExtent(layout?.FooterProperties)
            : Core.Units.Length.Zero;

        PageGeometry page = new()
        {
            Size = new DocSize(
                Dimension(properties, "page-width") ?? PageGeometry.Default.Size.Width,
                Dimension(properties, "page-height") ?? PageGeometry.Default.Size.Height),
            Margins = new PageMargins(
                (Length(properties, "margin-left") ?? PageMargins.Default.Left)
                    + BorderBand(borders?.Left, properties, "left"),
                (Length(properties, "margin-right") ?? PageMargins.Default.Right)
                    + BorderBand(borders?.Right, properties, "right"),
                headerDistance + headerHeight,
                footerDistance + footerHeight),
            HeaderDistance = headerDistance,
            FooterDistance = footerDistance,
            HeaderHeight = headerHeight,
            FooterHeight = footerHeight,

            // svg:height is the height, fo:min-height is a floor — Writer's SwFrameSize::Fixed against
            // SwFrameSize::Minimum. A fixed one does not grow, so content that outruns it overflows
            // instead of moving the body, and the paginator has to be told which it is.
            HasFixedHeaderHeight = HasFixedHeight(layout?.HeaderProperties),
            HasFixedFooterHeight = HasFixedHeight(layout?.FooterProperties),

            // ODF's footer is top-aligned below the body rather than bottom-aligned against the page, so
            // the spacing that separates the two is where its first line goes. Stated only when there is a
            // footer at all: a page with none should not claim an offset for it.
            FooterOffset = master?.HasFooter() == true
                ? FurnitureSpacing(layout?.FooterProperties)
                : null,
            Columns = ColumnCount(properties),
            ColumnGap = ColumnGap(properties),

            // The page layout's own writing mode, which is a different statement from a paragraph's
            // and does a different thing: it reverses the order of the section's columns. The
            // vertical modes are left alone here as everywhere else.
            IsRightToLeft = properties?.Get(OdfNamespaces.Style, "writing-mode") is "rl-tb" or "rl",
            IsLandscape = string.Equals(
                properties?.Get(OdfNamespaces.Style, "print-orientation"),
                "landscape",
                StringComparison.OrdinalIgnoreCase),

            // ODF says which pages a master applies to rather than that margins mirror, so mirrored
            // margins show up as a page-usage of "mirrored" instead of a flag on the margins.
            HasMirroredMargins = string.Equals(
                properties?.Get(OdfNamespaces.Style, "page-usage"),
                "mirrored",
                StringComparison.OrdinalIgnoreCase),

            Borders = borders,
        };

        return new WritingSection
        {
            Page = page,

            // ODF has no first-page flag: a document that wants a different first page gives it its
            // own master, reached through the first paragraph's style. So the separate slots are
            // populated only when the master itself names them, which is how a "left page" master
            // distinguishes even pages.
            // style:num-format on the page layout, which ODF states by example — "1", "i", "I", "a", "A".
            PageNumberFormat = Layout.NoteNumbering.Parse(
                    properties?.Get(OdfNamespaces.Style, "num-format"))
                ?? Layout.NoteNumberFormat.Arabic,

            HasDifferentFirstPage = master?.FirstHeader is not null || master?.FirstFooter is not null,
            HasDifferentEvenPages = master?.LeftHeader is not null || master?.LeftFooter is not null,
        };
    }

    /// <summary>
    /// The page border a page layout declares, or null when it declares none that draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>ODF states this the other way round from every Word format, and reading it as Word's
    /// moves the text.</strong> Word gives a margin to the text and a distance from an edge to the
    /// border; ODF gives the page layout a box of its own, so <c>fo:margin-left</c> is the distance
    /// to the <em>border</em> and <c>fo:padding-left</c> the gap from the border to the text — the
    /// conversion <c>editeng::BorderDistanceFromWord</c> performs on import
    /// (<c>editeng/source/items/frmitems.cxx</c>:4143-4174), and what LibreOffice's own writer emits
    /// on the way back out. Measured: 26.2.4.2 converting a DOCX with <c>w:pgMar w:left="1440"</c>,
    /// <c>w:sz="36"</c> and <c>w:space="15"</c> writes <c>fo:margin-left="0.2083in"</c> (15 pt),
    /// <c>fo:border="4.51pt solid #396533"</c> and <c>fo:padding="0.7291in"</c> (52.5 pt), which sum
    /// to the inch Word stated. Taking <c>fo:margin-left</c> for the text's margin would start every
    /// line 57 pt too far left.
    /// </para>
    /// <para>
    /// So the border's <see cref="PageBorderSide.Space"/> is <c>fo:margin</c> itself and the offset
    /// is always from the paper's edge — ODF has no equivalent of <c>w:offsetFrom="text"</c> because
    /// it does not need one.
    /// </para>
    /// </remarks>
    private static PageBorders? Borders(OdfPropertySet? properties)
    {
        if (properties is null) return null;

        PageBorderSide all = Side(properties, "border", default);

        PageBorders borders = new()
        {
            Top = Side(properties, "border-top", all) with { Space = Margin(properties, "margin-top") },
            Left = Side(properties, "border-left", all) with { Space = Margin(properties, "margin-left") },
            Bottom = Side(properties, "border-bottom", all) with { Space = Margin(properties, "margin-bottom") },
            Right = Side(properties, "border-right", all) with { Space = Margin(properties, "margin-right") },
            Shadow = Shadow(properties),
        };

        return borders.Draws ? borders : null;
    }

    /// <summary>
    /// One side from CSS's three-part shorthand, or the four-sided value where the side says nothing.
    /// </summary>
    /// <remarks>
    /// <c>fo:border</c> sets all four and <c>fo:border-left</c> and friends override their own side,
    /// which is CSS's cascade and the same one <c>OdtLayoutSource.Tables</c> follows for a cell — with
    /// the same rule that a stated <c>none</c> has to beat the fallback rather than fall through it.
    /// </remarks>
    private static PageBorderSide Side(OdfPropertySet properties, string name, PageBorderSide fallback)
    {
        string? stated = properties.Get(OdfNamespaces.FoCompatible, name);
        if (string.IsNullOrWhiteSpace(stated)) return fallback;
        if (string.Equals(stated.Trim(), "none", StringComparison.Ordinal)) return default;

        Core.Units.Length width = Core.Units.Length.Zero;
        Colour colour = Colour.Black;

        foreach (string part in stated.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (OdfValue.ParseLength(part) is { } measured)
            {
                width = OdfWriterUnits.ToCore(measured);
                continue;
            }

            if (part.StartsWith('#') && OdfValue.ParseColour(part) is { } named) colour = named;
        }

        // A shorthand naming a style and a colour but no width is still a border, drawn at Writer's
        // thinnest visible stroke rather than not at all — the rule the table reader already applies.
        if (width <= Core.Units.Length.Zero) width = HairlineBorder;

        return new PageBorderSide(width, colour, Core.Units.Length.Zero);
    }

    /// <summary>The width a border with no stated one is drawn at: half a point, Writer's hairline.</summary>
    private static readonly Core.Units.Length HairlineBorder = Core.Units.Length.FromPoints(0.5);

    /// <summary>One <c>fo:margin-*</c>, which for a bordered page is the distance to the border.</summary>
    private static Core.Units.Length Margin(OdfPropertySet properties, string name)
        => Length(properties, name) ?? Core.Units.Length.Zero;

    /// <summary>
    /// How much of the page one side's border and its padding take out of the margin.
    /// </summary>
    /// <remarks>
    /// Zero for a side that draws no line, which is Writer's own rule rather than a simplification:
    /// <c>SvxBoxItem::CalcLineSpace</c> answers zero for a side with no line unless its caller asks
    /// otherwise, and <c>SwBorderAttrs</c> does not ask. So a page layout carrying a stray
    /// <c>fo:padding</c> and no border keeps its margins.
    /// </remarks>
    private static Core.Units.Length BorderBand(
        PageBorderSide? side, OdfPropertySet? properties, string edge)
    {
        if (side is not { } stated || !stated.Draws || properties is null) return Core.Units.Length.Zero;

        Core.Units.Length padding =
            Length(properties, "padding-" + edge)
            ?? Length(properties, "padding")
            ?? Core.Units.Length.Zero;

        return stated.Width + padding;
    }

    /// <summary>
    /// The shadow <c>style:shadow</c> declares, or zero for none.
    /// </summary>
    /// <remarks>
    /// ODF states the offset outright — <c>#000000 0.0626in 0.0626in</c> — where Word derives it from
    /// the right border's width, so this is the one format that does not have to choose a side. The
    /// value is <c>none</c> when there is no shadow, and the first length in it is the offset; the two
    /// lengths are the horizontal and vertical offsets and LibreOffice writes them equal.
    /// </remarks>
    private static Core.Units.Length Shadow(OdfPropertySet properties)
    {
        string? stated = properties.Get(OdfNamespaces.Style, "shadow");
        if (string.IsNullOrWhiteSpace(stated)) return Core.Units.Length.Zero;
        if (string.Equals(stated.Trim(), "none", StringComparison.Ordinal)) return Core.Units.Length.Zero;

        foreach (string part in stated.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (OdfValue.ParseLength(part) is { } measured)
            {
                Core.Units.Length offset = OdfWriterUnits.ToCore(measured);
                if (offset > Core.Units.Length.Zero) return offset;
            }
        }

        return Core.Units.Length.Zero;
    }

    /// <summary>
    /// The height a header or footer occupies inside the page margin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ODF states the height two ways and they behave differently, which is not obvious from the
    /// attribute names and was settled by rendering both and measuring the result.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <c>svg:height</c> is a <em>fixed</em> height, honoured exactly — and it absorbs the
    ///     spacing: a 12 mm header 25 mm from the page edge puts the body at 37 mm however large
    ///     <c>fo:margin-bottom</c> is.
    ///   </item>
    ///   <item>
    ///     <c>fo:min-height</c> makes the height <em>dynamic</em>. LibreOffice maps it to
    ///     <c>HeaderIsDynamicHeight</c> (<c>xmloff/source/style/PageMasterImportPropMapper.cxx</c>)
    ///     and then sizes the frame to its content, so the declared value is not a floor in practice —
    ///     a header declaring 6 mm around one 12 pt line renders 4.9 mm tall. The spacing is added on
    ///     top of that.
    ///   </item>
    /// </list>
    /// <para>
    /// The dynamic case therefore needs the header's content laid out to be exact, which cannot happen
    /// before the page it sits on is known. The declared minimum plus the spacing is used instead — the
    /// same approximation LibreOffice's own DOC exporter falls back to, and which its comment calls
    /// "totally nonoptimum, but the best we can do"
    /// (<c>sw/source/filter/ww8/writerwordglue.cxx</c>). It errs towards leaving too much room, so text
    /// starts slightly low rather than overlapping the header.
    /// </para>
    /// </remarks>
    private static Length FurnitureExtent(OdfPropertySet? properties)
    {
        if (properties is null) return Core.Units.Length.Zero;

        if (OdfValue.ParseLength(properties.Get(OdfNamespaces.SvgCompatible, "height")) is { } fixedHeight)
        {
            return OdfWriterUnits.ToCore(fixedHeight);
        }

        Length declared = OdfWriterUnits.ToCore(
            OdfValue.ParseLength(properties.Get(OdfNamespaces.FoCompatible, "min-height")))
            ?? Core.Units.Length.Zero;

        return declared + FurnitureSpacing(properties);
    }

    /// <summary>
    /// True when the furniture states a height rather than a floor, so it does not grow with its content.
    /// </summary>
    /// <remarks>
    /// The same test <see cref="FurnitureExtent"/> makes, asked as a question rather than as a measurement,
    /// because the paginator needs the answer and not the number: <c>svg:height</c> is
    /// <c>SwFrameSize::Fixed</c> and <c>fo:min-height</c> is <c>SwFrameSize::Minimum</c>, and only the
    /// second lets a running head or foot move the body.
    /// </remarks>
    private static bool HasFixedHeight(OdfPropertySet? properties)
        => properties is not null
           && OdfValue.ParseLength(properties.Get(OdfNamespaces.SvgCompatible, "height")) is not null;

    /// <summary>
    /// The gap between a header or footer and the body text.
    /// </summary>
    /// <remarks>
    /// The margin below a header, or above a footer. Both are written as the side facing the body, so
    /// whichever is present is the spacing that separates the furniture from the text — which makes one
    /// lookup with a fallback right for both, and means neither caller has to say which it is asking about.
    /// </remarks>
    private static Length FurnitureSpacing(OdfPropertySet? properties)
        => OdfWriterUnits.ToCore(
               OdfValue.ParseLength(properties?.Get(OdfNamespaces.FoCompatible, "margin-bottom"))
               ?? OdfValue.ParseLength(properties?.Get(OdfNamespaces.FoCompatible, "margin-top")))
           ?? Core.Units.Length.Zero;

    /// <summary>
    /// A measure from a page layout, on Writer's own grid.
    /// </summary>
    /// <remarks>
    /// Rounded to whole twips at the point it is read, because that is what LibreOffice's import does and
    /// because everything downstream compares against a layout that was measured that way — a margin
    /// carrying a third of a twip of extra precision narrows the text area by it on both sides.
    /// </remarks>
    private static Length? Length(OdfPropertySet? properties, string localName)
        => OdfWriterUnits.ToCore(
            OdfValue.ParseLength(properties?.Get(OdfNamespaces.FoCompatible, localName)));

    private static Length? Dimension(OdfPropertySet? properties, string localName)
        => Length(properties, localName) is { } value
           && value > Core.Units.Length.Zero
           && value.Millimetres <= MaxDimensionMillimetres
            ? value
            : null;

    /// <summary>
    /// A distance that cannot go below zero.
    /// </summary>
    /// <remarks>
    /// A header taller than the margin it sits in is legal in ODF and means the body starts lower, not
    /// that the header starts above the page. Clamping keeps the header inside the sheet.
    /// </remarks>
    private static Length Difference(Length margin, Length extent)
    {
        Length remaining = margin - extent;
        return remaining > Core.Units.Length.Zero ? remaining : Core.Units.Length.Zero;
    }

    /// <summary>
    /// How many columns the page layout declares.
    /// </summary>
    /// <remarks>
    /// From <c>style:columns</c>'s own count when it states one, otherwise from the number of
    /// <c>style:column</c> children — a layout with unequal columns lists them and need not repeat the
    /// number.
    /// </remarks>
    private static int ColumnCount(OdfPropertySet? properties)
    {
        if (properties?.Child(OdfNamespaces.Style, "columns") is not { } columns) return 1;

        if (columns.Attribute(XName.Get("column-count", OdfNamespaces.FoCompatible))?.Value is { } text
            && int.TryParse(text, out int declared)
            && declared > 0)
        {
            return Math.Min(declared, 64);
        }

        int listed = columns.Elements(XName.Get("column", OdfNamespaces.Style)).Count();
        return listed > 0 ? Math.Min(listed, 64) : 1;
    }

    /// <summary>
    /// The gap between columns.
    /// </summary>
    /// <remarks>
    /// ODF states it as <c>fo:column-gap</c> on the columns element, or — for unequal columns — as a
    /// margin on each column, in which case the first column's right margin is representative. Taking
    /// it from the first rather than averaging keeps the common case exact.
    /// </remarks>
    private static Length ColumnGap(OdfPropertySet? properties)
    {
        if (properties?.Child(OdfNamespaces.Style, "columns") is not { } columns)
        {
            return Core.Units.Length.Zero;
        }

        if (OdfValue.ParseLength(columns.Attribute(XName.Get("column-gap", OdfNamespaces.FoCompatible))?.Value)
            is { } gap)
        {
            return OdfWriterUnits.ToCore(gap);
        }

        XElement? first = columns.Elements(XName.Get("column", OdfNamespaces.Style)).FirstOrDefault();
        return OdfWriterUnits.ToCore(
                   OdfValue.ParseLength(
                       first?.Attribute(XName.Get("end-indent", OdfNamespaces.FoCompatible))?.Value))
               ?? Core.Units.Length.Zero;
    }
}
