using System.Xml.Linq;
using Paperless.Core.Graphics;
using Paperless.WordProcessing.Layout;
using Paperless.WordProcessing.Ooxml;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A VML shape's fill, its outline and the straight connectors VML writes a rule as.
/// </summary>
/// <remarks>
/// <para>
/// <strong>We drew none of these until round 52, and no column of the gate can see it.</strong>
/// Measured before the change on the five Work Breakdown Structure templates:
/// <c>068_Work_Breakdown_Structure_Template_Green_Theme</c>'s reference emits 41 fills and 36
/// strokes and ours emitted <b>zero of each</b> while placing all 41 labels correctly. A blind
/// reviewer given only the rendered pair reported it from the other side — <em>"the reference
/// draws pale-green filled boxes with green borders around every label; ours draws nothing — bare
/// text on white"</em> — and the reviewer of the same page afterwards, who had not seen the first,
/// reported that neither half draws line art the other does not.
/// </para>
/// <para>
/// The four rules pinned here each cost something if they go the other way, and the fourth is the
/// one that decides how far this reaches.
/// </para>
/// </remarks>
public sealed class VmlShapePaintTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string V = "urn:schemas-microsoft-com:vml";
    private const string O = "urn:schemas-microsoft-com:office:office";

    private static XElement Pict(string inner)
        => XElement.Parse($"<w:pict xmlns:w=\"{W}\" xmlns:v=\"{V}\" xmlns:o=\"{O}\">{inner}</w:pict>");

    private static PageFrame One(string inner)
        => DocxVmlFrames.ReadAll(Pict(inner), 0, null, _ => []).ShouldHaveSingleItem();

    /// <summary>
    /// A theme-indexed colour resolves to the literal RGB beside the index, not through the index.
    /// </summary>
    /// <remarks>
    /// <c>ConversionHelper::decodeColor</c> separates the value at its space and returns on a
    /// seven-character <c>#RRGGBB</c> (<c>oox/source/vml/vmlformatting.cxx:252-257</c>) long before
    /// the palette branch at line 282. Confirmed twice in the reference's own content stream:
    /// <c>068</c> draws 41 fills at <c>#E2EFD9</c> from <c>fillcolor="#e2efd9 [665]"</c>, and
    /// <c>069</c> draws 18 at <c>#F2F2F2</c>, 3 at <c>#D5DCE4</c> and 1 at <c>#8496B0</c> — which
    /// is what we now draw, colour for colour and count for count.
    /// </remarks>
    [Fact]
    public void ARectTakesTheLiteralColourBesideItsPaletteIndex()
    {
        PageFrame frame = One(
            "<v:rect style=\"position:absolute;margin-left:0;margin-top:0;width:100pt;height:20pt\""
            + " fillcolor=\"#e2efd9 [665]\" strokecolor=\"#70ad47 [3209]\"/>");

        frame.Fill.ShouldBe(Colour.FromRgb(0xE2EFD9));
        frame.BorderColour.ShouldBe(Colour.FromRgb(0x70AD47));
        frame.IsLine.ShouldBeFalse("a rect's outline is its four sides, not its diagonal");
    }

    /// <summary>
    /// A stated <c>strokeweight</c> is honoured and its absence is a hairline.
    /// </summary>
    /// <remarks>
    /// Read off the 300 dpi reference raster rather than off the <c>w</c> operator — <c>068</c>'s
    /// whole reference PDF carries a single <c>0.1 w</c>, which is not the drawn width. A
    /// <c>v:rect</c> border stating no weight comes out one device pixel; the connector stating
    /// <c>strokeweight="1pt"</c> comes out four pixels at 300 dpi, which is 0.96 pt.
    /// </remarks>
    [Theory]
    [InlineData("", 0.1)]
    [InlineData(" strokeweight=\"1pt\"", 1.0)]
    [InlineData(" strokeweight=\"2.25pt\"", 2.25)]
    public void AnUnstatedStrokeWeightIsAHairline(string weight, double points)
    {
        PageFrame frame = One(
            "<v:rect style=\"position:absolute;margin-left:0;margin-top:0;width:100pt;height:20pt\""
            + $" strokecolor=\"black [3213]\"{weight}/>");

        frame.BorderColour.ShouldBe(Colour.FromRgb(0x000000));
        frame.BorderWidth.Points.ShouldBe(points, 0.001);
    }

    /// <summary>
    /// A straight connector states one extent as zero and is still drawn, as its box's diagonal.
    /// </summary>
    /// <remarks>
    /// <c>width:0;height:12.75pt</c> is how VML writes a vertical rule, and there are 87 of them in
    /// the words corpus. Both the group walk and the floating arm used to reject any shape with no
    /// area, so every one of them was dropped before it could be painted. <c>flip:x</c> chooses the
    /// other diagonal — the preset's own path runs top-left to bottom-right.
    /// </remarks>
    [Fact]
    public void AZeroWidthConnectorIsDrawnAsADiagonal()
    {
        PageFrame frame = One(
            "<v:shape type=\"#_x0000_t32\" o:connectortype=\"straight\""
            + " style=\"position:absolute;margin-left:26pt;margin-top:38pt;width:0;height:12.75pt\""
            + " strokecolor=\"#70ad47 [3209]\" strokeweight=\"1pt\"/>");

        frame.IsLine.ShouldBeTrue();
        frame.IsLineMirrored.ShouldBeFalse();
        frame.BorderColour.ShouldBe(Colour.FromRgb(0x70AD47));
        frame.Size.Width.Points.ShouldBe(0, 0.001);
        frame.Size.Height.Points.ShouldBe(12.75, 0.01);
        frame.Fill.ShouldBeNull("a connector has no area to fill");
    }

    /// <summary><c>flip:x</c> takes the other diagonal.</summary>
    [Fact]
    public void AFlippedConnectorTakesTheOtherDiagonal()
    {
        One("<v:shape type=\"#_x0000_t32\" o:connectortype=\"straight\""
            + " style=\"position:absolute;margin-left:0;margin-top:0;width:100pt;height:0.05pt;flip:x\""
            + " strokecolor=\"red\"/>")
            .IsLineMirrored.ShouldBeTrue();
    }

    /// <summary>A named preset colour resolves; an unrecognised name draws nothing.</summary>
    /// <remarks>
    /// <c>black</c>, <c>white</c> and <c>red</c> are the three names this corpus uses — 138, 55 and
    /// 78 times. An unknown name yields nothing rather than black, because inventing ink is the
    /// failure that cannot be seen.
    /// </remarks>
    [Theory]
    [InlineData("black", 0x000000u)]
    [InlineData("white [3212]", 0xFFFFFFu)]
    [InlineData("red", 0xFF0000u)]
    [InlineData("#abc", 0xAABBCCu)]
    public void APresetNameResolves(string stated, uint expected)
        => One($"<v:rect style=\"position:absolute;margin-left:0;margin-top:0;width:10pt;height:10pt\""
               + $" fillcolor=\"{stated}\"/>")
            .Fill.ShouldBe(Colour.FromRgb(expected));

    /// <summary>
    /// Nothing is defaulted, and nothing outside a rectangle or a connector is painted at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the test that decides the change's reach, and each row of it is a measurement.
    /// LibreOffice gives an unstated VML fill white and an unstated stroke black
    /// (<c>FillModel::pushToPropMap</c>, <c>StrokeModel::pushToPropMap</c>). Reproducing that would
    /// put a white fill and a black box around all <b>37</b> <c>#_x0000_t75</c> picture shapes in
    /// the words corpus, not one of which states either — so a stated colour is required.
    /// </para>
    /// <para>
    /// A <c>#_x0000_t136</c> WordArt states a <c>fillcolor</c> that fills glyph outlines rather
    /// than a rectangle (15 shapes, 4 documents), and a <c>#_x0000_t15</c> states one for a
    /// pentagon (3 shapes). Filling their boxes would be a confident wrong answer; they keep
    /// drawing nothing, which is under-drawing rather than a regression.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("<v:rect style=\"position:absolute;margin-left:0;margin-top:0;width:10pt;height:10pt\"/>")]
    [InlineData("<v:rect style=\"position:absolute;margin-left:0;margin-top:0;width:10pt;height:10pt\""
                + " fillcolor=\"#ff0000\" filled=\"f\" strokecolor=\"black\" stroked=\"f\"/>")]
    [InlineData("<v:shape type=\"#_x0000_t136\" style=\"position:absolute;margin-left:0;margin-top:0;"
                + "width:10pt;height:10pt\" fillcolor=\"#ff0000\"/>")]
    [InlineData("<v:shape type=\"#_x0000_t75\" style=\"position:absolute;margin-left:0;margin-top:0;"
                + "width:10pt;height:10pt\"/>")]
    public void NoPaintIsInvented(string markup)
    {
        PageFrame frame = One(markup);

        frame.Fill.ShouldBeNull();
        frame.BorderColour.ShouldBeNull();
        frame.BorderWidth.Points.ShouldBe(0, 0.001);
    }

    /// <summary>A group's member is painted from its own attributes, in the group's space.</summary>
    /// <remarks>
    /// 35 of <c>068</c>'s 41 boxes and 5 of its 12 connectors are inside a nested <c>v:group</c>,
    /// so a rule applied only to the top level would have reached a seventh of the page.
    /// </remarks>
    [Fact]
    public void AGroupMemberIsPaintedToo()
    {
        List<PageFrame> frames = DocxVmlFrames.ReadAll(
            Pict("<v:group style=\"position:absolute;margin-left:0;margin-top:0;"
                 + "width:100pt;height:100pt\" coordorigin=\"0,0\" coordsize=\"1000,1000\">"
                 + "<v:rect style=\"position:absolute;left:0;top:0;width:500;height:500\""
                 + " fillcolor=\"#e2efd9 [665]\" strokecolor=\"#70ad47 [3209]\"/>"
                 + "<v:shape type=\"#_x0000_t32\" o:connectortype=\"straight\""
                 + " style=\"position:absolute;left:500;top:0;width:0;height:1000\""
                 + " strokecolor=\"#70ad47 [3209]\" strokeweight=\"1pt\"/>"
                 + "</v:group>"),
            0,
            null,
            _ => []);

        frames.Count.ShouldBe(2, "the zero-width connector must survive the group walk");

        frames[0].Fill.ShouldBe(Colour.FromRgb(0xE2EFD9));
        frames[0].BorderColour.ShouldBe(Colour.FromRgb(0x70AD47));
        frames[0].Size.Width.Points.ShouldBe(50, 0.01);

        frames[1].IsLine.ShouldBeTrue();
        frames[1].BorderWidth.Points.ShouldBe(1.0, 0.001);
        frames[1].Size.Width.Points.ShouldBe(0, 0.001);
        frames[1].Size.Height.Points.ShouldBe(100, 0.01);
    }
}
