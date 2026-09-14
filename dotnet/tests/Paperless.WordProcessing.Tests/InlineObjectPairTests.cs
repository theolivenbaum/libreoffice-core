using Paperless.Core.Documents;
using Paperless.Core.Geometry;
using Paperless.TestKit;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// Two adjacent inline objects, too wide between them for one line, are split across two.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a control, and it exists because a previous round concluded the opposite.</b> Round
/// 124 (<c>probes/ink-hunt-r124</c> §7) read the line-break tables and argued that no such split
/// can ever happen: <c>U+0001</c>, the anchor character every word-processing reader puts where an
/// inline object sits, is UAX #14 <c>Line_Break=CM</c>, and LB9 forbids a break <em>before</em> a
/// combining mark — so two adjacent anchors are welded and the second object can never leave the
/// first object's line. The reading of the tables is correct; the conclusion is not.
/// </para>
/// <para>
/// <c>inline-object-pair.docx</c> holds two inline pictures 4.5 in wide in one paragraph whose
/// measure is 6.5 in, so the pair overflows and the second must move. This tree draws them at
/// <c>y</c> 72.0–153.0 and 153.0–234.0, and 26.2.4.2 draws them at the same two rectangles. A
/// second probe at the witness's own geometry — 6.5 in and 6.5 in — agrees as well: ours 72.0–258.6
/// and 258.6–471.0 against 71.9–258.5 and 258.6–471.0. The anchors never reach LB9 as a pair: a run
/// holding nothing but anchors goes through <c>MeasuredParagraph</c>'s own path, where an inline
/// object widens every prefix past its boundary and the object is pushed to the next line when
/// anything precedes it.
/// </para>
/// <para>
/// So the defect round 124 saw on <c>RMI_…GettingOffOil.doc</c> — two as-char pictures drawn on one
/// baseline, one over the other — is not this mechanism and is still open against the WW8 reader.
/// See <c>probes/wordsgroup-r128/results.md</c> §3.
/// </para>
/// </remarks>
public sealed class InlineObjectPairTests
{
    /// <summary>Each object on its own line, and the second below the first.</summary>
    [Fact]
    public void TwoWideInlineObjectsTakeTwoLines()
    {
        RecordingDrawingSink sink = new();

        using (DocumentSource source = DocumentSource.FromFile(
                   Corpus.Require("inline-object-pair.docx")))
        {
            using IDocument document = new WordProcessingReader().Read(source);

            IPageSequence pages = ((IPaginatedDocument)document).Layout();
            for (int i = 0; i < pages.Count; i++) pages[i].Draw(sink);
        }

        IReadOnlyList<DocRect> images = [.. sink.Pages.SelectMany(page => page.Images)];
        images.Count.ShouldBe(2);

        // Not one on top of the other: the second starts at the first's bottom edge.
        images[1].Y.Points.ShouldBeGreaterThan(images[0].Y.Points + (images[0].Height.Points / 2));
        images[1].Y.Points.ShouldBe(
            images[0].Y.Points + images[0].Height.Points, 1.0);
    }
}
