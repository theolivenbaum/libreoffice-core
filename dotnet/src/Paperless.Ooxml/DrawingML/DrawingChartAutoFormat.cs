using System.Globalization;
using System.Xml.Linq;
using Paperless.Core.Graphics;

namespace Paperless.Ooxml.DrawingML;

/// <summary>
/// Which object a chart's automatic formatting is being asked about.
/// </summary>
public enum ChartAutoObject
{
    /// <summary>A series drawn as a line — line, scatter, radar. Its colour is its stroke.</summary>
    LinearSeries,

    /// <summary>A series drawn as an area — bar, pie, area. Its colour is its fill.</summary>
    FilledSeries,
}

/// <summary>
/// The colours a chart gives a series that states none — <c>ObjectFormatter</c>'s automatic
/// formatting, ported.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A <c>c:ser</c> with no <c>c:spPr</c> is not a series with no colour.</strong> It is a
/// series whose colour comes from the chart's style index and the document theme's accent cycle,
/// and reading it as "no colour" draws every line in a chart black and leaves every legend key
/// blank. Measured on <c>Demick_JetBlue.pptx</c>, whose three line series state nothing at all
/// and whose reference draws them in the Aspect theme's <c>F07F09</c>, <c>9F2936</c> and
/// <c>1B587C</c> — accent 1, 2 and 3 exactly.
/// </para>
/// <para>
/// <strong>The style index is <c>c:style/@val</c> and its default is 2, not 1.</strong>
/// <c>ChartSpaceModel</c>'s constructor is <c>mnStyle( 2 )</c>
/// (<c>oox/source/drawingml/chart/chartspacemodel.cxx:27</c>), and style 2 is the plain accent
/// cycle — accent 1 to accent 6 with nothing done to them. That is why so many charts come out
/// right with no <c>c:style</c> in the file at all.
/// </para>
/// <para>
/// <strong>The cycle is shaded across cycles rather than across series.</strong>
/// <c>DetailFormatterBase::getPhColor</c> (<c>objectformatter.cxx:766-816</c>) takes
/// <c>pattern[idx % size]</c> and then shades or tints it by which *cycle* the series is in,
/// stepping through <c>[-70%, +70%]</c> in <c>1.4 / (maxCycle + 2)</c> steps. With six accents
/// and six or fewer series that step lands exactly on zero, so the common case is the accents
/// untouched; a single-colour style (3 to 8, one accent for every series) has a cycle size of
/// one and therefore shades every series differently, which is the whole point of those styles.
/// </para>
/// <para>
/// The series index is <c>c:ser/c:idx</c> and the maximum is taken over every type group in the
/// plot area (<c>plotareaconverter.cxx:452-457</c>) — not the series' position in the file, and
/// not a count within one group. A combination chart whose line group states <c>c:idx val="2"</c>
/// takes accent 3 however few series that group holds.
/// </para>
/// </remarks>
public static class DrawingChartAutoFormat
{
    /// <summary>The style index a chart space with no <c>c:style</c> has.</summary>
    /// <remarks><c>ChartSpaceModel::ChartSpaceModel</c>, <c>mnStyle( 2 )</c>.</remarks>
    public const int DefaultStyle = 2;

    /// <summary>
    /// <c>THEMED_STYLE_SUBTLE</c> — the first entry of <c>a:lnStyleLst</c>.
    /// </summary>
    /// <remarks>
    /// <c>include/oox/drawingml/theme.hxx:48</c>. Every automatic series entry names it; the
    /// intense variant appears only in the fill tables for styles 17 and up.
    /// </remarks>
    public const int SubtleStyleIndex = 1;

    /// <summary>Accent 1 to accent 6, untouched — <c>spAutoFormatPattern2</c>.</summary>
    private static readonly (string Colour, string? Modification, int Value)[] AccentPattern =
    [
        ("accent1", null, 0), ("accent2", null, 0), ("accent3", null, 0),
        ("accent4", null, 0), ("accent5", null, 0), ("accent6", null, 0),
    ];

    /// <summary>Six tints of <c>dk1</c> — <c>spAutoFormatPattern1</c>, the greyscale styles.</summary>
    private static readonly (string Colour, string? Modification, int Value)[] GreyPattern =
    [
        ("dk1", "tint", 88500), ("dk1", "tint", 55000), ("dk1", "tint", 78000),
        ("dk1", "tint", 92500), ("dk1", "tint", 70000), ("dk1", "tint", 30000),
    ];

    /// <summary>The same six with a darker first entry — <c>spAutoFormatPattern4</c>.</summary>
    private static readonly (string Colour, string? Modification, int Value)[] DarkGreyPattern =
    [
        ("dk1", "tint", 5000), ("dk1", "tint", 55000), ("dk1", "tint", 78000),
        ("dk1", "tint", 15000), ("dk1", "tint", 70000), ("dk1", "tint", 30000),
    ];

    /// <summary>Six half-shaded accents — <c>spAutoFormatPattern3</c>.</summary>
    private static readonly (string Colour, string? Modification, int Value)[] ShadedAccentPattern =
    [
        ("accent1", "shade", 50000), ("accent2", "shade", 50000), ("accent3", "shade", 50000),
        ("accent4", "shade", 50000), ("accent5", "shade", 50000), ("accent6", "shade", 50000),
    ];

    /// <summary><c>spLinearSeriesLines</c>: a line series' stroke, by chart style.</summary>
    private static readonly AutoFormatEntry[] LinearSeriesLines =
    [
        Pattern(1, 1, 300, GreyPattern),
        Pattern(2, 2, 300, AccentPattern),
        .. FadedAccents(3, 300),
        Pattern(9, 9, 500, GreyPattern),
        Pattern(10, 10, 500, AccentPattern),
        .. FadedAccents(11, 500),
        Pattern(17, 17, 500, GreyPattern),
        Pattern(18, 18, 500, AccentPattern),
        .. FadedAccents(19, 500),
        Pattern(25, 25, 700, GreyPattern),
        Pattern(26, 26, 700, AccentPattern),
        .. FadedAccents(27, 700),
        Pattern(33, 33, 500, GreyPattern),
        Pattern(34, 34, 500, AccentPattern),
        .. FadedAccents(35, 500),
        Pattern(41, 42, 500, DarkGreyPattern),
        Pattern(42, 42, 500, AccentPattern),
        .. FadedAccents(43, 500),
    ];

    /// <summary><c>spFilledSeries2dFills</c>: a bar, pie or area series' fill, by chart style.</summary>
    /// <remarks>
    /// The themed index differs between the subtle and intense halves of the real table and is
    /// not read here, because a fill entry's <c>mnThemedIdx</c> reaches only
    /// <c>Theme::getFillStyle</c>'s gradient — the colour, which is what a solid bar shows, comes
    /// from the pattern alone.
    /// </remarks>
    private static readonly AutoFormatEntry[] FilledSeriesFills =
    [
        Pattern(1, 1, 100, GreyPattern),
        Pattern(2, 2, 100, AccentPattern),
        .. FadedAccents(3, 100),
        Pattern(9, 9, 100, GreyPattern),
        Pattern(10, 10, 100, AccentPattern),
        .. FadedAccents(11, 100),
        Pattern(17, 17, 100, GreyPattern),
        Pattern(18, 18, 100, AccentPattern),
        .. FadedAccents(19, 100),
        Pattern(25, 25, 100, GreyPattern),
        Pattern(26, 26, 100, AccentPattern),
        .. FadedAccents(27, 100),
        Pattern(33, 33, 100, GreyPattern),
        Pattern(34, 34, 100, AccentPattern),
        .. FadedAccents(35, 100),
        Pattern(41, 42, 100, DarkGreyPattern),
        Pattern(42, 42, 100, AccentPattern),
        .. FadedAccents(43, 100),
    ];

    /// <summary><c>spFilledSeriesLines</c>: the outline of a filled series, by chart style.</summary>
    /// <remarks>
    /// Invisible for every style below 33 except 9 to 16, which outline in <c>lt1</c>. That is why
    /// an ordinary bar chart has no bar outline at all and drawing one is visible immediately.
    /// </remarks>
    private static readonly AutoFormatEntry[] FilledSeriesLines =
    [
        Invisible(1, 8),
        Single(9, 16, "lt1", null, 0),
        Invisible(17, 32),
        Single(33, 33, "dk1", "shade", 50000),
        Pattern(34, 34, 100, ShadedAccentPattern),
        .. AccentsModified(35, "shade", 50000),
        Invisible(41, 48),
    ];

    /// <summary>
    /// The colour a series takes when it states none, as a <c>a:solidFill</c>, or null for none.
    /// </summary>
    /// <param name="style">The chart's <c>c:style/@val</c>.</param>
    /// <param name="what">Whether the series is drawn as a line or as an area.</param>
    /// <param name="stroke">True for the outline rather than the interior.</param>
    /// <param name="index">The series' <c>c:ser/c:idx</c>, or its point index when varying.</param>
    /// <param name="maximum">The largest such index in the whole plot area.</param>
    /// <param name="theme">The theme the accent names resolve against.</param>
    /// <param name="styles">
    /// The theme's format matrix. A stroke's accent is a <em>placeholder</em> pushed into the
    /// theme's subtle line style rather than the drawn colour itself — see
    /// <see cref="ThroughSubtleLineStyle"/>. Null leaves the accent raw, which is what a caller
    /// with no theme to ask has to do.
    /// </param>
    public static Colour? ColourOf(
        int style,
        ChartAutoObject what,
        bool stroke,
        int index,
        int maximum,
        DrawingTheme? theme,
        DrawingStyleMatrix? styles = null)
    {
        AutoFormatEntry[] table = (what, stroke) switch
        {
            (ChartAutoObject.LinearSeries, true) => LinearSeriesLines,
            (ChartAutoObject.LinearSeries, false) => [],
            (ChartAutoObject.FilledSeries, true) => FilledSeriesLines,
            _ => FilledSeriesFills,
        };

        if (Entry(table, style) is not { } entry) return null;
        if (Resolve(entry, index, maximum, theme) is not { } placeholder) return null;

        return stroke ? ThroughSubtleLineStyle(placeholder, styles, theme) : placeholder;
    }

    /// <summary>
    /// The accent a stroke's automatic entry names, put through the theme's subtle line style.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The accent is not the colour drawn; it is what the theme's line style is drawn
    /// <em>in terms of</em>.</strong> <c>LineFormatter</c> copies
    /// <c>Theme::getLineStyle(THEMED_STYLE_SUBTLE)</c> whole and then resolves it with
    /// <c>getPhColor(nSeriesIdx)</c> as the placeholder —
    /// <c>aLineProps.pushToPropMap(rPropMap, …, getPhColor(nSeriesIdx))</c>,
    /// <c>oox/source/drawingml/chart/objectformatter.cxx:857-864</c>. So whatever the theme wrapped
    /// around its <c>phClr</c> acts on the accent.
    /// </para>
    /// <para>
    /// Measured on <c>Demick_JetBlue.pptx</c>, whose theme's first <c>a:lnStyleLst</c> entry is a
    /// <c>phClr</c> under <c>shade 50000</c> and <c>satMod 103000</c>: the reference draws its three
    /// automatic series in <c>B45D03</c>, <c>761D26</c> and <c>12415C</c>, not in the accents
    /// <c>F07F09</c>, <c>9F2936</c> and <c>1B587C</c> they are derived from. Taking the accent raw
    /// draws every automatic chart line noticeably too bright on any theme that states a transform
    /// there, and every theme Office ships states one.
    /// </para>
    /// <para>
    /// A theme entry with no <c>a:solidFill</c> — a bare width, or an <c>a:noFill</c> — leaves the
    /// accent alone rather than making the series invisible. LibreOffice would draw no line at all
    /// in that case, but no corpus theme states it and inventing an absence is the more expensive
    /// way to be wrong.
    /// </para>
    /// </remarks>
    /// <param name="placeholder">The accent the automatic table names, cycle shading applied.</param>
    /// <param name="styles">The theme's format matrix, or null.</param>
    /// <param name="theme">The theme the substituted colour resolves against.</param>
    public static Colour ThroughSubtleLineStyle(
        Colour placeholder, DrawingStyleMatrix? styles, DrawingTheme? theme)
    {
        if (styles?.LineStyle(SubtleStyleIndex) is not { } line) return placeholder;
        if (Drawing.Child(DrawingStyleMatrix.Substitute(line, placeholder), "solidFill")
            is not { } fill)
        {
            return placeholder;
        }

        foreach (XElement child in fill.Elements())
            if (DrawingColour.Read(child)?.Resolve(theme) is { } resolved) return resolved;

        return placeholder;
    }

    /// <summary>
    /// How wide a line series' automatic stroke is, as a percentage of the theme's subtle line.
    /// </summary>
    /// <remarks>
    /// <c>LineFormatter</c>'s constructor multiplies the themed line's width by
    /// <c>mnRelLineWidth</c> (<c>objectformatter.cxx:851-853</c>), so a chart at the default style
    /// draws its lines at three times the theme's own — 2.25 pt against 0.75 pt on every theme
    /// Office ships. Returns 0 when the style draws no line at all.
    /// </remarks>
    /// <param name="style">The chart's <c>c:style/@val</c>.</param>
    /// <param name="what">Whether the series is drawn as a line or as an area.</param>
    public static int RelativeLineWidth(int style, ChartAutoObject what)
    {
        AutoFormatEntry[] table = what == ChartAutoObject.LinearSeries
            ? LinearSeriesLines
            : FilledSeriesLines;

        return Entry(table, style) is { } entry && !entry.Invisible ? entry.RelativeLineWidth : 0;
    }

    /// <summary>The style index a chart space states, or <see cref="DefaultStyle"/>.</summary>
    /// <remarks>
    /// A file written by Office 2010 or later states it twice: a <c>c14:style</c> of 100 plus the
    /// value inside an <c>mc:Choice</c>, and the plain <c>c:style</c> inside the
    /// <c>mc:Fallback</c> beside it. LibreOffice's markup-compatibility handling declares no
    /// support for the <c>c14</c> namespace, so the fallback is what it reads and the choice is
    /// discarded — which is why a chart carrying <c>c14:style val="102"</c> formats as style 2.
    /// </remarks>
    /// <param name="chartSpace">The <c>c:chartSpace</c> root.</param>
    public static int StyleOf(XElement? chartSpace)
    {
        if (chartSpace is null) return DefaultStyle;

        foreach (XElement candidate in chartSpace.Descendants(
                     XName.Get("style", OoxmlNamespaces.DrawingMLChart)))
        {
            if (Drawing.Attribute(candidate, "val") is not { } text) continue;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                continue;
            if (value is >= 1 and <= 48) return value;
        }

        return DefaultStyle;
    }

    private static Colour? Resolve(
        AutoFormatEntry entry, int index, int maximum, DrawingTheme? theme)
    {
        if (entry.Invisible) return null;
        if (entry.Pattern.Length == 0) return null;

        (string name, string? modification, int value) =
            entry.Pattern[Math.Max(index, 0) % entry.Pattern.Length];

        XElement colour = new(Drawing.Name("schemeClr"), new XAttribute("val", name));
        if (modification is not null)
        {
            colour.Add(new XElement(
                Drawing.Name(modification),
                new XAttribute("val", value.ToString(CultureInfo.InvariantCulture))));
        }

        if (DrawingColour.Read(colour)?.Resolve(theme) is not { } resolved) return null;
        if (!entry.Cycled) return resolved;

        // The shade or tint that separates one cycle of the pattern from the next. Applied on the
        // resolved colour rather than as another a:shade, because that is what
        // Color::addChartTintTransformation does — it appends a transform to an already-resolved
        // sRGB colour rather than to the scheme reference.
        int cycle = Math.Max(index, 0) / entry.Pattern.Length;
        int cycles = Math.Max(maximum, 0) / entry.Pattern.Length;
        double amount = (((cycle + 1.0) / (cycles + 2.0)) * 1.4) - 0.7;

        return ChartTint(resolved, amount);
    }

    /// <summary>
    /// A colour shaded or tinted the way <c>Color::addChartTintTransformation</c> does.
    /// </summary>
    /// <remarks>
    /// Negative is a <c>shade</c> of <c>1 + amount</c> and positive a <c>tint</c> of
    /// <c>1 - amount</c> — note that both are stated as the *remaining* fraction, so a
    /// <c>fTint</c> of −0.42 becomes <c>shade 58%</c> and one of +0.42 becomes <c>tint 58%</c>
    /// (<c>oox/source/drawingml/color.cxx:488-495</c>). Routed back through
    /// <see cref="DrawingColour"/> so that the gamma-corrected forms of both transforms are the
    /// ones the rest of the reader already uses.
    /// </remarks>
    /// <param name="colour">The resolved colour.</param>
    /// <param name="amount">The signed fraction, in <c>[-1, 1]</c>.</param>
    public static Colour ChartTint(Colour colour, double amount)
    {
        int scaled = (int)Math.Clamp((amount * 100000.0) + 0.5, -100000.0, 100000.0);
        if (scaled == 0) return colour;

        string kind = scaled < 0 ? "shade" : "tint";
        int value = scaled < 0 ? scaled + 100000 : 100000 - scaled;

        XElement element = new(
            Drawing.Name("srgbClr"),
            new XAttribute("val", $"{colour.R:X2}{colour.G:X2}{colour.B:X2}"),
            new XElement(Drawing.Name(kind), new XAttribute("val", value.ToString(CultureInfo.InvariantCulture))));

        return DrawingColour.Read(element)?.Resolve(null) ?? colour;
    }

    private static AutoFormatEntry? Entry(AutoFormatEntry[] table, int style)
    {
        foreach (AutoFormatEntry entry in table)
        {
            if (entry.First <= style && style <= entry.Last) return entry;
        }

        return null;
    }

    private static AutoFormatEntry Pattern(
        int first, int last, int width, (string Colour, string? Modification, int Value)[] pattern)
        => new(first, last, width, pattern, Cycled: true, Invisible: false);

    private static AutoFormatEntry Single(
        int first, int last, string colour, string? modification, int value)
        => new(first, last, 100, [(colour, modification, value)], Cycled: false, Invisible: false);

    private static AutoFormatEntry Invisible(int first, int last)
        => new(first, last, 0, [], Cycled: false, Invisible: true);

    /// <summary>
    /// Six consecutive styles, one accent each, faded across the series.
    /// </summary>
    /// <remarks>
    /// <c>AUTOFORMAT_FADEDACCENTS</c>. The pattern has one entry, so every series lands in a
    /// cycle of its own and the shade/tint step above separates them — which is what makes styles
    /// 3 to 8 a single-colour chart rather than six copies of one line.
    /// </remarks>
    private static AutoFormatEntry[] FadedAccents(int first, int width)
        => [.. Enumerable.Range(0, 6).Select(i => new AutoFormatEntry(
            first + i,
            first + i,
            width,
            [($"accent{i + 1}", null, 0)],
            Cycled: true,
            Invisible: false))];

    /// <summary>Six consecutive styles, one modified accent each, not faded.</summary>
    /// <remarks><c>AUTOFORMAT_ACCENTSMOD</c>, which sets a single colour and no pattern.</remarks>
    private static AutoFormatEntry[] AccentsModified(int first, string modification, int value)
        => [.. Enumerable.Range(0, 6).Select(i => new AutoFormatEntry(
            first + i,
            first + i,
            100,
            [($"accent{i + 1}", modification, value)],
            Cycled: false,
            Invisible: false))];

    /// <summary>One row of one of <c>ObjectFormatter</c>'s automatic format tables.</summary>
    private readonly record struct AutoFormatEntry(
        int First,
        int Last,
        int RelativeLineWidth,
        (string Colour, string? Modification, int Value)[] Pattern,
        bool Cycled,
        bool Invisible);
}
