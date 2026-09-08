using Paperless.Core.Graphics;
using Paperless.Core.Units;

namespace Paperless.MsBinary.Escher;

/// <summary>
/// A shape's fill and outline, as the Escher property table states them.
/// </summary>
/// <remarks>
/// <para>
/// The four properties are <c>fillColor</c> (385), <c>fFilled</c> (443), <c>lineColor</c> (448)
/// and <c>lineWidth</c> (459), with <c>fLine</c> (508) deciding whether there is an outline at
/// all. Nothing in this tree read any of them until round 84 — a worksheet shape's fill was
/// unread in all three of its formats — and the <c>.xls</c> half of that reaches this file.
/// </para>
/// <para>
/// <strong>A colour is only taken when the shape states it, although the format gives both a
/// default.</strong> MS-ODRAW defaults <c>fillColor</c> to white and <c>lineColor</c> to black, so
/// reading the defaults would put a white box under the text of every shape that mentions neither
/// — which is a confident answer about documents this project has not measured. Presence is the
/// test, exactly as it is for <see cref="EscherPicture.TransparentColour"/>, and the two booleans
/// are honoured wherever they are stated: a shape saying <c>fFilled</c> false has no fill even if
/// it names a colour.
/// </para>
/// <para>
/// <strong>Only the literal <c>MSO_CLR</c> form is honoured.</strong> The top byte decides whether
/// the other three are a literal <c>0x00BBGGRR</c> or an index into a scheme this layer cannot
/// see; a non-literal value yields nothing rather than a wrong colour, which is the same choice
/// the picture knockout makes and for the same reason.
/// </para>
/// </remarks>
public static class EscherInk
{
    /// <summary>The width the format gives a line that states none: one point, in EMUs.</summary>
    private const uint DefaultLineWidth = 9525;

    /// <summary>A shape's resolved interior and outline.</summary>
    /// <param name="Fill">The interior colour, or null when the shape is not filled.</param>
    /// <param name="Stroke">The outline colour, or null when it has none.</param>
    /// <param name="StrokeWidth">How wide that outline is.</param>
    public readonly record struct Ink(Colour? Fill, Colour? Stroke, Length StrokeWidth)
    {
        /// <summary>True when there is something to paint.</summary>
        public bool HasInk => Fill is not null || Stroke is not null;
    }

    /// <summary>Reads a shape's fill and outline.</summary>
    /// <param name="properties">The shape's property table.</param>
    public static Ink Read(EscherPropertyTable properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        Colour? fill = properties.Boolean(EscherPropertyIds.Filled, fallback: true)
                       && properties.Has(EscherPropertyIds.FillColour)
            ? Literal(properties.Value(EscherPropertyIds.FillColour))
            : null;

        bool lined = properties.Boolean(EscherPropertyIds.Lined, fallback: true)
                     && properties.Has(EscherPropertyIds.LineColour);

        Colour? stroke = lined ? Literal(properties.Value(EscherPropertyIds.LineColour)) : null;

        Length width = stroke is null
            ? Length.Zero
            : Length.FromEmu(properties.Value(EscherPropertyIds.LineWidth, DefaultLineWidth));

        return new Ink(fill, stroke, width);
    }

    /// <summary>An <c>MSO_CLR</c>'s literal <c>0x00BBGGRR</c>, or null when it is a reference.</summary>
    private static Colour? Literal(uint value)
        => (value & 0xFF000000u) != 0
            ? null
            : Colour.FromRgb(
                ((value & 0x000000FFu) << 16) | (value & 0x0000FF00u) | ((value & 0x00FF0000u) >> 16));
}
