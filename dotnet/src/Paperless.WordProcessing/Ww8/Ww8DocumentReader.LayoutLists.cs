using Paperless.Core.Units;
using Paperless.Text.Layout;

namespace Paperless.WordProcessing.Ww8;

/// <content>
/// Turning a paragraph's <c>ilfo</c> and <c>ilvl</c> into the label it draws and the indents it draws it at.
/// </content>
/// <remarks>
/// <para>
/// The same shape as the ODF and OOXML list reads: Writer models a label as a portion at the head of the
/// item's first line followed by a tab, so the label is the paragraph's <em>prefix</em> and the level's
/// indents replace the paragraph's own. The counter itself is <see cref="Ww8Numbering"/>'s work and the
/// extraction pass already asks for it; what this adds is the geometry, which extraction has no use for.
/// </para>
/// <para>
/// What differs from the other two is where the prefix goes in. Both XML readers hand their label to a run
/// walker, which emits it before the paragraph's own text and so keeps every later offset right for free.
/// DOC has no walker to hand it to: <see cref="ReadRuns"/> indexes into text that has already been
/// assembled, and every note anchored in the paragraph is recorded as an offset into that same text. So the
/// label is prepended to the text <em>and</em> to the parallel array of source positions, which is what
/// keeps the runs aligned, and the note offsets are shifted by its length.
/// </para>
/// </remarks>
public sealed partial class Ww8DocumentReader
{
    /// <summary>
    /// The label a paragraph draws and the indents it is laid out with, or null when it is in no list.
    /// </summary>
    /// <remarks>
    /// Advances the counter, so it must be called once per paragraph and in document order — which is why
    /// it is called from where the paragraph closes rather than from wherever a label is next wanted.
    /// </remarks>
    /// <param name="format">The paragraph's resolved properties, which name its list instance and level.</param>
    private Ww8ListLabel? LabelOf(Ww8ParagraphFormat format)
    {
        if (format.ListNumber <= 0) return null;

        int level = Math.Clamp(format.ListLevel ?? 0, 0, Ww8Numbering.LevelCount - 1);

        Ww8ListLevel? definition = _numbering.FindLevel(format.ListNumber, level);
        if (definition is null) return null;

        string? text = _numbering.Advance(format.ListNumber, level);

        // A level whose nfc is `none` counts and draws nothing, and still indents — that is how a level
        // contributes to the hierarchy without appearing. So no text is not the same as no list.
        return new Ww8ListLabel(
            text ?? string.Empty, definition.Value.Separator, GeometryOf(definition.Value));
    }

    /// <summary>
    /// A level's own indents, from the <c>grpprlPapx</c> its <c>LVL</c> carries.
    /// </summary>
    /// <remarks>
    /// Read by the layout pass's own sprm walk rather than by a second decoder, because the PAPX in an
    /// <c>LVL</c> is an ordinary grpprl and <c>sprmPDxaLeft</c> and <c>sprmPDxaLeft1</c> are the same sprms
    /// a paragraph states its indents with. The rest of what the walk understands is simply not present
    /// there, so nothing has to be masked off.
    /// </remarks>
    private static Ww8ListGeometry GeometryOf(Ww8ListLevel level)
    {
        Ww8LayoutFormat format = ApplyLayoutSprms(default, level.ParagraphProperties);

        return new Ww8ListGeometry(
            Length.FromTwips(format.LeftIndent ?? 0), Length.FromTwips(format.FirstLineIndent ?? 0));
    }

    /// <summary>
    /// A paragraph's format with the level's indents and the stop its tab goes to laid over it.
    /// </summary>
    /// <remarks>
    /// The stop is added at the block's indent measured <em>from the line's start</em>, which for a hanging
    /// label is the hanging distance — the first line begins that far left of the block, and
    /// <see cref="TabStop.Position"/> records why the origin matters. A level with no hanging indent needs
    /// no stop: its label and its text start in the same place.
    /// </remarks>
    private static ParagraphFormat Listed(ParagraphFormat format, Ww8ListLabel? label)
    {
        if (label is not { } list) return format;

        List<TabStop> stops = [.. format.TabStops];

        if (list.Separator == "\t" && list.Geometry.FirstLineIndent < Length.Zero)
        {
            stops.Add(new TabStop(Length.Zero - list.Geometry.FirstLineIndent));
            stops.Sort((left, right) => left.Position.Emu.CompareTo(right.Position.Emu));
        }

        return format with
        {
            StartIndent = list.Geometry.StartIndent,
            FirstLineIndent = list.Geometry.FirstLineIndent,
            TabStops = stops,
        };
    }
}

/// <summary>A level's indents, in the units layout works in.</summary>
/// <param name="StartIndent">How far the item's block is indented.</param>
/// <param name="FirstLineIndent">The first line's extra indent, negative for a hanging label.</param>
internal readonly record struct Ww8ListGeometry(Length StartIndent, Length FirstLineIndent);

/// <summary>What a numbered paragraph is laid out with: its label, its separator and its indents.</summary>
/// <param name="Text">The label, which is empty for a level that counts and draws nothing.</param>
/// <param name="Separator">What goes between the label and the text: a tab, a space or nothing.</param>
/// <param name="Geometry">The level's indents.</param>
internal readonly record struct Ww8ListLabel(
    string Text, string Separator, Ww8ListGeometry Geometry)
{
    /// <summary>The whole of what is prepended to the paragraph's text.</summary>
    public string Prefix => Text.Length == 0 ? string.Empty : Text + Separator;
}
