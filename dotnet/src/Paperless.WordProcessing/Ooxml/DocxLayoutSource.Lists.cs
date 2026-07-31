using System.Globalization;
using System.Xml.Linq;
using Paperless.Core.Units;
using Paperless.Text.Layout;

namespace Paperless.WordProcessing.Ooxml;

/// <content>
/// Reading a <c>w:numPr</c> into the label and the indents its paragraph is laid out with.
/// </content>
/// <remarks>
/// <para>
/// The same shape as the ODF list read, and for the same reason: Writer models a label as a portion at the
/// head of the item's first line followed by a tab, so the label is the paragraph's <em>prefix</em> and the
/// level's indents replace the paragraph's own. What differs is where the pieces come from.
/// </para>
/// <para>
/// OOXML puts the list <em>structure</em> on the paragraph — a <c>w:numPr</c> naming an instance and a level
/// — where ODF nests elements, so there is no walk state to keep: a numbered paragraph says so itself.
/// The counter, the format and the <c>w:lvlText</c> template all come from <see cref="WordNumbering"/>,
/// which the extraction pass already uses; this adds only the geometry, which the extraction pass has no
/// use for.
/// </para>
/// <para>
/// The trap is <c>w:suff</c>. Its <em>default</em> is <c>tab</c>, and a level that states nothing therefore
/// separates its label from its text with a tab — so a reader treating the absence as "nothing" runs the
/// number straight into the first word.
/// </para>
/// </remarks>
public sealed partial class DocxLayoutSource
{
    /// <summary>
    /// The label a paragraph takes and the indents it is laid out with, or null when it is not in a list.
    /// </summary>
    /// <param name="properties">The paragraph's <c>w:pPr</c>.</param>
    /// <param name="styleId">Its style, which may be what names the list.</param>
    /// <remarks>
    /// The numbering can be named three ways and all three are followed, because Word writes all three: on
    /// the paragraph, on its style, or by the list naming the style rather than the other way round — which
    /// is how heading numbering is usually written.
    /// </remarks>
    private DocxListLabel? LabelOf(XElement? properties, string? styleId)
    {
        if (_numbering is null) return null;

        (string? numId, int level) = NumberingOf(properties, styleId);
        if (numId is null or "0") return null;

        level = Math.Clamp(level, 0, WordNumbering.LevelCount - 1);

        WordNumberingLevel? definition = _numbering.FindLevel(numId, level);
        string? text = _numbering.Advance(numId, level);

        // A `w:numFmt` of `none` numbers nothing and still indents, which is how a level contributes to the
        // hierarchy without being drawn. So an empty label is not the same as no list at all.
        return new DocxListLabel(text ?? "", definition?.Suffix ?? "tab", GeometryOf(definition));
    }

    /// <summary>Which list instance and level a paragraph belongs to.</summary>
    private (string? NumId, int Level) NumberingOf(XElement? properties, string? styleId)
    {
        XElement? stated = Word.Child(properties, "numPr");
        string? numId = Word.Value(stated, "numId");

        int Level(XElement? from)
            => int.TryParse(
                Word.Value(from, "ilvl"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int parsed)
                ? parsed
                : 0;

        if (numId is not null) return (numId, Level(stated));
        if (styleId is null) return (null, 0);

        WordProperty fromStyle = _styles.ResolveInStyleChain(
            styleId, WordStyleType.Paragraph, runProperty: false, "numPr");

        if (fromStyle.HasValue) return (Word.Value(fromStyle.Element, "numId"), Level(fromStyle.Element));

        return (_numbering!.FindInstanceForStyle(styleId), 0);
    }

    /// <summary>
    /// A level's indents, from its own <c>w:ind</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>w:start</c> — or its legacy spelling <c>w:left</c> — is the block's indent, and the first line's is
    /// stated as <em>either</em> <c>w:hanging</c> or <c>w:firstLine</c>: the first is a positive number
    /// meaning "back by this much" and the second a signed one meaning "forward by this much". They are
    /// mutually exclusive, and reading a hanging value as a first-line one indents the label instead of
    /// hanging it — the number ends up to the right of the text it should be left of.
    /// </para>
    /// <para>
    /// The tab a <c>tab</c> suffix goes to is the block's indent: Word puts the item's text at
    /// <c>w:start</c> and the label at <c>w:start</c> minus the hanging distance, which is the same
    /// arrangement ODF states with a separate stop position. So there is no third number to read.
    /// </para>
    /// </remarks>
    private static DocxListGeometry GeometryOf(WordNumberingLevel? definition)
    {
        XElement? indents = Word.Child(definition?.ParagraphProperties, "ind");
        if (indents is null) return default;

        Length start = Twips(Word.Attribute(indents, "start") ?? Word.Attribute(indents, "left"));

        Length first = Word.Attribute(indents, "hanging") is { } hanging
            ? Length.Zero - Twips(hanging)
            : Twips(Word.Attribute(indents, "firstLine"));

        return new DocxListGeometry(start, first);
    }

    private static Length Twips(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long twips)
            ? Length.FromTwips(twips)
            : Length.Zero;

    /// <summary>What goes between the label and the item's text.</summary>
    /// <remarks>
    /// <c>w:suff</c>, whose default is <c>tab</c> — the one value a level usually does not state and the one
    /// it usually means.
    /// </remarks>
    private static string Separator(string suffix) => suffix switch
    {
        "space" => " ",
        "nothing" => "",
        _ => "\t",
    };

    /// <summary>
    /// A paragraph's format with the list level's indents and its tab stop laid over it.
    /// </summary>
    /// <remarks>
    /// The level's indents replace the paragraph's, as in ODF, and the stop is added at the block's indent
    /// measured <em>from the line's start</em> — which for a hanging label is the hanging distance, since the
    /// line begins that far left of the block. See <see cref="TabStop.Position"/> for why the origin matters.
    /// </remarks>
    private static ParagraphFormat Listed(ParagraphFormat format, DocxListLabel? label)
    {
        if (label is not { } list) return format;

        List<TabStop> stops = [.. format.TabStops];

        if (Separator(list.Suffix) == "\t" && list.Geometry.FirstLineIndent < Length.Zero)
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

/// <summary>A level's indents.</summary>
/// <param name="StartIndent">How far the item's block is indented.</param>
/// <param name="FirstLineIndent">The first line's extra indent, negative for a hanging label.</param>
internal readonly record struct DocxListGeometry(Length StartIndent, Length FirstLineIndent);

/// <summary>What a numbered paragraph is laid out with: its label, its separator and its indents.</summary>
/// <param name="Text">The label, which is empty for a level that counts and draws nothing.</param>
/// <param name="Suffix">The <c>w:suff</c>: <c>tab</c>, <c>space</c> or <c>nothing</c>.</param>
/// <param name="Geometry">The level's indents.</param>
internal readonly record struct DocxListLabel(
    string Text, string Suffix, DocxListGeometry Geometry);
