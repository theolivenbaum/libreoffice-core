using Paperless.Core.Documents;
using Paperless.TestKit;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A DOCX <c>REF</c> field draws its bookmark's text, not the result Word cached beside it.
/// </summary>
/// <remarks>
/// <para>
/// A <c>REF</c> whose first argument is a bookmark name becomes a <c>SwGetRefField</c> with
/// <c>ReferenceFieldSource::BOOKMARK</c> and part <c>TEXT</c>
/// (<c>sw/source/writerfilter/dmapper/DomainMapper_Impl.cxx</c>:8541-8635), and
/// <c>SwGetRefField::UpdateField</c> recomputes it from the bookmark on load. The two disagree
/// whenever the author moved or extended the bookmark and Word did not refresh the field, and then
/// the reference draws one string and the file states another.
/// </para>
/// <para>
/// Every expectation here was measured against 26.2.4.2 before it was written, from its own
/// rendering of <c>tests/corpus/features/words-reference-field.docx</c> — the committed fixture, one
/// arm per paragraph, each field caching a deliberately wrong result so that what the reference
/// draws is its own answer rather than the file's. <c>probes/docxref-r158/</c> holds the generator
/// and the measurement.
/// </para>
/// <para>
/// Its witness is <c>FAA 2025-26 Holdover Tables.docx</c>, where the reference draws
/// <c>(Tables Table 55, Table 56, Table 57 and Table 58)</c> against the file's cached
/// <c>(Tables 55, 56, 57 and 58)</c> and expands one note's <c>REF</c> to a whole caption. That one
/// note wraps to a third line, which pushes a trailing bullet onto a page of its own: <b>166 pages
/// against the reference's 167 before this, 167 against 167 after</b>.
/// </para>
/// </remarks>
public sealed class DocxReferenceFieldTests
{
    private const string Fixture = "words-reference-field.docx";

    /// <summary>
    /// A bookmark covering more than the cache is drawn in full.
    /// </summary>
    /// <remarks>
    /// Arm A, and the whole of the FAA defect in miniature: the mark covers <c>Table 7</c> across two
    /// runs and the field cached <c>7</c>. 26.2.4.2 draws <c>Table 7</c>.
    /// </remarks>
    [Fact]
    public void ABookmarkCoveringMoreThanTheCacheIsDrawnInFull()
        => Arm("A more than the cache").ShouldBe("Table 7 .");

    /// <summary>
    /// A bookmark whose halves are in different paragraphs is drawn to the end of the first.
    /// </summary>
    /// <remarks>
    /// Arm B. <c>SwGetRefFieldType::FindAnchor</c> (<c>reffld.cxx</c>:1559-1591) answers −1 for an end
    /// in another node and <c>UpdateField</c> (<c>:603-607</c>) reads that as the paragraph's length,
    /// so the second half's paragraph contributes nothing: 26.2.4.2 draws <c>half one</c>.
    /// </remarks>
    [Fact]
    public void ABookmarkRunningPastItsParagraphStopsAtTheParagraphEnd()
        => Arm("B into the next paragraph").ShouldBe("half one .");

    /// <summary>A line break inside the bookmark reaches the field as a space.</summary>
    /// <remarks>
    /// Arm E. <c>FilterText</c> (<c>reffld.cxx</c>:461-489) replaces every character below U+0020,
    /// and a <c>w:br</c> is U+000A in the node — which is why a caption written over two lines comes
    /// back as one. The FAA caption is exactly that shape.
    /// </remarks>
    [Fact]
    public void ALineBreakInsideTheBookmarkBecomesASpace()
        => Arm("E a line break inside").ShouldBe("Line one Line two .");

    /// <summary>And so does a tab.</summary>
    /// <remarks>Arm H, the same rule with U+0009.</remarks>
    [Fact]
    public void ATabInsideTheBookmarkBecomesASpace()
        => Arm("H a tab inside").ShouldBe("before after .");

    /// <summary>
    /// A soft hyphen is dropped and a non-breaking hyphen becomes an ordinary one.
    /// </summary>
    /// <remarks>Arm G, the other two clauses of <c>FilterText</c>.</remarks>
    [Fact]
    public void TheTwoHyphensAreFiltered()
        => Arm("G the two hyphens").ShouldBe("softhyphen and non-breaking .");

    /// <summary>
    /// A field inside the bookmark contributes its value, so a numbered caption keeps its number.
    /// </summary>
    /// <remarks>
    /// Arm F, and the reason the expansion is built from the walked text rather than from the
    /// <c>w:t</c> elements: <c>GetExpandText</c> expands a field portion, so the <c>SEQ</c> that
    /// numbers a caption is part of what the reference draws. <b>Its value is the cached one here</b>
    /// — this reader does not recompute a <c>SEQ</c> — which is right for a file Word last saved and
    /// wrong for one whose caption numbers are stale; the fixture caches the current value, and the
    /// divergence is recorded in the probe.
    /// </remarks>
    [Fact]
    public void AFieldInsideTheBookmarkContributesItsValue()
        => Arm("F a numbering field inside").ShouldBe("Figure 1 .");

    /// <summary>The compact <c>w:fldSimple</c> spelling is the same field.</summary>
    /// <remarks>Arm L, which states its instruction as an attribute and its cache as its children.</remarks>
    [Fact]
    public void TheCompactFormIsExpandedToo()
        => Arm("L the compact form").ShouldBe("Table 7 .");

    /// <summary>
    /// A collapsed bookmark keeps the producer's cache, which is a deliberate divergence.
    /// </summary>
    /// <remarks>
    /// Arms C and D: 26.2.4.2 draws <b>nothing</b> for a <c>REF</c> naming a collapsed bookmark, and
    /// the two cross-reference prefixes do not change that — measured over
    /// <c>__RefHeading__1234_567890</c>, <c>__RefNumPara__1234_567890</c>, <c>_Toc12345</c> and
    /// <c>_Ref999</c>, all four of which draw nothing. The <c>#i81002#</c> branch that makes a
    /// collapsed cross-reference bookmark stand for its whole node is unreachable from a DOCX,
    /// because <c>StartOrEndBookmark</c> asks <c>MarkManager</c> for an ordinary bookmark.
    /// <para>
    /// Not reproduced: an empty expansion would erase whatever the file cached, and this reader's
    /// standing policy is that a wrong substitution is worse than a stale one. Corpus reach is nil —
    /// no <c>REF</c> in the 271 corpus DOCX names a collapsed bookmark.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACollapsedBookmarkKeepsTheCache()
    {
        Arm("C a collapsed bookmark").ShouldBe("stale .");
        Arm("D a collapsed cross-reference").ShouldBe("stale .");
    }

    /// <summary>
    /// A name no bookmark holds keeps the cache rather than drawing the reference's error string.
    /// </summary>
    /// <remarks>
    /// Arm I: 26.2.4.2 draws <c>Error: Reference source not found</c>
    /// (<c>STR_GETREFFLD_REFITEMNOTFOUND</c>). Deliberately not reproduced, on the same reasoning as
    /// the RTF reader's: the lookup that missed is <em>our</em> bookmark table, so the error would
    /// fire wherever this reader is the incomplete one.
    /// </remarks>
    [Fact]
    public void ANameNoBookmarkHoldsKeepsTheCache()
        => Arm("I no such bookmark").ShouldBe("stale .");

    /// <summary>
    /// The switches that ask for something other than the text keep the cache.
    /// </summary>
    /// <remarks>
    /// Arms J and K. <c>\p</c> is the above/below part — 26.2.4.2 draws <c>below</c> — and
    /// <c>PAGEREF</c> is the same field with <c>ReferenceFieldPart::PAGE</c>, which it draws as
    /// <c>1</c>. Neither is computed here, and for both the producer's cache is a better answer than
    /// a wrong one.
    /// </remarks>
    [Fact]
    public void APartThisReaderDoesNotComputeKeepsTheCache()
    {
        Arm("J the page switch").ShouldBe("stale .");
        Arm("K a page reference").ShouldBe("stale .");
    }

    /// <summary>The instruction reader, on which arguments name a bookmark at all.</summary>
    [Fact]
    public void TheInstructionReaderNamesTheBookmarkOnlyWhenTheTextIsWhatIsQuoted()
    {
        FieldInstructions.ReferenceBookmark(" REF _Ref1 \\h ").ShouldBe("_Ref1");
        FieldInstructions.ReferenceBookmark(" REF _Ref1 \\h \\* MERGEFORMAT ").ShouldBe("_Ref1");
        FieldInstructions.ReferenceBookmark(" REF \"a name\" \\h ").ShouldBe("a name");
        FieldInstructions.ReferenceBookmark(" REF _Ref1 \\p ").ShouldBeNull();
        FieldInstructions.ReferenceBookmark(" REF _Ref1 \\n ").ShouldBeNull();
        FieldInstructions.ReferenceBookmark(" PAGEREF _Ref1 \\h ").ShouldBeNull();
        FieldInstructions.ReferenceBookmark(" REF ").ShouldBeNull();
    }

    /// <summary>What the fixture's arm of that label lays out, after the label and its colon.</summary>
    private static string Arm(string label)
    {
        string line = Lines().FirstOrDefault(text => text.StartsWith(label + ": ", StringComparison.Ordinal))
                      ?? throw new InvalidOperationException($"the fixture states no arm '{label}'");

        return line[(label.Length + 2)..];
    }

    private static List<string> Lines()
    {
        using DocumentSource source = DocumentSource.FromFile(Corpus.Require(Fixture));
        using IDocument document = new WordProcessingReader().Read(source);

        var pages = (WordProcessingPages)((IPaginatedDocument)document).Layout();
        return [.. pages.Paragraphs.Select(paragraph => paragraph.Text)];
    }
}
