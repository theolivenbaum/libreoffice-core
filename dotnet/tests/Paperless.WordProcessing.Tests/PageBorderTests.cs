using System.IO.Compression;
using System.Text;
using Paperless.Core.Documents;
using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.TestKit;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A section's <c>w:pgBorders</c> is drawn round the page, which until now nothing drew.
/// </summary>
/// <remarks>
/// <para>
/// It was not implemented at all — <c>pgBorders</c> appeared nowhere in the tree — and no gate
/// column could see it, because a border adds no words and no pages. Seven of the 272 corpus DOCX
/// declare one and every one of the seven passes the word gate;
/// <c>Case-Study-Heathrow-Airport.docx</c> is the witness, where this one rectangle is 11.55 of the
/// 14.70 <c>|ink|%</c> that put it at the head of the words track's per-page ink ranking against
/// 26.2.4.2.
/// </para>
/// <para>
/// <strong>The numbers below are measured off 26.2.4.2's own PDF of that document, not derived from
/// the specification.</strong> A4 595.304 × 841.89 with <c>w:sz="36"</c>, <c>w:space="15"</c>,
/// <c>w:offsetFrom="page"</c> and <c>w:shadow="1"</c>, its content stream holds four
/// <c>4.5 w … S</c> strokes at <c>0.2235 0.3961 0.2 RG</c> with centrelines at 17.25, 573.60, 21.69
/// and 17.25, and two black <c>re f*</c> rectangles for the shadow. The one thing that cannot be
/// guessed is that the shadow <em>shrinks the box</em> — the right and bottom edges come in by its
/// width instead of the shadow hanging off the paper — and that is what the assertions below are
/// for.
/// </para>
/// </remarks>
public sealed class PageBorderTests
{
    private const double Tolerance = 0.02;

    /// <summary>The four sides land where 26.2.4.2 puts them, to a fiftieth of a point.</summary>
    [Fact]
    public void TheFourSidesLandWhereLibreOfficeDrawsThem()
    {
        List<DrawnStroke> strokes = [.. Drawn(Shadow: true).StrokedPaths
            .Where(s => IsGreen(s.Stroke.Paint))];

        strokes.Count.ShouldBe(4);
        strokes.ShouldAllBe(s => s.Stroke.Width == Length.FromPoints(4.5));

        // Top and bottom: horizontal, spanning the box, at space + width/2 from the top and at the
        // same distance from the bottom *plus the shadow's own width*.
        Horizontal(strokes, 17.25);
        Horizontal(strokes, 841.89 - 15 - 4.5 - 2.25);

        // Left and right, the same rule mirrored.
        Vertical(strokes, 17.25);
        Vertical(strokes, 595.304 - 15 - 4.5 - 2.25);
    }

    /// <summary>
    /// The shadow is two black bars offset down and to the right by its own width.
    /// </summary>
    [Fact]
    public void TheShadowIsTwoBarsOffsetByItsOwnWidth()
    {
        List<DrawnFill> black = [.. Drawn(Shadow: true).FilledPaths
            .Where(f => IsBlack(f.Paint))];

        black.Count.ShouldBe(2);

        DrawnFill bottom = black.MaxBy(f => f.Bounds.Y.Points);
        DrawnFill right = black.MinBy(f => f.Bounds.Y.Points);

        bottom.Bounds.Height.Points.ShouldBe(4.5, Tolerance);
        bottom.Bounds.Y.Points.ShouldBe(841.89 - 15 - 4.5, Tolerance);
        bottom.Bounds.X.Points.ShouldBe(15 + 4.5, Tolerance);

        right.Bounds.Width.Points.ShouldBe(4.5, Tolerance);
        right.Bounds.X.Points.ShouldBe(595.304 - 15 - 4.5, Tolerance);
    }

    /// <summary>Without the shadow the box uses the whole inset, and nothing black is drawn.</summary>
    /// <remarks>
    /// The control for the assertion above: if the shrink were unconditional the right and bottom
    /// sides would sit 4.5 pt inside where Word puts them on every unshadowed border in the corpus,
    /// which is five of the seven.
    /// </remarks>
    [Fact]
    public void WithoutTheShadowTheBoxKeepsTheWholeInset()
    {
        DrawnPage page = Drawn(Shadow: false);

        page.FilledPaths.Count(f => IsBlack(f.Paint)).ShouldBe(0);

        List<DrawnStroke> strokes = [.. page.StrokedPaths
            .Where(s => IsGreen(s.Stroke.Paint))];

        strokes.Count.ShouldBe(4);
        Vertical(strokes, 595.304 - 15 - 2.25);
        Horizontal(strokes, 841.89 - 15 - 2.25);
    }

    /// <summary>A section declaring no border draws none, which is nearly every section.</summary>
    [Fact]
    public void ASectionWithNoBorderDrawsNone()
    {
        Drawn(Shadow: false, borders: false).StrokedPaths
            .Count(s => IsGreen(s.Stroke.Paint)).ShouldBe(0);
    }

    private static readonly Colour Green = new(0x39, 0x65, 0x33);

    private static bool IsGreen(Paint paint) => paint is SolidPaint solid && solid.Colour == Green;

    private static bool IsBlack(Paint paint) => paint is SolidPaint solid && solid.Colour == Colour.Black;

    private static void Horizontal(List<DrawnStroke> strokes, double y)
        => strokes.ShouldContain(
            s => s.Bounds.Height == Length.Zero
                 && Math.Abs(s.Bounds.Y.Points - y) < Tolerance,
            $"a horizontal side at y = {y}");

    private static void Vertical(List<DrawnStroke> strokes, double x)
        => strokes.ShouldContain(
            s => s.Bounds.Width == Length.Zero
                 && Math.Abs(s.Bounds.X.Points - x) < Tolerance,
            $"a vertical side at x = {x}");

    private static DrawnPage Drawn(bool Shadow, bool borders = true)
    {
        RecordingDrawingSink sink = new();

        using (DocumentSource source = DocumentSource.FromBytes(Package(Shadow, borders), "b.docx"))
        {
            using IDocument document = new WordProcessingReader().Read(source);
            IPageSequence pages = ((IPaginatedDocument)document).Layout();
            pages[0].Draw(sink);
        }

        return sink.Pages[0];
    }

    /// <summary>
    /// One A4 page with one word on it, and the section properties the reference was measured with.
    /// </summary>
    private static byte[] Package(bool shadow, bool borders)
    {
        string side(string name) =>
            $"""<w:{name} w:val="single" w:sz="36" w:space="15" w:color="396533"{(shadow ? " w:shadow=\"1\"" : "")}/>""";

        string pgBorders = borders
            ? $"""
               <w:pgBorders w:offsetFrom="page">
                 {side("top")}{side("left")}{side("bottom")}{side("right")}
               </w:pgBorders>
               """
            : "";

        string document =
            $"""
             <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
             <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
               <w:body>
                 <w:p><w:r><w:t>Bordered</w:t></w:r></w:p>
                 <w:sectPr>
                   <w:pgSz w:w="11906" w:h="16838"/>
                   <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"
                            w:header="708" w:footer="708" w:gutter="0"/>
                   {pgBorders}
                 </w:sectPr>
               </w:body>
             </w:document>
             """;

        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Write(archive, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Write(archive, "word/document.xml", document);
        }

        return stream.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using Stream entry = archive.CreateEntry(name).Open();
        entry.Write(Encoding.UTF8.GetBytes(content));
    }
}
