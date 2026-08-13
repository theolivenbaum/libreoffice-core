using Paperless.Core.Documents;
using Paperless.Core.Geometry;
using Paperless.TestKit;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A cropped picture in a <c>.doc</c>: the four Escher properties, the larger rectangle, and the
/// clip that makes it read as a crop.
/// </summary>
/// <remarks>
/// <para>
/// <b>This closes "Escher picture cropping, implemented nowhere in the word path".</b> The
/// arithmetic has been in <c>Paperless.Core.Geometry.PictureCrop</c> and the property read in
/// <c>Paperless.MsBinary.Escher.EscherPicture</c> since the slide round; what was missing was the
/// plumbing, and the plumbing is not one call. A frame has no rectangle until <c>FrameLayout</c>
/// has placed it, so the fractions travel on <see cref="FramePicture.Crop"/> and
/// <see cref="PageFrame.Crop"/> and <c>PageDrawing</c> does the arithmetic where
/// <c>PlacedFrame.Area</c> exists.
/// </para>
/// <para>
/// <b>The clip is new and is half the change.</b> <c>PageDrawing.DrawFrame</c> called
/// <c>DrawImage(image, frame.Area)</c> bare. Drawing into a larger rectangle without a clip does
/// not crop a picture — it puts the whole of it over the text on every side — so a round that
/// shipped only the rectangle would be a regression rather than a fix.
/// </para>
/// <para>
/// <b>There are three fixtures because a round trip cannot produce the file the corpus is made
/// of.</b> <c>picture-crop.docx</c> states <c>a:srcRect l="10000" t="20000" r="30000" b="40000"</c>
/// on an inline picture 288 × 216 pt; <c>picture-crop.doc</c> is that file through
/// <c>soffice --convert-to doc</c>, which is what turns the <c>a:srcRect</c> into Escher's 256–259
/// and puts the shape in the <c>Data</c> stream where an inline picture lives; and
/// <c>picture-crop-goal.doc</c> is that file patched into the shape Word writes. See the theory
/// below for why the third is not redundant.
/// </para>
/// <para>
/// The <c>.docx</c> half is <b>not</b> cropped by this round — <c>DocxFrames</c> still drops
/// <c>a:srcRect</c> — and is asserted in that state, so it reads as the uncropped control the
/// other three tests measure against and so the fix shows up as a move off this answer.
/// </para>
/// </remarks>
public sealed class FramePictureCropTests
{
    /// <summary>
    /// Both shapes a <c>.doc</c> can state the same crop in, held to the same rectangle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The two differ in what <c>dxaGoal</c> means, and that is the whole trap.</b>
    /// <c>picture-crop.doc</c> is LibreOffice's own export: it states the crop twice, in the
    /// PICF's <c>dxaCropLeft</c> and siblings <em>and</em> in Escher 256–259, and sizes the goal
    /// to the whole 480 × 540 pt picture. <c>picture-crop-goal.doc</c> is that file patched into
    /// the shape Word writes and the corpus is made of — no PICF crop at all and a goal that is
    /// already the visible 288 × 216 pt.
    /// </para>
    /// <para>
    /// One formula covers both: the frame is the goal <em>less the PICF crop</em>, and the
    /// destination is that grown by the Escher fractions. An implementation that insets the goal
    /// by the Escher fractions instead passes on the exported file and is wrong on every real
    /// one — which is exactly what this round shipped first, and what the second fixture exists
    /// to make impossible.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("picture-crop.doc")]
    [InlineData("picture-crop-goal.doc")]
    public void ACroppedPictureInADocIsDrawnLargerThanItsFrame(string name)
    {
        DocRect destination = OnlyImageOf(name);

        // 288 / (1 - 0.1 - 0.3) = 480 pt across and 216 / (1 - 0.2 - 0.4) = 540 pt down.
        destination.Width.Points.ShouldBe(480, 1.0, name);
        destination.Height.Points.ShouldBe(540, 1.0, name);
    }

    /// <summary>
    /// The offset follows the crop and not the size: 10% of the picture's own width to the left
    /// of the frame and 20% of its own height above it.
    /// </summary>
    [Theory]
    [InlineData("picture-crop.doc")]
    [InlineData("picture-crop-goal.doc")]
    public void TheWholePictureStartsAboveAndLeftOfTheFrame(string name)
    {
        DocRect frame = OnlyImageOf("picture-crop.docx");
        DocRect destination = OnlyImageOf(name);

        (frame.X - destination.X).Points.ShouldBe(48, 1.5, name);
        (frame.Y - destination.Y).Points.ShouldBe(108, 1.5, name);
    }

    /// <summary>
    /// The uncropped half of the pair is drawn at exactly its frame, which is what makes the
    /// offsets above differences rather than absolutes.
    /// </summary>
    [Fact]
    public void TheUncroppedHalfIsDrawnAtItsFrame()
    {
        DocRect frame = OnlyImageOf("picture-crop.docx");

        frame.Width.Points.ShouldBe(288, 1.0);
        frame.Height.Points.ShouldBe(216, 1.0);
    }

    [Theory]
    [InlineData("picture-crop.doc")]
    [InlineData("picture-crop-goal.doc")]
    public void ACroppedPictureIsClipped(string name) => ClipsOf(name).ShouldBeGreaterThan(0);

    /// <summary>
    /// And an uncropped one is not. Clipping unconditionally would be invisible on the page and
    /// would change the bytes of every rendering in the corpus that carries a picture, which is
    /// exactly the kind of reach a round cannot then attribute to anything.
    /// </summary>
    [Fact]
    public void AnUncroppedPictureIsNotClipped() => ClipsOf("picture-crop.docx").ShouldBe(0);

    // ------------------------------------------------------------------ helpers

    /// <summary>Where the fixture's one picture is drawn.</summary>
    private static DocRect OnlyImageOf(string name)
    {
        RecordingDrawingSink sink = Draw(name);
        return sink.Pages.SelectMany(page => page.Images).ShouldHaveSingleItem();
    }

    /// <summary>How many clips the fixture's rendering takes.</summary>
    private static int ClipsOf(string name) => Draw(name).Clips;

    private static RecordingDrawingSink Draw(string name)
    {
        RecordingDrawingSink sink = new();

        using (DocumentSource source = DocumentSource.FromFile(Corpus.Require(name)))
        {
            using IDocument document = new WordProcessingReader().Read(source);

            IPageSequence pages = ((IPaginatedDocument)document).Layout();
            for (int i = 0; i < pages.Count; i++) pages[i].Draw(sink);
        }

        return sink;
    }
}
