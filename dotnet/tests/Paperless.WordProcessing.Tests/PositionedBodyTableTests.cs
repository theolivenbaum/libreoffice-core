using System.IO.Compression;
using System.Text;
using Paperless.Core.Documents;
using Paperless.Core.Units;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A body table carrying <c>w:tblpPr</c> names a place on the page rather than a place in the text: it
/// is drawn at <c>w:tblpY</c> from <c>w:vertAnchor</c>, and it does not push the flow down.
/// </summary>
/// <remarks>
/// <para>
/// Writer's DOCX importer turns such a table into a fly holding a table
/// (<c>TablePositionHandler::getTablePosition</c>, <c>TablePositionHandler.cxx:123-146</c>), and a fly is
/// not in the flow. <see cref="FlowLayouter"/> had done this for a running head since round 44, with a
/// remark saying of the body *"no measurement was taken there"*. Round 50 took it.
/// </para>
/// <para>
/// The position law was read out of 26.2.4.2's own PDFs on the corpus's eight positioned
/// <c>Printable_Graph_Paper_Template</c> documents, by predicting the first horizontal table rule's y
/// from the page geometry and <c>w:tblpY</c> alone: <b>within 1.15 pt on seven of the eight</b>, against
/// our own placement, which sat at the top margin on all eight and was out by up to 26 pt. See
/// <see cref="PageTable.VerticalOffset"/> for the table of figures.
/// </para>
/// <para>
/// The consequence, and why it costs page counts rather than only looking wrong: on
/// <c>080_Printable_Graph_Paper_Template_Black_Theme</c> both sides draw the identical 86 strokes on page
/// 1, and then the reference draws the document's remaining texts on page 1 while we drew them on page 2
/// <em>at the same offsets</em>. The table had consumed the flow.
/// </para>
/// <para>
/// <see cref="AFlyOverTheFlowDoesNotSwallowATextBearingLine"/> is the limit, and it is measured rather
/// than assumed — see the remarks on <c>Paginator.RunsIntoTheFly</c> for the 084/087 pair that fixes it.
/// </para>
/// </remarks>
public sealed class PositionedBodyTableTests
{
    /// <summary>A4 top margin, in points: <c>w:top="1440"</c>.</summary>
    private static readonly Length TopMargin = Length.FromPoints(72);

    /// <summary>
    /// A page-anchored table is drawn <c>w:tblpY</c> below the sheet's top edge, not at the margin.
    /// </summary>
    [Fact]
    public void APageAnchoredTableIsDrawnAtItsStatedOffsetFromTheSheet()
    {
        WordProcessingPages pages = Lay(vertAnchor: "page", tblpY: 2880, after: "");

        PlacedTable table = pages.Pages[0].Tables.ShouldHaveSingleItem();
        table.Area.Y.ShouldBe(Length.FromTwips(2880));
    }

    /// <summary>
    /// A table anchored to the text is drawn <c>w:tblpY</c> below where the flow has reached, which for
    /// the first block in the body is the top margin.
    /// </summary>
    [Fact]
    public void ATextAnchoredTableIsDrawnAtItsStatedOffsetFromTheFlow()
    {
        WordProcessingPages pages = Lay(vertAnchor: null, tblpY: 720, after: "");

        PlacedTable table = pages.Pages[0].Tables.ShouldHaveSingleItem();
        table.Area.Y.ShouldBe(TopMargin + Length.FromTwips(720));
    }

    /// <summary>
    /// The flow does not move out of a floated table's way: the paragraph after it still starts at the
    /// top margin rather than below the table.
    /// </summary>
    /// <remarks>
    /// This is the half that decides page counts. The table here is 400 pt tall and starts 144 pt down,
    /// so stacking it would put the paragraph at 544 pt; floating it leaves the paragraph at the margin.
    /// </remarks>
    [Fact]
    public void TheFlowAfterAFloatedTableStaysWhereItWas()
    {
        WordProcessingPages pages = Lay(
            vertAnchor: "page", tblpY: 2880, after: "<w:r><w:t>After</w:t></w:r>");

        PlacedLine line = pages.Pages[0].Lines.ShouldHaveSingleItem();
        line.Top.ShouldBe(Length.Zero);
    }

    /// <summary>
    /// A fly placed over the point the flow has already reached does not swallow a line with ink: the
    /// table stays in the flow, so the text is not drawn through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured on the two corpus templates that anchor their grid <em>above</em> the top margin, so the
    /// fly covers the flow's own starting position.
    /// <c>084_Printable_Graph_Paper_Template_Editable_Layout</c> is followed by an empty paragraph and
    /// 26.2.4.2 renders it on one page; <c>087_…Green_Theme</c> is followed by an empty paragraph and
    /// then a <c>Title: ___ Date: ___</c> line, and the reference renders it on two, putting that line at
    /// the top of page 2. Emptying 087's two text runs and re-rendering brings it back to one page.
    /// </para>
    /// <para>
    /// So: a floated table is drawn where the flow already is only when nothing with ink was going to be
    /// drawn there. Here the table is anchored 36 pt above the top margin and is 400 pt tall, so the
    /// following line lands inside it — and the table therefore stacks, putting the line below it.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFlyOverTheFlowDoesNotSwallowATextBearingLine()
    {
        WordProcessingPages pages = Lay(
            vertAnchor: "page", tblpY: 720, after: "<w:r><w:t>After</w:t></w:r>");

        PlacedTable table = pages.Pages[0].Tables.ShouldHaveSingleItem();
        table.Area.Y.ShouldBe(TopMargin);

        PlacedLine line = pages.Pages[0].Lines.ShouldHaveSingleItem();
        line.Top.ShouldBeGreaterThan(Length.FromPoints(300));
    }

    /// <summary>An ordinary table is unaffected: it stacks and the flow follows it down.</summary>
    [Fact]
    public void AnUnpositionedTableStillStacks()
    {
        WordProcessingPages pages = LayRaw(
            tablePosition: "",
            after: "<w:r><w:t>After</w:t></w:r>");

        PlacedTable table = pages.Pages[0].Tables.ShouldHaveSingleItem();
        table.Area.Y.ShouldBe(TopMargin);

        PlacedLine line = pages.Pages[0].Lines.ShouldHaveSingleItem();
        line.Top.ShouldBeGreaterThan(Length.FromPoints(300));
    }

    private static WordProcessingPages Lay(string? vertAnchor, int tblpY, string after)
    {
        string anchor = vertAnchor is null ? "" : $""" w:vertAnchor="{vertAnchor}" """.Trim() + " ";
        return LayRaw($"""<w:tblpPr {anchor}w:horzAnchor="margin" w:tblpY="{tblpY}"/>""", after);
    }

    private static WordProcessingPages LayRaw(string tablePosition, string after)
    {
        using IDocument document = Open(tablePosition, after);
        return (WordProcessingPages)((IPaginatedDocument)document).Layout();
    }

    private static IDocument Open(string tablePosition, string after)
    {
        MemoryStream package = BuildPackage(tablePosition, after);
        using DocumentSource source = DocumentSource.FromStream(package, "positioned-table.docx");
        return new WordProcessingReader().Read(source);
    }

    private static MemoryStream BuildPackage(string tablePosition, string after)
    {
        const string ContentTypes = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels"
                       ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml"
                        ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml"
                        ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            </Types>
            """;

        const string RootRelationships = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Target="word/document.xml"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"/>
            </Relationships>
            """;

        const string DocumentRelationships = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Target="styles.xml"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"/>
            </Relationships>
            """;

        const string Styles = """
            <?xml version="1.0" encoding="UTF-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:rPrDefault>
                  <w:rPr>
                    <w:rFonts w:ascii="Liberation Serif" w:hAnsi="Liberation Serif"/>
                    <w:sz w:val="20"/>
                  </w:rPr>
                </w:rPrDefault>
              </w:docDefaults>
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                <w:name w:val="Normal"/>
              </w:style>
            </w:styles>
            """;

        // One cell, one row, 8000 twips of exact height — 400 pt, tall enough that stacking it and
        // floating it put the paragraph after it in unmistakably different places.
        string document = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:tbl>
                  <w:tblPr>{tablePosition}<w:tblW w:w="5000" w:type="dxa"/></w:tblPr>
                  <w:tblGrid><w:gridCol w:w="5000"/></w:tblGrid>
                  <w:tr>
                    <w:trPr><w:trHeight w:val="8000" w:hRule="exact"/></w:trPr>
                    <w:tc><w:tcPr><w:tcW w:w="5000" w:type="dxa"/></w:tcPr>
                      <w:p><w:pPr><w:rPr><w:sz w:val="4"/></w:rPr></w:pPr></w:p>
                    </w:tc>
                  </w:tr>
                </w:tbl>
                <w:p>{after}</w:p>
                <w:sectPr>
                  <w:pgSz w:w="11906" w:h="16838"/>
                  <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"
                           w:header="708" w:footer="708" w:gutter="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """;

        MemoryStream result = new();
        using (ZipArchive archive = new(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", ContentTypes);
            Write(archive, "_rels/.rels", RootRelationships);
            Write(archive, "word/_rels/document.xml.rels", DocumentRelationships);
            Write(archive, "word/styles.xml", Styles);
            Write(archive, "word/document.xml", document);
        }

        result.Position = 0;
        return result;
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using Stream entry = archive.CreateEntry(path).Open();
        entry.Write(Encoding.UTF8.GetBytes(content));
    }
}
