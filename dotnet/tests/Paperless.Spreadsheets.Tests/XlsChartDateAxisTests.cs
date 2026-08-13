using Paperless.Core.Charts;
using Shouldly;
using static Paperless.Spreadsheets.Tests.BiffChartFixture;

namespace Paperless.Spreadsheets.Tests;

/// <summary>
/// Which of two label rules a BIFF category axis takes, and what decides it.
/// </summary>
/// <remarks>
/// <para>
/// <c>XclImpChLabelRange::Convert</c> (<c>sc/source/filter/excel/xichart.cxx:3013-3047</c>) is an
/// <c>if</c> and an <c>else</c> over <c>CHDATERANGE</c>'s <c>DATEAXIS</c> flag, and it sets
/// <c>TEXTOVERLAP</c>, <c>TEXTBREAK</c> and <c>ARRANGEORDER</c> in the <c>else</c> alone. Reading
/// only the <c>else</c> — which is what this reader did — applies a date axis' overlap rule from a
/// record the reference never consults for one.
/// </para>
/// <para>
/// It matters because overlap is the first thing <see cref="ChartAxisLabels.Resolve"/> tests: an
/// axis that allows it returns before the auto-rotate ladder is reached, so a crowded date axis
/// came out upright where the reference turns it 45°. Measured on
/// <c>Template Pilot Logbook JAR-FCL V3.0.xls</c>, whose two charts both state
/// <c>CHDATERANGE</c> flags <c>0x00ff</c>: no rotated text at all in our PDF against 848 rotated
/// glyphs in the reference's.
/// </para>
/// </remarks>
public sealed class XlsChartDateAxisTests
{
    /// <summary><c>CHLABELRANGE</c>: a crossing, the label frequency, a tick frequency, flags.</summary>
    private static byte[] LabelRange(ushort frequency) => Record(
        ChLabelRange, [.. Word(1), .. Word(frequency), .. Word(1), .. Word(0)]);

    /// <summary><c>CHDATERANGE</c>: eight fields and then the flag word this is about.</summary>
    private static byte[] DateRange(bool dateAxis) => Record(
        ChDateRange,
        [
            .. Word(0), .. Word(0), .. Word(1), .. Word(0),
            .. Word(1), .. Word(0), .. Word(0), .. Word(0),
            .. Word(dateAxis ? (ushort)0x0010 : (ushort)0x0000),
        ]);

    /// <summary>An X-axis group holding the records given. Axis 0 is the categories.</summary>
    private static byte[] CategoryAxis(params byte[][] children)
        => Group(ChAxis, [.. Word(0), .. new byte[16]], children);

    /// <summary>
    /// Without a date axis, the label frequency decides — which is the rule we already had.
    /// </summary>
    /// <remarks>
    /// Kept as the control. If this moved, the fix would have replaced one blanket rule with
    /// another rather than split them.
    /// </remarks>
    [Fact]
    public void ANonDateAxisTakesItsOverlapRuleFromTheLabelFrequency()
    {
        ChartPlot every = Chart(Substream([.. CategoryAxis(LabelRange(1), DateRange(false))]));
        every.CategoryAxisText.OverlapAllowed.ShouldBeTrue();
        every.CategoryAxisText.LineBreakAllowed.ShouldBeTrue();

        ChartPlot thinned = Chart(Substream([.. CategoryAxis(LabelRange(3), DateRange(false))]));
        thinned.CategoryAxisText.OverlapAllowed.ShouldBeFalse();
        thinned.CategoryAxisText.LineBreakAllowed.ShouldBeFalse();
    }

    /// <summary>
    /// A date axis ignores the frequency and keeps chart2's defaults, so its labels can rotate.
    /// </summary>
    /// <remarks>
    /// The frequency is 1 here — "label every category", the value that turns overlap *on* for
    /// every other axis and the value the corpus's own chart states. A reader that applied it
    /// anyway would leave <c>OverlapAllowed</c> true and this would fail.
    /// </remarks>
    [Fact]
    public void ADateAxisKeepsTheDefaultsWhateverTheLabelFrequencySays()
    {
        ChartPlot plot = Chart(Substream([.. CategoryAxis(LabelRange(1), DateRange(true))]));

        plot.CategoryAxisText.OverlapAllowed.ShouldBeFalse();
        plot.CategoryAxisText.LineBreakAllowed.ShouldBeFalse();
        plot.CategoryAxisText.Stagger.ShouldBe(ChartLabelStagger.Auto);
    }

    /// <summary>
    /// The two records decide together however they are ordered.
    /// </summary>
    /// <remarks>
    /// BIFF does not fix their order and LibreOffice keeps both halves on one
    /// <c>XclImpChLabelRange</c> until <c>Convert</c> reads them together, so a reader that let
    /// whichever arrived last win would be right on the corpus and wrong in general — the corpus's
    /// one file writes <c>CHLABELRANGE</c> first.
    /// </remarks>
    [Fact]
    public void TheOrderOfTheTwoRecordsDoesNotChangeTheAnswer()
    {
        ChartPlot first = Chart(Substream([.. CategoryAxis(LabelRange(1), DateRange(true))]));
        ChartPlot second = Chart(Substream([.. CategoryAxis(DateRange(true), LabelRange(1))]));

        second.CategoryAxisText.ShouldBe(first.CategoryAxisText);
        second.CategoryAxisText.OverlapAllowed.ShouldBeFalse();
    }

    /// <summary>A <c>CHDATERANGE</c> on the value axis says nothing about the categories.</summary>
    /// <remarks>
    /// The record is read under <c>_axis == AxisX</c> only, the same guard <c>CHLABELRANGE</c>
    /// carries. Without it a value axis' own record would silently retune the category axis.
    /// </remarks>
    [Fact]
    public void ADateRangeOnTheValueAxisIsNotTheCategoryAxisRule()
    {
        ChartPlot plot = Chart(Substream(
        [
            .. CategoryAxis(LabelRange(1)),
            .. Group(ChAxis, [.. Word(1), .. new byte[16]], DateRange(true)),
        ]));

        plot.CategoryAxisText.OverlapAllowed.ShouldBeTrue();
    }

    private const ushort ChLabelRange = 0x1020;
    private const ushort ChDateRange = 0x1062;
}
