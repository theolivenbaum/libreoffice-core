using Paperless.Core.Units;
using Paperless.Text.Fonts;
using Paperless.Text.Layout;
using Shouldly;

namespace Paperless.Text.Tests;

/// <summary>
/// A paragraph whose whole text is format control characters still has a height.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Itemisation.TextItemiser"/> cuts every format control out of the item list, and
/// U+2028 — what all four word-processing readers and both DrawingML readers emit for a manual
/// line break — is one. So the sub-runs of a paragraph made of nothing else cover nothing,
/// <see cref="MeasuredParagraph"/> ends up with no runs, and <c>MeasureLine</c>'s last resort —
/// the empty-paragraph rule, which takes the first run's metrics — has no run to take them from.
/// Every line of such a paragraph measured <b>nought points</b>.
/// </para>
/// <para>
/// Measured against LibreOffice 26.2.4.2 by <c>probes/words-r52/br-paragraph-probe.py</c>, nine
/// authored DOCX differing only in what one paragraph between <c>AAA</c> and <c>BBB</c> holds,
/// Cambria on A4 — points the paragraph under test adds:
/// </para>
/// <code>
///   case                        reference   before   after
///   one empty paragraph             12.65    11.50   11.50
///   one paragraph of one w:br       25.30     0.00   23.00
///   two empty paragraphs            25.30    23.00   23.00
///   one paragraph of two w:br       37.95     0.00   34.50
///   a space and then a w:br         25.30    23.00   23.00
/// </code>
/// <para>
/// So the reference gives a paragraph with N breaks N+1 lines and we now do too. The residual —
/// 11.50 against 12.65 per line — is the standing line-height deficit that every row of the table
/// carries equally, including the rows that were already right, and is not this rule's business.
/// </para>
/// <para>
/// <b>The space is what identifies the mechanism.</b> A single space in front of the break was
/// always enough to make the paragraph measure correctly, because a space is not a control and so
/// leaves a run behind for the fallback to find. Nothing about the break itself was ever wrong:
/// <see cref="TrailingLineBreakTests.AParagraphOfNothingButABreakIsTwoLines"/> shows the filler
/// producing both lines all along.
/// </para>
/// </remarks>
public class ControlOnlyParagraphHeightTests
{
    private static readonly Length Size = Length.FromPoints(12);

    /// <summary>A paragraph of one line separator is as tall as a paragraph of text.</summary>
    /// <remarks>
    /// Against a paragraph holding a character rather than against an empty one, because an empty
    /// paragraph never reaches here: <c>Normalise</c> drops a zero-length run, so a caller with no
    /// text has no formatted run for this rule to keep and the single-face path measures it instead.
    /// </remarks>
    [Fact]
    public void AParagraphOfNothingButABreakIsAsTallAsAnEmptyOne()
    {
        OpenTypeFace face = Carlito();

        Length control = MeasuredParagraph
            .Measure("\u2028", [new FormattedRun(0, 1, face, Size)])
            .MeasureLine(0, 0).Height;

        (Length ordinary, _) = MeasuredParagraph
            .Measure("x", [new FormattedRun(0, 1, face, Size)])
            .HeightOf(0, 1);

        control.ShouldBeGreaterThan(Length.Zero);
        control.ShouldBe(ordinary);
    }

    /// <summary>
    /// Every line of the paragraph, not merely the first, and the empty one the break opens too.
    /// </summary>
    /// <remarks>
    /// The line the filler opens past the last character is measured over an empty range at the very
    /// end of the text, which is a different arm of <c>MeasureLine</c> from the one the first line
    /// takes. A fix that only served position nought would leave the paragraph half its height.
    /// </remarks>
    [Fact]
    public void EveryLineOfSuchAParagraphHasTheHeight()
    {
        OpenTypeFace face = Carlito();
        MeasuredParagraph paragraph = MeasuredParagraph
            .Measure("\u2028\u2028", [new FormattedRun(0, 2, face, Size)]);

        foreach ((int start, int end) in new[] { (0, 0), (1, 1), (2, 2) })
        {
            paragraph.MeasureLine(start, end).Height.ShouldBeGreaterThan(Length.Zero);
        }
    }

    /// <summary>
    /// The height is the paragraph's own face and size, not some default.
    /// </summary>
    /// <remarks>
    /// The run kept is the first <em>formatted</em> run, which is what the reader stated; a rule that
    /// invented a face would give every break-only paragraph in the corpus one height regardless of
    /// what the document asked for. Two sizes, so the answer is a slope rather than a point.
    /// </remarks>
    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    public void TheHeightIsTheParagraphsOwnSize(int points)
    {
        OpenTypeFace face = Carlito();
        Length size = Length.FromPoints(points);

        Length control = MeasuredParagraph
            .Measure("\u2028", [new FormattedRun(0, 1, face, size)])
            .MeasureLine(0, 0).Height;

        (Length ordinary, _) = MeasuredParagraph
            .Measure("x", [new FormattedRun(0, 1, face, size)])
            .HeightOf(0, 1);

        control.ShouldBe(ordinary);
    }

    /// <summary>
    /// Laid out, a paragraph of N breaks is N + 1 lines and each of them has the height.
    /// </summary>
    /// <remarks>
    /// The filler and the measurement together, since it is their product the page shows. This is the
    /// assertion the corpus moved on: before, the line count was already right and the total height
    /// was nought.
    /// </remarks>
    [Theory]
    [InlineData("\u2028", 2)]
    [InlineData("\u2028\u2028", 3)]
    [InlineData("\u2028\u2028\u2028", 4)]
    public void ABreakOnlyParagraphLaysOutAsNPlusOneLinesWithHeight(string text, int lines)
    {
        OpenTypeFace face = Carlito();
        ParagraphLayouter layouter = new(face);

        LaidOutParagraph laid = layouter.Layout(
            MeasuredParagraph.Measure(text, [new FormattedRun(0, text.Length, face, Size)]),
            textAreaWidth: Length.FromPoints(400),
            emSize: Size);

        laid.Lines.Count.ShouldBe(lines);
        laid.Height.ShouldBeGreaterThan(Length.Zero);
        foreach (LineBox box in laid.Lines) box.Height.ShouldBe(laid.Lines[0].Height);
    }

    /// <summary>
    /// A paragraph that has any non-control character is untouched, which is 3936 of the corpus's
    /// break-bearing paragraphs against the 22 this rule reaches.
    /// </summary>
    /// <remarks>
    /// The control that makes the rule falsifiable: a change that gave every paragraph an extra run
    /// would pass every assertion above and would move the whole corpus. A space is the narrowest
    /// case — it is a blank, so it is transparent to a line's height by
    /// <see cref="BlankLineHeightTests"/>'s rule, and it still must leave the run count alone.
    /// </remarks>
    [Theory]
    [InlineData(" \u2028")]
    [InlineData("\u2028x")]
    [InlineData("x")]
    public void AParagraphHoldingAnythingElseKeepsItsOwnRuns(string text)
    {
        OpenTypeFace face = Carlito();

        MeasuredParagraph.Measure(text, [new FormattedRun(0, text.Length, face, Size)])
            .Runs.Count.ShouldBe(1);

        MeasuredParagraph.Measure(text, [new FormattedRun(0, text.Length, face, Size)])
            .Runs[0].Run.Length.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// The kept run is zero length, so nothing but the fallback can see it.
    /// </summary>
    /// <remarks>
    /// <c>Fold</c> matches a run on <c>touches</c> or <c>contains</c> and both are false for an empty
    /// range; <c>RunsBetween</c> clips it away, so the drawing pass never meets it and no glyph run is
    /// emitted for a paragraph that has no glyphs. Giving the run the paragraph's whole length instead
    /// would put it in the fold — where it would be right by accident here and wrong the moment a real
    /// run sat beside it.
    /// </remarks>
    [Fact]
    public void TheKeptRunIsInvisibleToDrawingAndToTheFold()
    {
        OpenTypeFace face = Carlito();
        MeasuredParagraph paragraph = MeasuredParagraph
            .Measure("\u2028", [new FormattedRun(0, 1, face, Size)]);

        paragraph.Runs.Count.ShouldBe(1);
        paragraph.Runs[0].Run.Length.ShouldBe(0);
        paragraph.RunsBetween(0, 1).ShouldBeEmpty();
        paragraph.WidthBetween(0, 1).ShouldBe(Length.Zero);
    }

    /// <summary>
    /// A control that is not a line separator is the same defect and is fixed by the same rule.
    /// </summary>
    /// <remarks>
    /// U+200E and U+FEFF are cut by the same predicate for the same reason. No corpus document is
    /// known to hold a paragraph of nothing else, so this is the rule stated at its own width rather
    /// than at the width of the one witness that drove it.
    /// </remarks>
    [Theory]
    [InlineData("\u200E")]
    [InlineData("\uFEFF")]
    [InlineData("\u2060")]
    public void AnyControlOnlyParagraphHasAHeight(string text)
    {
        OpenTypeFace face = Carlito();

        MeasuredParagraph.Measure(text, [new FormattedRun(0, text.Length, face, Size)])
            .MeasureLine(0, 0).Height.ShouldBeGreaterThan(Length.Zero);
    }

    /// <summary>Text with no runs at all stays with no runs — there is nothing to keep.</summary>
    /// <remarks>
    /// The rule keeps the <em>first formatted run</em>, so a caller that supplied none must still get
    /// none rather than a fabricated one. <c>PageParagraph.Measure</c> always supplies one, but the
    /// slide and sheet paths reach this directly.
    /// </remarks>
    [Fact]
    public void AParagraphWithNoRunsAtAllIsUnchanged()
        => MeasuredParagraph.Measure("\u2028", []).Runs.ShouldBeEmpty();

    private static OpenTypeFace Carlito()
    {
        string? path = FindFont("Carlito-Regular.ttf");
        Assert.SkipWhen(path is null, "Carlito is not installed; see check-env.sh");
        return OpenTypeFace.ReadFile(path!).ShouldNotBeNull();
    }

    private static string? FindFont(string fileName)
    {
        foreach (string directory in new[]
                 {
                     "/usr/share/fonts/truetype/crosextra",
                     "/usr/share/fonts/truetype/dejavu",
                     "/usr/share/fonts",
                 })
        {
            if (!Directory.Exists(directory)) continue;

            string[] found = Directory.GetFiles(directory, fileName, SearchOption.AllDirectories);
            if (found.Length > 0) return found[0];
        }

        return null;
    }
}
