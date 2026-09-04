using System.Xml.Linq;
using Paperless.Core.Geometry;
using Paperless.Core.Units;
using Paperless.WordProcessing.Layout;
using Paperless.WordProcessing.Ooxml;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// <c>wp:effectExtent</c> on an inline drawing, which is room on the line rather than size on the
/// drawing.
/// </summary>
/// <remarks>
/// <para>
/// A shadow, a glow or a fat stroke paints outside the <c>wp:extent</c> a drawing states, and
/// <c>wp:effectExtent</c> is how much on each side. For a <c>wp:inline</c> LibreOffice adds all four
/// edges straight to the object's own margins — <c>GraphicImport.cxx</c>:1036-1055, guarded by
/// <c>IMPORT_AS_DETECTED_INLINE</c> and a zero rotation, with the comment <em>"EffectExtent contains
/// all needed additional space, including fat stroke and shadow. Simple add it to the margins."</em>
/// Writer then rests the object's rectangle <em>including</em> that spacing on the baseline
/// (<c>SwFlyCntPortion::SetBase</c> sizing itself from
/// <c>SwAsCharAnchoredObjectPosition::GetObjBoundRectInclSpacing</c>), so the extent grows the line.
/// </para>
/// <para>
/// Measured in <c>dotnet/probes/words-inline-effectextent/</c>, against both installed references —
/// 24.2.7.2 and 26.2.4.2, which agree to the twip on every fixture. One 50.4 pt shape between two
/// 12 pt text lines, the gap between those lines measured against a zero-extent control:
/// </para>
/// <list type="table">
///   <item><term><c>l=t=r=b="0"</c></term><description>64.25 pt — the control</description></item>
///   <item><term><c>27432</c> (2.16 pt)</term><description>68.55 pt, <b>+4.30</b></description></item>
///   <item><term><c>91440</c> (7.2 pt)</term><description>78.65 pt, <b>+14.40</b></description></item>
///   <item><term><c>137160</c> (10.8 pt)</term><description>85.85 pt, <b>+21.60</b></description></item>
///   <item><term>top only</term><description>75.05 pt, <b>+10.80</b></description></item>
///   <item><term>bottom only</term><description>75.05 pt, <b>+10.80</b></description></item>
/// </list>
/// <para>
/// So each edge is independent and additive, and the growth is exactly the stated EMUs rounded to the
/// twip — 2.16 pt is 43.2 twips, which lands at 43 and doubles to the 4.30 above rather than to 4.32.
/// </para>
/// <para>
/// The corpus case is <c>WordArt_Shapes_Arrows_Catalog1.docx</c>, 340 unrotated inline shapes all
/// carrying one of those three extents. Without this the document paginated to <b>45 pages against
/// both references' 52</b>; with it, 52, holding the same shapes on every one of them.
/// </para>
/// </remarks>
public sealed class FrameEffectExtentTests
{
    /// <summary>The four edges are read from an inline drawing, each rounded to the twip.</summary>
    /// <remarks>
    /// <para>
    /// The rounding is the reader's shared <c>Emu</c> helper and it is what the reference does too:
    /// 27432 EMU is 2.16 pt, which is 43.2 twips and lands at 43, so a shape carrying that extent on
    /// its top and bottom grows its line by <b>4.30 pt</b> and not by 4.32. That is the measured
    /// figure — see the type's own remarks — so asserting the unrounded EMUs here would pin a value
    /// neither renderer produces. The other two edges divide evenly: 91440 is 144 twips and 137160 is
    /// 216.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFourEdgesAreRead()
    {
        PageFrame frame = Inline("""<wp:effectExtent l="27432" t="91440" r="137160" b="0"/>""");

        frame.EffectExtent.Left.ShouldBe(Length.FromTwips(43));
        frame.EffectExtent.Top.ShouldBe(Length.FromTwips(144));
        frame.EffectExtent.Right.ShouldBe(Length.FromTwips(216));
        frame.EffectExtent.Bottom.ShouldBe(Length.Zero);
    }

    /// <summary>
    /// The catalogue's three extents grow a line by the figures both references were measured at.
    /// </summary>
    /// <remarks>
    /// The whole of the pagination fix in one assertion: these are the +4.30, +14.40 and +21.60 pt
    /// from the probe, as the difference between the drawing's own height and the room it takes.
    /// </remarks>
    [Theory]
    [InlineData(27432, 4.30)]
    [InlineData(91440, 14.40)]
    [InlineData(137160, 21.60)]
    public void TheMeasuredGrowthIsReproduced(int emu, double points)
    {
        PageFrame frame = Inline($"""<wp:effectExtent l="0" t="{emu}" r="0" b="{emu}"/>""");

        (frame.InlineExtent.Height - frame.Size.Height).ShouldBe(Length.FromPoints(points));
    }

    /// <summary>
    /// The extent grows the room the drawing takes on its line and leaves the drawing's own size alone.
    /// </summary>
    /// <remarks>
    /// The split matters because the shape is still painted at the size the file gives it: 10.8 pt on
    /// each edge of a 50.4 pt shape makes a 72 pt line and a 50.4 pt shape, not a 72 pt shape.
    /// </remarks>
    [Fact]
    public void TheExtentGrowsTheLineAndNotTheDrawing()
    {
        PageFrame frame = Inline("""<wp:effectExtent l="137160" t="137160" r="137160" b="137160"/>""");

        frame.Size.Height.ShouldBe(Length.FromEmu(640080));
        frame.InlineExtent.Height.ShouldBe(Length.FromPoints(72));
        frame.InlineExtent.Width.ShouldBe(frame.Size.Width + Length.FromPoints(21.6));
    }

    /// <summary>A drawing stating no extent takes exactly its own size on the line.</summary>
    [Fact]
    public void NoExtentLeavesTheLineAtTheDrawingsOwnSize()
    {
        PageFrame frame = Inline("");

        frame.EffectExtent.ShouldBe(Margins.Zero);
        frame.InlineExtent.Height.ShouldBe(frame.Size.Height);
        frame.InlineExtent.Width.ShouldBe(frame.Size.Width);
    }

    /// <summary>
    /// The <c>dist*</c> attributes beside it are discarded on an inline drawing, not added to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not an omission. LibreOffice zeroes the matching margin merely because the attribute is
    /// present — <c>GraphicImport.cxx</c>:1387-1398 is four cases of
    /// <c>case LN_CT_Inline_distT: m_nTopMargin = 0;</c>, which never reads <c>nIntValue</c> at all.
    /// </para>
    /// <para>
    /// Measured: a fixture stating <c>distT="137160" distB="137160"</c> and no effect extent moves the
    /// line below it by <b>0.00 pt</b> against the zero control, on both installed references. So a
    /// reader that added the two would be 21.6 pt out on every such drawing.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDistanceAttributesDoNotGrowAnInlineDrawing()
    {
        PageFrame frame = Inline("", dist: """distT="137160" distB="137160" distL="137160" distR="137160" """);

        frame.InlineExtent.Height.ShouldBe(frame.Size.Height);
        frame.InlineExtent.Width.ShouldBe(frame.Size.Width);
    }

    /// <summary>
    /// An anchored drawing carries no extent, because LibreOffice reaches it by a different route.
    /// </summary>
    /// <remarks>
    /// A floating drawing's extent goes into its <em>wrap</em> margins, through the much longer
    /// <c>WrapTextMode_PARALLEL</c> branch that needs the shape's own bound rectangle. Reading the four
    /// numbers there would be wrong rather than partial — see the note on
    /// <see cref="PageFrame.Spacing"/> for the measurement that keeps it unread.
    /// </remarks>
    [Fact]
    public void AnAnchoredDrawingTakesNoEffectExtent()
    {
        XElement drawing = XElement.Parse(
            $"""
            <w:drawing xmlns:w="{W}" xmlns:wp="{Wp}" xmlns:a="{A}" xmlns:wps="{Wps}">
              <wp:anchor distT="0" distB="0" distL="0" distR="0">
                <wp:extent cx="1828800" cy="640080"/>
                <wp:effectExtent l="137160" t="137160" r="137160" b="137160"/>
                <wp:wrapNone/>
                <a:graphic><a:graphicData><wps:wsp>
                  <wps:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="1828800" cy="640080"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                  </wps:spPr>
                </wps:wsp></a:graphicData></a:graphic>
              </wp:anchor>
            </w:drawing>
            """);

        PageFrame frame = DocxFrames
            .ReadAll(drawing, null, anchorOffset: 0, pictures: null)
            .ShouldHaveSingleItem();

        frame.EffectExtent.ShouldBe(Margins.Zero);
        frame.InlineExtent.Height.ShouldBe(frame.Size.Height);
    }

    private static PageFrame Inline(string effectExtent, string dist = "")
    {
        XElement drawing = XElement.Parse(
            $"""
            <w:drawing xmlns:w="{W}" xmlns:wp="{Wp}" xmlns:a="{A}" xmlns:wps="{Wps}">
              <wp:inline {dist}>
                <wp:extent cx="1828800" cy="640080"/>
                {effectExtent}
                <a:graphic><a:graphicData><wps:wsp>
                  <wps:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="1828800" cy="640080"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                  </wps:spPr>
                </wps:wsp></a:graphicData></a:graphic>
              </wp:inline>
            </w:drawing>
            """);

        return DocxFrames
            .ReadAll(drawing, null, anchorOffset: 0, pictures: null)
            .ShouldHaveSingleItem();
    }

    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
}
