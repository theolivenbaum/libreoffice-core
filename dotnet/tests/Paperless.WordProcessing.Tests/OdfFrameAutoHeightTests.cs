using Paperless.Core.Documents;
using Paperless.TestKit;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A <c>draw:frame</c> may state <c>svg:width</c> and no <c>svg:height</c>, and it still holds
/// its text.
/// </summary>
/// <remarks>
/// <para>
/// Both lengths are optional (ODF 1.3 §10.4.2) and Writer leaves the height off a frame that
/// grows to its content — a text box or a framed table after "autofit height". What it writes
/// instead is the floor, on the box: <c>&lt;draw:text-box fo:min-height="0in"&gt;</c>. Requiring
/// both lengths to place a frame therefore dropped the frame <em>and everything inside it</em>,
/// and said nothing: a page with no frame on it looks like a page whose author put none there.
/// </para>
/// <para>
/// Reach on the converted corpus: 49 of the 338 <c>.odt</c> hold 58 such frames carrying
/// <strong>50 942 alphanumeric characters</strong> between them, and in ten of those the frame is
/// the page — <c>020_Project_Timeline_Template_Modern_Theme</c> drew 0 characters against the
/// reference's 316, <c>part-147_approval list_20230119</c> 114 against 3570.
/// </para>
/// <para>
/// <strong>What this does not yet do is grow the frame.</strong> The frame takes its floor as its
/// height and its text is drawn past the bottom, so the text is present and placed but the frame
/// reserves no room for it: body text is not pushed down and the page count stays short where the
/// frame is tall. <c>Case-Study-Heathrow-Airport</c> is the shape of what remains — 8 characters
/// became 2052 of the reference's 6461, on 1 page against 3. Growing it needs the content laid out
/// before the frame is placed, which is the opposite of the order <c>FrameLayout</c> runs in.
/// </para>
/// </remarks>
public sealed class OdfFrameAutoHeightTests
{
    /// <summary>The frame's text is drawn, and where the file puts the frame.</summary>
    /// <remarks>
    /// 26.2.4.2's own PDF of the fixture puts PLAIN at yMin 56.7289 — the 2 cm top margin — and
    /// BOXED at 85.5589, which is that plus the frame's <c>svg:y</c> of 1 cm. A reading that lost
    /// the frame keeps PLAIN and looks like an ordinary one-word document, which is why the
    /// assertion names both words rather than only the one that matters.
    /// </remarks>
    [Fact]
    public void AFrameWithNoStatedHeightStillDrawsItsText()
    {
        RecordingDrawingSink sink = new();

        using (DocumentSource source =
               DocumentSource.FromFile(Corpus.Require("odt-frame-auto-height.fodt")))
        {
            using IDocument document = new WordProcessingReader().Read(source);

            IPageSequence pages = ((IPaginatedDocument)document).Layout();
            pages.Count.ShouldBe(1);
            pages[0].Draw(sink);
        }

        Dictionary<string, DrawnWord> words =
            DrawnWords.On(sink.Pages[0])
                      .Where(word => word.Text is "PLAIN" or "BOXED")
                      .ToDictionary(word => word.Text, word => word);

        words.Keys.Order(StringComparer.Ordinal).ShouldBe(["BOXED", "PLAIN"]);

        // 1 cm = 28.35 pt of svg:y separates the two baselines, both being the same 11 pt line.
        (words["BOXED"].Baseline - words["PLAIN"].Baseline).ShouldBe(720.0 / 25.4, 0.5);
    }
}
