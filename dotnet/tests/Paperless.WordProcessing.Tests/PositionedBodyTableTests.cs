using System.IO.Compression;
using System.Text;
using Paperless.Core.Documents;
using Paperless.Core.Geometry;
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

    /// <summary>A table half the column's width, so that a fly of it leaves room beside itself.</summary>
    private const int NarrowGrid = 5000;

    /// <summary>And one as wide as the column, so that it does not: 11906 − 1440 − 1440.</summary>
    private const int WideGrid = 9026;

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

    /// <summary>
    /// A fly that fills the column pushes the flow under itself rather than staying in the flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other side of <see cref="AFlyOverTheFlowDoesNotSwallowATextBearingLine"/>. Writer's body
    /// flies take the parallel surround, so the flow is pushed clear of one — <em>beside</em> it where
    /// there is room and <em>below</em> it where there is not. Nothing here can wrap into a strip beside
    /// a fly, which is why the narrow case refuses to float at all; the full-width case needs no
    /// wrapping, only a position.
    /// </para>
    /// <para>
    /// Measured against 24.2.7.2 on two authored probes differing only in the table's width. A 200 pt
    /// table in a 451.3 pt column puts the following <c>AFTER</c> at <b>x = 266.25 pt</b>, level with
    /// the table's first row and hard against its right edge; a table as wide as the column puts it at
    /// <b>y = 133.03 pt</b>, under the table's last row and at the column's own left edge. Both are in
    /// <c>dotnet/probes/words-floating-table/</c>.
    /// </para>
    /// <para>
    /// Here the table is anchored 36 pt above the top margin and is 400 pt tall, so the line after it
    /// would have landed inside it. Floated, the line goes to the table's bottom edge — 36 + 400 = 436
    /// pt down the page, which is 364 pt below the margin the narrow case leaves it at.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFlyFillingTheColumnPushesTheFlowUnderItself()
    {
        WordProcessingPages pages = Lay(
            vertAnchor: "page", tblpY: 720, after: "<w:r><w:t>After</w:t></w:r>", grid: WideGrid);

        PlacedTable table = pages.Pages[0].Tables.ShouldHaveSingleItem();
        table.Area.Y.ShouldBe(Length.FromTwips(720));

        PlacedLine line = pages.Pages[0].Lines.ShouldHaveSingleItem();
        line.Top.ShouldBe(Length.FromTwips(720) + Length.FromTwips(8000) - TopMargin);
    }

    /// <summary>
    /// Every paragraph after the fly is pushed under it, an empty one included — but the displacement
    /// is recorded on the paragraph it lands on, because a frame anchored there does not move with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Writer positions a paragraph's anchored objects when the paragraph is <em>first</em> formatted,
    /// which is before a fly above it has pushed the paragraph clear of itself, and it does not position
    /// them again afterwards. So the flow moves and the object stays. Only the paragraph the
    /// displacement actually lands on is affected: the one after it is formatted below an already-moved
    /// predecessor, so its own objects are placed where it really is.
    /// </para>
    /// <para>
    /// Measured on 21 authored documents in <c>probes/words-fly-clearance/</c> and confirmed on
    /// <c>HC-Bulletin-template.docx</c>, whose masthead logo and photograph hang off the paragraph
    /// directly after a full-width fly: moving them with the flow put both at the bottom of page one
    /// where the reference has them at the top. First-page ink, <b>38.41 before and 10.96 after</b>.
    /// </para>
    /// <para>
    /// The two halves are asserted together because either alone is satisfied by a wrong rule: leaving
    /// the flow where it was would also leave the frame, and moving both would also move neither.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFlyMovesTheFlowUnderItselfAndLeavesTheAnchoredFrameBehind()
    {
        WordProcessingPages pages = Lay(
            vertAnchor: "page", tblpY: 720, after: "<w:r><w:t>After</w:t></w:r>",
            grid: WideGrid, empties: 1);

        Length under = Length.FromTwips(720) + Length.FromTwips(8000) - TopMargin;
        List<PlacedLine> lines = [.. pages.Pages[0].Lines];

        lines.Count.ShouldBe(2);

        // The empty paragraph takes the displacement, and the inked one follows it a line lower.
        lines[0].Top.ShouldBe(under);
        lines[1].Top.ShouldBe(under + lines[0].Box.Height);

        // But a frame anchored to the displaced paragraph measures from where the flow was.
        lines[0].ParagraphTop.ShouldBe(Length.Zero);
        lines[1].ParagraphTop.ShouldBe(lines[1].Top);
    }

    /// <summary>
    /// <c>w:tblpX</c> moves a positioned table across, by exactly what it says.
    /// </summary>
    /// <remarks>
    /// It was not read at all: a positioned table took <c>w:tblInd</c>, which these files do not state,
    /// and sat at the margin. <c>087_Printable_Graph_Paper_Template_Green_Theme</c> states
    /// <c>w:tblpX="-594"</c> and the reference draws its grid from x = 35.3 pt where we drew it from
    /// 70.6 — a dense grid a whole page across, so every line of it landed between two of the
    /// reference's. The two corrections that go with it are in
    /// <c>DocxLayoutSource.PositionedLeftEdge</c>; asserted here as a difference so that this test says
    /// only that the offset itself arrives, whole.
    /// </remarks>
    [Fact]
    public void AStatedHorizontalOffsetMovesAPositionedTable()
    {
        DocRect at(int? tblpX) => Lay(
            vertAnchor: "page", tblpY: 2880, after: "", tblpX: tblpX)
            .Pages[0].Tables.ShouldHaveSingleItem().Area;

        (at(0).X - at(-594).X).ShouldBe(Length.FromTwips(594));
        (at(720).X - at(0).X).ShouldBe(Length.FromTwips(720));
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

    private static WordProcessingPages Lay(
        string? vertAnchor,
        int tblpY,
        string after,
        int? tblpX = null,
        int grid = NarrowGrid,
        int empties = 0)
    {
        string anchor = vertAnchor is null ? "" : $""" w:vertAnchor="{vertAnchor}" """.Trim() + " ";
        string across = tblpX is null ? "" : $""" w:tblpX="{tblpX}" """.Trim() + " ";
        return LayRaw(
            $"""<w:tblpPr {anchor}{across}w:horzAnchor="margin" w:tblpY="{tblpY}"/>""",
            after, grid, empties);
    }

    private static WordProcessingPages LayRaw(
        string tablePosition, string after, int grid = NarrowGrid, int empties = 0)
    {
        using IDocument document = Open(tablePosition, after, grid, empties);
        return (WordProcessingPages)((IPaginatedDocument)document).Layout();
    }

    private static IDocument Open(string tablePosition, string after, int grid, int empties)
    {
        MemoryStream package = BuildPackage(tablePosition, after, grid, empties);
        using DocumentSource source = DocumentSource.FromStream(package, "positioned-table.docx");
        return new WordProcessingReader().Read(source);
    }

    private static MemoryStream BuildPackage(
        string tablePosition, string after, int grid, int empties)
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
                  <w:tblPr>{tablePosition}<w:tblW w:w="{grid}" w:type="dxa"/></w:tblPr>
                  <w:tblGrid><w:gridCol w:w="{grid}"/></w:tblGrid>
                  <w:tr>
                    <w:trPr><w:trHeight w:val="8000" w:hRule="exact"/></w:trPr>
                    <w:tc><w:tcPr><w:tcW w:w="{grid}" w:type="dxa"/></w:tcPr>
                      <w:p><w:pPr><w:rPr><w:sz w:val="4"/></w:rPr></w:pPr></w:p>
                    </w:tc>
                  </w:tr>
                </w:tbl>
                {string.Concat(Enumerable.Repeat("<w:p/>", empties))}
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
