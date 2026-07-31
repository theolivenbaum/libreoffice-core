using Paperless.Core.Documents;
using Paperless.Core.Units;
using Paperless.TestKit;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// Checks that a list item reaches the paginator as its label, a separator and its own text.
/// </summary>
/// <remarks>
/// <para>
/// The companion to the fidelity comparison, which measures where each line starts. That catches an indent
/// read in the wrong unit and a missing label alike, because both move a line by more than a tolerance — but
/// it cannot tell <c>1.</c> from <c>i.</c> or from a bullet, since all three begin at the same place. So the
/// label's own text is pinned here, along with the two indents that make it hang.
/// </para>
/// <para>
/// The three formats state the same list three ways and one of them has a further wrinkle: DOC has no run
/// walker to hand a prefix to, so the label is spliced into text that is already assembled and every run
/// offset after it has to move. That is what the run assertions are for — a splice that shifted the text and
/// not the offsets would leave every run naming the wrong characters while the drawn positions stayed right.
/// </para>
/// </remarks>
public sealed class ListLabelTests
{
    [Theory]
    [InlineData("list-numbered.fodt")]
    [InlineData("list-numbered.docx")]
    [InlineData("list-numbered.doc")]
    public void AnItemsLabelIsItsPrefix(string fileName)
    {
        List<PageParagraph> items = Items(fileName);

        // Counted rather than stored: all three formats hold a template and a start value, so a reader that
        // failed to count would repeat the first number rather than draw nothing.
        items.Select(item => item.Text.Split('\t')[0])
            .ShouldBe(["1.", "2.", "3."]);

        // A tab, not a space — the separator every one of the three defaults to, and the one that makes the
        // level's stop position matter at all.
        items[0].Text.ShouldBe("1.\tFirst item of the numbered list.");
    }

    [Theory]
    [InlineData("list-numbered.fodt")]
    [InlineData("list-numbered.docx")]
    [InlineData("list-numbered.doc")]
    public void TheLevelsIndentsReplaceTheParagraphsOwn(string fileName)
    {
        // The corpus document's level hangs its label 360 twips — a quarter inch, 0.635 cm — from a block
        // indented 720, which is LibreOffice's own default numbering geometry. The paragraph style states a
        // zero first-line indent, so a reader that let the paragraph win would draw the label at the margin.
        foreach (PageParagraph item in Items(fileName))
        {
            Math.Abs(item.Format.StartIndent.Twips - 720).ShouldBeLessThanOrEqualTo(
                2, $"{fileName}: the block indent is {item.Format.StartIndent.Twips} twips");
            Math.Abs(item.Format.FirstLineIndent.Twips + 360).ShouldBeLessThanOrEqualTo(
                2, $"{fileName}: the hanging indent is {item.Format.FirstLineIndent.Twips} twips");
        }
    }

    [Theory]
    [InlineData("list-numbered.fodt")]
    [InlineData("list-numbered.docx")]
    [InlineData("list-numbered.doc")]
    public void AStopSitsWhereTheItemsTextBegins(string fileName)
    {
        // Measured from the line's start, which for a hanging label is the hanging distance itself: the first
        // line begins that far left of the block the text returns to. A stop recorded at the block's own
        // indent instead would put the text half a centimetre too far right.
        foreach (PageParagraph item in Items(fileName))
        {
            item.Format.TabStops.ShouldContain(
                stop => Math.Abs(stop.Position.Twips - 360) <= 2,
                $"{fileName}: the level's tab stop reaches the paragraph");
        }
    }

    [Theory]
    [InlineData("list-numbered.fodt")]
    [InlineData("list-numbered.docx")]
    [InlineData("list-numbered.doc")]
    public void TheLabelDoesNotDisturbTheRunsAfterIt(string fileName)
    {
        foreach (PageParagraph item in Items(fileName))
        {
            if (!item.HasRuns) continue;

            // The runs still partition the whole text, label included. A prefix added to the text without
            // moving the offsets leaves the last run ending short by the label's length, which drops that
            // much text from every line after it.
            item.Runs.Sum(run => run.Length).ShouldBe(item.Text.Length, fileName);
            item.Runs[0].Start.ShouldBe(0, fileName);
        }
    }

    [Theory]
    [InlineData("list-multilevel.fodt")]
    [InlineData("list-multilevel.docx")]
    [InlineData("list-multilevel.doc")]
    public void ALabelCanShowItsAncestorsCountersToo(string fileName)
    {
        // Verified against LibreOffice's own render of all three, which puts exactly these seven labels at
        // 74.80, 92.80, 92.80, 146.80, 146.80, 74.80 and 92.80 pt. Three rules at once:
        //
        //  - `1.ii.` and not `1.2.`, because each component takes the format of *its own* level;
        //  - `a)` alone at the third level, whose definition asks for one component only, so the count of
        //    components is per level rather than per document;
        //  - `2.i.` after the second top-level item, because a shallower level advancing restarts every level
        //    under it. Without that the nested count would read `2.iii.`.
        Items(fileName, expected: 7)
            .Select(item => item.Text.Split('\t')[0])
            .ShouldBe(["1.", "1.i.", "1.ii.", "a)", "b)", "2.", "2.i."]);
    }

    /// <summary>
    /// The paragraphs that a list numbered, in order.
    /// </summary>
    /// <remarks>
    /// Found by the presence of a label rather than by position, because the corpus document deliberately
    /// puts an unnumbered paragraph either side of the list — the two that prove a list's indents do not
    /// leak onto the paragraphs around it.
    /// </remarks>
    /// <param name="fileName">The corpus document.</param>
    /// <param name="expected">How many numbered items it holds.</param>
    private static List<PageParagraph> Items(string fileName, int expected = 3)
    {
        string path = Corpus.Require(fileName);

        using FileStream stream = File.OpenRead(path);
        using DocumentSource source = DocumentSource.FromStream(stream, Path.GetFileName(path));
        using IDocument document = new WordProcessingReader().Read(source);

        WordProcessingPages pages = (WordProcessingPages)((IPaginatedDocument)document).Layout();

        List<PageParagraph> items =
        [
            .. pages.Paragraphs.Where(paragraph => paragraph.Text.Contains('\t', StringComparison.Ordinal)),
        ];

        items.Count.ShouldBe(
            expected, $"{fileName}: the corpus document has {expected} numbered items");

        return items;
    }
}
