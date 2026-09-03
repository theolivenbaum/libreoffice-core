using System.Xml.Linq;
using Paperless.WordProcessing.Layout;
using Paperless.WordProcessing.Ooxml;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A Word document's anchored shapes declare their geometry, and it is read.
/// </summary>
/// <remarks>
/// <para>
/// It was not, for as long as the DOCX reader has existed. The same <c>spPr</c> was consulted for
/// the fill and the outline while <c>a:prstGeom</c> was passed over, so every anchored shape in a
/// Word file was painted as its bounding rectangle whatever it asked to be. The preset catalogue
/// was never the gap: all 187 presets are in <c>PresetShapeGeometry.txt</c> and the slide side has
/// resolved them all along.
/// </para>
/// <para>
/// Six corpus templates showed it at once, and they are ordinary business documents: a project
/// timeline's milestone circles came out as squares (<c>ellipse</c>, 32 uses across the six) and
/// its diamonds as squares (<c>diamond</c>), a roadmap's chevrons as bars (<c>homePlate</c>, 33),
/// alongside <c>rightArrow</c>, <c>roundRect</c> and <c>bentConnector3</c>.
/// </para>
/// </remarks>
public sealed class FramePresetGeometryTests
{
    /// <summary>The preset a shape names is carried to the drawing.</summary>
    [Theory]
    [InlineData("ellipse")]
    [InlineData("diamond")]
    [InlineData("homePlate")]
    [InlineData("rightArrow")]
    [InlineData("roundRect")]
    [InlineData("bentConnector3")]
    public void TheShapesPresetIsRead(string preset) =>
        Frame(preset).Preset.ShouldBe(preset);

    /// <summary>
    /// Three presets are deliberately not carried, and each for its own reason.
    /// </summary>
    /// <remarks>
    /// <c>rect</c> is the bounding box the drawing already paints, so resolving it through the
    /// catalogue would build a four-point path to arrive exactly where not asking arrives — and it
    /// is much the commonest preset in the corpus. <c>line</c> and <c>straightConnector1</c> mean
    /// the diagonal <c>PageFrame.IsLine</c> already draws; their preset outline is the box, so
    /// taking it here would put three sides on the page that are not in the file.
    /// </remarks>
    [Theory]
    [InlineData("rect")]
    [InlineData("line")]
    [InlineData("straightConnector1")]
    public void TheThreeThatAreLeftToTheirOwnHandlingAreNotCarried(string preset) =>
        Frame(preset).Preset.ShouldBeNull();

    /// <summary>A shape with no <c>a:prstGeom</c> at all carries none.</summary>
    [Fact]
    public void AShapeWithNoStatedGeometryCarriesNone() =>
        Frame(null).Preset.ShouldBeNull();

    /// <summary>An <c>a:avLst</c> value overrides the preset's own default.</summary>
    /// <remarks>
    /// Only a literal. <c>a:gd</c>'s <c>fmla</c> also carries computed guides — <c>*/ 3 4 5</c>
    /// and the like — which belong to the preset rather than to the shape, so anything that is not
    /// <c>val &lt;n&gt;</c> is left for the catalogue to supply. Taking a computed formula as a
    /// number would silently reshape the very presets whose adjustments are most intricate.
    /// </remarks>
    [Fact]
    public void AStatedAdjustmentIsRead()
    {
        PageFrame frame = Frame("roundRect", """<a:avLst><a:gd name="adj" fmla="val 12500"/></a:avLst>""");

        frame.Adjustments.ShouldNotBeNull();
        frame.Adjustments!["adj"].ShouldBe(12500);
    }

    /// <summary>A computed guide is left to the preset rather than read as a value.</summary>
    [Theory]
    [InlineData("""<a:avLst><a:gd name="adj" fmla="*/ 3 4 5"/></a:avLst>""")]
    [InlineData("""<a:avLst><a:gd name="adj" fmla="pin 0 adj 50000"/></a:avLst>""")]
    [InlineData("""<a:avLst><a:gd name="adj" fmla=""/></a:avLst>""")]
    [InlineData("<a:avLst/>")]
    public void AComputedOrEmptyGuideIsNotAnAdjustment(string values) =>
        Frame("roundRect", values).Adjustments.ShouldBeNull();

    private static PageFrame Frame(string? preset, string values = "")
    {
        string geometry = preset is null
            ? string.Empty
            : $"""<a:prstGeom prst="{preset}">{values}</a:prstGeom>""";

        XElement drawing = XElement.Parse(
            $"""
            <w:drawing xmlns:w="{W}" xmlns:wp="{Wp}" xmlns:a="{A}" xmlns:wps="{Wps}">
              <wp:anchor>
                <wp:extent cx="914400" cy="914400"/>
                <wp:wrapNone/>
                <a:graphic><a:graphicData><wps:wsp>
                  <wps:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
                    {geometry}
                  </wps:spPr>
                </wps:wsp></a:graphicData></a:graphic>
              </wp:anchor>
            </w:drawing>
            """);

        return DocxFrames
            .ReadAll(drawing, null, anchorOffset: 0, pictures: null,
                     new DocxFrameContext(null, InHeaderFooter: false, CompatibilityMode: 15))
            .ShouldHaveSingleItem();
    }

    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
}
