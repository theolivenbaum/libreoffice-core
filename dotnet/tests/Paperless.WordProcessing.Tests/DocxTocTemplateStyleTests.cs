using Paperless.Core.Documents;
using Paperless.Core.Units;
using Paperless.TestKit;
using Paperless.Text.Layout;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A paragraph in a built-in <c>heading N</c> that a <c>TOC \t</c> switch names draws none of its
/// own direct paragraph formatting.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A LibreOffice behaviour rather than a rule of the format, reproduced because it decides
/// pages.</strong> A <c>TOC</c> carrying <c>\t "Heading 2,1,Heading 3,2"</c> registers those styles
/// as the index's source styles (<c>DomainMapper_Impl::handleToc</c>,
/// <c>DomainMapper_Impl.cxx</c>:7663-7707), and from then on every paragraph in one of them, anywhere
/// in the document, is imported with its <c>w:pPr</c> beyond the <c>w:pStyle</c> discarded. See
/// <c>DocxTocStyles</c> for what is kept and for the seat, which is <em>not</em> located.
/// </para>
/// <para>
/// Every expectation is a measurement of 26.2.4.2's own <c>--convert-to fodt</c> of the committed
/// fixture <c>tests/corpus/features/words-toc-template-styles.docx</c> — the importer's answer,
/// before any layout or rounding. Three further controls could not live in that file, because the
/// registration is document-wide and the arms below would void them: a <c>\t</c> naming only
/// <c>Heading 2</c>, a <c>\o</c> switch with no <c>\t</c>, and a document with no <c>TOC</c> at all.
/// Each is its own document under <c>probes/tocstyle-r160/</c> and each keeps its direct formatting.
/// </para>
/// <para>
/// Its witness is <c>24-25_FAA_Holdover_Tables.docx</c>, whose <c>TABLE 50</c> caption states
/// <c>&lt;w:spacing w:after="0"/&gt;</c> over a style stating 6 pt. Reproducing the 6 pt is what
/// pushes the page-break paragraph under that table onto a page of its own: <b>154 pages against the
/// reference's 155 before this, 155 against 155 after</b>.
/// </para>
/// </remarks>
public sealed class DocxTocTemplateStyleTests
{
    private const string Fixture = "words-toc-template-styles.docx";

    /// <summary>The style's 6 pt, which the fixture's arms state 0 for.</summary>
    private static readonly Length StyleSpacing = Length.FromTwips(120);

    /// <summary>
    /// A direct <c>w:spacing</c> is discarded, so the style's own spacing is what is drawn.
    /// </summary>
    /// <remarks>Arm A, and the whole of the witness.</remarks>
    [Fact]
    public void ADirectSpacingIsDiscarded()
        => Arm("A-named-spacing").SpaceAfter.ShouldBe(StyleSpacing);

    /// <summary>So is a direct alignment.</summary>
    /// <remarks>
    /// Arm B: the fixture says <c>&lt;w:jc w:val="left"/&gt;</c> over a style that centres, and
    /// 26.2.4.2's resolved view of the paragraph carries the style's <c>fo:text-align="center"</c>.
    /// </remarks>
    [Fact]
    public void ADirectAlignmentIsDiscarded()
        => Arm("B-named-jc").Alignment.ShouldBe(TextAlignment.Centre);

    /// <summary>And a direct indent.</summary>
    /// <remarks>Arm C, <c>&lt;w:ind w:left="720"/&gt;</c>, drawn at the style's nought.</remarks>
    [Fact]
    public void ADirectIndentIsDiscarded()
        => Arm("C-named-ind").StartIndent.ShouldBe(Length.Zero);

    /// <summary>
    /// A page break is <em>kept</em>, which is the one exception the measurement found.
    /// </summary>
    /// <remarks>
    /// Arm D: its resolved style is the only one of the five that survives as an automatic style,
    /// and it holds <c>fo:break-before="page"</c> and nothing else.
    /// </remarks>
    [Fact]
    public void APageBreakIsKept()
    {
        ParagraphFormat format = Arm("D-named-break");
        format.StartsNewPage.ShouldBeTrue();
        format.SpaceAfter.ShouldBe(StyleSpacing, "everything but the break is still discarded");
    }

    /// <summary>
    /// A paragraph written <em>before</em> the field loses its formatting too.
    /// </summary>
    /// <remarks>
    /// Arm E. The registration is a property of the document, not of the text after the field, which
    /// is also why the three controls above had to be separate files.
    /// </remarks>
    [Fact]
    public void ThePositionOfTheFieldDoesNotMatter()
        => Arm("E-named-before").SpaceAfter.ShouldBe(StyleSpacing);

    /// <summary>
    /// A paragraph in a heading the switch does <em>not</em> name keeps its own formatting.
    /// </summary>
    /// <remarks>Arm G: <c>heading 4</c> while the switch names <c>heading 3</c>.</remarks>
    [Fact]
    public void AnUnnamedHeadingKeepsItsFormatting()
        => Arm("G-other-style").SpaceAfter.ShouldBe(Length.Zero);

    /// <summary>
    /// A custom style carrying a built-in heading's <em>name</em> keeps its formatting.
    /// </summary>
    /// <remarks>
    /// Arm I. This is the control that decides the implementation: matching on the name alone would
    /// void this paragraph, and 26.2.4.2 does not. <c>w:customStyle</c> is what separates them.
    /// </remarks>
    [Fact]
    public void ACustomStyleWearingAHeadingsNameKeepsItsFormatting()
        => Arm("I-custom-name").SpaceAfter.ShouldBe(Length.Zero);

    /// <summary>And a custom style with an outline level of its own.</summary>
    /// <remarks>
    /// Arm J: so it is the built-in heading and not the outline level, which is the other reading
    /// the witness alone could not rule out.
    /// </remarks>
    [Fact]
    public void ACustomStyleWithAnOutlineLevelKeepsItsFormatting()
        => Arm("J-custom-outline").SpaceAfter.ShouldBe(Length.Zero);

    /// <summary>And a built-in style that is not a heading.</summary>
    /// <remarks>Arm K, <c>Title</c> named by its own switch.</remarks>
    [Fact]
    public void ABuiltInStyleThatIsNotAHeadingKeepsItsFormatting()
        => Arm("K-builtin-title").SpaceAfter.ShouldBe(Length.Zero);

    private static ParagraphFormat Arm(string label)
    {
        using DocumentSource source = DocumentSource.FromFile(Corpus.Require(Fixture));
        using IDocument document = new WordProcessingReader().Read(source);

        var pages = (WordProcessingPages)((IPaginatedDocument)document).Layout();
        PageParagraph paragraph =
            pages.Paragraphs.FirstOrDefault(p => p.Text == label)
            ?? throw new InvalidOperationException($"the fixture states no arm '{label}'");

        return paragraph.DeclaredFormat;
    }
}
