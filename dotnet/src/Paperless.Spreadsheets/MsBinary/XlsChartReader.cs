using Paperless.Core.Charts;
using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Numbers;
using Paperless.Core.Units;
using Paperless.MsBinary.Escher;

namespace Paperless.Spreadsheets.MsBinary;

/// <summary>
/// The BIFF chart record identifiers, named as <c>sc/source/filter/inc/xlchart.hxx</c> names them.
/// </summary>
internal static class BiffChartRecords
{
    public const ushort Chart = 0x1002;
    public const ushort Series = 0x1003;
    public const ushort DataFormat = 0x1006;
    public const ushort LineFormat = 0x1007;
    public const ushort AreaFormat = 0x100A;
    public const ushort String = 0x100D;
    public const ushort TypeGroup = 0x1014;
    public const ushort Legend = 0x1015;
    public const ushort Bar = 0x1017;
    public const ushort Line = 0x1018;
    public const ushort Pie = 0x1019;
    public const ushort Area = 0x101A;
    public const ushort Scatter = 0x101B;
    public const ushort Axis = 0x101D;
    public const ushort ValueRange = 0x101F;
    public const ushort LabelRange = 0x1020;
    public const ushort DateRange = 0x1062;
    public const ushort AxisLine = 0x1021;
    public const ushort DefaultText = 0x1024;
    public const ushort Text = 0x1025;
    public const ushort Font = 0x1026;
    public const ushort ObjectLink = 0x1027;
    public const ushort Frame = 0x1032;
    public const ushort Begin = 0x1033;
    public const ushort End = 0x1034;
    public const ushort RadarLine = 0x103E;
    public const ushort RadarArea = 0x1040;
    public const ushort AxesSet = 0x1041;
    public const ushort SourceLink = 0x1051;
    public const ushort EscherFormat = 0x1066;

    /// <summary>An axis' own number format index — <c>EXC_ID_CHFORMAT</c>, <c>xlchart.hxx:635</c>.</summary>
    public const ushort NumberFormat = 0x104E;

    /// <summary>True for any record this reader acts on or has to track the nesting of.</summary>
    /// <remarks>
    /// The whole 0x08xx and 0x10xx range plus the few sheet records a chart substream reuses.
    /// Deciding by range rather than by a list is deliberate: an unrecognised chart record must
    /// still be counted so that a <c>CHBEGIN</c> attaches to the right header.
    /// </remarks>
    public static bool IsChartRecord(ushort id) => id is >= 0x1000 and <= 0x10FF;
}

/// <summary>
/// Reads a BIFF chart substream into a <see cref="ChartPlot"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A chart substream is a tree written flat.</strong> Every container record is followed
/// by <c>CHBEGIN</c>, its children, and <c>CHEND</c> — so what a record means depends entirely on
/// which container it is inside. <c>CHSTRING</c> under a <c>CHTEXT</c> is a title; the same record
/// under a <c>CHSERIES</c>' source link is a series name. This tracks the open containers on a
/// stack, which is what <c>XclImpChGroupBase::ReadRecordGroup</c>
/// (<c>sc/source/filter/excel/xichart.cxx:397-420</c>) achieves by recursion.
/// </para>
/// <para>
/// <strong>What it reads is what a page shows.</strong> The chart's titles, its axis titles, its
/// type and direction, and the value axis' scale — everything <see cref="ChartPlot"/> needs to be
/// laid out and painted by the same engine the SpreadsheetML and ODF charts already go through.
/// </para>
/// <para>
/// <strong>The series data is resolved through the workbook, not through the substream.</strong>
/// A BIFF series names its values through a <c>CHSOURCELINK</c> whose payload is a formula token
/// array, and only a link of type <c>EXC_CHSRCLINK_WORKSHEET</c> carries one
/// (<c>XclImpChSourceLink::ReadChSourceLink</c>, <c>xichart.cxx</c>); a link stating
/// <c>DIRECTLY</c> or <c>DEFAULT</c> names nothing, and LibreOffice produces a series with an
/// empty range for it and draws no marks. The rectangle that link names is decoded here and the
/// cells behind it are handed in by <see cref="XlsChartData"/>, which the workbook reader fills
/// as it reads its sheets — because the cells a chart plots are routinely on a sheet the chart
/// itself is not embedded in, and may not have been read when the chart is met. Reading the
/// <c>LABEL</c>/<c>NUMBER</c> records that trail the chart substream instead would be reading
/// the wrong thing: those are the sheet's own cells, not the series'.
/// </para>
/// </remarks>
internal sealed class XlsChartBuilder
{
    private readonly Stack<ushort> _open = new();
    private ushort _header;

    private string? _title;
    private string? _categoryTitle;
    private string? _valueTitle;

    private string? _pendingText;
    private int _pendingLink = -1;

    private int _axis = -1;
    private bool _valueGrid;
    private bool _categoryGrid;

    /// <summary>The <c>ifmt</c> the value axis' own <c>CHFORMAT</c> states, or none.</summary>
    /// <remarks>
    /// Kept as the index rather than resolved on the spot for the same reason a colour is: the
    /// workbook's format table is not the chart substream's to reach into, and is handed over
    /// when the plot is built.
    /// </remarks>
    private int _valueFormatIndex = NoNumberFormat;

    private ChartPlotKind _kind = ChartPlotKind.Bar;
    private ChartBarDirection _direction = ChartBarDirection.Column;
    private bool _stacked;
    private ChartScaleRequest _valueScale;
    private ChartAxisText _categoryText = DefaultCategoryText;

    /// <summary>Whether <c>CHLABELRANGE</c> asked for every category to be labelled.</summary>
    /// <remarks>
    /// Kept beside <see cref="_isDateAxis"/> rather than folded straight into
    /// <see cref="_categoryText"/> because the two records that decide the answer are separate and
    /// unordered, exactly as <c>XclImpChLabelRange</c> keeps <c>maLabelData</c> and
    /// <c>maDateData</c> apart until <c>Convert</c>.
    /// </remarks>
    private bool _everyLabel = true;

    /// <summary>Whether <c>CHDATERANGE</c> declared the category axis a date axis.</summary>
    private bool _isDateAxis;
    private bool _hasType;
    private bool _hasLegend;

    private readonly List<SeriesLinks> _series = [];
    private bool _expectSeriesName;

    private int _pendingDefaultText = NoDefaultText;
    private int _openDefaultText = NoDefaultText;
    private int _globalFont = NoFont;
    private int _axesSetFont = NoFont;
    private int _firstFont = NoFont;

    /// <summary>The <c>CHFONT</c> of the <c>CHTEXT</c> currently open, whatever it turns out to be.</summary>
    /// <remarks>
    /// A <c>CHTEXT</c> writes its font before it says what it dresses — the <c>CHOBJECTLINK</c>
    /// that names it a title or an axis title comes last — so the index has to be held until the
    /// group closes, exactly as its string already is.
    /// </remarks>
    private int _pendingFont = NoFont;

    private int _titleFont = NoFont;
    private int _axisTitleFont = NoFont;
    private int _labelFont = NoFont;

    private BiffChartColour? _background;
    private BiffChartColour? _plotBackground;
    private bool _hasBackground;
    private bool _hasPlotBackground;

    /// <summary>The chart's own size, as <c>CHCHART</c> states it.</summary>
    /// <remarks>
    /// In 1/65536 of a point, and the rectangle Excel drew the chart at rather than the one it
    /// prints at. Kept because it is the only statement of the chart's aspect the file makes;
    /// a chart sheet's printed rectangle is computed from the paper instead.
    /// </remarks>
    public DocSize? StatedSize { get; private set; }

    /// <summary>True once a <c>CHCHART</c> record has been seen.</summary>
    public bool HasChart { get; private set; }

    /// <summary>Feeds one record of the chart substream.</summary>
    /// <param name="id">The record identifier.</param>
    /// <param name="stream">Positioned at the record's first byte.</param>
    public void Read(ushort id, BiffRecordReader stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        switch (id)
        {
            case BiffChartRecords.Begin:
                _open.Push(_header);
                return;

            case BiffChartRecords.End:
                Close(_open.Count > 0 ? _open.Pop() : (ushort)0);
                return;

            default:
                break;
        }

        // Only a chart record can head a group; the page-setup and drawing records a chart
        // substream also carries sit outside the tree entirely.
        if (BiffChartRecords.IsChartRecord(id)) _header = id;

        // "The next record" is one record only: the flag is spent whether or not a CHSTRING
        // is what turned up.
        bool expectName = _expectSeriesName;
        _expectSeriesName = false;

        // CHDEFAULTTEXT carries no group of its own; the CHTEXT it names is the record right
        // after it, and only that one. XclImpChChart::ReadChDefaultText (xichart.cxx:3912-3921)
        // reaches forward exactly that far and drops the record when the next one is not a
        // CHTEXT, so the identifier is spent here whatever turned up.
        int defaultText = _pendingDefaultText;
        _pendingDefaultText = NoDefaultText;

        switch (id)
        {
            case BiffChartRecords.DefaultText:
                _pendingDefaultText = stream.ReadUInt16();
                break;

            case BiffChartRecords.Font:
                ReadFont(stream.ReadUInt16());
                break;

            case BiffChartRecords.AreaFormat:
                ReadAreaFormat(stream);
                break;

            case BiffChartRecords.LineFormat:
                ReadLineFormat(stream);
                break;

            case BiffChartRecords.EscherFormat:
                ReadEscherFormat(stream);
                break;

            case BiffChartRecords.Chart:
                HasChart = true;
                stream.Skip(8);
                StatedSize = new DocSize(FixedPoints(stream), FixedPoints(stream));
                break;

            case BiffChartRecords.Text:
                _pendingText = null;
                _pendingLink = -1;
                _pendingFont = NoFont;
                _openDefaultText = defaultText;
                break;

            case BiffChartRecords.Series:
                _series.Add(new SeriesLinks());
                break;

            case BiffChartRecords.SourceLink when InnermostIs(BiffChartRecords.Series):
                ReadSourceLink(stream);
                break;

            case BiffChartRecords.String when expectName && InnermostIs(BiffChartRecords.Series):
                // A series whose name is typed rather than linked writes it as a CHSTRING
                // immediately after the title link, which is what ReadChSourceLink reaches
                // forward for (xichart.cxx:763-769). Read flat, that is "the next record".
                _expectSeriesName = false;
                stream.Skip(2);
                if (_series.Count > 0) _series[^1].Name = stream.ReadString(eightBitLength: true);
                break;

            case BiffChartRecords.String when Inside(BiffChartRecords.Text):
                // Two unused bytes, an eight-bit character count, then the characters.
                stream.Skip(2);
                _pendingText = stream.ReadString(eightBitLength: true);
                break;

            case BiffChartRecords.ObjectLink when Inside(BiffChartRecords.Text):
                _pendingLink = stream.ReadUInt16();
                break;

            case BiffChartRecords.Axis:
                _axis = stream.ReadUInt16();
                break;

            case BiffChartRecords.AxisLine when Inside(BiffChartRecords.Axis):
                if (stream.ReadUInt16() == MajorGridLine) MarkGrid();
                break;

            case BiffChartRecords.NumberFormat when Inside(BiffChartRecords.Axis) && _axis == AxisY:
                _valueFormatIndex = stream.ReadUInt16();
                break;

            case BiffChartRecords.ValueRange:
                ReadValueRange(stream);
                break;

            case BiffChartRecords.LabelRange when _axis == AxisX:
                ReadLabelRange(stream);
                break;

            case BiffChartRecords.DateRange when _axis == AxisX:
                ReadDateRange(stream);
                break;

            case BiffChartRecords.Legend:
                _hasLegend = true;
                break;

            case BiffChartRecords.Bar:
                stream.Skip(4);
                ushort bar = stream.ReadUInt16();
                SetKind(ChartPlotKind.Bar);
                _direction = (bar & BarHorizontal) != 0
                    ? ChartBarDirection.Bar
                    : ChartBarDirection.Column;
                _stacked = (bar & (BarStacked | BarPercent)) != 0;
                break;

            case BiffChartRecords.Line:
                _stacked |= (stream.ReadUInt16() & (LineStacked | LinePercent)) != 0;
                SetKind(ChartPlotKind.Line);
                break;

            case BiffChartRecords.Area:
                _stacked |= (stream.ReadUInt16() & (LineStacked | LinePercent)) != 0;
                SetKind(ChartPlotKind.Area);
                break;

            case BiffChartRecords.Pie:
                SetKind(ChartPlotKind.Pie);
                break;

            case BiffChartRecords.Scatter:
                SetKind(ChartPlotKind.Scatter);
                break;

            case BiffChartRecords.RadarLine or BiffChartRecords.RadarArea:
                SetKind(ChartPlotKind.Radar);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// The chart, or null when the substream held none.
    /// </summary>
    /// <remarks>
    /// A chart with no series is still a chart: LibreOffice draws its frame, its axes, its
    /// gridlines and every title it carries, and so does this. Returning null for one would
    /// lose the page it prints on.
    /// </remarks>
    /// <summary>Every rectangle this chart's series name, for the workbook to gather.</summary>
    /// <remarks>
    /// Reported before the values are wanted rather than asked for when they are, because the
    /// sheet holding them may be read after the sheet the chart is embedded in — see
    /// <see cref="XlsChartData"/>.
    /// </remarks>
    public IEnumerable<XlsChartRange> Ranges()
    {
        foreach (SeriesLinks series in _series)
        {
            if (series.Values is { } values) yield return values;
            if (series.Categories is { } categories) yield return categories;
            if (series.Title is { } title) yield return title;
        }
    }

    /// <summary>
    /// The chart, or null when the substream held none.
    /// </summary>
    /// <param name="data">
    /// The cells the workbook gathered for this chart's links, or null when nothing gathered
    /// them — in which case the chart is built with no series, exactly as before this existed.
    /// </param>
    /// <param name="sheets">Resolves a token's <c>ixti</c> to a sheet index.</param>
    /// <param name="ownSheet">
    /// Which sheet the chart itself sits on, which is what a reference with no sheet part means.
    /// </param>
    /// <param name="fonts">
    /// The workbook's <c>FONT</c> buffer, which is where a <c>CHFONT</c>'s index points. Null
    /// leaves the chart with no family, which is what it had before this was read at all.
    /// </param>
    /// <param name="formats">
    /// Resolves the workbook's format table, which is where an axis' <c>CHFORMAT</c> index
    /// points. Null leaves an axis carrying one on the source's format, which is the same answer
    /// the reference reaches when the index resolves to nothing.
    /// </param>
    public ChartPlot? Build(
        XlsChartData? data,
        XlsExternSheets? sheets,
        int ownSheet,
        XlsCellFormats? fonts = null,
        Func<int, NumberFormatCode?>? formats = null)
    {
        if (!HasChart) return null;

        (IReadOnlyList<string?> categories, IReadOnlyList<ChartSeries> series,
            NumberFormatCode? sourceFormat) = BuildSeries(data, sheets, ownSheet, fonts);

        ChartPlot plot = new()
        {
            Title = _title,
            CategoryAxisTitle = _categoryTitle,
            ValueAxisTitle = _valueTitle,
            Kind = _kind,
            Direction = _direction,
            IsStacked = _stacked,
            Categories = categories,
            Series = series,
            ValueScale = _valueScale,
            CategoryAxisText = _categoryText,
            ValueFormat = ValueFormatOf(sourceFormat, formats),
            ValueGrid = _valueGrid ? GridColour : null,
            CategoryGrid = _categoryGrid ? GridColour : null,
            Legend = _hasLegend ? ChartLegendPosition.Right : ChartLegendPosition.None,
            TextFamily = FamilyOf(fonts),
            Background = _background?.Resolve(fonts),
            PlotBackground = _plotBackground?.Resolve(fonts),
        };

        // Each of the three is overridden only where the substream names a font for it, so a
        // chart that names none keeps chart2's own defaults — which is what ChartPlot already
        // holds and what the reference falls back to for the same reason.
        if (FontOf(_titleFont, GlobalDefaultText, fonts) is { } title)
            plot = plot with { TitleSize = title.Height, IsTitleBold = title.Weight >= BoldWeight };

        if (FontOf(_axisTitleFont, AxesSetDefaultText, fonts) is { } axisTitle)
        {
            plot = plot with
            {
                AxisTitleSize = axisTitle.Height,
                IsAxisTitleBold = axisTitle.Weight >= BoldWeight,
            };
        }

        if (FontOf(_labelFont, AxesSetDefaultText, fonts) is { } label)
            plot = plot with { LabelSize = label.Height, IsLabelBold = label.Weight >= BoldWeight };

        return plot;
    }

    /// <summary>
    /// The <c>FONT</c> one piece of chart text is set in, or null when nothing names one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>XclImpChText::UpdateText</c> (<c>sc/source/filter/excel/xichart.cxx:1042-1057</c>) is the
    /// whole of the fallback: a text object keeps its own <c>CHFONT</c> and takes the default
    /// text's when it has none. Which default depends on what the text is —
    /// <c>XclImpChChart::GetDefaultText</c> (<c>:3956-3970</c>) gives the chart title and the
    /// legend the <em>global</em> default and gives an axis title, an axis label and a data label
    /// the <em>axes-set</em> default in BIFF8, the global one in BIFF5.
    /// </para>
    /// <para>
    /// The generation is not tested for here because it does not have to be: BIFF5 writes no
    /// axes-set default at all, so asking for it and falling through to the global one reaches
    /// the same font by the same route.
    /// </para>
    /// </remarks>
    /// <param name="stated">The index the object's own <c>CHFONT</c> gave, or <see cref="NoFont"/>.</param>
    /// <param name="defaultText">Which <c>CHDEFAULTTEXT</c> stands in for it.</param>
    /// <param name="fonts">The workbook's <c>FONT</c> buffer.</param>
    private BiffFont? FontOf(int stated, int defaultText, XlsCellFormats? fonts)
    {
        int index = stated != NoFont ? stated
            : defaultText == AxesSetDefaultText && _axesSetFont != NoFont ? _axesSetFont
            : _globalFont;

        return index == NoFont ? null : fonts?.FontAt(index);
    }

    /// <summary>
    /// The format the value axis writes its tick labels through, or null for General.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>XclImpChAxis::Convert</c> (<c>sc/source/filter/excel/xichart.cxx:3363-3377</c>) is two
    /// lines and an ordering: a <c>CHFORMAT</c> that resolves is set on the axis and turns
    /// <c>LinkNumberFormatToSource</c> <em>off</em>; anything else leaves it on, and the format
    /// then comes from the cells the axis plots — see <see cref="XlsChartData.FormatOf"/>.
    /// </para>
    /// <para>
    /// <strong>What is deliberately not read is <c>CHSOURCELINK</c>'s own <c>ifmt</c>.</strong> It
    /// looks like the answer and is not: it feeds a data label
    /// (<c>XclImpChText::ConvertNumFmt</c>, <c>xichart.cxx:1684</c>), and on
    /// <c>Template Pilot Logbook JAR-FCL V3.0.xls</c>' second chart it is 370 — an index no
    /// <c>FORMAT</c> record in that workbook defines, while the cells it names carry <c>0.0</c>,
    /// which is what the reference draws.
    /// </para>
    /// </remarks>
    private NumberFormatCode? ValueFormatOf(
        NumberFormatCode? sourceFormat, Func<int, NumberFormatCode?>? formats)
    {
        if (_valueFormatIndex != NoNumberFormat && formats?.Invoke(_valueFormatIndex) is { } stated)
        {
            return stated;
        }

        return sourceFormat;
    }

    /// <summary>
    /// Reads one <c>CHSOURCELINK</c>: which part of a series it feeds, and from where.
    /// </summary>
    /// <remarks>
    /// <c>XclImpChSourceLink::ReadChSourceLink</c> (<c>xichart.cxx:744-770</c>). Only a link of
    /// type <c>EXC_CHSRCLINK_WORKSHEET</c> carries a formula at all; <c>DEFAULT</c> and
    /// <c>DIRECTLY</c> name nothing and are what a series writes for the parts it has no source
    /// for, which is why almost every chart in the corpus holds four of these and two are empty.
    /// </remarks>
    private void ReadSourceLink(BiffRecordReader stream)
    {
        if (_series.Count == 0) return;

        int destination = stream.ReadByte();
        int link = stream.ReadByte();
        stream.Skip(4);

        // A title link is followed by the literal string when there is one, whatever its link
        // type says — an unlinked series name is exactly the DIRECTLY case.
        if (destination == SourceTitle) _expectSeriesName = true;

        if (link != SourceLinkWorksheet) return;

        int length = stream.ReadUInt16();
        if (XlsChartFormula.Read(stream, length, stream.Version) is not { } range) return;

        SeriesLinks series = _series[^1];
        switch (destination)
        {
            case SourceValues: series.Values = range; break;
            case SourceCategories: series.Categories = range; break;
            case SourceTitle: series.Title = range; break;
            default: break;
        }
    }

    /// <summary>Turns the series' links into series, with whatever cells were gathered.</summary>
    /// <remarks>
    /// <para>
    /// A series with no resolvable value link is dropped rather than drawn empty. That is what
    /// LibreOffice shows for one — a legend entry and no marks — and an empty series here would
    /// additionally drag the value axis to the 0…12 default scale that a chart with no numbers
    /// at all gets, which is the whole defect this reads the links to remove.
    /// </para>
    /// <para>
    /// The categories come from the first series that names any. BIFF writes the same category
    /// link on every series of a chart, and Calc likewise takes the first
    /// (<c>XclImpChTypeGroup::CreateDataSeries</c> hands the group's categories to the
    /// diagram once).
    /// </para>
    /// <para>
    /// <strong>The value axis' linked format comes from the first series that has one</strong>, and
    /// not from all of them. <c>AxisHelper::getExplicitNumberFormatKeyForAxis</c> counts the format
    /// of every series attached to the axis and takes the most frequent
    /// (<c>chart2/source/tools/AxisHelper.cxx:276-295</c>); the two reduce to the same answer
    /// whenever the series agree, and every chart substream in the corpus has all of its series in
    /// one column of one format. Recorded as a simplification rather than as a port.
    /// </para>
    /// </remarks>
    private (IReadOnlyList<string?> Categories, IReadOnlyList<ChartSeries> Series,
        NumberFormatCode? ValueFormat) BuildSeries(
        XlsChartData? data, XlsExternSheets? sheets, int ownSheet, XlsCellFormats? fonts)
    {
        if (data is null || _series.Count == 0) return ([], [], null);

        List<string?> categories = [];
        List<ChartSeries> built = [];
        NumberFormatCode? valueFormat = null;

        foreach (SeriesLinks series in _series)
        {
            if (series.Values is not { } values || Resolve(values, sheets, ownSheet) is not { } valueSheet)
            {
                continue;
            }

            List<double?> numbers = data.Numbers(valueSheet, values);
            if (numbers.TrueForAll(number => number is null)) continue;

            valueFormat ??= data.FormatOf(valueSheet, values);

            if (categories.Count == 0
                && series.Categories is { } labels
                && Resolve(labels, sheets, ownSheet) is { } labelSheet)
            {
                categories.AddRange(data.Texts(labelSheet, labels));
            }

            string? name = series.Name;
            if (name is null
                && series.Title is { } title
                && Resolve(title, sheets, ownSheet) is { } titleSheet)
            {
                name = data.TextOf(titleSheet, title.FirstRow, title.FirstColumn);
            }

            built.Add(new ChartSeries(
                name is { Length: > 0 } ? name : null,
                numbers,
                Fill: series.Fill?.Resolve(fonts),
                Line: series.Line?.Resolve(fonts)));
        }

        // Categories are indexed by point, so a shorter list than the longest series leaves the
        // tail of that series unlabelled rather than mislabelled.
        return (categories, built, valueFormat);
    }

    private static int? Resolve(XlsChartRange range, XlsExternSheets? sheets, int ownSheet)
        => range.Ixti < 0 ? ownSheet : sheets?.SheetOf(range.Ixti);

    /// <summary>
    /// Notes one <c>CHFONT</c>, against the default text it belongs to when it belongs to one.
    /// </summary>
    /// <remarks>
    /// The record is a bare index into the workbook's <c>FONT</c> buffer and nothing else
    /// (<c>XclImpChFont::ReadChFont</c>, <c>xichart.cxx:941</c>); which text it dresses is
    /// decided entirely by where it sits. Only a <c>CHTEXT</c> opened by a <c>CHDEFAULTTEXT</c> is
    /// one of the chart's defaults, so the stack has to say so — the same <c>CHFONT</c> under a
    /// legend or an axis is that object's own font and not a default for anything.
    /// <para>
    /// <strong>The innermost check is what carries that, and nothing else does.</strong> Clearing
    /// the open identifier when the <c>CHTEXT</c> group closes looks like a second guard and is
    /// dead code: reaching a <c>CHFONT</c> with a <c>CHTEXT</c> innermost means that
    /// <c>CHTEXT</c>'s own header record assigned the identifier on the way in, so there is no
    /// path on which a stale one can be read. It was written, found to fail no case under
    /// mutation, and removed rather than left as an untested comfort.
    /// </para>
    /// <para>
    /// <strong>BIFF gives each axis its own label font and <see cref="ChartPlot"/> holds one for
    /// both</strong>, so one of the two has to be picked and the category axis is it: its labels
    /// are what <see cref="ChartAxisLabels"/> tests for collision, so the size it is measured at
    /// decides whether the axis is rotated or thinned and therefore how many labels a page shows,
    /// while the value axis' size only widens a band. Of the corpus's fifteen chart substreams
    /// <strong>fourteen state the same size on both axes</strong> and the choice is moot;
    /// <c>2012-GA-Survey-Chapter-6-Tables-16Dec2013-V2.xls</c> is the one that states 8 pt on its
    /// category axis and 10 pt on its value axis. Recorded rather than resolved — resolving it
    /// means a second property on the model and a reason from more than one file.
    /// </para>
    /// </remarks>
    private void ReadFont(ushort index)
    {
        if (_firstFont == NoFont) _firstFont = index;

        // An axis' own CHFONT sits directly inside its CHAXIS and dresses its tick labels.
        if (InnermostIs(BiffChartRecords.Axis))
        {
            if (_axis == AxisX || _labelFont == NoFont) _labelFont = index;
            return;
        }

        if (!InnermostIs(BiffChartRecords.Text)) return;

        _pendingFont = index;

        if (_openDefaultText == GlobalDefaultText && _globalFont == NoFont) _globalFont = index;
        else if (_openDefaultText == AxesSetDefaultText && _axesSetFont == NoFont) _axesSetFont = index;
    }

    /// <summary>
    /// Reads one <c>CHAREAFORMAT</c> and files it against whatever it dresses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>XclImpChAreaFormat::ReadChAreaFormat</c> (<c>xichart.cxx:486-500</c>): a foreground and
    /// a background <c>RGB</c>, a pattern, flags, and — in BIFF8 only — the two palette indices
    /// that supersede the <c>RGB</c>s. A pattern of <c>EXC_PATT_NONE</c> is "no fill" and is a
    /// different statement from "no record", so it fills nothing rather than falling back.
    /// </para>
    /// <para>
    /// <strong>The <c>AUTO</c> flag is not honoured, and the corpus says that is safe here.</strong>
    /// An automatic area takes a colour from Excel's own chart palette rather than from the record,
    /// which is a table this reader does not have. A record-level census of the six OLE2 workbooks
    /// on the sheets track that hold a chart substream finds <strong>114 <c>CHAREAFORMAT</c>
    /// records and not one of them automatic</strong> — so on this corpus reading the stated colour
    /// is the whole answer, and a chart that does state <c>AUTO</c> keeps the layout's own default
    /// exactly as before. Say it as a limit rather than as a rule: it is untested against a file
    /// that needs the palette, because no such file is here.
    /// </para>
    /// </remarks>
    private void ReadAreaFormat(BiffRecordReader stream)
    {
        uint foreground = stream.ReadUInt32();
        stream.Skip(4);                              // the background RGB, used only by a pattern
        ushort pattern = stream.ReadUInt16();
        ushort flags = stream.ReadUInt16();

        int index = stream.Version == BiffVersion.Biff8 && stream.RecordLeft >= 4
            ? stream.ReadUInt16()
            : NoPaletteIndex;

        if ((flags & AutomaticFormat) != 0) return;

        Fill(pattern == PatternNone ? null : new BiffChartColour(foreground, index), supersedes: false);
    }

    /// <summary>
    /// Reads one <c>CHLINEFORMAT</c>, which is what gives a series its outline.
    /// </summary>
    /// <remarks>
    /// <c>XclImpChLineFormat::ReadChLineFormat</c> (<c>xichart.cxx:453-465</c>): an <c>RGB</c>, a
    /// pattern, a weight, flags, and the BIFF8 palette index. Only a series' line is taken —
    /// the axis lines and the frames' borders already have their own rules and are drawn without
    /// consulting the file, so reading them here would change what they look like without
    /// completing them.
    /// </remarks>
    private void ReadLineFormat(BiffRecordReader stream)
    {
        uint rgb = stream.ReadUInt32();
        ushort pattern = stream.ReadUInt16();
        stream.Skip(2);                              // the weight, in Excel's four steps
        ushort flags = stream.ReadUInt16();

        int index = stream.Version == BiffVersion.Biff8 && stream.RecordLeft >= 2
            ? stream.ReadUInt16()
            : NoPaletteIndex;

        if ((flags & AutomaticFormat) != 0 || pattern == LineNone) return;
        if (!InSeriesFormat() || _series.Count == 0) return;

        _series[^1].Line = new BiffChartColour(rgb, index);
    }

    /// <summary>
    /// Reads one <c>CHESCHERFORMAT</c>, whose fill supersedes any <c>CHAREAFORMAT</c> beside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The precedence is not inferred — <c>XclImpChFrameBase::ConvertAreaBase</c> carries it as a
    /// comment: <em>"CHESCHERFORMAT overrides CHAREAFORMAT (even if it is auto)"</em>. The record's
    /// payload is a bare DFF <c>msofbtOPT</c>: an eight-byte header whose instance is the property
    /// count, then the entries, which is exactly what
    /// <see cref="EscherPropertyTable.Read(ReadOnlySpan{byte}, int)"/> already parses for every
    /// shape in a <c>.xls</c>, a <c>.doc</c> and a <c>.ppt</c>.
    /// </para>
    /// <para>
    /// <strong>This is what separates a right colour from a plausible one.</strong> Measured on
    /// <c>EHEST-Pre-departure-checklist…xls</c>: all nine of its chart substreams state their three
    /// filled series as palette indices 24, 10 and 13 — <c>#9999FF</c>, <c>#FF0000</c>,
    /// <c>#FFFF00</c> — and the reference draws <c>#6699FF</c>, <c>#FF0000</c>, <c>#FFFF00</c>.
    /// Two of the three agree and the first does not, because that one series and the plot wall
    /// each carry a <c>CHESCHERFORMAT</c> as well. Reading only the palette would have looked
    /// right on two thirds of the marks on the page.
    /// </para>
    /// <para>
    /// Only the solid fill colour is taken. A DFF property set can also state a gradient, a
    /// texture or a picture, and a chart that does gets its <c>CHAREAFORMAT</c> colour rather than
    /// a wrong flat one; the census below says nothing on this track needs more. Three of the six
    /// chart-bearing workbooks state a <c>CHESCHERFORMAT</c> at all, twenty records between them.
    /// </para>
    /// </remarks>
    private void ReadEscherFormat(BiffRecordReader stream)
    {
        // The DFF header: two bytes of version and instance, two of type, four of length.
        if (stream.RecordLeft < DffHeaderLength) return;

        byte[] header = stream.ReadBytes(DffHeaderLength);
        int count = (header[1] << 4) | (header[0] >> 4);
        byte[] payload = stream.ReadBytes(stream.RecordLeft);

        EscherPropertyTable properties = EscherPropertyTable.Read(payload, count);
        if (!properties.Has(EscherPropertyIds.FillColour)) return;
        if (!properties.Boolean(EscherPropertyIds.Filled, fallback: true)) return;

        uint value = properties.Value(EscherPropertyIds.FillColour);

        // An MSO colour word states its own kind in the top byte, and only a literal one can be
        // resolved here: a palette index, a scheme index or a desktop index each names a table a
        // chart substream does not carry, so the CHAREAFORMAT beside it stays the answer. Bit 1
        // is what these files set — a literal that Excel merely says is drawn from its palette —
        // and it is the whole of the corpus's twenty such records.
        if ((value & (IndexedColour | SchemeColour | SystemColour)) != 0) return;

        Fill(new BiffChartColour(value & 0x00FFFFFF, NoPaletteIndex), supersedes: true);
    }

    /// <summary>Files one area colour against the object whose frame is open.</summary>
    /// <remarks>
    /// Which object that is comes entirely from the stack, exactly as it does for a font: a
    /// <c>CHFRAME</c> directly inside <c>CHCHART</c> is the chart's own background, the same
    /// record inside <c>CHAXESSET</c> is the plot area's wall, and a <c>CHAREAFORMAT</c> inside a
    /// series' <c>CHDATAFORMAT</c> is that series' fill. A frame under a legend or a text block is
    /// neither and is deliberately dropped — those are drawn by rules of their own.
    /// </remarks>
    private void Fill(BiffChartColour? colour, bool supersedes)
    {
        if (InSeriesFormat())
        {
            if (_series.Count > 0) _series[^1].Fill = colour;
            return;
        }

        // A legend's or a text block's frame is neither the chart's background nor the wall.
        if (!InnermostIs(BiffChartRecords.Frame)) return;
        if (Inside(BiffChartRecords.Legend) || Inside(BiffChartRecords.Text)) return;

        if (Inside(BiffChartRecords.AxesSet))
        {
            if (_hasPlotBackground && !supersedes) return;
            _hasPlotBackground = true;
            _plotBackground = colour;
        }
        else
        {
            if (_hasBackground && !supersedes) return;
            _hasBackground = true;
            _background = colour;
        }
    }

    /// <summary>Whether the open containers are a series' own <c>CHDATAFORMAT</c>.</summary>
    private bool InSeriesFormat()
        => InnermostIs(BiffChartRecords.DataFormat) && Inside(BiffChartRecords.Series);

    /// <summary>
    /// The family the chart’s text is set in, or null when the substream names none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chart's global default text first. <c>XclImpChChart::GetDefaultText</c>
    /// (<c>xichart.cxx:3956-3969</c>) hands <c>EXC_CHDEFTEXT_GLOBAL</c> to the title and the
    /// legend in every generation, and to the axis labels and axis titles as well in BIFF5; only
    /// BIFF8 splits those onto <c>EXC_CHDEFTEXT_AXESSET</c>. So the global default is the face the
    /// most chart text falls back to, which is what a single family should be.
    /// </para>
    /// <para>
    /// <strong>Nothing in this corpus separates the three answers below</strong>, and it is worth
    /// saying rather than implying. A record-level census of all 61 OLE2 workbooks on the sheets
    /// track finds six holding a chart substream, fifteen substreams between them — and every one
    /// of the fifteen states exactly one family across every <c>CHFONT</c> it carries, with its
    /// global and axes-set defaults agreeing in all fifteen. The order is ported from the C++
    /// rather than fitted, and the fixtures are synthetic because only a synthetic file can make
    /// the three disagree.
    /// </para>
    /// </remarks>
    private string? FamilyOf(XlsCellFormats? fonts)
    {
        int index = _globalFont != NoFont ? _globalFont
            : _axesSetFont != NoFont ? _axesSetFont
            : _firstFont;

        return index == NoFont ? null : fonts?.FontFamilyAt(index);
    }

    private void Close(ushort container)
    {
        if (container != BiffChartRecords.Text) return;

        // A title with no text is the placeholder Excel writes for every object that could carry
        // one; only a linked, non-empty block names anything.
        if (_pendingText is { Length: > 0 } text)
        {
            switch (_pendingLink)
            {
                case LinkTitle:
                    _title ??= text;
                    if (_titleFont == NoFont) _titleFont = _pendingFont;
                    break;
                case LinkValueAxis:
                    _valueTitle ??= text;
                    if (_axisTitleFont == NoFont) _axisTitleFont = _pendingFont;
                    break;
                case LinkCategoryAxis:
                    _categoryTitle ??= text;
                    if (_axisTitleFont == NoFont) _axisTitleFont = _pendingFont;
                    break;
                default: break;
            }
        }

        _pendingText = null;
        _pendingLink = -1;
        _pendingFont = NoFont;
    }

    private void ReadValueRange(BiffRecordReader stream)
    {
        double minimum = stream.ReadDouble();
        double maximum = stream.ReadDouble();
        double major = stream.ReadDouble();
        stream.Skip(16);
        ushort flags = stream.ReadUInt16();

        _valueScale = new ChartScaleRequest(
            (flags & AutoMinimum) != 0 ? null : minimum,
            (flags & AutoMaximum) != 0 ? null : maximum,
            (flags & AutoMajor) != 0 || major <= 0.0 ? null : major,
            (flags & Reversed) != 0);
    }

    /// <summary>
    /// Reads <c>CHLABELRANGE</c>, whose label frequency decides how the category labels are set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>XclImpChLabelRange::Convert</c> (<c>sc/source/filter/excel/xichart.cxx:3039-3047</c>)
    /// turns one field into three properties and a comment that says why: <em>do not overlap text
    /// unless all labels are visible</em>, and the same for line breaking. So a chart that labels
    /// every category — which is the default and is what every chart in the corpus states — draws
    /// all of them, overlapping, and none of the thinning, rotating or staggering in
    /// <see cref="ChartAxisLabels"/> happens at all.
    /// </para>
    /// <para>
    /// <strong>Only on an axis that is not a date axis.</strong> Those three lines are the
    /// <c>else</c> of an <c>if</c> over <c>CHDATERANGE</c>'s <c>DATEAXIS</c> flag, and this
    /// frequency decides nothing on the other branch — see <see cref="ReadDateRange"/>, which is
    /// why the answer is composed by <see cref="CategoryTextOf"/> from both records rather than
    /// settled here.
    /// </para>
    /// <para>
    /// <strong>This is worth 25 words a page on a checklist with eight chart pages</strong>, and
    /// it is invisible to anything but a drawn comparison: the labels our layout dropped were
    /// dropped because they collide, which is the correct answer to a question BIFF does not ask.
    /// The frequency itself is not honoured, by either renderer — the C++ carries
    /// <c>//TODO #i58731# show n-th category</c> beside this and thins by collision instead.
    /// </para>
    /// </remarks>
    private void ReadLabelRange(BiffRecordReader stream)
    {
        stream.Skip(2);
        _everyLabel = stream.ReadUInt16() == 1;
        _categoryText = CategoryTextOf(_everyLabel, _isDateAxis);
    }

    /// <summary>
    /// Reads <c>CHDATERANGE</c>, whose one flag decides which of two label rules applies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>XclImpChLabelRange::Convert</c> (<c>xichart.cxx:3013-3047</c>) is an <c>if</c> and an
    /// <c>else</c> over exactly this flag, and the label properties are set in the <em>else</em>
    /// alone. A date axis takes the other branch, sets a scaling and a time increment, and never
    /// touches <c>TEXTOVERLAP</c>, <c>TEXTBREAK</c> or <c>ARRANGEORDER</c> at all — so what stands
    /// is chart2's own defaults for them, all three of which are set at
    /// <c>chart2/source/model/main/Axis.cxx:239-242</c>: <c>TextBreak</c> false,
    /// <c>TextOverlap</c> false, <c>ArrangeOrder</c> automatic.
    /// </para>
    /// <para>
    /// <strong>That is the difference between an axis that rotates its labels and one that cannot.</strong>
    /// <see cref="ChartAxisLabels.Resolve"/> returns on its first test when overlap is allowed, so
    /// a chart whose <c>CHLABELRANGE</c> says "label every category" — which is the default, and
    /// what nearly every chart states — never reaches the auto-rotate ladder. Applying that to a
    /// date axis as well is a rule the reference deliberately does not have. Measured on
    /// <c>Template Pilot Logbook JAR-FCL V3.0.xls</c>, whose <c>CHDATERANGE</c> flags are
    /// <c>0x00ff</c> and therefore include <c>DATEAXIS</c>: the reference sets 848 glyphs at 45°
    /// across the document and we set none.
    /// </para>
    /// <para>
    /// The record is read even when it arrives before its <c>CHLABELRANGE</c>, because BIFF does
    /// not fix their order and LibreOffice keeps both halves on one object and decides at the end.
    /// </para>
    /// </remarks>
    private void ReadDateRange(BiffRecordReader stream)
    {
        stream.Skip(16);
        _isDateAxis = (stream.ReadUInt16() & DateAxis) != 0;
        _categoryText = CategoryTextOf(_everyLabel, _isDateAxis);
    }

    /// <summary>What a category axis states about its labels, given the two records that decide it.</summary>
    private static ChartAxisText CategoryTextOf(bool everyLabel, bool dateAxis) => dateAxis
        ? new ChartAxisText(
            Rotation: 0.0,
            OverlapAllowed: false,
            LineBreakAllowed: false,
            Stagger: ChartLabelStagger.Auto)
        : new ChartAxisText(
            Rotation: 0.0,
            OverlapAllowed: everyLabel,
            LineBreakAllowed: everyLabel,
            Stagger: ChartLabelStagger.SideBySide);

    /// <summary>
    /// Which axis a major gridline belongs to.
    /// </summary>
    /// <remarks>
    /// BIFF numbers its axes by dimension — X is the categories and Y the values — and a bar
    /// chart turned on its side keeps that numbering while swapping which way each is drawn. The
    /// plot model names them by role instead, so the two are the same either way and no
    /// transposition is needed here.
    /// </remarks>
    private void MarkGrid()
    {
        if (_axis == AxisY) _valueGrid = true;
        else if (_axis == AxisX) _categoryGrid = true;
    }

    /// <summary>The first type group decides the chart; a second is a combination chart.</summary>
    private void SetKind(ChartPlotKind kind)
    {
        if (_hasType) return;
        _hasType = true;
        _kind = kind;
    }

    private bool Inside(ushort container) => _open.Contains(container);

    /// <summary>
    /// Whether the innermost open container is this one.
    /// </summary>
    /// <remarks>
    /// <c>CHSOURCELINK</c> appears under <c>CHSERIES</c> and under <c>CHTEXT</c>, meaning
    /// entirely different things, and a <c>CHTEXT</c> sits <em>inside</em> the series it labels.
    /// So membership is not enough here where it is for a title: only the innermost container
    /// separates a series' value link from a data label's text link.
    /// </remarks>
    private bool InnermostIs(ushort container) => _open.Count > 0 && _open.Peek() == container;

    /// <summary>
    /// A colour a chart record states, before the workbook's palette has been consulted.
    /// </summary>
    /// <remarks>
    /// BIFF5 states the <c>RGB</c> in the record and BIFF8 states a palette index that supersedes
    /// it, so a record carries both and which one wins is decided when the palette is to hand.
    /// Holding the pair rather than resolving on the spot is what lets the chart substream be
    /// walked before the workbook reader hands its formats over.
    /// </remarks>
    /// <param name="Rgb">The literal colour, as BIFF5 states it: blue, green, red, reserved.</param>
    /// <param name="PaletteIndex">The BIFF8 index, or <see cref="NoPaletteIndex"/>.</param>
    private readonly record struct BiffChartColour(uint Rgb, int PaletteIndex)
    {
        /// <summary>Resolves against the workbook's palette, preferring the index when there is one.</summary>
        /// <param name="fonts">The workbook's palette, or null when there is none to consult.</param>
        public Colour Resolve(XlsCellFormats? fonts)
            => (PaletteIndex == NoPaletteIndex ? null : fonts?.PaletteColour(PaletteIndex))
                // BIFF writes the RGB little-endian — red first — so it is not the 0xRRGGBB
                // FromRgb takes and the two outer bytes have to be swapped.
                ?? Colour.FromRgb(((Rgb & 0xFF) << 16) | (Rgb & 0xFF00) | ((Rgb >> 16) & 0xFF));
    }

    /// <summary>The rectangles one series names, before any of them is resolved.</summary>
    private sealed class SeriesLinks
    {
        /// <summary>The series' fill, as its <c>CHDATAFORMAT</c>'s <c>CHAREAFORMAT</c> states it.</summary>
        public BiffChartColour? Fill { get; set; }

        /// <summary>Its outline, from the <c>CHLINEFORMAT</c> beside that.</summary>
        public BiffChartColour? Line { get; set; }

        public XlsChartRange? Values { get; set; }

        public XlsChartRange? Categories { get; set; }

        public XlsChartRange? Title { get; set; }

        /// <summary>The name written literally, when the series states one that way.</summary>
        public string? Name { get; set; }
    }

    /// <summary>A length stated in 1/65536 of a point, which is how a chart states its frame.</summary>
    private static Length FixedPoints(BiffRecordReader stream)
        => Length.FromPoints(stream.ReadInt32() / 65536.0);

    /// <summary>Which part of a series a <c>CHSOURCELINK</c> feeds — <c>EXC_CHSRCLINK_*</c>.</summary>
    private const int SourceTitle = 0;

    private const int SourceValues = 1;

    private const int SourceCategories = 2;

    /// <summary>The only link type that carries a formula.</summary>
    private const int SourceLinkWorksheet = 2;

    private const int LinkTitle = 1;
    private const int LinkValueAxis = 2;
    private const int LinkCategoryAxis = 3;

    /// <summary>Which default text a <c>CHDEFAULTTEXT</c> names — <c>EXC_CHDEFTEXT_*</c>.</summary>
    private const int GlobalDefaultText = 2;

    /// <summary>The BIFF8-only default for axis labels, axis titles and data labels.</summary>
    private const int AxesSetDefaultText = 3;

    /// <summary>No <c>CHDEFAULTTEXT</c> is open — <c>EXC_CHDEFTEXT_NONE</c>.</summary>
    private const int NoDefaultText = 0xFFFF;

    /// <summary>No <c>CHFONT</c> was stated — <c>EXC_FONT_NOTFOUND</c>.</summary>
    private const int NoFont = -1;

    /// <summary>No <c>CHFORMAT</c> was stated — <c>EXC_FORMAT_NOTFOUND</c>.</summary>
    private const int NoNumberFormat = -1;

    /// <summary>
    /// The weight at which a <c>FONT</c> counts as the family's bold face.
    /// </summary>
    /// <remarks>
    /// <c>XclImpFont::GuessScriptType</c>'s neighbours read the record's <c>nWeight</c> straight
    /// into a <c>FontWeight</c>, and <c>lclGetApiWeight</c> puts the boundary at
    /// <c>EXC_FONTWGHT_BOLD</c>, which is 700. Every <c>FONT</c> in the corpus is 400 or 700.
    /// </remarks>
    private const int BoldWeight = 700;

    /// <summary>A DFF record header: version and instance, type, then a four-byte length.</summary>
    private const int DffHeaderLength = 8;

    /// <summary>An MSO colour word whose low bytes are an index into the file's palette.</summary>
    private const uint IndexedColour = 0x01000000;

    /// <summary>One whose low bytes are an index into a colour scheme.</summary>
    private const uint SchemeColour = 0x08000000;

    /// <summary>One naming a desktop colour, which a headless renderer has no source for.</summary>
    private const uint SystemColour = 0x10000000;

    /// <summary>The record carried no palette index, which is every BIFF before 8.</summary>
    private const int NoPaletteIndex = -1;

    /// <summary>Bit zero of both format records' flags — <c>EXC_CHAREAFORMAT_AUTO</c>.</summary>
    private const ushort AutomaticFormat = 0x0001;

    /// <summary>An area that fills nothing — <c>EXC_PATT_NONE</c>.</summary>
    private const ushort PatternNone = 0;

    /// <summary>A line that draws nothing — <c>EXC_CHLINEFORMAT_NONE</c>.</summary>
    private const ushort LineNone = 5;

    /// <summary>
    /// What a category axis states when it carries no <c>CHLABELRANGE</c> at all.
    /// </summary>
    /// <remarks>
    /// <c>XclChLabelRange</c>'s own constructor (<c>sc/source/filter/excel/xlchart.cxx:268-274</c>)
    /// sets <c>mnLabelFreq</c> to 1, so an axis that says nothing labels every category and
    /// therefore allows overlap — the same answer as an axis that states the default.
    /// </remarks>
    private static readonly ChartAxisText DefaultCategoryText = new(
        Rotation: 0.0,
        OverlapAllowed: true,
        LineBreakAllowed: true,
        Stagger: ChartLabelStagger.SideBySide);

    private const int AxisX = 0;
    private const int AxisY = 1;

    private const ushort MajorGridLine = 1;

    private const ushort BarHorizontal = 0x0001;
    private const ushort BarStacked = 0x0002;
    private const ushort BarPercent = 0x0004;
    private const ushort LineStacked = 0x0001;
    private const ushort LinePercent = 0x0002;

    /// <summary><c>EXC_CHDATERANGE_DATEAXIS</c>, <c>xlchart.hxx:716</c>.</summary>
    private const ushort DateAxis = 0x0010;

    private const ushort AutoMinimum = 0x0001;
    private const ushort AutoMaximum = 0x0002;
    private const ushort AutoMajor = 0x0004;
    private const ushort Reversed = 0x0040;

    /// <summary>
    /// The colour a gridline is drawn in when the file does not say.
    /// </summary>
    /// <remarks>
    /// Black, which is what Excel's own default chart gridline is and what LibreOffice draws for
    /// these charts. The <c>CHLINEFORMAT</c> beside the <c>CHAXISLINE</c> states the real colour
    /// as a palette index; reading it is recorded in the module's TODO.
    /// </remarks>
    private static readonly Colour GridColour = Colour.Black;
}
