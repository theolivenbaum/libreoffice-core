using Paperless.Core.Geometry;
using Paperless.Core.Units;
using Paperless.WordProcessing.Layout;
using Paperless.WordProcessing.Model;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// An object that follows the text flow is positioned against the page <em>body</em>, captured
/// although its wrap would exempt it, held inside the sheet rather than the body, and never pulled up
/// from below.
/// </summary>
/// <remarks>
/// <para>
/// One function decides all four: <c>SwEnvironmentOfAnchoredObject::GetVertEnvironmentLayoutFrame</c>
/// (<c>sw/source/core/objectpositioning/environmentofanchoredobject.cxx</c>:64-95) answers
/// <c>FindPageFrame()</c> for an object that does not follow the text flow, and otherwise walks up from
/// the anchor to the first cell, fly, header, footer, footnote, <b>page body</b> or page frame. A
/// paragraph in the body lands on the page body frame, whose top is <c>w:pgMar/@w:top</c> below the
/// sheet's — and the four consequences are <c>PAGE_FRAME</c>'s base
/// (<c>tocntntanchoredobjectposition.cxx</c>:590-596), <c>mbFollowTextFlow</c> in
/// <c>mbDoNotCaptureAnchoredObj</c> (<c>anchoredobjectposition.cxx</c>:125-144), the
/// <c>compatibilityMode</c> 15 narrowing's own <c>rPageAlignLayFrame.IsPageFrame()</c> test (:562-566),
/// and <c>bCheckBottom = !DoesObjFollowsTextFlow()</c>
/// (<c>tocntntanchoredobjectposition.cxx</c>:457).
/// </para>
/// <para>
/// <b>Which objects follow it is the point, and it is one unguarded line.</b>
/// <see cref="PageFrame.FollowsTextFlow"/> has the seats: false for every shape and every picture
/// outside a table, true for a chart or an OLE object whose anchor does not state
/// <c>layoutInCell="0"</c>, because such an object is a <c>SwXTextEmbeddedObject</c> and
/// <c>DomainMapper_Impl.cxx</c>:9792 sets the property on it with no <c>IsInTable()</c> test where
/// both of <c>GraphicImport</c>'s own writes have one.
/// </para>
/// <para>
/// Measured on <c>028_Unit_Circle_Chart_Optimized_Graph.docx</c> against 26.2.4.2: its chart states a
/// <c>relativeFrom="page"</c> offset of 175.52 pt on a page whose <c>w:pgMar/@w:top</c> is 1440 twips,
/// and the reference draws it <b>71.6 pt lower than the offset states</b> — that margin, less the
/// 0.36 pt the two writers' text origins differ by. <c>probes/pagev-r192</c> holds the variants; the
/// discriminating one is a pair of fixtures identical but for whether the anchored object is a
/// <c>wps:wsp</c> or a chart, since <c>probes/ofpie-r190/page-anchor-fixture.py</c> established that
/// the <em>shape</em> is drawn at the sheet's own top edge and left the document unexplained.
/// </para>
/// </remarks>
public sealed class FrameFollowsTextFlowTests
{
    /// <summary>A4 with a one-inch top margin, so the body's top is 72 pt below the sheet's.</summary>
    private static readonly PageGeometry Page = new()
    {
        Size = new DocSize(Length.FromPoints(595.3), Length.FromPoints(841.9)),
        Margins = PageMargins.Uniform(Length.FromPoints(72)),
    };

    /// <summary>The margin, which is the whole of the distance under test.</summary>
    private const double MarginPoints = 72;

    /// <summary><c>028</c>'s own stated offset, 2229163 EMU.</summary>
    private const double OffsetPoints = 175.52;

    /// <summary>
    /// A page-relative offset counts from the body's top when the object follows the text flow, and
    /// from the sheet's when it does not.
    /// </summary>
    [Theory]
    [InlineData(false, OffsetPoints)]
    [InlineData(true, MarginPoints + OffsetPoints)]
    public void APageRelativeOffsetCountsFromTheVerticalEnvironment(bool follows, double expected)
        => Docx(Chart(follows) with { VerticalOffset = Length.FromPoints(OffsetPoints) })
            .Y.Points.ShouldBe(expected, 0.01);

    /// <summary>
    /// A wrap-through object escapes the capture, and following the text flow takes that escape away.
    /// </summary>
    /// <remarks>
    /// <c>mbDoNotCaptureAnchoredObj = bConsidered &amp;&amp; !mbFollowTextFlow &amp;&amp;
    /// DO_NOT_CAPTURE_DRAW_OBJS_ON_PAGE</c> is a product of three, so either of the first two being
    /// false captures the object. <see cref="FrameObjectKind"/> carries the first.
    /// </remarks>
    [Theory]
    [InlineData(false, -200)]
    [InlineData(true, 0)]
    public void FollowingTheTextFlowCapturesAnObjectItsWrapWouldExempt(bool follows, double expected)
    {
        PageFrame frame = Chart(follows) with
        {
            Wrap = TextWrap.Through,
            VerticalOffset = Length.FromPoints(-200),
        };

        Docx(frame).Y.Points.ShouldBe(expected, 0.01);
    }

    /// <summary>
    /// The top correction fires for a following object, and it is the sheet's top rather than the
    /// body's.
    /// </summary>
    /// <remarks>
    /// The area is the page because the <c>compatibilityMode</c> 15 narrowing needs
    /// <c>rPageAlignLayFrame.IsPageFrame()</c> and that frame is the body here — so the narrowing that
    /// the flag's name suggests is exactly what a following object does not get. Measured:
    /// <c>028</c> with its offset rewritten to −200 pt is drawn by 26.2.4.2 at <b>0.36</b> pt, the
    /// sheet's own top.
    /// </remarks>
    [Fact]
    public void AFollowingObjectIsHeldInsideTheSheetAndNotTheBody()
    {
        PageFrame frame = Chart(follows: true) with { VerticalOffset = Length.FromPoints(-200) };

        Docx(frame).Y.ShouldBe(Length.Zero);
    }

    /// <summary>
    /// The bottom correction does not fire for a following object, so it hangs off the page.
    /// </summary>
    /// <remarks>
    /// <c>bCheckBottom</c> guards only the first of <c>ImplAdjustVertRelPos</c>' two corrections
    /// (<c>anchoredobjectposition.cxx</c>:653-664), and the caller passes
    /// <c>!DoesObjFollowsTextFlow()</c>. The non-following row is the control and is the rule
    /// <see cref="FrameCapturedOnPageTests"/> pins: a frame past the bottom comes back until its bottom
    /// rests on the area's.
    /// </remarks>
    [Theory]
    [InlineData(false, 841.9 - 36)]
    [InlineData(true, MarginPoints + 820)]
    public void OnlyANonFollowingObjectIsPulledUpFromBelowThePage(bool follows, double expected)
    {
        // 820 pt down a 841.9 pt page puts a 36 pt frame 14.1 pt past the bottom edge, so the
        // correction under test is the one that fires rather than the one that does not.
        PageFrame frame = Chart(follows) with { VerticalOffset = Length.FromPoints(820) };

        Docx(frame).Y.Points.ShouldBe(expected, 0.01);
    }

    /// <summary>
    /// A paragraph-relative following object is held inside the sheet where a non-following one is
    /// held inside the body.
    /// </summary>
    /// <remarks>
    /// The same narrowing question at the origin the corpus's charts mostly use, which is why this is
    /// the row with reach: an embedded object anchored to its paragraph is affected by the capture
    /// area although its base does not move.
    /// <para>
    /// <b>This row and the bottom correction above are read out of the source and not measured at a
    /// reference of their own</b> — <c>028</c> states <c>page</c> and so exercises neither.
    /// <c>probes/pagev-r192</c> §4 says what would settle them.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false, MarginPoints)]
    [InlineData(true, 20)]
    public void AFollowingObjectIsNotNarrowedToTheBody(bool follows, double expected)
    {
        PageFrame frame = Chart(follows) with
        {
            VerticalOrigin = FrameVerticalOrigin.Paragraph,
            VerticalOffset = Length.FromPoints(-80),
        };

        Docx(frame, anchorTop: 100).Y.Points.ShouldBe(expected, 0.01);
    }

    /// <summary>
    /// The horizontal position is untouched, which is a property of the C++ rather than an omission.
    /// </summary>
    /// <remarks>
    /// <c>GetHoriEnvironmentLayoutFrame</c>'s walk stops at a cell, a fly or a page and has no body
    /// frame in it (<c>environmentofanchoredobject.cxx</c>:34-61), so a body anchor's horizontal
    /// environment is the page either way. Measured on <c>028</c>, whose chart the reference draws at
    /// the x this tree already gave it.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheHorizontalPositionDoesNotMove(bool follows)
    {
        PageFrame frame = Chart(follows) with
        {
            HorizontalOrigin = FrameHorizontalOrigin.Page,
            HorizontalOffset = Length.FromPoints(96),
        };

        Docx(frame).X.Points.ShouldBe(96, 0.01);
    }

    /// <summary>
    /// <c>028</c>'s chart, reduced to what decides its position: a 486 x 36 pt embedded object stated
    /// against the page.
    /// </summary>
    /// <remarks>
    /// Its height is not the document's 493.5 pt, because a frame that tall on an A4 page is squeezed
    /// by <c>SwFlyFreeFrame::CheckClip</c> and this is a test about the base rather than about the
    /// squeeze.
    /// </remarks>
    private static PageFrame Chart(bool follows) => new()
    {
        Size = new DocSize(Length.FromPoints(486), Length.FromPoints(36)),
        Anchor = FrameAnchor.Paragraph,
        ObjectKind = FrameObjectKind.Fly,
        Wrap = TextWrap.Both,
        FollowsTextFlow = follows,
        VerticalOrigin = FrameVerticalOrigin.Page,
        VerticalAlignment = FrameVerticalAlignment.Offset,
        HorizontalOrigin = FrameHorizontalOrigin.Column,
        HorizontalAlignment = FrameHorizontalAlignment.Offset,
    };

    /// <summary>
    /// The three pagination options a DOCX brings: the capture flag off, the wrapped-object capture on,
    /// and the <c>compatibilityMode</c> 15 narrowing on.
    /// </summary>
    private static DocRect Docx(PageFrame frame, double anchorTop = 0)
        => FrameLayout.Place(
            frame,
            Page,
            Page.TextArea,
            Length.FromPoints(anchorTop),
            capturesOnPage: false,
            capturesWrappedObjects: true,
            narrowsCaptureToBody: true);
}
