using Paperless.Core.Documents;
using Paperless.Core.Geometry;
using Paperless.Spreadsheets.Layout;
using Paperless.TestKit;
using Shouldly;

namespace Paperless.Spreadsheets.Tests;

/// <summary>
/// A cropped picture on a sheet: the four Escher properties, the larger rectangle, and the clip
/// that makes it read as a crop rather than as a picture spilled across the grid.
/// </summary>
/// <remarks>
/// <para>
/// <b>The crop cannot be applied where it is read, and that is the whole shape of this.</b> A
/// sheet's drawing is anchored to cells, so its rectangle does not exist until the page's column
/// widths and row heights are resolved — <c>XlsDrawing</c> reads the fractions,
/// <see cref="SheetDrawing.Crop"/> carries them, and <c>SheetPageGraphics</c> grows the rectangle
/// and takes the clip.
/// </para>
/// <para>
/// <b>The clip is new here.</b> Nothing on this path clipped a picture before: <c>DrawImage</c>
/// was called with the anchor's box and no <c>Save</c>/<c>ClipPath</c>/<c>Restore</c> around it.
/// A larger destination without that clip does not crop a picture, it draws the whole of it over
/// the cells on every side — so the two halves are one change and are tested together.
/// </para>
/// <para>
/// <b>The fixture is round-tripped through LibreOffice on purpose.</b> <c>picture-crop.xlsx</c>
/// states <c>a:srcRect l="10000" t="20000" r="30000" b="40000"</c> on a picture 288 × 216 pt;
/// <c>picture-crop.xls</c> is that file through <c>soffice --convert-to xls</c>, which is what
/// turns the <c>a:srcRect</c> into Escher's 256–259. The image is 100 × 100 pixels and the size
/// is load-bearing: BIFF export states the crop in the <em>bitmap's</em> pixels, so an 8-pixel
/// image brings a 10% crop back as 0.0990.
/// </para>
/// <para>
/// The <c>.xlsx</c> half of the pair is <b>not</b> cropped by this round — the SpreadsheetML
/// reader still drops <c>a:srcRect</c> — and the test below says so rather than leaving it
/// untested, because the pair existing is what will catch the two paths disagreeing when it is
/// wired.
/// </para>
/// </remarks>
public sealed class SheetPictureCropTests
{
    /// <summary>The picture's own frame: 288 × 216 pt, stated by both halves of the pair.</summary>
    private const double FrameWidthPoints = 288;
    private const double FrameHeightPoints = 216;

    // ------------------------------------------------------------------ the property read

    [Fact]
    public void TheFourEscherPropertiesReachTheModel()
    {
        SheetDrawing drawing = Only("picture-crop.xls");

        drawing.Crop.IsNone.ShouldBeFalse("a crop was read");
        drawing.Crop.Left.ShouldBe(0.10, 0.001);
        drawing.Crop.Top.ShouldBe(0.20, 0.001);
        drawing.Crop.Right.ShouldBe(0.30, 0.001);
        drawing.Crop.Bottom.ShouldBe(0.40, 0.001);
    }

    /// <summary>
    /// The SpreadsheetML half states the same crop as <c>a:srcRect</c> and it is not read. Pinned
    /// as the state it is in, so the fix shows up as a move off this answer.
    /// </summary>
    [Fact]
    public void TheSameCropOnTheXlsxPathIsNotReadYet()
        => Only("picture-crop.xlsx").Crop.IsNone.ShouldBeTrue();

    // ------------------------------------------------------------------ the rectangle drawn

    [Fact]
    public void ACroppedPictureIsDrawnLargerThanItsAnchor()
    {
        DocRect destination = OnlyImageOf("picture-crop.xls");

        // 288 / (1 - 0.1 - 0.3) = 480 pt across and 216 / (1 - 0.2 - 0.4) = 540 pt down. The
        // figures asserted are LibreOffice 26.2.4.2's own: it draws the whole picture at
        // 479.565 x 539.405 pt, its distance from the nominal 480 x 540 being the row heights the
        // one-cell anchor's extent is resolved through on both sides.
        destination.Width.Points.ShouldBe(479.57, 1.0);
        destination.Height.Points.ShouldBe(539.41, 1.0);
    }

    /// <summary>
    /// The offset follows the crop rather than the size: the whole picture starts 10% of its own
    /// width to the left of the anchor and 20% of its own height above it.
    /// </summary>
    [Fact]
    public void TheWholePictureStartsAboveAndLeftOfTheAnchor()
    {
        DocRect anchor = OnlyImageOf("picture-crop.xlsx");
        DocRect destination = OnlyImageOf("picture-crop.xls");

        (anchor.Left - destination.Left).Points.ShouldBe(48, 1.5);
        (anchor.Top - destination.Top).Points.ShouldBe(108, 1.5);
    }

    /// <summary>
    /// The uncropped half is drawn at exactly its anchor, which is what makes the figures above
    /// differences rather than absolutes and is why the pair is read on both paths.
    /// </summary>
    [Fact]
    public void TheUncroppedHalfIsDrawnAtItsAnchor()
    {
        DocRect anchor = OnlyImageOf("picture-crop.xlsx");

        anchor.Width.Points.ShouldBe(FrameWidthPoints, 1.0);
        anchor.Height.Points.ShouldBe(FrameHeightPoints, 1.0);
    }

    // ------------------------------------------------------------------ the clip

    /// <summary>
    /// The clip is what turns a larger rectangle into a crop. Without it this change would be
    /// strictly worse than doing nothing: the picture would be drawn 480 × 540 pt over whatever
    /// the sheet has on all four sides of it.
    /// </summary>
    [Fact]
    public void ACroppedPictureIsClipped()
        => ClipsOf("picture-crop.xls").ShouldBeGreaterThan(0);

    /// <summary>
    /// And an uncropped one is not, which is the half that keeps this round's reach honest: an
    /// unconditional clip would put a <c>q</c>/<c>W n</c>/<c>Q</c> into every rendering carrying a
    /// picture and change all of them for nothing.
    /// </summary>
    [Fact]
    public void AnUncroppedPictureIsNotClipped()
        => ClipsOf("picture-crop.xlsx").ShouldBe(0);

    // ------------------------------------------------------------------ helpers

    /// <summary>The single drawing on the fixture's single sheet.</summary>
    private static SheetDrawing Only(string name)
    {
        using IPaginatedDocument document =
            (IPaginatedDocument)PaperlessDocument.Open(Corpus.Require(name));

        return ((SpreadsheetPages)document.Layout()).Sheets[0].Drawings.Items.ShouldHaveSingleItem();
    }

    /// <summary>Where the fixture's one picture is actually drawn.</summary>
    private static DocRect OnlyImageOf(string name) => Draw(name).Images.ShouldHaveSingleItem();

    /// <summary>How many clips the fixture's rendering takes.</summary>
    private static int ClipsOf(string name)
    {
        using IPaginatedDocument document =
            (IPaginatedDocument)PaperlessDocument.Open(Corpus.Require(name));

        RecordingDrawingSink sink = new();
        foreach (SheetPage page in ((SpreadsheetPages)document.Layout()).Pages) page.Draw(sink);
        return sink.Clips;
    }

    /// <summary>The one page the fixture renders to.</summary>
    private static DrawnPage Draw(string name)
    {
        using IPaginatedDocument document =
            (IPaginatedDocument)PaperlessDocument.Open(Corpus.Require(name));

        RecordingDrawingSink sink = new();
        foreach (SheetPage page in ((SpreadsheetPages)document.Layout()).Pages) page.Draw(sink);

        return sink.Pages.Single(page => page.Images.Count > 0);
    }
}
