using Paperless.Core.Documents;
using Paperless.Core.Units;
using Paperless.Spreadsheets.Layout;
using Paperless.TestKit;
using Shouldly;

namespace Paperless.Spreadsheets.Tests;

/// <summary>
/// A centred wrapping cell keeps the word its line begins with.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a lost-text defect wearing an alignment defect's clothes.</strong> A wrapped
/// line keeps the spaces it broke after, because Calc's own output draws them
/// (<c>SheetTextLayout.Wrap</c> takes a line to its <c>End</c> and not to its <c>VisibleEnd</c>).
/// Centring the line against that width subtracts half the blank from the <em>left</em>, so a
/// line whose trailing blanks outweigh its text starts outside the cell — and the cell's own clip
/// then removes whatever begins there. Nothing warns; the characters are simply not on the page.
/// </para>
/// <para>
/// Measured on <c>Infotabelle_WLAN im Flugzeug.xlsx</c> page 2, a German airline price table
/// whose runs of spaces stand in for tab stops. Its second line carried 46 trailing spaces worth
/// 151 pt of a 436 pt line against 283.1 pt of room, so the line was placed at x = −25.2 pt in a
/// cell clipped from 50.4 pt, and the bold word <c>kostenlos</c> that began it was drawn entirely
/// outside. The PDF's text layer held <c>kostenlos</c> five times against the reference's six,
/// and it holds six now. The word was in the file, in <c>paperless extract</c>, in the wrapped
/// line's own range and in the shaped run the whole time — only the placement lost it.
/// </para>
/// <para>
/// Against LibreOffice 26.2.4.2's own PDF of that page: the reference draws the two affected
/// lines at 52.044 pt and 52.611 pt and we now draw both at 51.392 pt, the remaining 0.7 pt being
/// the cell margin and the same on every cell in the file. Before, the second was not drawn.
/// </para>
/// <para>
/// The fixture is flat ODF and deliberately smaller than the corpus document: one 4 cm centred
/// wrapping cell holding <c>WESTERLY</c>, 44 spaces and <c>EASTERLY</c>, which is enough to make
/// the first line overflow by more than the width of the word on it.
/// </para>
/// </remarks>
public sealed class SheetCentredWrapBlankTests
{
    /// <summary>Column A is 4 cm and the page margin 2 cm, so the centred cell begins here.</summary>
    private const double CellLeftPoints = 170.079;

    [Fact]
    public void ACentredLineWhoseTrailingBlanksOverflowStartsAtTheCellRatherThanOutsideIt()
    {
        List<DrawnGlyphRun> runs = Draw();

        DrawnGlyphRun first = runs.First(run => run.Text.StartsWith("WESTERLY", StringComparison.Ordinal));

        // The run is the word plus all 44 of its trailing spaces: 177.37 pt against the 110 pt
        // the cell had. Centring against the whole of that put it at 138.088 pt — 31.99 pt left
        // of the cell itself — so 32 of the word's 52.27 pt fell outside the clip.
        first.Width.Points.ShouldBe(177.37, 0.05, "the line keeps its trailing blanks");
        first.Origin.X.Points.ShouldBeGreaterThanOrEqualTo(
            CellLeftPoints, "a centred line never begins left of the cell it is in");

        // The reference places it at 171.468 pt, which is its left margin: EditEngine keeps only
        // the blanks that fit, so its line fills the width and (nMaxLineWidth - nCenterWidth) / 2
        // is nought. Ours is the same placement reached the other way round.
        first.Origin.X.Points.ShouldBe(171.07, 0.05, "at the cell's left margin, as the reference");
    }

    [Fact]
    public void ALineThatFitsIsStillCentredOnItsWholeWidth()
    {
        List<DrawnGlyphRun> runs = Draw();

        // The control, and the reason the fix is bounded at both ends rather than being "do not
        // count trailing blanks". The second line fits, so nothing about it moves — 200.635 pt
        // against the reference's 200.636 pt, which it also matched before this change.
        DrawnGlyphRun second = runs.First(run => run.Text.StartsWith("EASTERLY", StringComparison.Ordinal));

        second.Origin.X.Points.ShouldBe(200.636, 0.05, "a line that fits is centred as it always was");
    }

    [Fact]
    public void ARunsTrailingBlanksComeOffItsWidthAndNothingElseDoes()
    {
        // The measurement the placement is built on, taken directly rather than through a page.
        // Only U+0020 counts, which is what EditEngine tests for (tdf#168135,
        // editeng/source/editeng/impedit3.cxx:1650); a no-break space is not a blank here.
        SheetFace face = SheetText.DefaultFace!.Value;
        Length size = Length.FromPoints(10);

        SheetTextRun plain = SheetText.Shape("WESTERLY", face, size)!;
        SheetTextRun trailed = SheetText.Shape("WESTERLY    ", face, size)!;
        SheetTextRun protectedBlanks = SheetText.Shape("WESTERLY\u00A0\u00A0", face, size)!;

        trailed.Width.ShouldBeGreaterThan(plain.Width, "the spaces are in the run's width");
        trailed.WithoutTrailingBlanks.Points.ShouldBe(plain.Width.Points, 0.01, "and come off it again");
        plain.WithoutTrailingBlanks.Points.ShouldBe(plain.Width.Points, 0.01, "a run ending in a letter is unchanged");
        protectedBlanks.WithoutTrailingBlanks.Points.ShouldBe(
            protectedBlanks.Width.Points, 0.01, "a no-break space is not a blank");
    }

    private static List<DrawnGlyphRun> Draw()
    {
        using IPaginatedDocument document = (IPaginatedDocument)PaperlessDocument.Open(
            Corpus.Require("sheet-centred-wrap-blanks.fods"));

        RecordingDrawingSink sink = new();
        ((SpreadsheetPages)document.Layout()).Pages[0].Draw(sink);

        return [.. sink.Pages[0].Runs];
    }
}
