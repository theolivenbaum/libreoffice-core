using Paperless.Core.Units;

namespace Paperless.Text.Fonts;

/// <summary>Which of a font's competing metric sets a line height was derived from.</summary>
/// <remarks>
/// Reported rather than hidden. A line-height difference is one of the most visible ways two
/// renderers diverge on the same input, and knowing which set was believed turns an inexplicable
/// half-page offset into a one-line answer.
/// </remarks>
public enum LineMetricSource
{
    /// <summary>The <c>hhea</c> table, which is what a line is measured from unless something wins.</summary>
    HorizontalHeader,

    /// <summary>
    /// <c>OS/2</c>'s <c>usWinAscent</c> and <c>usWinDescent</c> — the historical Windows metrics,
    /// used when <c>hhea</c> states nothing usable.
    /// </summary>
    WindowsMetrics,

    /// <summary>
    /// <c>OS/2</c>'s typographic metrics, used when the font's <c>fsSelection</c> asks for them.
    /// </summary>
    TypographicMetrics,

    /// <summary>Nothing usable; the em square was assumed.</summary>
    Fallback,
}

/// <summary>
/// The device a font's metrics are quantised through before layout sees them.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is always a device.</b> Writer never scales a face's design units straight to the size the
/// document asks for — it formats against a reference device, and every vertical metric is quantised
/// onto that device's pixel grid on the way in and back onto the document's own unit on the way out.
/// Which device it is depends on one compatibility flag:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Normally a <c>VirtualDevice</c> in <c>VirtualDevice::RefDevMode::MSO1</c> with
/// <c>MapUnit::MapTwip</c> — <c>DocumentDeviceManager::CreateVirtualDevice_</c>,
/// <c>sw/source/core/doc/DocumentDeviceManager.cxx</c>:259. <c>MSO1</c> is <b><c>6*1440</c> = 8640
/// dpi</b> (<c>vcl/source/gdi/virdev.cxx</c>:407), so one twip is exactly six device pixels. See
/// <see cref="Reference"/>.
/// </description></item>
/// <item><description>
/// A real printer when the document asks for one: Word's "use printer metrics to lay out document"
/// makes <c>WW8Dop::fUsePrinterMetrics</c> into <c>!USE_VIRTUAL_DEVICE</c>
/// (<c>sw/source/filter/ww8/ww8par.cxx</c>:2008) and <c>getReferenceDevice</c> hands out an
/// <c>SfxPrinter</c>. See <see cref="Printer"/>.
/// </description></item>
/// </list>
/// <para>
/// The difference between the two is only the coarseness, and on the printer it is not small: at
/// 300 dpi a pixel is 4.8 twips, so Liberation Sans at 11 pt measures 13.00 pt per line rather than
/// the 12.65 pt its design units give — 2.8%, which over a long document is many pages. On the
/// virtual device a pixel is a sixth of a twip and the effect is worth exactly one twip, in either
/// direction, on about one line height in nine. That one twip is what
/// <c>dotnet/probes/lineheight-01/</c> is about: it moved 22 of 195 measured (face, size) pairs and
/// two earlier rounds could not reconstruct it because they swept 72–6000 dpi and the answer is
/// 8640.
/// </para>
/// <para>
/// 300 dpi for the printer because that is what a headless LibreOffice's printer reports:
/// <c>PPDParser</c> defaults both axes to 300 when the queue names no resolution and when there is no
/// PPD at all (<c>vcl/unx/generic/printer/ppdparser.cxx</c>:1500 and :1524). The resolution is the
/// whole of what the device contributes there, so a machine whose default queue says otherwise would
/// need a different number — which is the honest cost of a document asking to be laid out against
/// hardware.
/// </para>
/// <para>
/// <b>The logical unit is the twip</b>, because that is the map mode Writer's reference device is set
/// to. Calc's and Impress's reference devices are in 1/100 mm and Impress's is 600 dpi rather than
/// 8640, so neither is this grid; both still scale exactly here and each would need its own.
/// </para>
/// </remarks>
/// <param name="Dpi">The device resolution the metrics are rounded onto.</param>
/// <param name="QuantisesAdvances">
/// Whether horizontal advances go through the grid as well as the vertical metrics.
/// <para>
/// True only for a real printer, and measured rather than assumed: <c>probes/printer-metric-advance.py</c>
/// sweeps 96 authored rows with <c>fUsePrinterMetrics</c> varied on one body, and the quantised rule is
/// exact on all 96 with the flag set and out by 6.73 pt with it clear, where unquantised scaling is exact
/// on all 96. The dominant term is the em rounding, which a 300 dpi device makes worth 1.3% of every
/// advance and an 8640 dpi device makes exactly nothing — so on the virtual reference device the two
/// rules differ by less than a twip and the evidence says to take the one that was measured.
/// </para>
/// </param>
/// <param name="ScalesEastAsianFaces">
/// Whether Word's <c>MS_WORD_COMP_GRID_METRICS</c> compatibility rule applies: a face declaring one of
/// four East Asian code pages has its ascent, and its ascent and descent together, scaled by 127%
/// before the leading is added. See <see cref="AsWordDocument"/> and <see cref="EastAsianScaled"/>.
/// </param>
public readonly record struct MetricGrid(int Dpi, bool QuantisesAdvances, bool ScalesEastAsianFaces = false)
{
    /// <summary>A grid at a resolution, quantising advances as a real device does.</summary>
    public MetricGrid(int dpi) : this(dpi, true) { }

    /// <summary>The grid a document asking for printer metrics is laid out on.</summary>
    public static MetricGrid Printer { get; } = new(300, QuantisesAdvances: true);

    /// <summary>
    /// The virtual reference device Writer formats every other document against: 8640 dpi, six
    /// device pixels to the twip.
    /// </summary>
    public static MetricGrid Reference { get; } = new(6 * 1440, QuantisesAdvances: false);

    /// <summary>
    /// The same grid with Word's East Asian line scale switched on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MS_WORD_COMP_GRID_METRICS</c> is a document compatibility setting rather than a property of
    /// the device, and it is off by default — <c>DocumentSettingManager</c> initialises
    /// <c>mbMsWordCompGridMetrics(false)</c>, and an ODF file carries its own value. So the DOC and
    /// DOCX readers ask for this and the ODF one does not, which is measured rather than reasoned:
    /// the same two lines of WenQuanYi Zen Hei at 12 pt are 406 twips apart when LibreOffice reads
    /// them from a <c>.docx</c> and 325 apart when it reads them from a <c>.fodt</c>.
    /// </para>
    /// <para>
    /// Applied to whichever grid the document already asked for, because the flag and the device are
    /// independent in the C++ too: every call site of <c>lcl_ApplyCjkHeightAdjustment</c> passes the
    /// reference device it happens to have and asks the document for the flag separately. The
    /// printer combination is unmeasured — no corpus document sets <c>usePrinterMetrics</c> and
    /// names an East Asian face — and is written this way because that is what the C++ does, not
    /// because it has been seen.
    /// </para>
    /// </remarks>
    public MetricGrid AsWordDocument() => this with { ScalesEastAsianFaces = true };

    /// <summary>
    /// The 127% East Asian line scale, or the value unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>lcl_ApplyCjkHeightAdjustment</c> (<c>sw/source/core/txtnode/fntcache.cxx</c>:270-292,
    /// tdf#129808): with <c>MS_WORD_COMP_GRID_METRICS</c> set and the face declaring CP932, CP936,
    /// CP949 or CP950, <c>(nBase * 127) / 100</c> — integer division, on the value in twips.
    /// </para>
    /// <para>
    /// <b>What it multiplies is not the finished line height.</b> <c>GetFontHeight</c> reads
    /// <c>lcl_ApplyCjkHeightAdjustment(m_nPrtHeight, …) + GetFontLeading(…)</c>, so the scale
    /// reaches the device's ascent-plus-descent and the face's leading is added afterwards,
    /// unscaled; <c>GetFontAscent</c> is the same shape. That distinction is invisible on IPAGothic,
    /// whose <c>hhea</c> line gap is zero — which is why <c>probes/lineheight-01</c> §7(a) recorded
    /// the rule as scaling the height itself and was exact on all 39 of its pairs anyway. WenQuanYi
    /// Zen Hei has a gap of 92/1024 and separates them: at 12 pt the two rules give 412 twips and
    /// 406, and LibreOffice draws 406. See <c>probes/words-metrics-01/probe-cjk127.py</c>, which
    /// scores both against 117 measured pairs over three faces — this rule 117, the other 78.
    /// </para>
    /// </remarks>
    /// <param name="value">The ascent, or the ascent and descent together, before the leading.</param>
    /// <param name="face">Whether the face declares one of the four East Asian code pages.</param>
    public Length EastAsianScaled(Length value, bool face)
        => ScalesEastAsianFaces && face
            ? Length.FromTwips(value.Twips * 127 / 100)
            : value;

    /// <summary>Twips per device pixel on this grid.</summary>
    private double TwipsPerPixel => 1440.0 / Dpi;

    /// <summary>
    /// A design-unit measurement in whole device pixels at an em size.
    /// </summary>
    /// <remarks>
    /// <c>FontMetricData::ImplCalcLineSpacing</c> ends with three separate <c>round()</c> calls, one
    /// per metric (<c>vcl/source/font/fontmetric.cxx</c>:538-540). C++ <c>round</c> takes a half away
    /// from zero; .NET's <c>Math.Round</c> takes it to even, and on a grid this fine the halves are
    /// common rather than exotic — a sixth of the line gaps in the corpus land on one.
    /// </remarks>
    public long ToPixels(int designUnits, int unitsPerEm, Length emSize)
    {
        if (unitsPerEm <= 0 || Dpi <= 0) return 0;

        double em = Math.Round(emSize.Twips / TwipsPerPixel, MidpointRounding.AwayFromZero);
        return (long)Math.Round(designUnits * em / unitsPerEm, MidpointRounding.AwayFromZero);
    }

    /// <summary>Whole device pixels back in whole twips.</summary>
    /// <remarks>
    /// <c>CoordinateMapper::ViewToLogicDistanceY</c> is an <c>llround</c>
    /// (<c>vcl/source/outdev/CoordinateMapper.cxx</c>:279), which is again half away from zero.
    /// </remarks>
    public Length ToLength(long pixels)
        => Dpi <= 0
            ? Length.Zero
            : Length.FromTwips((long)Math.Round(pixels * TwipsPerPixel, MidpointRounding.AwayFromZero));

    /// <summary>
    /// An advance width as the device measures it: the whole run's advance in device pixels,
    /// <b>truncated</b>, and only then converted back to a length.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two quantisations, and the first is much the larger. The em is rounded to whole device
    /// pixels before any advance is scaled through it — <see cref="ToEmSize"/>'s rounding — so at
    /// 9 pt on a 300 dpi grid the device sets 38 pixels for 37.5 and <em>every</em> advance comes
    /// out 1.33% wider than the size the document asked for. The truncation that follows is worth
    /// at most one pixel, 0.24 pt, on a whole portion, and pulls the other way.
    /// </para>
    /// <para>
    /// <b>The truncation is of the total, not of each glyph.</b> That distinction is the whole of
    /// what <c>dotnet/probes/printer-metric-advance.py</c> settles: over 96 authored rows — two
    /// faces, four sizes, three glyphs, four run lengths, with <c>fUsePrinterMetrics</c> varied on
    /// one body — this rule is exact on 96 and rounding each glyph's advance separately is out by
    /// up to 6.96 pt. Rounding per glyph is what <c>GenericSalLayout::LayoutText</c> appears to
    /// say (<c>vcl/source/gdi/CommonSalLayout.cxx</c>:826-831, <c>std::round</c> when subpixel
    /// positioning is off) and it is not what the binary does: a mapped device turns subpixel
    /// positioning <em>on</em>, so the advances stay exact and the truncation happens once, where
    /// the device width is converted to logical units.
    /// </para>
    /// <para>
    /// On the virtual reference device none of this applies and the caller passes no grid — the
    /// same probe's control half has unquantised scaling exact on 96 of 96 there, and this rule
    /// out by 6.73 pt.
    /// </para>
    /// </remarks>
    /// <param name="designUnits">The run's advance in the face's design units.</param>
    /// <param name="unitsPerEm">The design grid that advance is in.</param>
    /// <param name="emSize">The font size the document asks for.</param>
    public Length ToAdvance(long designUnits, int unitsPerEm, Length emSize)
    {
        if (unitsPerEm <= 0 || Dpi <= 0) return Length.Zero;

        double em = Math.Round(emSize.Twips / TwipsPerPixel, MidpointRounding.AwayFromZero);
        return ToLength((long)Math.Floor(designUnits * em / unitsPerEm));
    }

    /// <summary>
    /// An em size as the device can actually set it: rounded to whole pixels and back.
    /// </summary>
    /// <remarks>
    /// A font is instantiated at an integer pixel size, so 11 pt on a 96 dpi device is 15 pixels
    /// rather than 14.667 and every advance it measures is 2.3% wider than the size asked for.
    /// That is invisible when the same device draws the text, and it is not invisible when one
    /// device measures and another draws — which is exactly what Calc does to decide a row's
    /// height (<see cref="MetricGrid"/>'s other users round the vertical metrics for the same
    /// reason).
    /// </remarks>
    public Length ToEmSize(Length emSize)
        => Dpi <= 0 || emSize <= Length.Zero
            ? emSize
            : ToLength((long)Math.Round(emSize.Twips / TwipsPerPixel, MidpointRounding.AwayFromZero));
}

/// <summary>
/// A font's vertical metrics as a line height, resolved from the several sets a font may carry.
/// </summary>
/// <param name="Ascent">Distance from the baseline to the line's top, in design units.</param>
/// <param name="Descent">Distance from the baseline to the line's bottom, positive, in design units.</param>
/// <param name="LineGap">Recommended extra leading between lines, in design units.</param>
/// <param name="Source">Which of the font's metric sets these came from.</param>
/// <param name="UnitsPerEm">The design grid the three measurements are in.</param>
/// <param name="Grid">
/// The device the measurements are rounded through, or null to scale them exactly — which is the usual
/// case and what a virtual reference device does. See <see cref="MetricGrid"/>.
/// </param>
/// <param name="LeadingAboveText">
/// Whether the face's external leading is charged to the ascent rather than left below the text.
/// <b>This is a property of the application, not of the font</b>, and LibreOffice's two text engines
/// disagree about it — see the remark on <see cref="ScaledAscent"/>.
/// </param>
/// <param name="DeclaresEastAsianCodePage">
/// Whether the face claims coverage of CP932, CP936, CP949 or CP950, which is what
/// <see cref="MetricGrid.ScalesEastAsianFaces"/> acts on. A property of the font rather than of the
/// text: Word scales the line for such a face even where the run holds nothing but Latin.
/// </param>
public readonly record struct LineMetrics(
    int Ascent,
    int Descent,
    int LineGap,
    LineMetricSource Source,
    int UnitsPerEm,
    MetricGrid? Grid = null,
    bool LeadingAboveText = false,
    bool DeclaresEastAsianCodePage = false)
{
    /// <summary>The distance from one baseline to the next, in design units.</summary>
    public int LineHeight => Ascent + Descent + LineGap;

    /// <summary>
    /// The line height at an em size.
    /// </summary>
    /// <param name="emSize">The font size the document asks for.</param>
    public Length ScaledLineHeight(Length emSize)
        => Grid is { } grid
            ? grid.EastAsianScaled(TextHeightOn(grid, emSize), DeclaresEastAsianCodePage)
              + LeadingOn(grid, emSize)
            : Scale(LineHeight, emSize);

    /// <summary>The ascent at an em size.</summary>
    /// <remarks>
    /// <para>
    /// Where a face's external leading sits in the line box is decided by the application, not by the
    /// font, and LibreOffice's two text engines answer differently — so this is
    /// <see cref="LeadingAboveText"/>'s question rather than a rule that can be applied here once.
    /// </para>
    /// <para>
    /// <b>Writer puts it above.</b> <c>SwFntObj::GetFontAscent</c> adds the external leading to the
    /// ascent it read from the device, guarded only by <c>#if !defined(MACOSX)</c> and carrying a TODO
    /// to do it on the other platforms too (<c>sw/source/core/txtnode/fntcache.cxx</c>:326-329);
    /// <c>GetFontHeight</c> adds the same leading to ascent-plus-descent (<c>:370-371</c>), so the
    /// descent stays the face's own and the three quantities close.
    /// </para>
    /// <para>
    /// <b>EditEngine does not</b>, which is what Impress, Calc and Writer's own drawing objects format
    /// through. <c>ImpEditEngine::RecalcFormatterFontMetrics</c> adds it only when
    /// <c>IsAddExtLeading()</c> (<c>editeng/source/editeng/impedit3.cxx</c>:3133-3135), and that flag is
    /// false unless something turns it on — <c>ImpEditEngine</c> initialises it so
    /// (<c>impedit2.cxx</c>:118), as does <c>SdrModel</c> (<c>svx/source/svdraw/svdmodel.cxx</c>:161).
    /// Only Writer's document compatibility setting and Math's own engine ever set it. A
    /// <c>FormatterFontMetric</c>'s height is then <c>nMaxAscent + nMaxDescent</c> with no gap in it.
    /// </para>
    /// <para>
    /// Measured on Liberation Sans, whose <c>hhea</c> gap is 67/2048, both ways round: Writer's first
    /// baseline sits 206 twips below a 72 pt top margin at 11 pt (72 + 9.958 + 0.360, each rounded to
    /// whole twips) where the ascent alone would give 199; and Impress puts two 18 pt baselines in a
    /// table cell 20.154 pt apart, which is ascent-plus-descent and not the 20.698 the gap would add.
    /// Both figures are read out of LibreOffice's own PDF content stream. A face whose gap is zero —
    /// Carlito, which nearly every OOXML document in the corpus resolves to through its theme — is
    /// identical either way, which is why getting this wrong stayed invisible for so long.
    /// </para>
    /// </remarks>
    public Length ScaledAscent(Length emSize)
        => Grid is { } grid
            ? grid.EastAsianScaled(
                  grid.ToLength(grid.ToPixels(Ascent, UnitsPerEm, emSize)),
                  DeclaresEastAsianCodePage)
              + (LeadingAboveText ? LeadingOn(grid, emSize) : Length.Zero)
            : Scale(LeadingAboveText ? Ascent + LineGap : Ascent, emSize);

    /// <summary>The descent at an em size.</summary>
    public Length ScaledDescent(Length emSize)
        => Grid is { } grid
            ? ScaledLineHeight(emSize) - ScaledAscent(emSize)
            : Scale(Descent, emSize);

    /// <summary>
    /// The ascent and descent together, as one rounding rather than two.
    /// </summary>
    /// <remarks>
    /// <c>OutputDevice::GetTextHeight</c> converts the summed device-pixel ascent and descent to logical
    /// units in a single step, so rounding each and adding gives a different answer on the grids where it
    /// matters.
    /// </remarks>
    private Length TextHeightOn(MetricGrid grid, Length emSize)
        => grid.ToLength(
            grid.ToPixels(Ascent, UnitsPerEm, emSize) + grid.ToPixels(Descent, UnitsPerEm, emSize));

    private Length LeadingOn(MetricGrid grid, Length emSize)
        => grid.ToLength(grid.ToPixels(LineGap, UnitsPerEm, emSize));

    /// <summary>
    /// The internal leading at an em size: how much of the line height is above and below the em.
    /// </summary>
    /// <remarks>
    /// Derived, not read from the font — ascent plus descent minus the requested size, which is the
    /// classic Windows definition and what single line spacing consumes. A font whose Windows metrics
    /// exceed its em square, which most do, therefore has positive internal leading, and that is
    /// where "single-spaced" lines get the gap they visibly have.
    /// </remarks>
    public Length ScaledInternalLeading(Length emSize)
        => Scale(Ascent + Descent, emSize) - emSize;

    private Length Scale(int designUnits, Length emSize)
        => UnitsPerEm <= 0
            ? Length.Zero
            : Length.FromEmu((long)Math.Round((double)designUnits * emSize.Emu / UnitsPerEm));
}

/// <summary>
/// Derives a line height from a face's metrics, the way LibreOffice does.
/// </summary>
/// <remarks>
/// <para>
/// Fonts disagree about which of their own metric sets to believe, so there is no single field to
/// read. The precedence is specific, and it is specific for historical reasons rather than
/// typographic ones (<c>research/06-rendering.md</c> section B.4):
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <c>hhea</c> first, since it is mandatory — but only if its signs are right. A font whose ascent is
/// negative or whose descent is positive has them the wrong way round, real fonts do this, and
/// believing one puts the baseline outside the line. This is what a line is normally measured from.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>OS/2</c>'s <c>usWinAscent</c> and <c>usWinDescent</c> only when <c>hhea</c> yielded nothing.
/// They carry no leading of their own, and none is borrowed from <c>hhea</c> — a line measured from
/// the Windows metrics is exactly ascent plus descent.
/// </description>
/// </item>
/// <item>
/// <description>
/// Over either of those, the typographic metrics when <c>fsSelection</c> bit 7 is set, which is the
/// font saying "believe my real typographic metrics, not the historically bloated Windows ones".
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>The Windows metrics are not the default, and this is worth stating because the received wisdom
/// says they are.</b> They were the default in LibreOffice once; today
/// <c>FontMetricData::ImplCalcLineSpacing</c> (<c>vcl/source/font/fontmetric.cxx</c>:434) reaches them
/// only when <c>hhea</c> gave nothing or when the family is one of four entries in the
/// <c>Office::Common::Misc::FontsUseWinMetrics</c> exception list — fonts known to state metrics that
/// make them unreadable. The list is not reproduced here: none of its four faces is one a document in
/// any corpus measured so far asks for, and honouring it needs the family name that
/// <see cref="Resolve"/> deliberately does not take.
/// </para>
/// <para>
/// Measured rather than read: a paragraph set in IPAGothic at 20pt, whose <c>hhea</c> and Windows
/// metrics differ by 7.6% of the em, renders with a 20.00pt line advance in LibreOffice 24.2 — the
/// <c>hhea</c> figure exactly. Across every font installed on the reference machine the two rules
/// disagree on three faces, all CJK, which is why believing the wrong one went unnoticed.
/// </para>
/// <para>
/// Getting the order wrong does not produce an error. It produces a line height a few per cent out,
/// which moves every baseline on the page and eventually moves a page break — so a document renders
/// plausibly and disagrees with the reference everywhere.
/// </para>
/// </remarks>
public static class LineSpacing
{
    /// <summary>
    /// The ascent and descent, as a fraction of the em, assumed for a face with no usable metrics.
    /// </summary>
    /// <remarks>
    /// Four-fifths above the baseline and one-fifth below, which is roughly where a Latin font puts
    /// them. A face this broken will not lay out correctly whatever is assumed; the point is to
    /// produce something rather than a zero-height line that makes every page infinitely long.
    /// </remarks>
    private const double FallbackAscentFraction = 0.8;

    /// <summary>Resolves a face's line metrics.</summary>
    /// <param name="face">The face to measure.</param>
    /// <param name="grid">
    /// The device grid to round the metrics through, or null to scale them exactly. Only a document that
    /// asks to be laid out against a printer passes one — see <see cref="MetricGrid"/>.
    /// </param>
    /// <param name="leadingAboveText">
    /// Whether to charge the face's external leading to the ascent. True for Writer's own text, false
    /// for everything that formats through EditEngine — Impress, Calc and Writer's drawing objects.
    /// See <see cref="LineMetrics.ScaledAscent"/>, which carries the citations and the measurements.
    /// </param>
    public static LineMetrics Resolve(
        OpenTypeFace face, MetricGrid? grid = null, bool leadingAboveText = false)
    {
        ArgumentNullException.ThrowIfNull(face);

        int unitsPerEm = face.UnitsPerEm;

        // Step one: hhea, if its signs make sense. Descent is stored negative and used positive.
        int ascent = 0;
        int descent = 0;
        int lineGap = 0;
        LineMetricSource source = LineMetricSource.Fallback;

        if (face.Horizontal.IsPlausible
            && (face.Horizontal.Ascender != 0 || face.Horizontal.Descender != 0))
        {
            ascent = face.Horizontal.Ascender;
            descent = -face.Horizontal.Descender;
            lineGap = Math.Max(0, face.Horizontal.LineGap);
            source = LineMetricSource.HorizontalHeader;
        }

        // Step two: OS/2, for the two cases that beat hhea — hhea having said nothing at all, and the
        // font asking for its typographic metrics by name.
        if (face.Os2 is { } os2)
        {
            if (source == LineMetricSource.Fallback
                && (os2.WindowsAscent != 0 || os2.WindowsDescent != 0))
            {
                ascent = os2.WindowsAscent;
                descent = os2.WindowsDescent;

                // No leading. The Windows metrics state none, and hhea's cannot be borrowed: hhea is
                // why this branch was taken, so whatever it holds was already rejected.
                lineGap = 0;
                source = LineMetricSource.WindowsMetrics;
            }

            if (os2.UseTypoMetrics
                && os2.TypoAscender >= 0
                && os2.TypoDescender <= 0
                && (os2.TypoAscender != 0 || os2.TypoDescender != 0))
            {
                ascent = os2.TypoAscender;
                descent = -os2.TypoDescender;
                lineGap = Math.Max(0, os2.TypoLineGap);
                source = LineMetricSource.TypographicMetrics;
            }
        }

        if (ascent + descent <= 0)
        {
            ascent = (int)Math.Round(unitsPerEm * FallbackAscentFraction);
            descent = unitsPerEm - ascent;
            lineGap = 0;
            source = LineMetricSource.Fallback;
        }

        return new LineMetrics(
            ascent, descent, lineGap, source, unitsPerEm, grid, leadingAboveText,
            DeclaresEastAsianCodePage: face.Os2?.DeclaresEastAsianCodePage ?? false);
    }

    /// <summary>
    /// The families whose <c>post</c> underline metrics LibreOffice refuses to use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a workaround of ours. It is LibreOffice's own shipped configuration —
    /// <c>Office::Common::Misc::FontsDontUseUnderlineMetrics</c>, tdf#152267 and tdf#154235 —
    /// consulted by <c>FontMetricData::ShouldNotUseUnderlineMetrics</c>
    /// (<c>vcl/source/font/fontmetric.cxx:190</c>) before it will read the face's own numbers.
    /// </para>
    /// <para>
    /// It matters far more than three names suggest, because these three <em>are</em> the
    /// metric-compatible substitutes for Arial, Times New Roman and Courier New, so they are what
    /// most of a real corpus is actually set in. Their <c>post</c> tables are wrong in a way that
    /// shows: Liberation Serif Bold declares a thickness of 195 units and an offset of 28, which
    /// at 28 pt is a 2.67 pt rule drawn 0.38 pt under the baseline — nearly touching the text and
    /// almost twice as thick as the 1.53 pt rule at 2.30 pt that LibreOffice actually draws.
    /// </para>
    /// </remarks>
    private static readonly string[] FontsWithoutUsableUnderlineMetrics =
        ["Liberation Serif", "Liberation Sans", "Liberation Mono"];

    /// <summary>
    /// The underline and strikethrough metrics, in design units.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both come from tables that may be absent or zero, and a zero-thickness line draws nothing —
    /// so each falls back to a fraction of the em rather than being left at zero. The underline sits
    /// below the baseline, which the font records as a negative offset; the strikethrough sits above
    /// it, recorded positive.
    /// </para>
    /// <para>
    /// A face on <see cref="FontsWithoutUsableUnderlineMetrics"/> is treated as declaring nothing
    /// at all, which sends it down the same descent-derived path LibreOffice uses — see
    /// <see cref="FromDescent"/>.
    /// </para>
    /// </remarks>
    public static FontVerticalMetrics ResolveDecorations(OpenTypeFace face, LineMetrics line)
    {
        ArgumentNullException.ThrowIfNull(face);

        return ResolveDecorations(face.FamilyName, face.Post, face.Os2, line);
    }

    /// <summary>
    /// The same, from the four things the answer actually depends on.
    /// </summary>
    /// <remarks>
    /// The family name is one of them, and not incidentally: it is the whole discriminator for
    /// <see cref="FontsWithoutUsableUnderlineMetrics"/>.
    /// </remarks>
    /// <param name="family">The face's family name, as the blacklist spells it.</param>
    /// <param name="post">Its <c>post</c> table.</param>
    /// <param name="os2">Its <c>OS/2</c> table, or null when it has none.</param>
    /// <param name="line">Its resolved line metrics, in design units.</param>
    public static FontVerticalMetrics ResolveDecorations(
        string? family, PostTable post, Os2Table? os2, LineMetrics line)
    {
        if (family is not null
            && Array.IndexOf(FontsWithoutUsableUnderlineMetrics, family) >= 0)
        {
            return FromDescent(line);
        }

        int unitsPerEm = line.UnitsPerEm > 0 ? line.UnitsPerEm : 1000;

        int underlineThickness = post.UnderlineThickness > 0
            ? post.UnderlineThickness
            : Math.Max(1, unitsPerEm / 20);

        int underlinePosition = post.UnderlinePosition != 0
            ? post.UnderlinePosition
            : -Math.Max(1, unitsPerEm / 10);

        int strikeoutThickness = os2?.StrikeoutSize > 0
            ? os2.Value.StrikeoutSize
            : underlineThickness;

        // A quarter of the em above the baseline is roughly the middle of a lower-case letter, which
        // is where a strikethrough belongs when the font declines to say.
        int strikeoutPosition = os2?.StrikeoutPosition is > 0
            ? os2!.Value.StrikeoutPosition
            : Math.Max(1, unitsPerEm / 4);

        return new FontVerticalMetrics(
            line.Ascent,
            line.Descent,
            line.LineGap,
            underlinePosition,
            underlineThickness,
            strikeoutPosition,
            strikeoutThickness);
    }

    /// <summary>
    /// Decorations derived from the line metrics rather than from the face's own tables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>FontMetricData::ImplInitTextLineSize</c>, <c>vcl/source/font/fontmetric.cxx:261-330</c>,
    /// which is the path VCL takes for every font whose <c>post</c> metrics it will not read. A
    /// rule is a quarter of the descent thick and hangs half a descent below the baseline, less
    /// half its own thickness so that the stated offset is to its top; a strikethrough sits a
    /// third of the way up the ascent, less its internal leading.
    /// </para>
    /// <para>
    /// <strong>The clamp applies to the thickness and not to the offset</strong>, which is easy to
    /// miss and reads as arbitrary until you see the two variables: the C++ reassigns its local
    /// <c>nDescent</c> for the line height while <c>nUnderlineOffset</c> is computed from the
    /// member <c>mnDescent</c>. It fires on a face whose descent is more than a third of its
    /// ascent — #i55341, "for some fonts it is not a good idea to calculate their text line
    /// metrics from the real font descent".
    /// </para>
    /// <para>
    /// LibreOffice does this arithmetic in device units, so its results carry a rounding of one
    /// hundredth of a millimetre — 0.028 pt — that design units cannot reproduce. Measured against
    /// its own PDF for 28 pt Liberation Serif Bold, this gives 2.269 pt where it draws 2.296 and
    /// 1.518 pt thick where it draws 1.531: a tenth of a pixel at 300 dpi.
    /// </para>
    /// </remarks>
    /// <param name="line">The face's resolved line metrics, in design units.</param>
    private static FontVerticalMetrics FromDescent(LineMetrics line)
    {
        int descent = line.Descent > 0 ? line.Descent : Math.Max(1, line.Ascent / 10);
        int clamped = 3 * descent > line.Ascent ? line.Ascent / 3 : descent;

        int thickness = Math.Max(1, (clamped * 25 + 50) / 100);
        int half = thickness / 2;

        // The face's internal leading: how much of its line box sits outside the em.
        int internalLeading = Math.Max(0, line.Ascent + line.Descent - line.UnitsPerEm);

        return new FontVerticalMetrics(
            line.Ascent,
            line.Descent,
            line.LineGap,

            // Negative below the baseline, which is the sign convention a post table uses and the
            // opposite of VCL's own — its offsets are positive downwards.
            -((descent / 2) - half),
            thickness,
            ((line.Ascent - internalLeading) / 3) + half,
            thickness);
    }

    /// <summary>
    /// The advance width of the CJK ideograph U+6C34, in design units, or zero when absent.
    /// </summary>
    /// <remarks>
    /// LibreOffice measures the CJK advance from this one character — 水, "water" — rather than from
    /// the em square, because a CJK font's ideographs are not always exactly one em wide and the
    /// grid CJK text is laid out on is what its ideographs actually measure.
    /// </remarks>
    public static int CjkAdvance(OpenTypeFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        return face.AdvanceForCharacter(0x6C34);
    }
}
