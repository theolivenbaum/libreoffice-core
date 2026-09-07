using Paperless.Core.Documents;
using Paperless.TestKit;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A header's <c>fo:margin-bottom</c> is a gap it adds to itself or one it eats, and
/// <c>style:dynamic-spacing</c> says which.
/// </summary>
/// <remarks>
/// <para>
/// LibreOffice maps the attribute to <c>SwHeaderAndFooterEatSpacingItem</c>
/// (<c>sw/source/core/bastyp/init.cxx</c>:434). With it set, <c>SwHeadFootFrame::FormatPrt</c>
/// starts the header's print area at <c>fo:min-height</c> less both spacings and then gives back
/// out of the gap exactly what the content overruns that by
/// (<c>sw/source/core/layout/hffrm.cxx</c>:116-170), so the frame's total comes to
/// </para>
/// <code>
/// total = max(min-height, content + (dynamic-spacing ? 0 : gap))
/// </code>
/// <para>
/// The declared minimum therefore <em>includes</em> the gap, and a header whose content fits
/// occupies the minimum and nothing more. Adding the gap on top instead pushes the body down by
/// it on every page of the document — which is silent, because every page is wrong the same way
/// and the text that falls off the bottom simply starts another page.
/// </para>
/// <para>
/// Reach on the converted corpus: 152 of the 338 <c>.odt</c> declare a dynamic-height header or
/// footer with the flag set and a gap worth more than half a point, and the gaps run to 89 pt.
/// </para>
/// </remarks>
public sealed class OdfHeaderDynamicSpacingTests
{
    /// <summary>1 cm, in points — the fixture's <c>fo:min-height</c> and its gap alike.</summary>
    private const double Centimetre = 720.0 / 25.4;

    /// <summary>
    /// The header with the flag occupies its declared minimum rather than the minimum plus its gap.
    /// </summary>
    /// <remarks>
    /// Measured as the drop from the header's own baseline to the first body baseline rather than
    /// from the top of the page, so the assertion says how much room the header took and does not
    /// also depend on where a Carlito ascender starts. 26.2.4.2's own PDF of the fixture, read with
    /// <c>pdftotext -bbox</c>, puts both header lines at yMin 56.7289 — the 2 cm top margin — and
    /// page 1's first body line at 85.0789, a drop of 28.35 pt, which is 1 cm to a hundredth.
    /// </remarks>
    [Fact]
    public void TheFlaggedHeaderOccupiesItsDeclaredMinimum()
    {
        (double eat, _) = HeaderDrops();

        // max(1 cm, one 11 pt line) with the gap absorbed = 1 cm.
        eat.ShouldBe(Centimetre, 0.5);
    }

    /// <summary>
    /// And a header that turns the flag off is not read the same way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This pins that the attribute is read at all — that the eaten gap above is the flag's doing
    /// and not a blanket change — rather than pinning the unflagged number, which is still an
    /// approximation. The exact answer there is <c>max(min-height, content + gap)</c> and the
    /// content cannot be measured before the page the header sits on is known, so the older
    /// <c>minimum + gap</c> stands: this fixture's second page drops 56.70 pt where 26.2.4.2 drops
    /// 41.80, one header line too many.
    /// </para>
    /// <para>
    /// Left as it was deliberately. Writer writes <c>style:dynamic-spacing</c> on everything it
    /// exports and writes it <c>true</c>; across the 338 converted <c>.odt</c> exactly one header
    /// style says <c>false</c> and one omits it, so correcting the unflagged branch would move
    /// nothing measured and would change a reading no measurement here covers.
    /// </para>
    /// </remarks>
    [Fact]
    public void AHeaderThatTurnsTheFlagOffIsNotReadTheSameWay()
    {
        (double eat, double add) = HeaderDrops();

        // 1 cm of gap separates the two readings at the very least, and does here.
        (add - eat).ShouldBeGreaterThan(Centimetre - 0.5);
    }

    /// <summary>
    /// The drop from the header's baseline to the first body baseline, on each of the two pages.
    /// </summary>
    private static (double Eat, double Add) HeaderDrops()
    {
        RecordingDrawingSink sink = new();

        using (DocumentSource source =
               DocumentSource.FromFile(Corpus.Require("header-dynamic-spacing.fodt")))
        {
            using IDocument document = new WordProcessingReader().Read(source);

            IPageSequence pages = ((IPaginatedDocument)document).Layout();
            pages.Count.ShouldBe(2);
            pages[0].Draw(sink);
            pages[1].Draw(sink);
        }

        return (Drop(sink.Pages[0], "HEADEREAT", "EATFIRST"),
                Drop(sink.Pages[1], "HEADERADD", "ADDFIRST"));
    }

    private static double Drop(DrawnPage page, string head, string body)
    {
        List<DrawnWord> words = DrawnWords.On(page);
        DrawnWord header = words.Single(word => word.Text == head);
        DrawnWord first = words.Single(word => word.Text == body);
        return first.Baseline - header.Baseline;
    }
}
