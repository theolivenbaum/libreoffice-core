using Paperless.Core.Documents;
using Paperless.TestKit;
using Paperless.TestKit.LibreOffice;
using Paperless.WordProcessing;
using Shouldly;

namespace Paperless.Fidelity.Tests;

/// <summary>
/// Checks that a list's label and its text land where LibreOffice puts them.
/// </summary>
/// <remarks>
/// <para>
/// Three numbers per item, and they are three different rules: the <em>label</em> sits at the level's margin
/// plus its (negative) indent, the item's <em>first line</em> at the level's tab stop, and a
/// <em>continuation</em> line at the margin alone — so the label hangs to the left of the block. A reader
/// that got the label right and the stop wrong would produce a list whose numbers line up and whose text
/// does not.
/// </para>
/// <para>
/// Line starts rather than every word, which is the assertion that belongs here: a list is checked by
/// <em>where each line begins</em>, and comparing every word on a long line measures the accumulated
/// difference between LibreOffice's own width and HarfBuzz's — a recorded deviation of about 0.15%, which on
/// a 120 pt line is more than the tab tolerance and has nothing to do with lists.
/// </para>
/// </remarks>
public sealed class ListComparisonTests : IDisposable
{
    /// <inheritdoc cref="TabStopComparisonTests.TolerancePoints"/>
    private const double TolerancePoints = 0.15;

    /// <summary>What LibreOffice's PDF export adds to every horizontal pen position.</summary>
    private const double PdfPenOffsetPoints = 0.1;

    private readonly LibreOfficeRunner _libreOffice = new();
    private readonly string _workDirectory =
        Directory.CreateTempSubdirectory("paperless-lists").FullName;

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
    [InlineData("list-numbered.fodt")]
    // The same list in DOCX, where the same three numbers come from a different place: the structure is on the
    // paragraph rather than in nested elements, and the geometry is `w:ind` on the level rather than a
    // separate stop position — Word puts the text at `w:start` and the label at `w:start` less `w:hanging`.
    [InlineData("list-numbered.docx")]
    // And in DOC, where the geometry is a grpprl inside the level's own `LVL` record rather than an attribute
    // — the same `sprmPDxaLeft` and `sprmPDxaLeft1` a paragraph would state its indents with. This is also the
    // one of the four formats whose label has to be spliced into text that is already assembled, so a
    // mistake in the splice shows up here as a run in the wrong font rather than as a position.
    [InlineData("list-numbered.doc")]
    // A three-deep outline whose second level shows its parent's counter too, in all three formats. Two rules
    // that a single-level list cannot reach: a label made of several components, each formatted by *its own*
    // level — so `1.ii.` and not `1.2.` — and a shallower level advancing restarting every level under it.
    [InlineData("list-multilevel.fodt")]
    [InlineData("list-multilevel.docx")]
    [InlineData("list-multilevel.doc")]
    // RTF, hand-written rather than exported, because LibreOffice's own RTF export writes no `\listtable` at
    // all and so its own render of what it wrote shows no numbers. This file carries the table, which is what
    // LibreOffice numbers from — while Paperless reads the `{\listtext}` group the same file also carries, as
    // Word-written files do. So the two reach the same labels by different routes, which is the whole point of
    // comparing them.
    [InlineData("list-numbered.rtf")]
    public void EveryLineOfAListStartsWhereLibreOfficeStartsIt(string fileName)
    {
        Assert.SkipUnless(LibreOfficeRunner.IsAvailable, "LibreOffice is not installed");

        string path = Corpus.Require(fileName);
        List<PdfWord> words = PdfWords.Read(_libreOffice.ConvertToPdf(path, _workDirectory));

        Assert.SkipWhen(
            words.Count == 0,
            "pdftotext is not available; install poppler-utils — see check-env.sh");

        List<PdfWord> renderedWords = ReadingOrder.Of(words);
        List<DrawnWord> drawnWords = ReadingOrder.Of(Drawn(path));

        // Before the positions, because a label that is missing entirely does not necessarily move a line:
        // this document's hanging indent is exactly its tab distance, so an item drawn with no number starts
        // its first line where the number should have been. The text is what tells the two apart.
        string.Join(' ', drawnWords.Select(word => word.Text))
            .ShouldBe(
                string.Join(' ', renderedWords.Select(word => word.Text)),
                $"{fileName}: the drawn text differs from the rendered text");

        List<double> rendered = Starts(renderedWords, word => word.Top, word => word.Left);
        List<double> drawn = Starts(drawnWords, word => word.Baseline, word => word.Left);

        drawn.Count.ShouldBe(
            rendered.Count, $"{fileName}: laid out {drawn.Count} lines, LibreOffice {rendered.Count}");

        for (int i = 0; i < rendered.Count; i++)
        {
            Math.Abs(drawn[i] - (rendered[i] - PdfPenOffsetPoints)).ShouldBeLessThanOrEqualTo(
                TolerancePoints,
                $"{fileName}: line {i + 1} starts at {drawn[i]:F2} pt drawn, "
                + $"{rendered[i] - PdfPenOffsetPoints:F2} pt rendered");
        }
    }

    /// <summary>
    /// Each line's leftmost position, given the page's words in reading order.
    /// </summary>
    /// <remarks>
    /// A new line begins wherever the vertical jumps by more than a line's worth, which is all this needs —
    /// the label and the text after it are on one line and the leftmost of the two is the label.
    /// </remarks>
    private static List<double> Starts<T>(
        List<T> words, Func<T, double> vertical, Func<T, double> horizontal)
    {
        List<double> starts = [];
        double previous = double.NegativeInfinity;

        foreach (T word in words)
        {
            if (starts.Count > 0
                && Math.Abs(vertical(word) - previous) <= ReadingOrder.SameLinePoints)
            {
                starts[^1] = Math.Min(starts[^1], horizontal(word));
                continue;
            }

            previous = vertical(word);
            starts.Add(horizontal(word));
        }

        return starts;
    }

    private static List<DrawnWord> Drawn(string path)
    {
        RecordingDrawingSink sink = new();

        using (FileStream stream = File.OpenRead(path))
        {
            using DocumentSource source = DocumentSource.FromStream(stream, Path.GetFileName(path));
            using IDocument document = new WordProcessingReader().Read(source);

            IPageSequence pages = ((IPaginatedDocument)document).Layout();
            pages[0].Draw(sink);
        }

        return [.. DrawnWords.On(sink.Pages[0])];
    }
}
