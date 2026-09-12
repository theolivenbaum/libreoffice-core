using Paperless.Core.Documents;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.Spreadsheets.Layout;
using Paperless.TestKit;
using Shouldly;

namespace Paperless.Spreadsheets.Tests;

/// <summary>
/// What an emptied pivot cell falls back to, on the one workbook that can tell the two candidates
/// apart.
/// </summary>
/// <remarks>
/// <para>
/// <c>clearContents(… HARDATTR | STYLES …)</c>
/// (<c>sc/source/filter/oox/pivottablebuffer.cxx</c>:1331-1336) and
/// <c>DeleteAreaTab(…, InsertDeleteFlags::ALL)</c> (<c>dpoutput.cxx</c>:1226) take a pivot cell
/// back to Calc's <strong>Default</strong> cell style. That style is the <c>cellStyleXfs</c>
/// entry the <c>Normal</c> <c>cellStyle</c> names — <em>not</em> <c>cellXfs[0]</c>, which is only
/// what a cell stating <c>s="0"</c> resolves through. Every workbook in the corpus gives the two
/// the same content on a font, so no real document can separate them.
/// </para>
/// <para>
/// <c>regression/pivot-default-style.xlsx</c> separates them, and every number below was read out
/// of the <c>.fods</c> that <strong>LibreOffice 26.2.4.2</strong> writes for this same file
/// (<c>probes/pivot-fmt-r110/make-default-fixture.py</c>, <c>fods-face.py</c>). It states three
/// fonts that differ in face, size and colour at once:
/// </para>
/// <list type="bullet">
/// <item><description>the <c>Normal</c> <c>cellStyleXf</c> — Liberation Sans 11, black;</description></item>
/// <item><description><c>cellXfs[0]</c> — Liberation Serif 18, red;</description></item>
/// <item><description><c>cellXfs[1]</c>, on every cell of the pivot's own rectangle — Liberation
/// Mono 8, bold, green, on a yellow fill.</description></item>
/// </list>
/// <para>
/// 26.2.4.2 resolves all twelve cells of the emptied rectangle to <strong>Liberation Sans 11
/// black</strong> — the <c>Normal</c> style — and the control sheet's <c>s="0"</c> block to
/// Liberation Serif 18 red, so the reading is not a reading of the instrument.
/// </para>
/// <para>
/// This tree takes only the <em>colour</em> from that fallback and
/// <see cref="OnlyTheColourIsClearedAndTheReferenceClearsTheWholeFont"/> says why, with the
/// cell-for-cell score for each of the five properties.
/// </para>
/// </remarks>
public sealed class SheetPivotDefaultStyleTests
{
    private const string Fixture = "pivot-default-style.xlsx";
    private const int BoldWeight = 700;
    private const int Cleared = 1;
    private const int Plain = 2;

    private static SpreadsheetPages Pages()
    {
        using IPaginatedDocument document =
            (IPaginatedDocument)PaperlessDocument.Open(Corpus.Require(Fixture));

        return (SpreadsheetPages)document.Layout();
    }

    private static SheetCellFormat Format(SpreadsheetPages pages, int sheet, int row, int column)
        => pages.Sheets[sheet].Formats.At(row, column);

    /// <summary>
    /// The cleared cells take the <c>Normal</c> style's <em>colour</em> — and this is the test
    /// that says the face, the declared class and the size are deliberately left alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 26.2.4.2 resolves all twelve cells of this fixture to Liberation Sans 11 pt black, so the
    /// reference clears the whole font and not just its colour. This tree clears only the colour,
    /// because clearing is only half of what the reference does: <c>ScDPOutput::Output</c> ends
    /// with <c>maFormatOutput.apply</c> (<c>dpoutput.cxx</c>:1190), which lays the pivot's own
    /// <c>&lt;format&gt;</c>/<c>dxf</c> records back over the generated styles, and this tree
    /// reads none of them. Those records are not rare — 286 across 12 of the corpus's 28 pivot
    /// parts (<c>probes/pivot-fmt-r110/format-census.py</c>).
    /// </para>
    /// <para>
    /// Scored against 26.2.4.2's own resolved view of the 9878 cells of the corpus's 19
    /// generatable pivot ranges (<c>probes/pivot-fmt-r110/clearfour.py</c>): clearing the colour
    /// agrees on 9873 against merging's 9804, but clearing the font identity agrees on 9750
    /// against 9835 and clearing the size on 9783 against 9872 — because
    /// <c>033_Event_planning_tracker</c>'s <c>dxf</c> records restore a 12 pt Consolas-modern
    /// that its own cells also state. So the assertions below are the shipped behaviour, not the
    /// reference's, and they are here so that whoever lands the <c>dxf</c> half changes them on
    /// purpose.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnlyTheColourIsClearedAndTheReferenceClearsTheWholeFont()
    {
        SpreadsheetPages pages = Pages();
        SheetCellFormat format = Format(pages, Cleared, 1, 1);

        format.FontFamily
            .ShouldBe("Liberation Mono", "the cell's own face survives here; 26.2.4.2 gives Liberation Sans");
        format.FontSize
            .ShouldBe(Length.FromPoints(8), "and its own size; 26.2.4.2 gives 11 pt");
    }

    /// <summary>And its colour, by the same reading.</summary>
    [Fact]
    public void AnEmptiedPivotCellTakesTheNormalCellStylesColour()
    {
        SpreadsheetPages pages = Pages();
        List<string> wrong = [];

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                SheetCellFormat format = Format(pages, Cleared, row, column);
                if (format.Colour != Colour.Black)
                    wrong.Add($"{(char)('A' + column)}{row + 1}: {format.Colour}");
            }
        }

        wrong.ShouldBeEmpty("26.2.4.2 gives all twelve #000000, not the cells' own #00a000");
    }

    /// <summary>
    /// The generated styles still land on top of the cleared base.
    /// </summary>
    /// <remarks>
    /// Row 4 is the grand total, which takes <c>Pivot Table Result</c>/<c>Title</c> and is bold in
    /// the reference's <c>.fods</c>; rows 1 to 3 are not.
    /// </remarks>
    [Fact]
    public void TheGrandTotalRowIsStillBoldAndTheRowsAboveAreNot()
    {
        SpreadsheetPages pages = Pages();

        for (int column = 0; column < 3; column++)
        {
            Format(pages, Cleared, 3, column).FontWeight
                .ShouldBe(BoldWeight, $"the grand total at row 4 column {column + 1}");
        }

        for (int row = 1; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Format(pages, Cleared, row, column).FontWeight
                    .ShouldBeLessThan(BoldWeight, $"row {row + 1} column {column + 1}");
            }
        }
    }

    /// <summary>
    /// The control: a cell stating <c>s="0"</c> keeps <c>cellXfs[0]</c>, which is a different
    /// format from the one the pivot cleared to.
    /// </summary>
    /// <remarks>
    /// Without this the test above would pass just as well on a tree that had confused the two
    /// tables the other way round, or on a fixture whose two entries were secretly the same.
    /// </remarks>
    [Fact]
    public void ACellStatingTheDefaultCellFormatIsNotTheNormalStyle()
    {
        SpreadsheetPages pages = Pages();

        //  Plain A7:C10 states s="0" outright.
        SheetCellFormat stated = Format(pages, Plain, 6, 0);
        stated.FontFamily.ShouldBe("Liberation Serif", "cellXfs[0]'s face");
        stated.FontSize.ShouldBe(Length.FromPoints(18));
        stated.Colour.ShouldBe(Colour.FromRgb(0xFF0000));

        //  Plain A1:C4 states s="1", the format the pivot's own cells carry, and no pivot is on
        //  this sheet to clear it.
        SheetCellFormat hard = Format(pages, Plain, 0, 0);
        hard.FontFamily.ShouldBe("Liberation Mono");
        hard.FontSize.ShouldBe(Length.FromPoints(8));
        hard.FontWeight.ShouldBe(BoldWeight);
    }
}
