using Paperless.Core.Geometry;
using Paperless.Core.Units;
using Paperless.WordProcessing.Layout;
using Paperless.WordProcessing.Model;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A frame bigger than its page is cut down to the page, and a picture's or a chart's aspect is kept
/// while it is.
/// </summary>
/// <remarks>
/// <para>
/// <c>SwFlyFreeFrame::CheckClip</c> (<c>sw/source/core/layout/flylay.cxx</c>:471-660), called from
/// <c>MakeAll</c> (:251) on every free fly. It first gives up the position and only squeezes if the
/// frame still does not fit, so the frames whose <em>size</em> it changes are exactly those bigger
/// than the sheet; :598-627 makes the squeeze proportional when the fly's lower is a
/// <c>SwGrfNode</c> or a <c>SwOLENode</c>, and :638-648 writes the result back into the frame format
/// for an OLE object — which is why the flat ODT can be used to read it.
/// </para>
/// <para>
/// <b>Every expectation here was read out of 26.2.4.2's own <c>.fodt</c> of
/// <c>023_Unit_Circle_Chart_Circular_Percentage</c></b>, one <c>wp:extent</c> changed at a time on a
/// 595.30 x 841.89 pt page (<c>probes/chart-fit-r97/framesize.py</c>). The document as authored
/// states a 682.10 x 493.50 pt chart and the reference resolves it to <b>595.30 x 430.70</b>; the
/// same page's picture given the same extent keeps 682.10 x 493.51, because the write-back is
/// OLE-only although the squeeze is not.
/// </para>
/// </remarks>
public sealed class FrameSqueezedToPageTests
{
    /// <summary>The witness's own page: A4 portrait.</summary>
    private static readonly PageGeometry Page = new()
    {
        Size = new DocSize(Length.FromPoints(595.3), Length.FromPoints(841.89)),
        Margins = PageMargins.Uniform(Length.FromPoints(72)),
    };

    /// <summary>
    /// The witness: a chart wider than the sheet comes back at the page's width and proportionally
    /// shorter.
    /// </summary>
    [Theory]
    [InlineData(682.10, 493.50, 595.30, 430.70)]
    [InlineData(708.66, 157.48, 595.30, 132.29)]
    [InlineData(314.96, 905.51, 292.79, 841.89)]
    [InlineData(708.66, 905.51, 595.30, 760.65)]
    [InlineData(393.70, 236.22, 393.70, 236.22)]
    public void AChartBiggerThanThePageIsCutDownToItProportionally(
        double width, double height, double expectedWidth, double expectedHeight)
    {
        DocRect placed = Place(Frame(width, height) with { IsImage = true });

        // 0.05 pt is one twip, and the ratio is applied in whole truncated twips on the reference
        // side — `aFrameRect.Width() * aOldSize.Height() / aOldSize.Width()` over a
        // `tools::Long` — where this multiplies EMU and rounds. The worst of the five rows is
        // 0.04 pt.
        placed.Width.Points.ShouldBe(expectedWidth, 0.05);
        placed.Height.Points.ShouldBe(expectedHeight, 0.05);
    }

    /// <summary>
    /// And it starts from the page's own edge, because the move that precedes the squeeze has
    /// already put it there.
    /// </summary>
    [Fact]
    public void ItIsFlushWithTheEdgeItOverflowed()
    {
        DocRect placed = Place(Frame(682.10, 493.50) with { IsImage = true });

        placed.X.ShouldBe(Length.Zero);
        placed.Y.Points.ShouldBe(117.50, 0.02);
    }

    /// <summary>
    /// A frame holding neither a picture nor a chart has its two axes cut independently: :598
    /// reaches the proportional branch only for a <c>SwNoTextFrame</c> lower.
    /// </summary>
    [Fact]
    public void ATextFrameKeepsTheAxisThatFits()
    {
        DocRect placed = Place(Frame(682.10, 493.50));

        placed.Width.Points.ShouldBe(595.30, 0.02);
        placed.Height.Points.ShouldBe(493.50, 0.02);
    }

    /// <summary>
    /// <c>DisableOffPagePositioning</c> and a wrap-through object together exempt it, and neither
    /// alone does.
    /// </summary>
    /// <remarks>
    /// <c>SwAnchoredObject::IsDraggingOffPageAllowed</c>
    /// (<c>sw/source/core/layout/anchoredobject.cxx</c>:790-801), the guard on
    /// <c>CheckClip</c>:493. <c>sw/source/writerfilter/filter/WriterFilter.cxx</c>:333 sets the
    /// setting for every writerfilter import.
    /// </remarks>
    [Theory]
    [InlineData(false, TextWrap.Both, 595.30)]
    [InlineData(false, TextWrap.Through, 595.30)]
    [InlineData(true, TextWrap.Both, 595.30)]
    [InlineData(true, TextWrap.Through, 682.10)]
    public void OnlyAWrapThroughObjectUnderTheSettingEscapes(
        bool disables, TextWrap wrap, double expectedWidth)
    {
        PageFrame frame = Frame(682.10, 493.50) with { IsImage = true, Wrap = wrap };

        FrameLayout.Place(
                frame, Page, Page.TextArea, Length.FromPoints(100),
                disablesOffPagePositioning: disables)
            .Width.Points.ShouldBe(expectedWidth, 0.02);
    }

    /// <summary>The witness's own anchor: 45.36 pt below a paragraph at the top of the body.</summary>
    private static PageFrame Frame(double width, double height) => new()
    {
        Size = new DocSize(Length.FromPoints(width), Length.FromPoints(height)),
        Anchor = FrameAnchor.Paragraph,
        VerticalOrigin = FrameVerticalOrigin.Paragraph,
        VerticalAlignment = FrameVerticalAlignment.Offset,
        VerticalOffset = Length.FromPoints(17.5),
        HorizontalOrigin = FrameHorizontalOrigin.Column,
        HorizontalAlignment = FrameHorizontalAlignment.Offset,
    };

    private static DocRect Place(PageFrame frame)
        => FrameLayout.Place(frame, Page, Page.TextArea, Length.FromPoints(100));
}
