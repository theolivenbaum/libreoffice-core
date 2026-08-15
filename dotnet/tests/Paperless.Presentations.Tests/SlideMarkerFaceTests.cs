using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.Presentations.Layout;
using Paperless.Presentations.Ooxml;
using Shouldly;
using System.Xml.Linq;

namespace Paperless.Presentations.Tests;

/// <summary>
/// Checks which face a paragraph's marker is drawn from — which LibreOffice decides separately
/// from the paragraph's own text face.
/// </summary>
/// <remarks>
/// <para>
/// <c>Outliner::ImpCalcBulletFont</c> takes the numbering format's own bullet font for a
/// <c>SVX_NUM_CHAR_SPECIAL</c> marker and only falls back to the paragraph's font when there is
/// none (<c>editeng/source/outliner/outliner.cxx:828-847</c>). For Impress there always is one,
/// because the rule is built over <c>SdStyleSheetPool::GetBulletFont</c> — OpenSymbol at normal
/// weight (<c>sd/source/core/stlpool.cxx:1169-1183</c>). A generated number is not
/// <c>CHAR_SPECIAL</c>, so it does take the fallback and keeps the text's own font.
/// </para>
/// <para>
/// Measured through <c>soffice</c> 26.2.4.2 on a probe deck, read out of the PDF's text
/// operators rather than from a raster: <c>a:buChar</c> with no <c>a:buFont</c> over Courier New
/// draws from OpenSymbol, the same over Times New Roman draws from OpenSymbol,
/// <c>a:buFontTx</c> draws from OpenSymbol, <c>a:buFont typeface="Arial"</c> draws from
/// Liberation Sans, and <c>a:buAutoNum</c> draws its number from Liberation Mono — the text's
/// own face. On the same probe a bold paragraph draws its text from Liberation Sans Bold and its
/// bullet from Liberation Sans.
/// </para>
/// <para>
/// The corpus effect: on <c>Course Selection 2025-26 Current Grade 09.pptx</c> the reference
/// embeds OpenSymbol and we embedded none, drawing the deck's three SmartArt bullets from DejaVu
/// Sans instead — and 19 of the 34 slides documents whose embedded font set disagreed with the
/// reference were this.
/// </para>
/// </remarks>
public class SlideMarkerFaceTests
{
    private static readonly XNamespace A =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static DocRect Area =>
        new(Length.Zero, Length.Zero, Length.FromPoints(400), Length.FromPoints(200));

    /// <summary>A one-paragraph body, with the caller's list style and paragraph properties.</summary>
    private static SlideTextBody Body(
        string paragraphProperties,
        string runAttributes = "",
        string? runFace = null,
        string listStyle = "<a:lstStyle/>")
    {
        string latin = runFace is null ? string.Empty : $"<a:latin typeface={Quoted(runFace)}/>";

        return PptxTextBody.Read(XElement.Parse(
            $"""
             <a:txBody xmlns:a="{A}">
               <a:bodyPr/>
               {listStyle}
               <a:p>
                 <a:pPr marL="342900" indent="-342900">{paragraphProperties}</a:pPr>
                 <a:r>
                   <a:rPr lang="en-GB" sz="2400" {runAttributes}>{latin}</a:rPr>
                   <a:t>Labelled</a:t>
                 </a:r>
               </a:p>
             </a:txBody>
             """));
    }

    private static string Quoted(string value) => "\"" + value + "\"";

    /// <summary>The two runs a labelled paragraph draws: its marker, then its text.</summary>
    private static (GlyphRun Marker, GlyphRun Text) Runs(SlideTextBody body)
    {
        List<PlacedGlyphRun> placed = SlideTextLayout.Place(body, Area, new SlideFonts());
        placed.Count.ShouldBe(2);

        return placed[0].Run.Origin.X <= placed[1].Run.Origin.X
            ? (placed[0].Run, placed[1].Run)
            : (placed[1].Run, placed[0].Run);
    }

    [Fact]
    public void ACharacterBulletWithNoStatedFaceIsDrawnFromOpenSymbol()
    {
        (GlyphRun marker, GlyphRun text) =
            Runs(Body("""<a:buChar char="&#8226;"/>""", runFace: "Courier New"));

        marker.Font.FamilyName.ShouldBe("OpenSymbol");
        text.Font.FamilyName.ShouldNotBe("OpenSymbol");
    }

    /// <summary>
    /// <c>a:buFontTx</c> reads as "follow the text" and in fact means "state no face", so the
    /// bullet keeps the default rather than taking either the text's face or an inherited one.
    /// </summary>
    [Fact]
    public void ABuFontTxDiscardsAnInheritedBulletFace()
    {
        SlideTextBody body = Body(
            """<a:buFontTx/><a:buChar char="&#8226;"/>""",
            runFace: "Times New Roman",
            listStyle:
            """
            <a:lstStyle>
              <a:lvl1pPr><a:buFont typeface="Courier New" pitchFamily="49" charset="0"/></a:lvl1pPr>
            </a:lstStyle>
            """);

        Runs(body).Marker.Font.FamilyName.ShouldBe("OpenSymbol");
    }

    /// <summary>A stated face is still what the bullet is set in — nothing here overrides one.</summary>
    [Fact]
    public void AStatedBulletFaceIsDrawnFromThatFaceRatherThanOpenSymbol()
    {
        (GlyphRun marker, GlyphRun text) = Runs(Body(
            """<a:buFont typeface="Courier New" pitchFamily="49" charset="0"/><a:buChar char="&#8226;"/>""",
            runFace: "Times New Roman"));

        marker.Font.FamilyName.ShouldNotBe("OpenSymbol");
        marker.Font.FamilyName.ShouldNotBe(text.Font.FamilyName);
    }

    /// <summary>
    /// Both producers of a bullet font are normal and upright — the oox import writes
    /// <c>FontWeight::NORMAL</c> into the descriptor it pushes and
    /// <c>SdStyleSheetPool::GetBulletFont</c> sets <c>WEIGHT_NORMAL</c>. So a bold, italic
    /// paragraph's bullet is neither.
    /// </summary>
    [Fact]
    public void ACharacterBulletIsNeitherBoldNorItalicWhenItsTextIsBoth()
    {
        (GlyphRun marker, GlyphRun text) = Runs(Body(
            """<a:buFont typeface="Arial" pitchFamily="34" charset="0"/><a:buChar char="&#8226;"/>""",
            runAttributes: """b="1" i="1" """,
            runFace: "Arial"));

        text.Font.Weight.ShouldBeGreaterThan(400);
        text.Font.IsItalic.ShouldBeTrue();

        marker.Font.Weight.ShouldBe(400);
        marker.Font.IsItalic.ShouldBeFalse();
    }

    /// <summary>
    /// A generated number is not <c>SVX_NUM_CHAR_SPECIAL</c>, so it takes the fallback branch and
    /// is drawn in the paragraph's own font — weight and slope included.
    /// </summary>
    [Fact]
    public void AnAutoNumberKeepsTheParagraphsOwnFace()
    {
        (GlyphRun marker, GlyphRun text) = Runs(Body(
            """<a:buAutoNum type="arabicPeriod"/>""",
            runAttributes: """b="1" """,
            runFace: "Arial"));

        marker.Text.ShouldBe("1.");
        marker.Font.FamilyName.ShouldBe(text.Font.FamilyName);
        marker.Font.Weight.ShouldBe(text.Font.Weight);
    }
}
