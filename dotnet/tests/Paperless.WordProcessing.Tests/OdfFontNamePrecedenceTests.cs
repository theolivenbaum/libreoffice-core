using Paperless.Core.Documents;
using Paperless.TestKit;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// The Writer half of ODF's two spellings of one font item.
/// </summary>
/// <remarks>
/// <para>
/// <c>OdfParagraphFormats.ResolveText</c> already decided the level before the spelling across the
/// <em>cascade</em> — the paragraph style, then each enclosing <c>text:span</c>'s — and asked for
/// each spelling through the whole parent chain while doing it. So the same error it was written
/// to avoid survived one level in: a parent style's <c>fo:font-family</c> beat its child's
/// <c>style:font-name</c>. The reasoning and the census are on
/// <see cref="Paperless.OpenDocument.Styles.OdfStyles.ResolveWithoutDefaults(string,
/// Paperless.OpenDocument.Styles.OdfStyleFamily, Paperless.OpenDocument.Styles.OdfPropertyKind,
/// IReadOnlyList{ValueTuple{string, string}}, out int)"/>.
/// </para>
/// <para>
/// The fixture's paragraph asks for Arial and inherits Calibri; 26.2.4.2's own PDF of it embeds
/// <c>LiberationSans</c>.
/// </para>
/// </remarks>
public sealed class OdfFontNamePrecedenceTests
{
    /// <summary>An inner <c>style:font-name</c> beats an inherited <c>fo:font-family</c>.</summary>
    [Fact]
    public void AnInnerFontNameBeatsAnOuterFontFamily()
    {
        RecordingDrawingSink sink = new();

        using (DocumentSource source =
               DocumentSource.FromFile(Corpus.Require("odf-font-name-precedence.fodt")))
        {
            using IDocument document = new WordProcessingReader().Read(source);

            IPageSequence pages = ((IPaginatedDocument)document).Layout();
            pages.Count.ShouldBe(1);
            pages[0].Draw(sink);
        }

        DrawnWord word = DrawnWords.On(sink.Pages[0]).Single(w => w.Text == "Hamburgefonstiv");

        word.Family.ShouldBe("Liberation Sans");
    }
}
