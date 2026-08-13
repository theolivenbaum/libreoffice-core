using Paperless.Core.Geometry;

namespace Paperless.MsBinary.Escher;

/// <summary>
/// The picture properties an Escher shape carries, for the hosts that draw one.
/// </summary>
/// <remarks>
/// <b>Any shape may hold a picture, not only a picture frame.</b> Escher has no separate picture
/// element — a <c>pib</c> is a property like a fill colour — so this is asked of every shape
/// rather than of a type, in all three hosts that delegate their drawings to MS-ODRAW.
/// </remarks>
public static class EscherPicture
{
    /// <summary>
    /// Where the whole picture goes when the shape crops it, or the placed rectangle unchanged
    /// when it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A crop makes the destination larger, and the caller's existing clip makes it look
    /// cropped.</b> The four properties state fractions of the picture thrown away at each edge,
    /// so the surviving part is what fills the shape and the whole of it is correspondingly
    /// bigger — <see cref="PictureCrop.Uncropped"/> does the arithmetic and says why the
    /// <c>+ 1</c> in LibreOffice's own <c>lcl_ApplyCropping</c> does not belong here.
    /// </para>
    /// <para>
    /// LibreOffice reaches the same picture two different ways depending on where the file came
    /// from — <c>lcl_ApplyCropping</c> either stores an <c>SdrGrafCropItem</c>, which is this
    /// larger-destination-plus-clip, or bakes the crop into the bitmap outright
    /// (<c>filter/source/msfilter/msdffimp.cxx:3826-3832</c>, the <c>pSet</c> branch). Both draw
    /// the same pixels in the same place. Anyone comparing destination rectangles against a
    /// reference PDF will find only the first half and should not read the second as a
    /// disagreement.
    /// </para>
    /// <para>
    /// Returns <paramref name="destination"/> unchanged when the crop keeps nothing, which a
    /// file can state: dropping the picture would be a hole where drawing it uncropped is the
    /// right picture in the right place.
    /// </para>
    /// </remarks>
    /// <param name="properties">The shape's property table.</param>
    /// <param name="destination">The rectangle the shape occupies.</param>
    public static DocRect Cropped(EscherPropertyTable properties, DocRect destination)
        => Crop(properties).Apply(destination);

    /// <summary>
    /// The four crop properties as fractions, without a rectangle to apply them to.
    /// </summary>
    /// <remarks>
    /// <b>For the two hosts that cannot do both at once.</b> A slide shape states its own
    /// rectangle, so <see cref="Cropped"/> reads the properties and finishes; a sheet's drawing
    /// is anchored to cells and a Word frame is placed by the layout engine, so on those two
    /// paths the fractions are read here and applied where the rectangle finally exists — see
    /// <see cref="PictureCropFractions"/>.
    /// </remarks>
    /// <param name="properties">The shape's property table.</param>
    public static PictureCropFractions Crop(EscherPropertyTable properties)
        => new(
            Fraction(properties, EscherPropertyIds.CropFromLeft),
            Fraction(properties, EscherPropertyIds.CropFromTop),
            Fraction(properties, EscherPropertyIds.CropFromRight),
            Fraction(properties, EscherPropertyIds.CropFromBottom));

    /// <summary>One crop property as a fraction of the picture, from its 16.16 fixed point.</summary>
    private static double Fraction(EscherPropertyTable properties, ushort id)
        => properties.SignedValue(id) / 65536.0;
}
