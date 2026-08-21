using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.Text.Fonts;
using Paperless.Text.Shaping;

namespace Paperless.Spreadsheets.Layout;

/// <summary>
/// One shaped line of furniture text — a header, a footer, or a row or column heading — with the
/// metrics needed to place it vertically.
/// </summary>
/// <remarks>
/// <para>
/// A separate helper from <c>SheetText</c>, which shapes cell text, because the two are placed by
/// different rules and need different things. A cell's text sits on a baseline derived from the
/// row; a header's is <em>centred</em> in its band, which needs the line height and the ascent
/// rather than just the advance width — <c>ScPrintFunc::PrintHF</c> moves the draw point down by
/// half the difference between the band and the text
/// (<c>sc/source/ui/view/printfun.cxx:1879</c>).
/// </para>
/// <para>
/// If the cell-text work wants these metrics too, this is the file to fold into: the face and the
/// resolver are the same, and only the placement rules differ.
/// </para>
/// </remarks>
internal static class SheetBandText
{
    /// <summary>
    /// The face Calc's furniture is drawn in.
    /// </summary>
    /// <remarks>
    /// The default cell font, not a separate one: <c>ScPrintFunc::MakeEditEngine</c> fills the
    /// header's defaults from <c>getDefaultCellAttribute</c> and only overrides the height unit
    /// (<c>printfun.cxx:1769-1774</c>), and <c>PrintPage</c> builds the heading font from a bare
    /// <c>ScPatternAttr</c> the same way (<c>printfun.cxx:2354</c>). So a header and a column
    /// heading are drawn in whatever a plain cell would be.
    /// </remarks>
    private const string DefaultFamily = "Liberation Sans";

    /// <summary>Ten point, which is Calc's default cell font height.</summary>
    public static Length DefaultSize { get; } = Length.FromPoints(10);

    /// <summary>
    /// The face the furniture is drawn in, together with the reference it was resolved through.
    /// </summary>
    /// <remarks>
    /// Both, and resolved in one place, because the reference cannot be rebuilt from the face
    /// afterwards: an <see cref="OpenTypeFace"/> is a parsed table directory and does not know
    /// which file it was read out of. The resolver's own <c>FaceKey</c> is that file's path, and
    /// it is what lets a PDF embed the face — see the remark on <see cref="Description"/>.
    /// </remarks>
    private static readonly Lazy<(OpenTypeFace? Face, FontReference? Reference)> Resolved =
        new(Load);

    private static readonly Lazy<LineMetrics?> Metrics = new(
        () => Resolved.Value.Face is { } face
            ? LineSpacing.Resolve(face, MetricGrid.Spreadsheet)
            : null);

    /// <summary>
    /// The same metrics on <c>chart2</c>'s own device, which is what a chart's text is measured
    /// with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A chart's labels are not laid out by Calc and so are not quantised onto Calc's output
    /// device: <c>chart2</c>'s view makes them as plain text shapes on a <c>VirtualDevice</c> of
    /// its own. <b>That device is 96 dpi in 1/100 mm</b> — see <see cref="MetricGrid.Chart"/>,
    /// which carries the measurement — where Calc's is 720 and Impress' 600.
    /// </para>
    /// <para>
    /// <b>Until round 60 this dropped the grid entirely</b>, on the reasoning that a text shape
    /// takes the face's metrics whole. It does not: it takes them through a coarse device, and a
    /// 96 dpi pixel is 0.75 pt. That is what made <see cref="ChartLineHeightAt(Length)"/> answer
    /// Carlito's 1.2207 em at every size where 26.2.4.2 stacks it at 1.1219 em at 10 pt and
    /// 1.2241 at 15.89 — sub-linear, because the em itself is rounded to 13 pixels at 10 pt and
    /// to 21 at 16.
    /// </para>
    /// <para>
    /// Going through the grid also drops the external leading, which is right for the same reason
    /// it is right everywhere else <see cref="MetricGrid"/> is used: <c>IsAddExtLeading()</c> is
    /// false in EditEngine, and a chart's text shape is an EditEngine text. Carlito's line gap is
    /// zero so this is invisible on nearly every OOXML workbook; both Liberation faces' is not,
    /// and they are what separated the two laws.
    /// </para>
    /// </remarks>
    private static LineMetrics OnChartDevice(LineMetrics metrics)
        => metrics with { Grid = MetricGrid.Chart };

    /// <summary>
    /// The same metrics with no device at all — the arithmetic chart text used before round 60,
    /// kept for the <em>drawing shape</em> text that also asks for it.
    /// </summary>
    /// <remarks>
    /// <b>This is a preserved behaviour and not a measured one.</b> A Calc drawing object's text
    /// is an EditEngine text like a chart's, but it is formatted against the draw layer's own
    /// reference device rather than <c>chart2</c>'s, and which device that is on 26.2.4.2 has not
    /// been measured on this project. Round 60 moved chart text onto
    /// <see cref="MetricGrid.Chart"/> and deliberately left shape text exactly where it was, so
    /// that a chart fix could not silently move every text box in the corpus. Naming it separately
    /// is what makes the untested half visible; see <see cref="ShapeLineHeightAt(Length, string?)"/>.
    /// </remarks>
    private static LineMetrics Ungridded(LineMetrics metrics) => metrics with { Grid = null };

    /// <summary>The distance from a line's top to its baseline, at a size.</summary>
    /// <param name="size">The em size.</param>
    public static Length AscentAt(Length size)
        => Metrics.Value is { } metrics ? metrics.ScaledAscent(size) : size * 0.9;

    /// <summary>How tall one line is, at a size.</summary>
    /// <remarks>
    /// Ascent plus descent without the line gap, which is what a single line occupies and what
    /// Calc's <c>GetTextHeight</c> answers for a one-line header. Measured on Liberation Sans at
    /// ten point that is 11.1 pt, and it is what puts LibreOffice's header baseline 10.55 pt
    /// below the top of a band 14.099 pt tall.
    /// </remarks>
    /// <param name="size">The em size.</param>
    public static Length LineHeightAt(Length size)
        => Metrics.Value is { } metrics
            ? metrics.ScaledAscent(size) + metrics.ScaledDescent(size)
            : size * 1.15;

    /// <summary>
    /// How tall one line of a <em>chart's</em> text is, at a size.
    /// </summary>
    /// <remarks>
    /// Ascent plus descent plus the line gap, which is the face's own line height and not
    /// <see cref="LineHeightAt(Length)"/>'s. The two differ because a chart is not laid out by Calc: its
    /// labels are made by <c>chart2</c>'s view as plain text shapes, which take the face's metrics
    /// whole, where a cell's height comes from <c>ScDrawStringsVars</c> and drops the gap
    /// (<c>sc/source/ui/view/output2.cxx:734</c>). Liberation Sans is 1.1499 em here against
    /// 1.1494 there, and the difference compounds through the insets that place the plot area
    /// rather than showing up in any one label.
    /// </remarks>
    /// <para>
    /// <strong>Round 59 measured the divergence and round 60 closed it.</strong> Read off
    /// <c>003_advanced_excel_pie</c>'s reference rendering, 26.2.4.2 stacks Carlito's lines
    /// <strong>11.23 pt apart at 10.01 pt</strong> — 1.1219 em — and <strong>19.45 pt apart at
    /// 15.89 pt</strong> — 1.2241 em, where this function used to answer 1.2207 at both. Carlito's
    /// hhea, OS/2 typo and OS/2 win metrics all give 1.2207, so the reference was not reading a
    /// different table: it was reading the same table <em>through a device</em>. A size series on
    /// three faces (<c>probes/sheets-r60/probe-chartvmetrics2.py</c>) puts every one of 39 measured
    /// pitches on an integer multiple of 0.75 pt — one pixel at 96 dpi — and
    /// <see cref="MetricGrid.Chart"/> is that device.
    /// </para>
    /// <para>
    /// It went unnoticed for as long as it did because it cancelled: a chart label is drawn at
    /// <c>blockCentre − blockHeight/2 + ascent</c>, and <see cref="AscentAt(Length)"/> is 9.51 at
    /// 10 pt where the reference's is 9.00, so a <em>single-line</em> label landed within 0.01 pt of
    /// the reference's while its box was 8.8% too tall. The error was only visible once a label
    /// wrapped or its box was measured for a fit test — which is what a pie's best-fit placement
    /// does to every one of them. <strong>The height and the ascent therefore had to move
    /// together</strong>, which is why <see cref="ChartAscentAt(Length)"/> exists rather than the
    /// drawing paths keeping <see cref="AscentAt(Length)"/>.
    /// </para>
    /// <param name="size">The em size.</param>
    public static Length ChartLineHeightAt(Length size)
        => Metrics.Value is { } metrics ? OnChartDevice(metrics).ScaledLineHeight(size) : size * 1.15;

    /// <summary>How far above a chart line's top its baseline sits, at a size.</summary>
    /// <remarks>
    /// The chart-device counterpart of <see cref="AscentAt(Length)"/>, and it exists for the same
    /// reason <see cref="ChartLineHeightAt(Length)"/> does: a chart's text is quantised onto
    /// <see cref="MetricGrid.Chart"/>'s 96 dpi pixels and Calc's own text onto 720 dpi ones. At
    /// ten point in Carlito this answers <b>9.01</b> where <see cref="AscentAt(Length)"/> answers
    /// 9.52, and the reference draws 9.00.
    /// <para>
    /// <b>The two errors used to cancel and that is why this went unseen.</b> A chart label is
    /// drawn at <c>blockCentre − blockHeight/2 + ascent</c>; the old height was 0.50 pt too tall
    /// and the old ascent 0.51 pt too high, so a <em>single-line</em> label landed within 0.01 pt
    /// of the reference's. Both have to move together or the labels that agree today stop
    /// agreeing.
    /// </para>
    /// </remarks>
    /// <param name="size">The em size.</param>
    public static Length ChartAscentAt(Length size)
        => Metrics.Value is { } metrics ? OnChartDevice(metrics).ScaledAscent(size) : size * 0.9;

    /// <summary>
    /// The metrics of a named face, or the furniture's own where it names none.
    /// </summary>
    /// <remarks>
    /// The furniture itself never names one — a header and a column heading are drawn in whatever
    /// a plain cell would be — but a shape's text does, and it is laid out by the same three calls.
    /// Returning null for a family that cannot be resolved would silently lose the text; falling
    /// back to the default face loses only the face, which is what a substitution is.
    /// </remarks>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    /// <param name="bold">
    /// Whether the family's bold face is wanted. Asking for bold of a family that has no bold
    /// face resolves back to its regular one rather than to nothing.
    /// </param>
    /// <param name="italic">
    /// Whether its italic face is wanted, on the same terms. A band takes both from the
    /// workbook's own default cell font, which the reference honours.
    /// </param>
    private static (OpenTypeFace? Face, FontReference Reference, LineMetrics? Metrics) FaceFor(
        string? family, bool bold = false, bool italic = false)
    {
        if (string.IsNullOrWhiteSpace(family) && !bold && !italic)
            return (Resolved.Value.Face, Description, Metrics.Value);

        return SheetFonts.ForFamily(family, bold, italic) is { } named
            ? (named.Face, named.Reference, named.Metrics)
            : (Resolved.Value.Face, Description, Metrics.Value);
    }

    /// <summary>The distance from a line's top to its baseline, at a size, in a named face.</summary>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    public static Length AscentAt(Length size, string? family)
        => FaceFor(family).Metrics is { } metrics ? metrics.ScaledAscent(size) : size * 0.9;

    /// <inheritdoc cref="AscentAt(Length, string?)"/>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    /// <param name="bold">Whether the family's bold face is wanted.</param>
    /// <param name="italic">Whether its italic face is wanted.</param>
    public static Length AscentAt(Length size, string? family, bool bold, bool italic)
        => FaceFor(family, bold, italic).Metrics is { } metrics
            ? metrics.ScaledAscent(size)
            : size * 0.9;

    /// <inheritdoc cref="LineHeightAt(Length)"/>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    public static Length LineHeightAt(Length size, string? family)
        => FaceFor(family).Metrics is { } metrics
            ? metrics.ScaledAscent(size) + metrics.ScaledDescent(size)
            : size * 1.15;

    /// <inheritdoc cref="LineHeightAt(Length, string?)"/>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    /// <param name="bold">Whether the family's bold face is wanted.</param>
    /// <param name="italic">Whether its italic face is wanted.</param>
    public static Length LineHeightAt(Length size, string? family, bool bold, bool italic)
        => FaceFor(family, bold, italic).Metrics is { } metrics
            ? metrics.ScaledAscent(size) + metrics.ScaledDescent(size)
            : size * 1.15;

    /// <inheritdoc cref="ChartLineHeightAt(Length)"/>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    public static Length ChartLineHeightAt(Length size, string? family)
        => ChartLineHeightAt(size, family, bold: false);

    /// <inheritdoc cref="ChartLineHeightAt(Length)"/>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    /// <param name="bold">Whether the family's bold face is wanted.</param>
    public static Length ChartLineHeightAt(Length size, string? family, bool bold)
        => FaceFor(family, bold).Metrics is { } metrics
            ? OnChartDevice(metrics).ScaledLineHeight(size)
            : size * 1.15;

    /// <inheritdoc cref="ChartAscentAt(Length)"/>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    public static Length ChartAscentAt(Length size, string? family)
        => ChartAscentAt(size, family, bold: false);

    /// <inheritdoc cref="ChartAscentAt(Length)"/>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    /// <param name="bold">Whether the family's bold face is wanted.</param>
    public static Length ChartAscentAt(Length size, string? family, bool bold)
        => FaceFor(family, bold).Metrics is { } metrics
            ? OnChartDevice(metrics).ScaledAscent(size)
            : size * 0.9;

    /// <summary>
    /// How tall one line of a Calc <em>drawing shape's</em> text is, at a size.
    /// </summary>
    /// <remarks>
    /// The arithmetic <see cref="ChartLineHeightAt(Length)"/> had before round 60 — the face's own
    /// <c>ascent + descent + lineGap</c>, on no device — kept under its own name so that the shape
    /// path is visibly a separate, <b>unmeasured</b> claim rather than an accident of sharing a
    /// function with the chart path. See the remark on <see cref="Ungridded(LineMetrics)"/> for
    /// what is and is not known about it.
    /// </remarks>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    public static Length ShapeLineHeightAt(Length size, string? family)
        => FaceFor(family).Metrics is { } metrics
            ? Ungridded(metrics).ScaledLineHeight(size)
            : size * 1.15;

    /// <inheritdoc cref="AscentAt(Length)"/>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    /// <param name="bold">Whether the family's bold face is wanted.</param>
    public static Length AscentAt(Length size, string? family, bool bold)
        => FaceFor(family, bold).Metrics is { } metrics ? metrics.ScaledAscent(size) : size * 0.9;

    /// <summary>
    /// How far above the baseline a capital reaches, at a size — the top of a line's <em>ink</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wanted because <c>ScPrintFunc::PrintHF</c> clips a band's text to the band's own rectangle
    /// and <c>ImpEditEngine::DrawText_ToPosition</c> throws away an area whose <em>primitive
    /// range</em> does not meet that rectangle
    /// (<c>editeng/source/editeng/impedit3.cxx:3367-3372</c>) — and a primitive range is the ink,
    /// not the line box. The distance from a line's top to its ink is therefore what decides
    /// whether a short band draws anything at all, and it is <c>ascent - capHeight</c>: 0.217 em
    /// for Liberation Sans, so 1.74 pt at 8 pt and 4.34 pt at 20 pt.
    /// </para>
    /// <para>
    /// Those two numbers are why this is here. Round 55 measured that a band draws nothing at
    /// 1.44 pt of 8 pt text and everything at 2.16 pt, and recorded an unexplained "text-fit
    /// threshold, about 0.27x the point size". It is not a threshold and nothing is fitted; it is
    /// this distance, and <c>probes/sheets-r56/probe-bandclip.py</c> reproduces both brackets from
    /// it with no free parameter.
    /// </para>
    /// <para>
    /// <strong>Cap height is a proxy for the glyph bounding box</strong>, which is what
    /// drawinglayer actually measures. It is exact for capitals and too high for text that reaches
    /// no further than the x-height, where it makes us draw a little more readily than the
    /// reference. The alternative is parsing <c>glyf</c> per glyph, which buys nothing on this
    /// corpus: the nearest case to the boundary is 85 pt clear of it.
    /// </para>
    /// </remarks>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    public static Length CapHeightAt(Length size, string? family)
        => CapHeightAt(size, family, bold: false, italic: false);

    /// <inheritdoc cref="CapHeightAt(Length, string?)"/>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    /// <param name="bold">Whether the family's bold face is wanted.</param>
    /// <param name="italic">Whether its italic face is wanted.</param>
    public static Length CapHeightAt(Length size, string? family, bool bold, bool italic)
    {
        (OpenTypeFace? face, _, LineMetrics? metrics) = FaceFor(family, bold, italic);
        Length ascent = metrics is { } resolved ? resolved.ScaledAscent(size) : size * 0.9;

        int units = face?.UnitsPerEm > 0 ? face.UnitsPerEm : 1000;
        int cap = face?.Os2?.CapHeight ?? 0;
        Length height = cap > 0
            ? size * ((double)cap / units * (1 + RoundCapitalOvershoot))
            : size * DefaultCapHeightRatio * (1 + RoundCapitalOvershoot);

        // Never taller than the ascent the same face reports, because the caller subtracts this
        // from the ascent and a negative answer would put a line's ink above its own box.
        return height > ascent ? ascent : height;
    }

    /// <summary>
    /// How far a round capital rises above the cap line, as a fraction of the cap height.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reference measures a line's ink with <c>OutputDevice::GetTextBoundRect</c>, which is
    /// the <em>glyph</em> bounding rectangle, and <c>O</c>, <c>C</c>, <c>S</c> and every other
    /// round capital is drawn a little above the flat ones so that it does not read as short.
    /// Cap height alone therefore under-states the ink of ordinary header text and, because the
    /// caller subtracts it from the ascent, over-states how tall a band has to be before anything
    /// is drawn in it — which is the failure that loses text.
    /// </para>
    /// <para>
    /// Measured rather than assumed. <c>probes/sheets-r56/probe-bandclip.py</c> bisects the band
    /// at which 26.2.4.2 starts drawing, in 0.1 pt steps at three sizes, and the mm100 rounding
    /// the margins go through brackets the ratio at <strong>0.2056 to 0.2087 em</strong> — 8 pt
    /// turns over between 1.59 and 1.70, 11 pt between 2.21 and 2.30, 20 pt between 4.11 and
    /// 4.20. Liberation Sans' bare <c>ascent - capHeight</c> is 0.2173 em, which is outside that
    /// bracket on the wrong side; two per cent of overshoot puts it at 0.2035, which is outside
    /// it on the side that draws.
    /// </para>
    /// <para>
    /// <strong>Deliberately biased towards drawing.</strong> Two per cent is a little more than
    /// the bracket needs, because being wrong here in the other direction deletes a header
    /// nobody asked to delete, and the only corpus case this rule reaches is 85 pt clear of the
    /// boundary either way.
    /// </para>
    /// </remarks>
    private const double RoundCapitalOvershoot = 0.02;

    /// <summary>What a face with no usable <c>OS/2</c> capital height is assumed to have.</summary>
    /// <remarks>
    /// 0.7 em, which is within a few thousandths of every face this corpus resolves to —
    /// Liberation Sans is 0.688, Liberation Serif 0.662, DejaVu Sans 0.729. It is only ever
    /// reached by a version-0 or version-1 <c>OS/2</c> table, which states no capital height at
    /// all.
    /// </remarks>
    private const double DefaultCapHeightRatio = 0.7;

    /// <summary>Shapes one line, or null when there is no face to shape it with.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The em size.</param>
    public static BandRun? Shape(string text, Length size) => Shape(text, size, null);

    /// <inheritdoc cref="Shape(string, Length)"/>
    /// <param name="text">The text.</param>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    public static BandRun? Shape(string text, Length size, string? family)
        => Shape(text, size, family, bold: false);

    /// <inheritdoc cref="Shape(string, Length)"/>
    /// <param name="text">The text.</param>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    /// <param name="bold">
    /// Whether to shape in the family's bold face. It has to be the same decision the measurer
    /// made or the two come apart — a title measured regular and drawn bold overruns the room it
    /// reserved — which is why <c>SheetChart</c> passes the same flag to both.
    /// </param>
    public static BandRun? Shape(string text, Length size, string? family, bool bold)
        => Shape(text, size, family, bold, italic: false);

    /// <inheritdoc cref="Shape(string, Length, string?, bool)"/>
    /// <param name="text">The text.</param>
    /// <param name="size">The em size.</param>
    /// <param name="family">The family name, or null for the furniture's own face.</param>
    /// <param name="bold">Whether to shape in the family's bold face.</param>
    /// <param name="italic">Whether to shape in its italic face.</param>
    public static BandRun? Shape(string text, Length size, string? family, bool bold, bool italic)
    {
        if (text.Length == 0) return null;

        (OpenTypeFace? resolved, FontReference reference, _) = FaceFor(family, bold, italic);
        if (resolved is not { } face) return null;

        ShapedText shaped = TextShaper.Default.Shape(face, text);

        List<PositionedGlyph> glyphs = new(shaped.Glyphs.Count);
        List<int> clusters = new(shaped.Glyphs.Count);
        Length pen = Length.Zero;

        foreach (ShapedGlyph glyph in shaped.Glyphs)
        {
            Length advance = shaped.Scale(glyph.Advance, size);
            glyphs.Add(new PositionedGlyph(
                glyph.GlyphId,
                new DocPoint(
                    pen + shaped.Scale(glyph.OffsetX, size),
                    -shaped.Scale(glyph.OffsetY, size)),
                advance));
            clusters.Add(glyph.Cluster);
            pen += advance;
        }

        return new BandRun(glyphs, clusters, reference, size, text, pen);
    }

    /// <summary>
    /// How a backend names the furniture's face: the resolver's own key, which is a file path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reference is kept from the resolution rather than rebuilt from the loaded face. Naming
    /// the family instead — <c>FaceKey = face.FamilyName</c>, which is what this did — gives
    /// <c>FileFontProvider</c> a key it cannot open, so the PDF writer referenced the face and
    /// embedded no <c>FontFile2</c> for it. A reader then substitutes or draws tofu, and neither
    /// the page count nor the extracted words change, which is why the sweep never saw it.
    /// </para>
    /// <para>
    /// Measured with <c>pdffonts</c> on <c>sheet-features.ods</c>: the two cell faces reported
    /// <c>emb yes</c> and the header's third face <c>emb no</c>, in a file whose text extracted
    /// correctly throughout.
    /// </para>
    /// </remarks>
    private static FontReference Description =>
        Resolved.Value.Reference
        ?? new FontReference { FamilyName = DefaultFamily, FaceKey = string.Empty };

    private static (OpenTypeFace? Face, FontReference? Reference) Load()
    {
        try
        {
            SystemFontResolver resolver = SystemFontResolver.Build();
            FontReference reference = resolver.Resolve(new FontRequest(DefaultFamily));
            return (resolver.LoadOpenType(reference), reference);
        }
        catch (Exception exception) when (exception is Core.MalformedDocumentException
                                             or IOException
                                             or UnauthorizedAccessException)
        {
            // No readable face is not a reason to fail a layout: the page, its geometry and
            // everything drawn as a path are already decided, and only the ink is missing.
            return (null, null);
        }
    }
}

/// <summary>A shaped line of furniture text, positioned once it is placed.</summary>
/// <remarks>
/// Shaped without an origin and given one later, because where it starts depends on its own
/// width: a header's right part ends at the band's right edge, so its start is only known once it
/// has been measured.
/// </remarks>
internal sealed class BandRun
{
    private readonly List<PositionedGlyph> _glyphs;
    private readonly List<int> _clusters;
    private readonly FontReference _font;
    private readonly Length _size;
    private readonly string _text;

    internal BandRun(
        List<PositionedGlyph> glyphs,
        List<int> clusters,
        FontReference font,
        Length size,
        string text,
        Length width)
    {
        _glyphs = glyphs;
        _clusters = clusters;
        _font = font;
        _size = size;
        _text = text;
        Width = width;
    }

    /// <summary>How far the run's pen travels.</summary>
    public Length Width { get; }

    /// <summary>The run placed at a baseline origin.</summary>
    public GlyphRun At(DocPoint origin) => new()
    {
        Font = _font,
        FontSize = _size,
        Origin = origin,
        Glyphs = _glyphs,
        Text = _text,
        ClusterMap = _clusters,
    };
}
