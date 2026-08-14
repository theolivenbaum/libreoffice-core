using Paperless.Core.Documents;
using Paperless.Core.Geometry;
using Paperless.Core.Units;
using Paperless.TestKit;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A text box of stated height formats only the lines that fit, and never the rest.
/// </summary>
/// <remarks>
/// <para>
/// A shape's text body either grows with its text or keeps the height the file gives it. In the
/// second case Writer stops formatting when the next line would begin below the box; the lines
/// after that are not laid out at all, so they are absent from the PDF's text operators rather
/// than merely clipped by a painting rectangle. Getting this wrong draws a whole running head's
/// worth of surplus text on every page of a document — <c>words/extra-001</c> holds three
/// documents sharing one Word template whose head is a 15.00 pt box holding four paragraphs of
/// 8 pt text, and drawing all four cost one of them 60 extractable words over 12 pages.
/// </para>
/// <para>
/// <strong>The rule is measured, not inferred.</strong>
/// <c>dotnet/probes/words-extra-01/probe-textbox-sweep.py</c> renders 60 authored boxes through
/// the installed LibreOffice 26.2.4.2 — stated heights from 1 pt to 100 pt at three inset sizes,
/// each holding six paragraphs of 8 pt text — and one rule fits every one of them:
/// </para>
/// <para>
/// <em>A line is formatted iff its top offset is strictly less than the box's content height, and
/// the first line is always formatted however short the box.</em>
/// </para>
/// <para>
/// The obvious alternative — keep a line only when it fits entirely — is refuted rather than
/// merely unsupported: a 10 pt box with zero insets draws two lines of a face taller than 5 pt.
/// <c>a:normAutofit</c> does not spare the content either (LibreOffice truncates rather than
/// shrinking) and neither does <c>bodyPr/@vertOverflow</c>, whose <c>overflow</c> and
/// <c>clip</c> values render identically. Only <c>a:spAutoFit</c> does.
/// </para>
/// <para>
/// The fixture is <c>textbox-overflow.docx</c>, authored by
/// <c>probes/words-extra-01/make-fixture.py</c> and ground-truthed against that same binary: its
/// PDF extracts <c>BOXA0 BOXA1 BOXA2 BOXB0 BOXC0…BOXC5</c>, which is the 3 / 1 / 6 these tests
/// assert. Three boxes rather than one because a reader that truncates everything and a reader
/// that truncates nothing each pass a third of them.
/// </para>
/// </remarks>
public sealed class TextBoxOverflowTests
{
    /// <summary>A box of stated height keeps the lines that start inside it and drops the rest.</summary>
    [Fact]
    public void AStatedHeightBoxFormatsOnlyTheLinesThatFit()
        => Drawn("BOXA").ShouldBe(3, "a 30 pt box holds three lines of this 8 pt text");

    /// <summary>
    /// A box too short for any line still draws one.
    /// </summary>
    /// <remarks>
    /// The part of the rule that is not arithmetic. An 8 pt box with 3.6 pt insets top and bottom
    /// has 0.8 pt of room, which is less than a line, and Writer formats a line into it anyway —
    /// which is why the corpus head still says "Document reference:" rather than nothing at all.
    /// A reader implementing only the arithmetic empties it.
    /// </remarks>
    [Fact]
    public void ABoxTooShortForOneLineStillDrawsOne()
        => Drawn("BOXB").ShouldBe(1, "the first line survives however short the box");

    /// <summary>An autofitting box grows to its text, so nothing is dropped.</summary>
    /// <remarks>
    /// The control, and the reason this is a property of the shape rather than of every frame:
    /// <c>BOXC</c> is 15 pt — shorter than <c>BOXA</c>, which loses three lines — and keeps all
    /// six, because <c>a:spAutoFit</c> says the stated height is not the real one.
    /// </remarks>
    [Fact]
    public void AnAutoFittingBoxKeepsAllOfIt()
        => Drawn("BOXC").ShouldBe(6, "spAutoFit grows the box rather than truncating the text");

    /// <summary>
    /// The truncation is the layout's, not the reader's: every paragraph is still in the model.
    /// </summary>
    /// <remarks>
    /// Extraction must not lose text a renderer declines to draw — <c>IDocument</c> is what a
    /// caller indexes with, and a box's text is in the document whether or not it is visible. So
    /// the cut belongs where it is, between laying the flow out and placing it, and this pins that
    /// it did not migrate into the reader.
    /// </remarks>
    [Fact]
    public void TruncationDoesNotReachTheContentModel()
    {
        PageFrame frame = FrameNamed("BOXA");

        frame.Blocks.Count.ShouldBe(6, "all six paragraphs are read");
        frame.HasFixedHeight.ShouldBeTrue("noAutofit is a stated height");
        FrameNamed("BOXC").HasFixedHeight.ShouldBeFalse("spAutoFit is not");
    }

    /// <summary>
    /// The box's text insets are read, because they are what the fit is measured against.
    /// </summary>
    /// <remarks>
    /// ECMA-376 §20.1.2.2.9's defaults are 91440 EMU (7.20 pt) left and right and 45720 (3.60 pt)
    /// top and bottom, and the fixture states exactly those. Reading them as zero makes a 15 pt
    /// box hold 15 pt of text rather than 7.8 pt, which is one line more than Writer draws — so
    /// this is not decoration, it is the operand of the rule above.
    /// </remarks>
    [Fact]
    public void ATextBoxCarriesItsBodyInsets()
    {
        PageFrame frame = FrameNamed("BOXA");

        frame.Padding.Left.Points.ShouldBe(7.20, 0.01);
        frame.Padding.Right.Points.ShouldBe(7.20, 0.01);
        frame.Padding.Top.Points.ShouldBe(3.60, 0.01);
        frame.Padding.Bottom.Points.ShouldBe(3.60, 0.01);
    }

    /// <summary>
    /// The rule is "top is inside", not "the whole line is inside".
    /// </summary>
    /// <remarks>
    /// Asserted directly on <see cref="FlowLayouter.Truncated"/> rather than through a document,
    /// because the two rules differ by exactly one line and a fixture that separated them would
    /// have to be balanced on a fraction of a point. A line 10 pt tall at the top of a 12 pt box
    /// is followed by one whose top is 10 pt — inside 12, so kept, although it reaches 20.
    /// </remarks>
    [Fact]
    public void ALineIsKeptWhenItsTopIsInsideEvenIfItsBottomIsNot()
    {
        PlacedFlow flow = Flow(Length.FromPoints(12), lineHeight: 10, lines: 4);

        FlowLayouter.Truncated(flow, Length.FromPoints(12))
            .Lines.Count.ShouldBe(2, "tops at 0 and 10 are inside 12; 20 and 30 are not");
    }

    /// <summary>Truncating a flow that already fits returns it untouched.</summary>
    [Fact]
    public void AFlowThatFitsIsNotTouched()
    {
        PlacedFlow flow = Flow(Length.FromPoints(100), lineHeight: 10, lines: 4);

        FlowLayouter.Truncated(flow, Length.FromPoints(100)).ShouldBeSameAs(flow);
    }

    /// <summary>A synthetic flow of evenly stacked lines, for the two rule tests above.</summary>
    private static PlacedFlow Flow(Length height, double lineHeight, int lines)
    {
        List<PlacedLine> placed = [];
        for (int i = 0; i < lines; i++)
        {
            placed.Add(new PlacedLine(
                i, 0, default, Length.FromPoints(lineHeight * i)));
        }

        return new PlacedFlow
        {
            Blocks = [],
            Lines = placed,
            Area = new DocRect(Length.Zero, Length.Zero, Length.FromPoints(200), height),
        };
    }

    /// <summary>How many of a box's own paragraphs were actually placed.</summary>
    private static int Drawn(string tag)
    {
        LaidOutPage page = Paginate()[0];

        foreach (PlacedFrame frame in page.Frames)
        {
            if (frame.Frame.Name != $"Text Box {tag}") continue;
            return frame.Content is null ? 0 : frame.Content.Lines.Count;
        }

        throw new InvalidOperationException($"the fixture has no frame named 'Text Box {tag}'");
    }

    /// <summary>One of the fixture's frames, before placement.</summary>
    private static PageFrame FrameNamed(string tag)
    {
        LaidOutPage page = Paginate()[0];

        foreach (PlacedFrame frame in page.Frames)
        {
            if (frame.Frame.Name == $"Text Box {tag}") return frame.Frame;
        }

        throw new InvalidOperationException($"the fixture has no frame named 'Text Box {tag}'");
    }

    private static IReadOnlyList<LaidOutPage> Paginate()
    {
        using IDocument document = new WordProcessingReader()
            .Read(DocumentSource.FromFile(Corpus.Require("textbox-overflow.docx")));

        return ((WordProcessingPages)((IPaginatedDocument)document).Layout()).Pages;
    }
}
