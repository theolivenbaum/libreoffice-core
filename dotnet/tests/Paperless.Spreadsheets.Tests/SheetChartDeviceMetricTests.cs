using Paperless.Core.Units;
using Paperless.Spreadsheets.Layout;
using Shouldly;

namespace Paperless.Spreadsheets.Tests;

/// <summary>
/// A chart's text is quantised onto <c>chart2</c>'s own 96 dpi device, and these are the
/// reference's own numbers.
/// </summary>
/// <remarks>
/// <para>
/// Every expected value below was read out of a rendering made by the installed
/// LibreOffice 26.2.4.2, not computed from the model being tested:
/// <c>probes/sheets-r60/probe-chartvmetrics2.py</c> renders thirteen sizes on three faces as a
/// one-line and a three-line chart title and measures the baseline pitch between the lines, and
/// <c>law-chartvmetrics.py</c> reads them back. Each rendering is divided by its own
/// <c>drawn em / stated em</c> before it is compared, which is never more than 0.7%.
/// </para>
/// <para>
/// <strong>The three faces are what separate the two candidate laws.</strong> Carlito's
/// <c>hhea</c> line gap is zero and Liberation Sans' is 67/2048 and Liberation Serif's 87/2048, so
/// a model that keeps the external leading is wrong on two of the three and right on the first;
/// and the sizes where <c>size × 4/3</c> is not a whole number are what separate the pixel
/// rounding from exact scaling. A single face at a single size separates neither.
/// </para>
/// <para>
/// The tolerance is a tenth of a point because that is the instrument's own worst case over the
/// 39 measurements — the em is read off a PDF text matrix to two decimals — not a slack chosen to
/// let a model through.
/// </para>
/// </remarks>
public sealed class SheetChartDeviceMetricTests
{
    /// <summary>The reference's own pitches: (family, stated size, measured baseline pitch).</summary>
    public static TheoryData<string, double, double> ReferencePitches => new()
    {
        { "Calibri", 6.0, 7.503 },
        { "Calibri", 8.0, 9.730 },
        { "Calibri", 10.0, 11.219 },
        { "Calibri", 11.0, 13.460 },
        { "Calibri", 12.0, 14.232 },
        { "Calibri", 14.0, 17.232 },
        { "Calibri", 16.0, 19.585 },
        { "Calibri", 18.0, 21.708 },
        { "Calibri", 20.0, 24.692 },
        { "Calibri", 24.0, 29.314 },
        { "Calibri", 28.0, 33.791 },
        { "Calibri", 32.0, 39.797 },
        { "Calibri", 40.0, 47.992 },
        { "Arial", 6.0, 6.741 },
        { "Arial", 8.0, 8.990 },
        { "Arial", 10.0, 11.219 },
        { "Arial", 11.0, 12.720 },
        { "Arial", 12.0, 12.731 },
        { "Arial", 14.0, 15.731 },
        { "Arial", 16.0, 17.339 },
        { "Arial", 18.0, 20.179 },
        { "Arial", 20.0, 22.461 },
        { "Arial", 24.0, 27.064 },
        { "Arial", 28.0, 30.790 },
        { "Arial", 32.0, 36.034 },
        { "Arial", 40.0, 44.252 },
        { "Times New Roman", 6.0, 6.741 },
        { "Times New Roman", 10.0, 11.219 },
        { "Times New Roman", 11.0, 11.960 },
        { "Times New Roman", 12.0, 12.731 },
        { "Times New Roman", 16.0, 18.084 },
        { "Times New Roman", 18.0, 19.439 },
        { "Times New Roman", 24.0, 27.064 },
        { "Times New Roman", 32.0, 35.291 },
        { "Times New Roman", 40.0, 43.520 },
    };

    [Theory]
    [MemberData(nameof(ReferencePitches))]
    public void AChartStacksItsLinesAtTheReferencesOwnPitch(string family, double size, double pitch)
    {
        SheetBandText.ChartLineHeightAt(Length.FromPoints(size), family)
            .Points.ShouldBe(pitch, 0.10);
    }

    /// <summary>
    /// The pitch is a whole number of 96 dpi pixels — 0.75 pt — at every size on every face.
    /// </summary>
    /// <remarks>
    /// This is the shape of the law rather than one of its values, and it is what a continuous
    /// model cannot satisfy: <c>ascent + descent</c> for Carlito is 1.2207 em, which is a whole
    /// number of pixels at almost no size at all.
    /// </remarks>
    [Theory]
    [InlineData("Calibri")]
    [InlineData("Arial")]
    [InlineData("Times New Roman")]
    public void EveryChartLinePitchIsAWholeNumberOf96DpiPixels(string family)
    {
        foreach (double size in new[] { 6.0, 8.0, 10.0, 11.0, 12.0, 14.0, 16.0, 18.0, 20.0, 24.0 })
        {
            double pixels = SheetBandText.ChartLineHeightAt(Length.FromPoints(size), family).Points
                / 0.75;
            Math.Abs(pixels - Math.Round(pixels)).ShouldBeLessThan(0.05, $"{family} at {size} pt");
        }
    }

    /// <summary>
    /// A chart label's ascent at ten point in Carlito is nine points, not the face's 9.52.
    /// </summary>
    /// <remarks>
    /// Measured by requiring a CENTER data label's block centre to come out size-independent over
    /// ten sizes — <c>law-chartvmetrics.py</c> § B, where the pixel law spreads it by 0.042 pt and
    /// the face's own continuous metrics by 0.603. It is 12 pixels of 13, and the reference draws
    /// 9.00.
    /// </remarks>
    [Fact]
    public void AChartLabelsAscentIsTheDevicesAndNotTheFaces()
    {
        SheetBandText.ChartAscentAt(Length.FromPoints(10), "Calibri").Points
            .ShouldBe(9.0, 0.05);
        SheetBandText.AscentAt(Length.FromPoints(10), "Calibri").Points
            .ShouldBe(9.52, 0.05);
    }

    /// <summary>
    /// A chart's line is not a cell's line, and both differ from a drawing shape's.
    /// </summary>
    /// <remarks>
    /// Three devices and three answers at the same size in the same face: <c>chart2</c>'s 96 dpi,
    /// Calc's 720 dpi output device, and — for a drawing shape's text — the ungridded arithmetic
    /// chart text used before round 60, which is kept under its own name because it is
    /// <b>unmeasured</b> rather than because it is believed. This pins that they are three
    /// separate answers, so that a future change to one cannot silently take the others with it.
    /// </remarks>
    [Fact]
    public void TheChartCellAndShapeMetricsAreThreeSeparateAnswers()
    {
        Length size = Length.FromPoints(10);

        SheetBandText.ChartLineHeightAt(size, "Calibri").Points.ShouldBe(11.25, 0.05);
        SheetBandText.LineHeightAt(size, "Calibri").Points.ShouldBe(12.20, 0.06);
        SheetBandText.ShapeLineHeightAt(size, "Calibri").Points.ShouldBe(12.21, 0.02);

        SheetBandText.ChartLineHeightAt(size, "Arial").Points.ShouldBe(11.25, 0.05);
        SheetBandText.ShapeLineHeightAt(size, "Arial").Points.ShouldBe(11.50, 0.02);
    }
}
