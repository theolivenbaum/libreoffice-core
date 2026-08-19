using System.Xml.Linq;
using Paperless.Core;
using Paperless.Core.Documents;
using Paperless.Presentations.Layout;
using Paperless.Presentations.Ooxml;
using Paperless.TestKit;
using Shouldly;

namespace Paperless.Presentations.Tests;

/// <summary>
/// WordArt — <c>a:bodyPr/a:prstTxWarp</c> — is drawn as a picture of words rather than as words.
/// </summary>
/// <remarks>
/// <para>
/// LibreOffice reads a <c>prstTxWarp</c> whose <c>@prst</c> is anything other than
/// <c>textNoShape</c> and puts the shape into text-path mode
/// (<c>oox/source/drawingml/textbodypropertiescontext.cxx:215-226</c>,
/// <c>oox/source/drawingml/shape.cxx:2202-2211</c>); Fontwork then converts the characters to
/// <c>tools::PolyPolygon</c> outlines
/// (<c>svx/source/customshapes/EnhancedCustomShapeFontWork.cxx</c>), so the glyphs never reach
/// the PDF at all — the ink is filled paths carrying no <c>ToUnicode</c>.
/// </para>
/// <para>
/// Measured on the installed 26.2.4.2 with <c>text-warp-deck.pptx</c>, which is authored for
/// this and holds the same three words three times: plain, <c>textNoShape</c>, and
/// <c>textArchUp</c>. The reference's PDF contains the phrase <strong>twice</strong> and
/// answers the third box with 3 fill operators over 187 curves. Paperless drew it three times,
/// and on <c>FAAAIandtheArtandScienceofV&amp;Vfinal.pptx</c> that was the whole of a 1189-against
/// -1133 word failure: 28 words a page on two pages, five labels of 48 characters, tokenised
/// per glyph because each warped box is also rotated.
/// </para>
/// <para>
/// <c>textNoShape</c> having its own case is not pedantry. Of the 163 decks in the corpus's
/// slides track, 67 carry a <c>prstTxWarp</c> and 65 of those carry only <c>textNoShape</c> —
/// so a reader that keyed off the element's presence would silence the text of 65 documents
/// that LibreOffice draws as ordinary text.
/// </para>
/// </remarks>
public class SlideTextWarpTests
{
    private const string Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Theory]
    [InlineData("textArchUp")]
    [InlineData("textArchDown")]
    [InlineData("textPlain")]
    [InlineData("textCirclePour")]
    public void AStatedWarpMakesTheBodyATextPath(string preset)
    {
        SlideTextBody body = PptxTextBody.Read(Body(preset));

        body.WarpPreset.ShouldBe(preset);
        body.IsTextPath.ShouldBeTrue();
    }

    /// <summary>
    /// <c>textNoShape</c> is the value that means no warp, and it is the common one.
    /// </summary>
    [Fact]
    public void TextNoShapeIsNotATextPath()
    {
        SlideTextBody body = PptxTextBody.Read(Body("textNoShape"));

        body.WarpPreset.ShouldBeNull();
        body.IsTextPath.ShouldBeFalse();
    }

    [Fact]
    public void ABodyStatingNoWarpIsNotATextPath()
    {
        SlideTextBody body = PptxTextBody.Read(Body(null));

        body.WarpPreset.ShouldBeNull();
        body.IsTextPath.ShouldBeFalse();
    }

    /// <summary>
    /// The warp is inherited from a placeholder's <c>a:bodyPr</c> like every other body property.
    /// </summary>
    /// <remarks>
    /// <c>PPTShapeContext</c> copy-constructs the slide shape's text body from the one
    /// <c>applyShapeReference</c> brought over from the layout or master
    /// (<c>oox/source/ppt/pptshapecontext.cxx:183-186</c>), so a slide's own empty
    /// <c>&lt;a:bodyPr/&gt;</c> overrides nothing.
    /// </remarks>
    [Fact]
    public void AWarpOnAPlaceholderBehindTheShapeIsInherited()
    {
        XElement inherited = new(XName.Get("bodyPr", Drawing), Warp("textArchUp"));

        SlideTextBody body = PptxTextBody.Read(
            Body(null), inheritedBodyProperties: [inherited]);

        body.IsTextPath.ShouldBeTrue();
    }

    /// <summary>The shape's own body properties beat an inherited warp, as they do an anchor.</summary>
    [Fact]
    public void TheNearestStatedWarpWins()
    {
        XElement inherited = new(XName.Get("bodyPr", Drawing), Warp("textArchUp"));

        SlideTextBody body = PptxTextBody.Read(
            Body("textNoShape"), inheritedBodyProperties: [inherited]);

        body.IsTextPath.ShouldBeFalse();
    }

    /// <summary>
    /// End to end: the warped box draws no glyph run, and its two neighbours still do.
    /// </summary>
    /// <remarks>
    /// The three boxes are identical but for the <c>prstTxWarp</c>, so this pins the difference
    /// on the warp and nothing else — and the count is the reference's own: two of three.
    /// </remarks>
    [Fact]
    public void OnlyTheWarpedBoxDrawsNoText()
    {
        LaidOutSlide slide = FixtureSlide();

        Text(slide, "PlainBox").ShouldBe("Fontwork keeps three");
        Text(slide, "NoShapeBox").ShouldBe("Fontwork keeps three");
        Text(slide, "WarpedBox").ShouldBeNull();
    }

    /// <summary>
    /// The words stay in the content tree: extraction is not rendering.
    /// </summary>
    /// <remarks>
    /// LibreOffice keeps them too — it draws a picture of them, it does not forget them — and a
    /// caller indexing a deck wants the WordArt banner as much as any other text on the slide.
    /// This is the guard against fixing a word-count failure by deleting words.
    /// </remarks>
    [Fact]
    public void TheWarpedTextIsStillExtracted()
    {
        using IDocument document = new PresentationReader().Read(
            DocumentSource.FromFile(Corpus.Require("text-warp-deck.pptx")));

        string text = document.Content.GetText();

        text.Split("Fontwork keeps three").Length.ShouldBe(4);
    }

    private static LaidOutSlide FixtureSlide()
    {
        using IDocument document = new PresentationReader().Read(
            DocumentSource.FromFile(Corpus.Require("text-warp-deck.pptx")));

        return ((SlidePages)((IPaginatedDocument)document).Layout()).Slides[0];
    }

    private static string? Text(LaidOutSlide slide, string name)
    {
        PlacedShape shape = slide.Shapes.First(candidate => candidate.Name == name);
        return shape.Text is { Runs.Count: > 0 } text
            ? string.Concat(text.Runs.Select(run => run.Run.Text))
            : null;
    }

    private static XElement Warp(string preset) => new(
        XName.Get("prstTxWarp", Drawing),
        new XAttribute("prst", preset),
        new XElement(XName.Get("avLst", Drawing)));

    private static XElement Body(string? preset)
    {
        XElement properties = new(XName.Get("bodyPr", Drawing));
        if (preset is not null) properties.Add(Warp(preset));

        return new XElement(
            XName.Get("txBody", Drawing),
            properties,
            new XElement(
                XName.Get("p", Drawing),
                new XElement(
                    XName.Get("r", Drawing),
                    new XElement(
                        XName.Get("rPr", Drawing),
                        new XAttribute("sz", "1800"),
                        new XElement(
                            XName.Get("latin", Drawing),
                            new XAttribute("typeface", "Liberation Sans"))),
                    new XElement(XName.Get("t", Drawing), "Fontwork keeps three"))));
    }
}
