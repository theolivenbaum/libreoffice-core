using Paperless.Core.Documents;
using Paperless.Core.Graphics;
using Paperless.TestKit;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// Checks that a frame's border and fill are read out of both formats that state them.
/// </summary>
/// <remarks>
/// <para>
/// The companion to <c>FrameDecorationComparisonTests</c>, which measures where the fill and the border land.
/// Two things it cannot see: a colour, because neither PDF reader records one, and a DOCX frame's border, whose
/// stroke LibreOffice writes as a single closed path where the stroke reader takes two-point lines. Both are
/// pinned here instead.
/// </para>
/// <para>
/// The two formats say the same thing with nothing in common. ODF puts four separate <c>fo:border</c> values
/// and an <c>fo:background-color</c> on a graphic style; OOXML puts a single DrawingML outline and an
/// <c>a:solidFill</c> on the shape, with the width in EMUs rather than as part of a CSS shorthand. And they
/// <em>draw</em> differently: a frame's border sits inside its edge and a shape's outline is centred on it,
/// which is a whole border width apart.
/// </para>
/// </remarks>
public sealed class FrameDecorationTests
{
    [Theory]
    [InlineData("frame-box.fodt")]
    [InlineData("frame-box.docx")]
    [InlineData("frame-box.rtf")]
    public void AFrameCarriesTheColoursTheDocumentStates(string fileName)
    {
        PageFrame frame = OnlyFrame(fileName);

        frame.Background.ShouldBe(Colour.FromRgb(0xCCFFCC), $"{fileName}: the fill colour");

        foreach (TableBorder side in Sides(frame))
        {
            side.Colour.ShouldBe(Colour.FromRgb(0xC9211E), $"{fileName}: a border's colour");

            // Two points, which the two formats spell differently: `2pt` inside a CSS shorthand, and 25400
            // EMUs in an attribute of its own. A reader taking the OOXML width as twips draws it 36 times too
            // wide, and one taking it as points draws a border wider than the page.
            side.Width.Points.ShouldBe(2, tolerance: 0.02, customMessage: $"{fileName}: its width");
        }
    }

    [Theory]
    [InlineData("frame-box.fodt")]
    // RTF too, which is the surprise: an RTF `{\shp}` is a shape by name and a Writer text frame by
    // behaviour — LibreOffice's import builds one from shape type 202 and draws its border inside the edge,
    // where the same box in DOCX has its outline centred on it.
    [InlineData("frame-box.rtf")]
    public void AFramesBorderSitsInsideItsEdge(string fileName)
    {
        OnlyFrame(fileName).BorderStraddlesTheEdge.ShouldBeFalse(fileName);
    }

    [Fact]
    public void AnOdfFramesBorderSitsInsideItsEdgeAtTheMeasuredPlace()
    {
        // Measured: LibreOffice strokes this frame's 2 pt left border down x = 57.7 pt where the frame's own
        // left edge is 56.7. So the frame is where the document says and the border grows inwards.
        OnlyFrame("frame-box.fodt").BorderStraddlesTheEdge.ShouldBeFalse();
    }

    [Fact]
    public void AnOoxmlTextBoxesOutlineIsCentredOnItsEdge()
    {
        // The same document exported to DOCX, where LibreOffice strokes the same border at 56.65 — because an
        // OOXML text box is a DrawingML shape, and a shape's outline straddles its edge. Half of it therefore
        // falls outside the box, which is the opposite of the ODF case and a whole border width away from it.
        OnlyFrame("frame-box.docx").BorderStraddlesTheEdge.ShouldBeTrue();
    }

    [Fact]
    public void AFrameThatStatesNoBorderGetsNone()
    {
        // The document that came before this feature, and the assertion that it did not appear on it: an ODF
        // frame saying `fo:border="none"` and no background must draw nothing, or every frame in the corpus
        // grows an outline it never asked for.
        PageFrame frame = OnlyFrame("wrap-frame.fodt");

        frame.Background.ShouldBeNull();
        foreach (TableBorder side in Sides(frame)) side.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void ADocxExportBakesInTheShapeDefaultsTheOriginalDidNotHave()
    {
        // Not a reader bug and worth pinning as behaviour, because it looks like one. `wrap-frame.fodt`'s
        // graphic style has no parent, so LibreOffice imports the frame as a drawing shape (see
        // `tests/corpus/README.md`) — and its DOCX export then writes that shape's *defaults* into the file as
        // an explicit `a:solidFill` of #729FCF and an `a:ln` of #3465A4. So the exported DOCX genuinely has a
        // blue box where the ODF original has none, and LibreOffice's own render of the two differs the same
        // way. Reading the file faithfully means agreeing with the DOCX render, not with the ODF one.
        PageFrame frame = OnlyFrame("wrap-frame.docx");

        frame.Background.ShouldBe(Colour.FromRgb(0x729FCF));

        // And the outline is still nothing drawn, because the export states its width as zero. LibreOffice
        // draws a hairline there; a stroke under a tenth of a point is a divergence worth knowing and not
        // worth a special case.
        foreach (TableBorder side in Sides(frame)) side.IsNone.ShouldBeTrue();
    }

    private static IEnumerable<TableBorder> Sides(PageFrame frame)
        => [frame.Borders.Top, frame.Borders.Left, frame.Borders.Bottom, frame.Borders.Right];

    private static PageFrame OnlyFrame(string fileName)
    {
        string path = Corpus.Require(fileName);

        using FileStream stream = File.OpenRead(path);
        using DocumentSource source = DocumentSource.FromStream(stream, Path.GetFileName(path));
        using IDocument document = new WordProcessingReader().Read(source);

        WordProcessingPages pages = (WordProcessingPages)((IPaginatedDocument)document).Layout();

        return pages.Paragraphs
            .SelectMany(paragraph => paragraph.Frames)
            .ShouldHaveSingleItem();
    }
}
