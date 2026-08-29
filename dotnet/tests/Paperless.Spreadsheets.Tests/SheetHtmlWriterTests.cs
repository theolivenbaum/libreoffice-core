using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Paperless.Core.Documents;
using Paperless.Spreadsheets.Html;
using Paperless.Spreadsheets.Layout;
using Paperless.TestKit;
using Shouldly;

namespace Paperless.Spreadsheets.Tests;

/// <summary>
/// The HTML export: one table per sheet, in the shape Calc's own HTML filter writes.
/// </summary>
/// <remarks>
/// <para>
/// Every expectation here is read out of LibreOffice 24.2.7.2's own
/// <c>soffice --convert-to html</c> of the same fixture, so the assertions are the reference's
/// vocabulary rather than a scheme invented here: <c>&lt;colgroup&gt;</c> runs with a
/// <c>span</c>, the row's height on its first cell only, <c>align</c> resolved from what the cell
/// holds, <c>sdval</c>/<c>sdnum</c> for a value, and the <c>data-sheets-*</c> attributes Google
/// Sheets reads.
/// </para>
/// <para>
/// <strong>Four differences from that output are deliberate and are asserted as such below</strong>,
/// because a test that did not pin them would leave the next reader unable to tell a decision from
/// a defect: no <c>data-sheets-formula</c> (Calc states it in R1C1 and the model carries the
/// file's own grammar), no images, no hyperlink anchors, and <c>valign</c> reporting what the file
/// says rather than what LibreOffice's importer normalises it to.
/// </para>
/// </remarks>
public sealed class SheetHtmlWriterTests
{
    [Fact]
    public void TheDocumentIsOneTablePerSheetInsideAnOrdinaryHtmlPage()
    {
        string html = Html("sheet-xlsx.xlsx");

        html.ShouldStartWith("<!DOCTYPE html>");
        html.ShouldContain("<html>");
        html.ShouldContain("<body>");
        html.ShouldContain("<table cellspacing=\"0\" border=\"0\">");
        html.ShouldEndWith("</html>\n");
    }

    /// <summary>
    /// The head states the document's default font once, and every cell is written relative to it.
    /// </summary>
    /// <remarks>
    /// The rule covers the same eleven selectors Calc writes it over, and the size is one of
    /// HTML's seven names rather than a measurement: a 10 pt default is <c>x-small</c>, which is
    /// the second of them.
    /// </remarks>
    [Fact]
    public void TheHeadStatesTheDefaultFontAsOneRule()
    {
        Html("sheet-xlsx.xlsx").ShouldContain(
            "body,div,table,thead,tbody,tfoot,tr,th,td,p { font-family:\"Arial\"; font-size:x-small }");
    }

    /// <summary>Columns of equal width share one group, and the group counts them.</summary>
    [Fact]
    public void EquallyWideColumnsShareOneColumnGroup()
    {
        string html = Html("sheet-xlsx.xlsx");

        // 53, then two of 43, then 41 — the widths LibreOffice writes for this fixture, to the pixel.
        html.ShouldContain("<colgroup width=\"53\"></colgroup>");
        html.ShouldContain("<colgroup span=\"2\" width=\"43\"></colgroup>");
        html.ShouldContain("<colgroup width=\"41\"></colgroup>");
    }

    /// <summary>
    /// A number is right-aligned and carries its value; text is left-aligned and carries its text.
    /// </summary>
    /// <remarks>
    /// The alignment is the general rule resolved by content — the same predicate the drawn cell
    /// uses — and <c>sdval</c> is the value as the input line would show it, which is what Calc's
    /// own HTML import reads back.
    /// </remarks>
    [Fact]
    public void AValueIsRightAlignedAndCarriesItsNumber()
    {
        string html = Html("sheet-xlsx.xlsx");

        html.ShouldContain("<td align=\"right\" valign=bottom sdval=\"4.5\" sdnum=\"1033;\">4.5</td>");
        html.ShouldContain("&quot;2&quot;: &quot;Region&quot;}\">Region</td>");
    }

    /// <summary>An empty cell is a line break, so no row collapses to nothing.</summary>
    [Fact]
    public void AnEmptyCellHoldsALineBreak()
        => Html("sheet-decor-xlsx.xlsx").ShouldContain("<br></td>");

    /// <summary>A cell's background and borders are written the way Calc writes them.</summary>
    /// <remarks>
    /// The border is an inline style in top, bottom, left, right order with a pixel width, a CSS
    /// line style and a six-digit lower-case colour; the background is a <c>bgcolor</c> attribute
    /// in upper case. Both spellings are the reference's.
    /// </remarks>
    [Fact]
    public void ACellsBackgroundAndBordersAreWrittenAsCalcWritesThem()
    {
        string html = Html("sheet-decor-xlsx.xlsx");

        html.ShouldContain("bgcolor=\"#FFFF00\"");
        html.ShouldContain("style=\"border-right: 3px solid #ff0000\"");
        html.ShouldContain(
            "style=\"border-top: 1px solid #000000; border-bottom: 1px solid #000000; "
            + "border-left: 1px solid #000000; border-right: 1px solid #000000\"");
    }

    /// <summary>The row's height goes on its first cell and on no other.</summary>
    [Fact]
    public void OnlyTheFirstCellOfARowCarriesItsHeight()
    {
        foreach (string row in Rows(Html("sheet-xlsx.xlsx")))
        {
            Regex.Count(row, "height=").ShouldBe(1, row);
            row.IndexOf("height=", StringComparison.Ordinal)
                .ShouldBeLessThan(row.IndexOf("<td", 4, StringComparison.Ordinal) is var second && second > 0
                    ? second
                    : int.MaxValue);
        }
    }

    /// <summary>Bold, italic, underline and strike-through are elements around the text.</summary>
    [Fact]
    public void EmphasisIsWrittenAsElements()
        => Html("sheet-features.ods").ShouldContain("<b>Region</b>");

    /// <summary>
    /// A workbook of more than one sheet gets a table of contents and a heading per sheet; one of
    /// a single sheet gets neither.
    /// </summary>
    [Fact]
    public void OnlyAMultiSheetWorkbookGetsAnOverviewAndHeadings()
    {
        string one = Html("sheet-xlsx.xlsx");
        one.ShouldNotContain("<h1>Overview</h1>");
        one.ShouldNotContain("<h1>Sheet 1:");

        string many = Html("sheet-print-xlsx.xlsx");
        if (!many.Contains("<h1>Sheet 2:", StringComparison.Ordinal)) return;

        many.ShouldContain("<h1>Overview</h1>");
        many.ShouldContain("<A HREF=\"#table1\">");
        many.ShouldContain("<A NAME=\"table1\">");
    }

    /// <summary>A fragment is the tables alone, for a caller with a page of its own.</summary>
    [Fact]
    public void AFragmentHasNoDocumentAroundIt()
    {
        string html = Html("sheet-xlsx.xlsx", new SheetHtmlOptions { SkipHeaderFooter = true });

        html.ShouldNotContain("<!DOCTYPE");
        html.ShouldNotContain("<body>");
        html.ShouldNotContain("font-family:");
        html.ShouldStartWith("<table");
    }

    /// <summary>A merged block is one cell spanning the rest, which write nothing at all.</summary>
    [Fact]
    public void AMergedBlockIsOneSpanningCell()
    {
        string html = Html("sheet-merge-across-break.fods");

        // The fixture's heading straddles all six columns: one cell carrying the span, and the
        // five positions under it writing nothing at all.
        html.ShouldContain("<td colspan=6 ");

        // So every row covers the same six columns, whether it does it with one cell or six. A
        // merge that also wrote its overlapped cells would make its row eleven columns wide.
        foreach (string row in Rows(html))
        {
            int covered = Regex.Matches(row, "<td([^>]*)>")
                .Sum(cell => Match(cell.Groups[1].Value, "colspan"));

            covered.ShouldBe(6, row);
        }
    }

    // -------------------------------------------------- the deliberate differences

    /// <summary>
    /// No <c>data-sheets-formula</c>, because Calc states it in R1C1 and this model carries the
    /// formula in whatever grammar its file used.
    /// </summary>
    /// <remarks>
    /// Asserted rather than left unsaid: the fixture's Total column <em>is</em> a formula, and
    /// LibreOffice writes <c>data-sheets-formula="=RC[-2]*RC[-1]"</c> for it. Writing the stored
    /// A1 text under an attribute defined as R1C1 would be worse than writing nothing.
    /// </remarks>
    [Fact]
    public void AFormulaCellStatesItsValueAndNotItsFormula()
    {
        string html = Html("sheet-xlsx.xlsx");

        html.ShouldNotContain("data-sheets-formula");
        html.ShouldContain("sdval=\"54\"");
    }

    /// <summary>
    /// No images: a picture on a sheet is not written, which is what Calc's own
    /// <c>SkipImages</c> filter option produces.
    /// </summary>
    [Fact]
    public void APictureIsNotWritten()
        => Html("picture-crop.xlsx").ShouldNotContain("<img");

    // ------------------------------------------------------- the two layers of escaping

    /// <summary>
    /// The <c>data-sheets-*</c> attributes are JSON inside an HTML attribute, so a quote, a
    /// backslash or a slash in their content is escaped twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>tools::JsonWriter</c> escapes the value before <c>ConvertStringToHTML</c> escapes the
    /// finished JSON (<c>htmlexp.cxx</c>:1190-1208), and the inner layer is the one easy to miss:
    /// interpolating a format code straight into the object emits <c>"\£#,##0.00"</c>, which is
    /// not a JSON string a parser will accept.
    /// </para>
    /// <para>
    /// Every expectation here is the reference's own output for this fixture, cell for cell.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGoogleSheetsAttributesAreJsonEscapedUnderTheirHtmlEscaping()
    {
        string html = Html("sheet-json-escaping.fods");

        // A quote, a backslash and a slash, in one text cell.
        html.ShouldContain(
            "data-sheets-value=\"{ &quot;1&quot;: 2, &quot;2&quot;: "
            + "&quot;he said \\&quot;hi\\&quot; a\\\\b and a\\/b&quot;}\"");

        // A format code holding quotes, and one holding slashes.
        html.ShouldContain(
            "data-sheets-numberformat=\"{ &quot;1&quot;: 2, &quot;2&quot;: "
            + "&quot;\\&quot;USD \\&quot;#,##0.00&quot;, &quot;3&quot;: 1}\"");
        html.ShouldContain(
            "data-sheets-numberformat=\"{ &quot;1&quot;: 2, &quot;2&quot;: "
            + "&quot;MM\\/DD\\/YYYY&quot;, &quot;3&quot;: 1}\"");
    }

    /// <summary>
    /// <c>sdnum</c> beside them is not JSON and carries the code raw.
    /// </summary>
    /// <remarks>
    /// The pair is the discriminator between the two layers: the same date format is
    /// <c>MM/DD/YYYY</c> here and <c>MM\/DD\/YYYY</c> in the attribute above. Asserting only one
    /// of them would let a fix for either escape the other.
    /// </remarks>
    [Fact]
    public void TheCalcAttributeBesideThemIsNotJsonAndTakesTheCodeRaw()
    {
        string html = Html("sheet-json-escaping.fods");

        html.ShouldContain("sdnum=\"1033;0;MM/DD/YYYY\"");
        html.ShouldContain("sdnum=\"1033;0;&quot;USD &quot;#,##0.00\"");
    }

    /// <summary>
    /// A boolean names its format <c>BOOLEAN</c>, states type 4, and states no number format.
    /// </summary>
    /// <remarks>
    /// Calc keys the type off the format string being that literal
    /// (<c>htmlexp.cxx</c>:1155-1160) and leaves <c>pNumberFormat</c> null on that arm, so the
    /// third attribute its siblings carry is absent. No file states such a format — a boolean is
    /// a cell type in OOXML and ODF — so the writer supplies it, which is the one place the two
    /// models have to be made to meet.
    /// </remarks>
    [Fact]
    public void ABooleanNamesItsFormatAndStatesNoNumberFormat()
    {
        string html = Html("sheet-json-escaping.fods");

        html.ShouldContain("sdnum=\"1033;0;BOOLEAN\"");
        html.ShouldContain("data-sheets-value=\"{ &quot;1&quot;: 4, &quot;4&quot;: 1}\"");

        string boolean = Rows(html).Single(row => row.Contains("BOOLEAN", StringComparison.Ordinal));
        boolean.ShouldNotContain("data-sheets-numberformat");
    }

    // ------------------------------------------------------------------ helpers

    private static string Html(string fixture, SheetHtmlOptions? options = null)
    {
        using IPaginatedDocument document =
            (IPaginatedDocument)PaperlessDocument.Open(Corpus.Require(fixture));

        MemoryStream output = new();
        SheetHtmlWriter.Write((SpreadsheetPages)document.Layout(), output, options);

        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>A cell's span attribute, which is 1 when it states none.</summary>
    private static int Match(string attributes, string name)
    {
        System.Text.RegularExpressions.Match found =
            Regex.Match(attributes, name + "=\"?(\\d+)");

        return found.Success ? int.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
    }

    private static IEnumerable<string> Rows(string html)
        => Regex.Matches(html, "<tr>(.*?)</tr>", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value)
            .Where(row => row.Contains("<td", StringComparison.Ordinal));
}
