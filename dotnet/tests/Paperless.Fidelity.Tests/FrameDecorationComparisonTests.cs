using Paperless.Core.Documents;
using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.TestKit;
using Paperless.TestKit.LibreOffice;
using Paperless.WordProcessing;
using Shouldly;

namespace Paperless.Fidelity.Tests;

/// <summary>
/// Checks that a frame's own fill and border are drawn where LibreOffice draws them.
/// </summary>
/// <remarks>
/// <para>
/// The frame comparisons so far measure where the <em>text</em> went, which is what a wrap mode decides. This
/// measures what the frame itself puts on the page, and it is a different shape of question: a fill is a
/// rectangle and a border is four strokes, neither visible to <c>pdftotext</c>. Both come out of LibreOffice's
/// PDF export as explicit geometry, which is what <see cref="PdfFills"/> and <see cref="PdfStrokes"/> read.
/// </para>
/// <para>
/// A frame's border is <em>not</em> laid out like a table's grid line, and this is the assertion that pins the
/// difference. A grid line straddles the boundary it sits on, so half its width falls either side; a frame's
/// border grows <em>inwards</em> from the frame's own edge. Measured on this document: the left stroke of a
/// 2 pt border runs down x = 57.7 where the frame's left edge is at 56.7, and each stroke spans its whole side
/// rather than stopping where the perpendicular ones cross it.
/// </para>
/// <para>
/// Colour is asserted on the drawn side alone rather than compared, because neither PDF reader records it —
/// they read geometry. That is not a gap worth closing here: what a wrongly-read border looks like is a border
/// in the wrong <em>place</em> or of the wrong width far more often than one of the wrong colour, and the
/// colour a document states is checkable without a reference.
/// </para>
/// </remarks>
public sealed class FrameDecorationComparisonTests : IDisposable
{
    /// <summary>How far a drawn edge may differ from LibreOffice's, in points.</summary>
    /// <remarks>
    /// A sixth of a point. LibreOffice's export grows a filled rectangle by 0.05 pt on every side — half its
    /// default hairline pen — and computes the right and bottom strokes from that grown rectangle while
    /// computing the left and top from the true one. So three of the eight numbers here are 0.05 out by
    /// construction, and none of them is out by more.
    /// </remarks>
    private const double TolerancePoints = 0.17;

    private readonly LibreOfficeRunner _libreOffice = new();
    private readonly string _workDirectory =
        Directory.CreateTempSubdirectory("paperless-frame-box").FullName;

    public void Dispose()
    {
        _libreOffice.Dispose();
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    [Theory]
    [InlineData("frame-box.fodt")]
    // The same box in DOCX, where the fill is `a:solidFill/a:srgbClr` on the shape's `spPr` rather than a
    // property of a graphic style. Only the fill: the border cannot be compared here, because LibreOffice
    // strokes a shape's outline as one closed five-point path and `PdfStrokes` reads two-point lines.
    [InlineData("frame-box.docx")]
    public void AFramesFillCoversWhatLibreOfficeFills(string fileName)
    {
        Assert.SkipUnless(LibreOfficeRunner.IsAvailable, "LibreOffice is not installed");

        string pdf = _libreOffice.ConvertToPdf(Corpus.Require(fileName), _workDirectory);

        // The frame's fill is the one rectangle on the page: the document has no shaded table, no footnote
        // separator and no rule. So the largest fill is it, and there is nothing to disambiguate against.
        PdfFill rendered = PdfFills.Read(pdf)
            .Where(fill => fill.Width > 1 && fill.Height > 1)
            .OrderByDescending(fill => fill.Width * fill.Height)
            .FirstOrDefault();

        Assert.SkipWhen(rendered == default, $"{fileName}: LibreOffice filled nothing");

        DrawnFill drawn = Fills(fileName)
            .OrderByDescending(fill => fill.Bounds.Width.Emu * fill.Bounds.Height.Emu)
            .FirstOrDefault();

        drawn.ShouldNotBe(default(DrawnFill), $"{fileName}: nothing was filled");

        Close(drawn.Bounds.X, rendered.Left, $"{fileName}: the fill's left edge");
        Close(drawn.Bounds.Y, rendered.Top, $"{fileName}: its top edge");
        Close(drawn.Bounds.Width, rendered.Width, $"{fileName}: its width");
        Close(drawn.Bounds.Height, rendered.Height, $"{fileName}: its height");

        // The colour the document states, which no PDF reader here records — see the remarks.
        drawn.Paint.ShouldBeOfType<SolidPaint>().Colour
            .ShouldBe(Colour.FromRgb(0xCCFFCC), $"{fileName}: the stated fill colour");
    }

    [Theory]
    [InlineData("frame-box.fodt")]
    public void AFramesBorderIsInsetByHalfItsWidthOnEverySide(string fileName)
    {
        Assert.SkipUnless(LibreOfficeRunner.IsAvailable, "LibreOffice is not installed");

        string pdf = _libreOffice.ConvertToPdf(Corpus.Require(fileName), _workDirectory);

        // Two points wide, which is the document's border and nothing else on the page: the only other
        // strokes LibreOffice writes here are its default hairlines.
        List<PdfStroke> rendered =
            [.. PdfStrokes.Read(pdf).Where(stroke => Math.Abs(stroke.Width - 2) < 0.1)];

        Assert.SkipWhen(rendered.Count == 0, $"{fileName}: LibreOffice stroked no 2 pt line");

        rendered.Count.ShouldBe(4, $"{fileName}: a frame's border is four independent strokes");

        List<DrawnStroke> drawn =
        [
            .. Strokes(fileName).Where(stroke => Math.Abs(stroke.Stroke.Width.Points - 2) < 0.1),
        ];

        drawn.Count.ShouldBe(4, $"{fileName}: drew {drawn.Count} strokes of the border's width");

        // Compared as the two horizontals and the two verticals, in order, because a stroke's identity is its
        // side: a reader that put the top border where the bottom belongs would match on multiset equality.
        Compare(
            [.. drawn.Where(Horizontal).OrderBy(stroke => stroke.Bounds.Y.Emu)],
            [.. rendered.Where(stroke => stroke.IsHorizontal).OrderBy(stroke => stroke.FromY)],
            fileName,
            "horizontal");

        Compare(
            [.. drawn.Where(stroke => !Horizontal(stroke)).OrderBy(stroke => stroke.Bounds.X.Emu)],
            [.. rendered.Where(stroke => stroke.IsVertical).OrderBy(stroke => stroke.FromX)],
            fileName,
            "vertical");

        foreach (DrawnStroke stroke in drawn)
        {
            stroke.Stroke.Paint.ShouldBeOfType<SolidPaint>().Colour
                .ShouldBe(Colour.FromRgb(0xC9211E), $"{fileName}: the stated border colour");
        }

        static bool Horizontal(DrawnStroke stroke) => stroke.Bounds.Width > stroke.Bounds.Height;
    }

    [Theory]
    [InlineData("frame-aligned.fodt")]
    // The same three frames in DOCX, where the named positions are `wp:align` elements and the references are
    // `relativeFrom` attributes — `column`, `margin` and `page` against ODF's `paragraph`, `page-content` and
    // `page`. LibreOffice renders the two identically, which is the point of having both.
    [InlineData("frame-aligned.docx")]
    public void AnAlignedFrameLandsWhereLibreOfficePutsIt(string fileName)
    {
        Assert.SkipUnless(LibreOfficeRunner.IsAvailable, "LibreOffice is not installed");

        string pdf = _libreOffice.ConvertToPdf(Corpus.Require(fileName), _workDirectory);

        // Each frame is coloured, so its fill *is* its position — which is what makes this measurable at all:
        // a frame's placement is otherwise only visible in where the text it pushed aside went, and these
        // frames deliberately push nothing sideways.
        List<PdfFill> rendered =
        [
            .. PdfFills.Read(pdf)
                .Where(fill => fill.Width > 1 && fill.Height > 1)
                .OrderBy(fill => fill.Top),
        ];

        Assert.SkipWhen(rendered.Count == 0, $"{fileName}: LibreOffice filled nothing");

        rendered.Count.ShouldBe(3, $"{fileName}: the document has three coloured frames");

        List<DrawnFill> drawn = [.. Fills(fileName).OrderBy(fill => fill.Bounds.Y.Emu)];

        drawn.Count.ShouldBe(3, $"{fileName}: drew {drawn.Count} fills");

        for (int i = 0; i < rendered.Count; i++)
        {
            Close(drawn[i].Bounds.X, rendered[i].Left, $"{fileName}: frame {i + 1}'s left edge");
            Close(drawn[i].Bounds.Y, rendered[i].Top, $"{fileName}: frame {i + 1}'s top edge");
            Close(drawn[i].Bounds.Width, rendered[i].Width, $"{fileName}: frame {i + 1}'s width");
        }
    }

    // ------------------------------------------------------------------------- the machinery

    private static void Compare(
        List<DrawnStroke> drawn, List<PdfStroke> rendered, string fileName, string axis)
    {
        drawn.Count.ShouldBe(rendered.Count, $"{fileName}: {axis} stroke count");

        for (int i = 0; i < rendered.Count; i++)
        {
            DocRect bounds = drawn[i].Bounds;
            PdfStroke reference = rendered[i];

            bool horizontal = reference.IsHorizontal;
            string where = $"{fileName}: {axis} stroke {i + 1}";

            Close(
                horizontal ? bounds.Y : bounds.X,
                horizontal ? reference.FromY : reference.FromX,
                $"{where}: its position along the other axis");

            Close(
                horizontal ? bounds.X : bounds.Y,
                Math.Min(
                    horizontal ? reference.FromX : reference.FromY,
                    horizontal ? reference.ToX : reference.ToY),
                $"{where}: where it starts");

            Close(
                horizontal ? bounds.Width : bounds.Height,
                reference.Length,
                $"{where}: how far it runs");
        }
    }

    private static void Close(Length drawn, double renderedPoints, string where)
        => Math.Abs(drawn.Points - renderedPoints).ShouldBeLessThanOrEqualTo(
            TolerancePoints,
            $"{where}: {drawn.Points:F3} pt drawn, {renderedPoints:F3} pt rendered");

    private static List<DrawnFill> Fills(string fileName)
        => [.. Recorded(fileName).SelectMany(page => page.FilledPaths)];

    private static List<DrawnStroke> Strokes(string fileName)
        => [.. Recorded(fileName).SelectMany(page => page.StrokedPaths)];

    private static List<DrawnPage> Recorded(string fileName)
    {
        RecordingDrawingSink sink = new();
        string path = Corpus.Require(fileName);

        using (FileStream stream = File.OpenRead(path))
        {
            using DocumentSource source = DocumentSource.FromStream(stream, Path.GetFileName(path));
            using IDocument document = new WordProcessingReader().Read(source);

            IPageSequence pages = ((IPaginatedDocument)document).Layout();
            for (int i = 0; i < pages.Count; i++) pages[i].Draw(sink);
        }

        return [.. sink.Pages];
    }
}
