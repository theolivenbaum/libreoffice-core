using System.IO.Compression;
using System.Text;
using Paperless.Core.Documents;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// That a table row holding a nested table still splits across a page, and loses nothing when it does.
/// </summary>
/// <remarks>
/// <para>
/// A row that cannot be split and does not fit is placed whole on a page of its own and allowed to
/// overflow, and everything past the page bottom is never drawn — so refusing to split is not a
/// conservative choice, it is silent content loss. Writer's <c>bTableLayoutTooComplex</c> refuses on any
/// nested table; the rule here is narrower and refuses only a cut that would go <em>through</em> one.
/// </para>
/// <para>
/// Measured on a two-file probe differing in exactly one nested table: 90 body paragraphs in a one-cell
/// table rendered 2 pages and 90 lines both ways without it, and 1 page and 64 lines with it, the last
/// paragraph gone. LibreOffice 26.2.4.2 drew all 90 in both files.
/// </para>
/// <para>
/// What this does <em>not</em> claim is parity with Writer's follow-flow lines, which build a second
/// frame for the nested table too and divide it in turn. A document whose whole body is wrapped in a
/// chain of single-cell tables — a shape web exporters produce constantly, and the one
/// <c>May 25 bulletin focus on carers in the workplace.docx</c> has — offers no legal cut at all, since
/// the one nested table spans the entire cell. That remains open.
/// </para>
/// </remarks>
public sealed class NestedTableRowSplitTests
{
    /// <summary>Every paragraph is placed, on more than one page, and the nested table exactly once.</summary>
    [Fact]
    public void ARowHoldingANestedTableSplitsAndKeepsEveryLine()
    {
        IReadOnlyList<LaidOutPage> pages = Paginate(nested: true);

        pages.Count.ShouldBeGreaterThan(1);
        Lines(pages).ShouldBe(Paragraphs + 1);
        NestedTables(pages).ShouldBe(1);
    }

    /// <summary>The control: the same document without the nested table, which always worked.</summary>
    [Fact]
    public void TheSameRowWithoutANestedTableIsUnaffected()
    {
        IReadOnlyList<LaidOutPage> pages = Paginate(nested: false);

        pages.Count.ShouldBeGreaterThan(1);
        Lines(pages).ShouldBe(Paragraphs);
        NestedTables(pages).ShouldBe(0);
    }

    private const int Paragraphs = 30;

    private static int Lines(IReadOnlyList<LaidOutPage> pages)
        => pages.Sum(page => page.Lines.Count + page.Tables.Sum(CountLines));

    private static int CountLines(PlacedTable table)
        => table.Cells.Sum(cell => cell.Content is { } flow
            ? flow.Lines.Count + flow.Tables.Sum(CountLines)
            : 0);

    private static int NestedTables(IReadOnlyList<LaidOutPage> pages)
        => pages.Sum(page => page.Tables.Sum(CountNested));

    private static int CountNested(PlacedTable table)
        => table.Cells.Sum(cell => cell.Content is { } flow
            ? flow.Tables.Count + flow.Tables.Sum(CountNested)
            : 0);

    private static IReadOnlyList<LaidOutPage> Paginate(bool nested)
    {
        using DocumentSource source = DocumentSource.FromStream(BuildPackage(nested), "rowsplit.docx");
        using IDocument document = new WordProcessingReader().Read(source);

        return ((WordProcessingPages)((IPaginatedDocument)document).Layout()).Pages;
    }

    private static MemoryStream BuildPackage(bool nested)
    {
        const string ContentTypes = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels"
                       ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml"
                        ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/settings.xml"
                        ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
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
              <Relationship Id="rId1" Target="settings.xml"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings"/>
            </Relationships>
            """;

        const string Settings = """
            <?xml version="1.0" encoding="UTF-8"?>
            <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"/>
            """;

        // At the top of the cell rather than in the middle of it, so that every cut the row could want
        // is below it and the test measures the refusal rather than the geometry.
        const string Nested = """
            <w:tbl><w:tblPr><w:tblW w:w="8000" w:type="dxa"/></w:tblPr>
              <w:tblGrid><w:gridCol w:w="8000"/></w:tblGrid>
              <w:tr><w:tc><w:tcPr><w:tcW w:w="8000" w:type="dxa"/></w:tcPr>
                <w:p><w:r><w:t>Inside the nested table.</w:t></w:r></w:p>
              </w:tc></w:tr>
            </w:tbl>
            """;

        StringBuilder body = new();
        for (int i = 0; i < Paragraphs; i++)
        {
            body.Append("<w:p><w:r><w:t>Body ").Append(i).Append("</w:t></w:r></w:p>");
        }

        string document = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:tbl>
                  <w:tblPr><w:tblW w:w="9000" w:type="dxa"/></w:tblPr>
                  <w:tblGrid><w:gridCol w:w="9000"/></w:tblGrid>
                  <w:tr><w:tc><w:tcPr><w:tcW w:w="9000" w:type="dxa"/></w:tcPr>
                    {(nested ? Nested : "")}
                    {body}
                  </w:tc></w:tr>
                </w:tbl>
                <w:sectPr>
                  <w:pgSz w:w="12240" w:h="4000"/>
                  <w:pgMar w:top="720" w:right="1440" w:bottom="720" w:left="1440"
                           w:header="360" w:footer="360"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """;

        MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", ContentTypes);
            Add(archive, "_rels/.rels", RootRelationships);
            Add(archive, "word/_rels/document.xml.rels", DocumentRelationships);
            Add(archive, "word/settings.xml", Settings);
            Add(archive, "word/document.xml", document);
        }

        stream.Position = 0;
        return stream;

        static void Add(ZipArchive archive, string name, string content)
        {
            using StreamWriter writer = new(archive.CreateEntry(name).Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }
}
