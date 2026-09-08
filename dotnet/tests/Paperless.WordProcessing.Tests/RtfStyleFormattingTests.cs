using System.Text;
using Paperless.Core.Documents;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// Where a run's face comes from when the run does not name one: <c>\deff</c>, <c>\plain</c>, and
/// the paragraph style's own character formatting.
/// </summary>
/// <remarks>
/// <para>
/// The reading this replaces was that RTF's stylesheet is decorative — that a writer restates a
/// paragraph's effective formatting inline after the <c>\s</c> that names its style, so a reader
/// has no cascade to resolve. LibreOffice's own RTF export does not: it writes the
/// <em>difference</em> from the style, so a whole table cell can read
/// <c>\pard\plain \s24\li85\lin85\intbl{\cf8\fs20\b TC Holder}</c> and name no font at all. Its
/// importer resolves that cascade at <c>RTFDocumentImpl::getProperties</c>
/// (<c>sw/source/writerfilter/rtftok/rtfdocumentimpl.cxx</c>:534-637), whose comment at :616-618 is
/// <em>"Take paragraph style into account for character properties as well, as paragraph style may
/// contain character properties"</em>; <c>\pard</c> selects style zero when the paragraph names none
/// (<c>rtfdispatchflag.cxx</c>:600-614) and <c>\plain</c> restores the default character state,
/// which carries <c>\deff</c>'s face (<c>:575-583</c>, and <c>rtfdocumentimpl.cxx</c>:2494-2500 for
/// where that face is put there).
/// </para>
/// <para>
/// Reading it the other way put every such run in font zero, which is <c>Times New Roman</c> in
/// every file LibreOffice writes. <strong>215 of the 338 converted corpus <c>.rtf</c> declare a
/// <c>\deff</c> naming a different face from their <c>\f0</c></strong>, and on
/// <c>Annex-10…GCAA.rtf</c> the reference lays 148 pages of table out in Carlito where this tree laid
/// them out in Liberation Serif and paginated to 169. See
/// <c>dotnet/probes/rtf-page-r77/results.md</c>.
/// </para>
/// </remarks>
public sealed class RtfStyleFormattingTests
{
    /// <summary>
    /// A run that names no <c>\f</c> is set in <c>\deff</c>'s face, not in font zero's.
    /// </summary>
    [Fact]
    public void ADeffDecidesTheFaceOfARunThatNamesNoFont()
        => First(@"\pard\plain\fs20 MARKER\par").Family.ShouldBe("Liberation Sans");

    /// <summary>
    /// <c>\plain</c> puts the face back to <c>\deff</c>'s rather than leaving the last run's.
    /// </summary>
    /// <remarks>
    /// The witness is a table row whose empty cells are written <c>\pard\plain …\f0\fs18 \cell</c>:
    /// with nothing to restore the default, that <c>\f0</c> set the face of every paragraph after it.
    /// </remarks>
    [Fact]
    public void PlainPutsTheFaceBackToTheDefault()
        => Read(@"\pard\plain\f0\fs20 FIRST\par \pard\plain\fs20 SECOND\par")
            .Select(paragraph => paragraph.Family)
            .ShouldBe(["Liberation Serif", "Liberation Sans"]);

    /// <summary>And the size with it: RTF's own default is <c>\fs24</c>, which is twelve points.</summary>
    [Fact]
    public void PlainPutsTheSizeBackToTheDefault()
        => Read(@"\pard\plain\fs40 FIRST\par \pard\plain SECOND\par")
            .Select(paragraph => paragraph.Size)
            .ShouldBe([Length.FromPoints(20), Length.FromPoints(12)]);

    /// <summary>
    /// A paragraph style's font <em>face</em> reaches no run at all — <c>\deff</c>'s wins.
    /// </summary>
    /// <remarks>
    /// The reading this replaces was that a style's face reaches a run that names none. It does
    /// not, at any level of the chain: <c>\deff</c>'s name is put into the importer's default
    /// character state (<c>rtfdocumentimpl.cxx</c>:2494-2500), every group state is copied from
    /// that state, and a run therefore carries the face as <em>direct</em> formatting that no style
    /// can beat. Measured on four probes whose <c>\deff</c>, whose style's <c>\f</c> and whose
    /// <c>Times New Roman</c> fallback are three different faces — style's own or inherited, with
    /// <c>\plain</c> and without — 26.2.4.2 answers <c>\deff</c>'s in all four
    /// (<c>probes/rtf-resid-r80</c>, the <c>d-*</c> family).
    /// </remarks>
    [Fact]
    public void AParagraphStylesFaceReachesNoRun()
        => First(
                @"\pard\plain\s2\fs20 MARKER\par",
                styles: @"{\s2\snext2\f2 Boxed;}")
            .Family.ShouldBe("Liberation Sans");

    /// <summary>Nor does an inherited one, which is the same rule one link up.</summary>
    [Fact]
    public void AnInheritedFaceReachesNoRunEither()
        => First(
                @"\pard\plain\s3\fs20 MARKER\par",
                styles: @"{\s2\snext2\f2 Middle;}{\s3\sbasedon2\snext3\li85 Boxed;}")
            .Family.ShouldBe("Liberation Sans");

    /// <summary>
    /// A style's own <c>\fs</c> does not reach a paragraph that names that style, and an
    /// ancestor's does.
    /// </summary>
    /// <remarks>
    /// <c>getDefaultSPRM</c> answers <c>24</c> — twelve points — for a size
    /// (<c>sw/source/writerfilter/rtftok/rtfsprm.cxx</c>:158-161), and
    /// <c>cloneAndDeduplicateSprm</c>'s <em>"not found - try to override style with default"</em>
    /// branch (:319-327) writes it over the paragraph for every property the <em>named</em> style
    /// states. Only the ancestors' statements escape it.
    /// </remarks>
    [Fact]
    public void AStylesOwnSizeIsReplacedByTheDefaultAndAnInheritedOneIsNot()
    {
        First(@"\pard\plain\s2 MARKER\par", styles: @"{\s2\snext2\fs36 Big;}")
            .Size.ShouldBe(Length.FromPoints(12));

        First(@"\pard\plain\s3 MARKER\par",
                styles: @"{\s2\snext2\fs36 Big;}{\s3\sbasedon2\snext3\li85 Child;}")
            .Size.ShouldBe(Length.FromPoints(18));
    }

    /// <summary>
    /// <c>\pard</c> selects style zero, and style zero's own statements are therefore the named
    /// style's — so a bare paragraph takes none of them.
    /// </summary>
    [Fact]
    public void PardSelectsStyleZeroAndTakesNoneOfItsOwnStatements()
        => First(@"\pard\plain MARKER\par", styles: @"{\s0\snext0\fs36 Normal;}")
            .Size.ShouldBe(Length.FromPoints(12));

    /// <summary>
    /// A style replaces the one before it rather than piling onto it.
    /// </summary>
    /// <remarks>
    /// <c>\pard</c> selects style zero and the <c>\sN</c> after it selects another, so accumulating
    /// the two would leave style zero's bold on a paragraph in a style that inherits nothing from it.
    /// The bold is stated one link up in both, because a style's own statement never reaches the
    /// paragraph that names it.
    /// </remarks>
    [Fact]
    public void ASelectedStyleReplacesTheOneBefore()
    {
        Formatting formatting = First(
            @"\pard\plain\s3\fs20 MARKER\par",
            styles: @"{\s1\snext1\b Bold;}{\s0\sbasedon1\snext0 Normal;}{\s3\snext3 Boxed;}");

        formatting.Weight.ShouldBe(400);
    }

    /// <summary>
    /// A style that turns a toggle off beats a parent that turns it on, and one that says nothing
    /// inherits it.
    /// </summary>
    /// <remarks>
    /// This is what the stated-word set is for. <c>\b0</c> and an absent <c>\b</c> leave the same
    /// group state, so recording the state alone would make every style say "not bold" and no
    /// <c>\sbasedon</c> parent could ever pass bold down.
    /// </remarks>
    [Fact]
    public void AStyleStatingBoldOffBeatsABoldParentAndSilenceDoesNot()
    {
        First(
            @"\pard\plain\s3\fs20 MARKER\par",
            styles: @"{\s2\snext2\b\f2 Bold;}{\s3\sbasedon2\snext3\b0 Quiet;}")
            .Weight.ShouldBe(400);

        First(
            @"\pard\plain\s3\fs20 MARKER\par",
            styles: @"{\s2\snext2\b\f2 Bold;}{\s3\sbasedon2\snext3\li85 Quiet;}")
            .Weight.ShouldBe(700);
    }

    /// <summary>Direct formatting written after the <c>\s</c> still wins, which is the usual case.</summary>
    /// <remarks>
    /// A guard rather than a new behaviour — the direct <c>\f</c> was always honoured — and it is
    /// here because the style is now applied where the <c>\s</c> appears, which is a place a later
    /// change could easily move past the run's own words.
    /// </remarks>
    [Fact]
    public void DirectFormattingAfterTheStyleWins()
        => First(
                @"\pard\plain\s2\f0\fs20 MARKER\par",
                styles: @"{\s2\snext2\f2 Boxed;}")
            .Family.ShouldBe("Liberation Serif");

    /// <summary>The face, size and weight one paragraph's text is set in.</summary>
    /// <param name="Family">The family the document asked for, before substitution.</param>
    /// <param name="Size">The em size.</param>
    /// <param name="Weight">400 or 700.</param>
    private readonly record struct Formatting(string? Family, Length Size, int Weight);

    private static Formatting First(string body, string styles = "")
        => Read(body, styles)[0];

    /// <summary>
    /// The formatting of each body paragraph, taking a uniform paragraph's own rather than its
    /// runs — <see cref="PageParagraph.Runs"/> is empty exactly when nothing varies across the text.
    /// </summary>
    private static IReadOnlyList<Formatting> Read(string body, string styles = "")
    {
        // \deff1 rather than \deff0, so "the default font" and "font zero" are two different
        // answers and a test cannot pass by taking the wrong one.
        string rtf =
            @"{\rtf1\ansi\deff1"
            + @"{\fonttbl{\f0\froman Liberation Serif;}{\f1\fswiss Liberation Sans;}"
            + @"{\f2\fmodern Liberation Mono;}}"
            + (styles.Length == 0 ? string.Empty : @"{\stylesheet" + styles + "}")
            + @"\paperw11906\paperh16838\margl1440\margr1440\margt1440\margb1440\sectd"
            + body
            + "}";

        using DocumentSource source = DocumentSource.FromStream(
            new MemoryStream(Encoding.ASCII.GetBytes(rtf)), "styles.rtf");
        using IDocument document = new WordProcessingReader().Read(source);
        WordProcessingPages pages = (WordProcessingPages)((IPaginatedDocument)document).Layout();

        return
        [
            .. pages.Paragraphs
                .Where(paragraph => paragraph.Text.Length > 0)
                .Select(paragraph => paragraph.HasRuns
                    ? new Formatting(
                        paragraph.Runs[0].Font?.RequestedFamily,
                        paragraph.Runs[0].EmSize,
                        paragraph.Runs[0].Font?.Weight ?? 400)
                    : new Formatting(
                        paragraph.Font?.RequestedFamily, paragraph.EmSize, paragraph.Font?.Weight ?? 400)),
        ];
    }
}
