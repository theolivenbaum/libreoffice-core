using Paperless.Core.Charts;
using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;

namespace Paperless.Spreadsheets.Layout;

/// <summary>
/// Paints a chart anchored on a sheet: a laid-out <see cref="ChartPlot"/> straight into the sink.
/// </summary>
/// <remarks>
/// <para>
/// The spreadsheet counterpart to <c>Paperless.Presentations.Layout.SlideChart</c>, and the two
/// share everything above the last step. <see cref="ChartLayout"/> gives back the same
/// <see cref="ChartDrawing"/> for both families; a slide turns it into <c>PlacedShape</c> values
/// for a backend to walk, and a sheet has no such list — <c>SheetPageGraphics</c> paints directly
/// — so this emits the fills, strokes and glyph runs itself. Everything that decides where a mark
/// goes is in <c>Paperless.Core.Charts</c> and is written once.
/// </para>
/// <para>
/// <strong>Paint order is the reference's.</strong> The chart's own background, the plot area's
/// wall, then the axes and their ticks, then the bars over them, then the text. Painting the bars
/// before the axes hides the axis line behind the first bar, which is a one-shape difference and
/// exactly what a fill-by-fill comparison against LibreOffice's PDF catches.
/// </para>
/// <para>
/// <strong>The print zoom scales the type, not just the rectangle.</strong> The box this is given
/// has already been through <c>SheetPageGraphics</c>'s scale; the font sizes have not, and a chart
/// laid out at 100% type inside a 50% rectangle reserves twice the room its labels need and
/// squeezes the plot area to nothing. So the sizes are scaled here rather than a transform being
/// pushed onto the sink, which keeps every glyph run in page coordinates and readable out of the
/// content stream.
/// </para>
/// </remarks>
internal static class SheetChart
{
    /// <summary>Paints one chart into the rectangle its anchor gave it.</summary>
    /// <param name="sink">Receives the drawing commands.</param>
    /// <param name="plot">The chart.</param>
    /// <param name="box">Where the frame lands on the page, already scaled.</param>
    /// <param name="scale">The print zoom, applied to the type.</param>
    public static void Draw(IDrawingSink sink, ChartPlot plot, DocRect box, double scale)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(plot);

        if (box.Width <= Length.Zero || box.Height <= Length.Zero) return;

        ChartDrawing drawing = ChartLayout.Place(Sized(plot, scale), box, Measurer.Instance);
        if (drawing.PlotArea.Width <= Length.Zero || drawing.PlotArea.Height <= Length.Zero) return;

        foreach (ChartBox filled in drawing.Boxes)
        {
            if (filled.Bounds.Width <= Length.Zero || filled.Bounds.Height <= Length.Zero) continue;

            GraphicsPath path = GraphicsPath.Rectangle(filled.Bounds);
            if (filled.Fill is { } fill) sink.FillPath(path, Paint.Solid(fill));
            if (filled.Line is { } line) sink.StrokePath(path, Pen(line, filled.LineWidth));
        }

        foreach (ChartLine line in drawing.Lines)
        {
            sink.StrokePath(
                new GraphicsPath().MoveTo(line.From).LineTo(line.To),
                Pen(line.Colour, line.Width, line.DashPattern, line.Cap));
        }

        // The free-form marks — wedges, polylines, areas — after the axes and before the text.
        foreach (ChartShape shape in drawing.Shapes)
        {
            if (shape.Path.Commands.Count == 0) continue;

            if (shape.Fill is { } fill) sink.FillPath(shape.Path, Paint.Solid(fill));
            if (shape.Line is { } line)
                sink.StrokePath(shape.Path, Pen(line, shape.LineWidth, shape.DashPattern, shape.Cap));
        }

        foreach (ChartLabel label in drawing.Labels) Text(sink, label);
    }

    /// <summary>
    /// The chart with every stated type size taken through the print zoom.
    /// </summary>
    /// <remarks>
    /// Returned unchanged at 100%, which is every sheet in the corpus, so the common case allocates
    /// nothing.
    /// </remarks>
    private static ChartPlot Sized(ChartPlot plot, double scale)
        => scale == 1.0 || !double.IsFinite(scale) || scale <= 0.0
            ? plot
            : plot with
            {
                TitleSize = plot.TitleSize * scale,
                AxisTitleSize = plot.AxisTitleSize * scale,
                LabelSize = plot.LabelSize * scale,
                // The two sizes that are null when the file states none: scaling them has to
                // preserve the null, or a chart that stated neither would come out of the zoom
                // with both pinned to the axis labels' unzoomed size.
                LegendSize = plot.LegendSize is { } legend ? legend * scale : null,
                DataLabelSize = plot.DataLabelSize is { } data ? data * scale : null,
            };

    /// <summary>
    /// The pen a chart's line is drawn with.
    /// </summary>
    /// <remarks>
    /// A zero width is passed through rather than replaced, because it is what the file states and
    /// what LibreOffice's export writes: <c>0 w</c> in the PDF, which every reader draws as the
    /// thinnest line the device has. Substituting a visible width makes every gridline and every
    /// bar outline heavier than the reference's.
    /// </remarks>
    private static Stroke Pen(
        Colour colour, Length width, IReadOnlyList<Length>? dash = null,
        LineCap cap = LineCap.Butt)
        => new(Paint.Solid(colour), width, cap, LineJoin.Miter, DashPattern: dash);

    /// <summary>
    /// Draws one label, shaped and placed by its anchor.
    /// </summary>
    /// <remarks>
    /// <strong>A rotated label is drawn about its own centre.</strong> The value axis' title is the
    /// only rotated text a chart holds, a quarter turn, and a glyph run carries an origin and a
    /// list of advances rather than a matrix — so it cannot be turned after the fact. It is laid
    /// out at the origin instead and the turn goes onto the sink's state stack. Composing the
    /// rotation about the page's origin rather than the label's centre puts the title off the left
    /// of the sheet, which reads as the title having vanished rather than as a placement bug.
    /// </remarks>
    private static void Text(IDrawingSink sink, ChartLabel label)
    {
        if (label.Text.Length == 0) return;

        // Null is "whatever the chart's labels are set in", and ChartLayout's stamping pass has
        // already replaced every null with the chart's own answer by the time a drawing is
        // handed over — so a null surviving to here means no weight was ever stated.
        bool bold = label.IsBold ?? false;

        // A label that shows a percentage beside a category or a series name is written on two
        // lines — Office's own separator (`chart2/source/tools/…/seriesconverter.cxx:168-172`),
        // which `ChartDataLabel.Separator` already defaults to "\n". Shaping the whole string as
        // one run draws the newline as a zero-width nothing and runs the two halves together, so
        // `East` and `26%` came out as the single token `East26%` on
        // `005_Contextures_chart_sample`. The words track fixed the identical defect in
        // `FrameChart` and left this one deliberately; `SlideChart` still has it.
        if (label.Rotation == 0.0 && label.Text.AsSpan().IndexOfAny('\n', '\r') >= 0)
        {
            Lines(sink, label, bold);
            return;
        }

        if (SheetBandText.ChartShape(label.Text, label.Size, label.Family, bold) is not { } run)
            return;

        Length line = SheetBandText.ChartLineHeightAt(label.Size, label.Family, bold);
        Length ascent = SheetBandText.ChartAscentAt(label.Size, label.Family, bold);

        if (label.Rotation != 0.0)
        {
            sink.Save();

            sink.Transform(AffineTransform.Concat(
                // Negated: `ChartLabel.Rotation` is anticlockwise, which is how both formats
                // state one and how chart2's own shapes carry it, and the drawing space here has
                // y growing downwards — so a positive angle handed straight to `Rotation` turns
                // the text the other way. Measured: a two-word value axis title comes out reading
                // top-to-bottom against the reference's bottom-to-top, and 45 degree category
                // labels descend to the right against the reference's ascending. The box does not
                // move, being symmetric about the same centre for either sign.
                AffineTransform.Rotation(-label.Rotation),
                AffineTransform.Translation(label.At.X.Emu, label.At.Y.Emu)));

            sink.DrawGlyphRun(
                run.At(new DocPoint(-(run.Width / 2), -(line / 2) + ascent)),
                Paint.Solid(label.Colour));

            sink.Restore();
            return;
        }

        // A non-square stretch leaves a residual horizontal factor a glyph run's single em cannot
        // carry, so it goes onto the sink instead: the run is placed at 1/stretch of where it goes
        // and drawn under a horizontal scale, which lands it exactly where it would have been and
        // that much wider. chart-bar-sheet.ods is stretched 0.625 across and 0.709 down, so every
        // word of it was 12% too wide before this.
        double stretch = double.IsFinite(label.Stretch) && label.Stretch > 0.0 ? label.Stretch : 1.0;
        Length width = run.Width * stretch;

        Length x = label.Anchor switch
        {
            ChartLabelAnchor.RightMiddle => label.At.X - width,
            ChartLabelAnchor.LeftMiddle => label.At.X,
            _ => label.At.X - (width / 2),
        };

        // CentreTop puts the label's *top* at the point and CentreBottom its bottom; the other
        // three centre it on the point.
        Length top = label.Anchor switch
        {
            ChartLabelAnchor.CentreTop => label.At.Y,
            ChartLabelAnchor.CentreBottom => label.At.Y - line,
            _ => label.At.Y - (line / 2),
        };

        if (stretch == 1.0)
        {
            sink.DrawGlyphRun(run.At(new DocPoint(x, top + ascent)), Paint.Solid(label.Colour));
            return;
        }

        sink.Save();
        sink.Transform(AffineTransform.Scale(stretch, 1.0));
        sink.DrawGlyphRun(
            run.At(new DocPoint(x / stretch, top + ascent)), Paint.Solid(label.Colour));
        sink.Restore();
    }

    /// <summary>
    /// A label holding line breaks, drawn as a stack of lines about the same anchor point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The block is as tall as its lines and each line keeps the whole label's horizontal
    /// alignment, which is what <c>chart2</c>'s text shape does with a multi-paragraph label: a
    /// centred label centres every line on the anchor, a left- or right-anchored one stacks them
    /// flush to that edge, and the block's height replaces one line's in the vertical anchoring so
    /// a <c>CentreBottom</c> two-line label still ends where it was told to.
    /// </para>
    /// <para>
    /// <strong>A rotated label never reaches here</strong> — the caller's guard sends it down the
    /// single-run path, because stacking under a rotation needs the lines offset along the rotated
    /// normal rather than down the page and no rotated label in this corpus carries a break. The
    /// horizontal stretch is applied exactly as the single-run path applies it, for the same
    /// reason: it is a residual factor a glyph run's single em cannot carry.
    /// </para>
    /// </remarks>
    private static void Lines(IDrawingSink sink, ChartLabel label, bool bold)
    {
        string[] parts = label.Text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        List<BandRun> runs = [];
        foreach (string part in parts)
        {
            if (SheetBandText.ChartShape(part, label.Size, label.Family, bold) is { } shaped)
                runs.Add(shaped);
        }

        if (runs.Count == 0) return;

        double stretch = double.IsFinite(label.Stretch) && label.Stretch > 0.0 ? label.Stretch : 1.0;
        Length line = SheetBandText.ChartLineHeightAt(label.Size, label.Family, bold);
        Length ascent = SheetBandText.ChartAscentAt(label.Size, label.Family, bold);
        Length block = line * runs.Count;

        Length top = label.Anchor switch
        {
            ChartLabelAnchor.CentreTop => label.At.Y,
            ChartLabelAnchor.CentreBottom => label.At.Y - block,
            _ => label.At.Y - (block / 2),
        };

        if (stretch != 1.0)
        {
            sink.Save();
            sink.Transform(AffineTransform.Scale(stretch, 1.0));
        }

        for (int at = 0; at < runs.Count; at++)
        {
            Length width = runs[at].Width * stretch;
            Length x = label.Anchor switch
            {
                ChartLabelAnchor.RightMiddle => label.At.X - width,
                ChartLabelAnchor.LeftMiddle => label.At.X,
                _ => label.At.X - (width / 2),
            };

            sink.DrawGlyphRun(
                runs[at].At(new DocPoint(stretch == 1.0 ? x : x / stretch, top + (line * at) + ascent)),
                Paint.Solid(label.Colour));
        }

        if (stretch != 1.0) sink.Restore();
    }

    /// <summary>
    /// Measures a line of chart text in the face a chart's labels are set in.
    /// </summary>
    /// <remarks>
    /// Stateless and shared, because a chart's labels are short, few, and all in one face:
    /// LibreOffice gives a chart's text the same default the rest of the document has, which for
    /// every sheet in the corpus resolves to Liberation Sans. A workbook whose chart states a face
    /// of its own is measured in the default and drawn in it too, so the two agree and the error is
    /// a substitution rather than a misplacement.
    /// </remarks>
    private sealed class Measurer : IChartTextMeasurer
    {
        public static Measurer Instance { get; } = new();

        /// <summary>
        /// Measures in the face the chart states; <paramref name="bold"/> is deliberately not
        /// honoured yet.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="ChartPlot.TextFamily"/> was added on the slides track, which measured it
        /// there and left this consumer unwired on purpose: turning it on changes every workbook's
        /// chart layout, so it wanted the round that sweeps the sheets track. That is this round,
        /// and the sweep is in <c>probes/sheets-r26</c>.
        /// </para>
        /// <para>
        /// Measuring and drawing must take the same family or the two come apart — a label
        /// measured in Liberation Sans and drawn in Carlito is centred on the wrong width. So the
        /// family goes through <see cref="SheetBandText"/>'s family-taking overloads on both
        /// paths, and a chart that names nothing still resolves to
        /// <c>SheetBandText</c>'s default.
        /// </para>
        /// <para>
        /// <strong><paramref name="bold"/> arrived the same way and was ignored for one round
        /// longer.</strong> <see cref="ChartPlot.IsTitleBold"/> was added on the slides track,
        /// where an OOXML chart's title and axis titles were measured bold against LibreOffice's
        /// own model; the reader a workbook's chart reaches is the same one, so it handed a weight
        /// to this measurer and to <see cref="SheetChart"/>'s drawing and both dropped it. It is
        /// honoured now, and the corpus said the cost of not doing so plainly: the reference draws
        /// <c>Template Pilot Logbook JAR-FCL V3.0.xls</c>' chart title and both its axis titles in
        /// Liberation Sans <em>Bold</em> — <c>pdftohtml -xml</c> marks them <c>&lt;b&gt;</c> — and
        /// we drew all three regular while the model already said otherwise.
        /// </para>
        /// <para>
        /// <strong>Measuring and drawing take the same flag for the same reason they take the same
        /// family.</strong> A bold face is wider, so a title measured regular reserves too little
        /// room at the top of the chart and every gridline below it moves.
        /// </para>
        /// </remarks>
        public DocSize Measure(string text, Length size, string? family, bool bold)
        {
            ArgumentNullException.ThrowIfNull(text);

            Length height = SheetBandText.ChartLineHeightAt(size, family, bold);
            return text.Length == 0
                ? new DocSize(Length.Zero, height)
                : new DocSize(
                    SheetBandText.ChartShape(text, size, family, bold)?.Width ?? Length.Zero,
                    height);
        }
    }
}
