using System.Text;
using Paperless.Core.Documents;
using Paperless.Core.Units;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// <c>\htmautsp</c>, which decides whether two adjacent paragraphs' spacings are summed or
/// collapsed — and the window in which stating it still counts.
/// </summary>
/// <remarks>
/// <para>
/// RTF is the opt-in direction: <c>SettingsTable</c>'s constructor sets
/// <c>m_bDoNotUseHTMLParagraphAutoSpacing</c> true for an RTF import outright —
/// <em>"HTML paragraph auto-spacing is opt-in for RTF, opt-out for OOXML"</em>,
/// <c>sw/source/writerfilter/dmapper/SettingsTable.cxx</c>:118-126 — so an RTF that states nothing
/// <em>adds</em> the two spacings, and <c>\htmautsp</c> clears the flag
/// (<c>rtfdispatchflag.cxx</c>:1354-1357) so the larger wins instead.
/// </para>
/// <para>
/// <strong>But the settings table is sent once, at the document's first run.</strong>
/// <c>RTFDocumentImpl::checkFirstRun</c> calls <c>outputSettingsTable</c>
/// (<c>rtfdocumentimpl.cxx</c>:414-435), so a <c>\htmautsp</c> written after the body has begun is
/// stored in an <c>m_aSettingsTableSprms</c> nobody reads again — the same fact
/// <c>rtfsprm.cxx</c>:238-241 records in a comment of its own. <c>150-5370-10H.rtf</c> writes it
/// 265 KB into a 5 MB document, and honouring it there collapsed every paragraph boundary in a
/// 746-page specification.
/// </para>
/// </remarks>
public sealed class RtfHtmlAutoSpacingTests
{
    /// <summary>With no <c>\htmautsp</c> the two spacings add: 24 pt above 24 pt.</summary>
    [Fact]
    public void WithoutTheWordTheSpacingsAdd()
        => UpperSpace(string.Empty).Points.ShouldBe(13.8 + 24 + 24, 0.4);

    /// <summary>Stated before any body content, the larger wins.</summary>
    [Fact]
    public void StatedBeforeTheBodyTheLargerWins()
        => UpperSpace(@"\htmautsp").Points.ShouldBe(13.8 + 24, 0.4);

    /// <summary>Stated after the body has begun, it is too late to be read.</summary>
    [Fact]
    public void StatedAfterTheBodyHasBegunItIsIgnored()
        => UpperSpace(string.Empty, late: true).Points.ShouldBe(13.8 + 24 + 24, 0.4);

    /// <summary>
    /// A <c>{\header}</c> group does not begin the body, because a header is a substream resolved
    /// when its section ends rather than where its group was written.
    /// </summary>
    [Fact]
    public void AHeaderGroupDoesNotBeginTheBody()
        => UpperSpace(@"{\header\pard\plain\fs20 HEAD\par }\htmautsp")
            .Points.ShouldBe(13.8 + 24, 0.4);

    /// <summary>
    /// The distance from the first paragraph's line to the second's: one 13.8 pt line plus
    /// whatever the two spacings come to once the collapsing rule has been applied.
    /// </summary>
    private static Length UpperSpace(string prologue, bool late = false)
    {
        string rtf =
            @"{\rtf1\ansi\deff0{\fonttbl{\f0\froman Liberation Serif;}}"
            + @"\paperw11906\paperh16838\margl1440\margr1440\margt1440\margb1440\sectd"
            + prologue
            + (late ? @"\pard\plain ZERO\par \htmautsp" : string.Empty)
            + @"\pard\plain\sa480 AAAA\par "
            + @"\pard\plain\sb480 BBBB\par }";

        using DocumentSource source = DocumentSource.FromStream(
            new MemoryStream(Encoding.ASCII.GetBytes(rtf)), "spacing.rtf");
        using IDocument document = new WordProcessingReader().Read(source);
        WordProcessingPages pages = (WordProcessingPages)((IPaginatedDocument)document).Layout();

        PlacedLine Line(string text) => pages.Pages[0].Lines
            .First(line => pages.TextOf(line).Contains(text, StringComparison.Ordinal));

        return Line("BBBB").Top - Line("AAAA").Top;
    }
}
