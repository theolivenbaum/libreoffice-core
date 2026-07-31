using System.Xml.Linq;
using Paperless.Core.Numbering;
using Paperless.Core.Units;
using Paperless.OpenDocument;
using Paperless.OpenDocument.Styles;
using Paperless.Text.Layout;

namespace Paperless.WordProcessing.OpenDocument;

/// <content>
/// Reading a <c>text:list</c> into the label and the indents its items are laid out with.
/// </content>
/// <remarks>
/// <para>
/// A list label is not a separate kind of thing to draw. Writer models it as a <em>portion</em> at the head
/// of the item's first line followed by a tab to the level's stop, and modelling it the same way here means
/// the layout engine needs no new machinery at all: the label is emitted as the paragraph's prefix — the
/// mechanism a footnote's citation already uses, which exists precisely so that prefixing text does not
/// shift the offsets of anything anchored in the paragraph — and the level's indents replace the
/// paragraph's own.
/// </para>
/// <para>
/// Measured on a list declaring <c>fo:margin-left="1.27cm"</c>, <c>fo:text-indent="-0.635cm"</c> and a
/// <c>listtab</c> stop at 1.27 cm: LibreOffice puts the label at 74.70 pt, the first line's text at 92.70,
/// and a continuation line at 92.70 — which is exactly what a 36 pt start indent, an 18 pt hanging first
/// line and a tab stop at 36 pt produce. So there is nothing to special-case in the layouter.
/// </para>
/// </remarks>
public sealed partial class OdtLayoutSource
{
    /// <summary>
    /// How deeply lists may nest before further levels reuse the innermost definition.
    /// </summary>
    /// <remarks>
    /// ODF allows ten numbered levels and Writer's own limit is ten. A file may nest further; the labels then
    /// come from level ten, which is what Writer does rather than losing the item altogether.
    /// </remarks>
    public const int MaxListLevels = 10;

    /// <summary>The lists the walk is currently inside, outermost first.</summary>
    private readonly List<OdtOpenList> _lists = [];

    /// <summary>
    /// Whether the next paragraph starts a list item, and so takes the label.
    /// </summary>
    /// <remarks>
    /// A list item can hold several paragraphs and only the first is labelled — the rest are continuation
    /// text at the item's indent. So this is armed by <c>text:list-item</c> and disarmed by the first
    /// paragraph that consumes it.
    /// </remarks>
    private bool _labelPending;

    /// <summary>Enters a <c>text:list</c>, pushing a level whose counter starts fresh.</summary>
    /// <remarks>
    /// The style name is carried down: a nested <c>text:list</c> usually names none and inherits its
    /// parent's, which is how one list style serves all ten levels.
    /// </remarks>
    private void EnterList(XElement list)
    {
        string? styleName =
            list.Attribute(XName.Get("style-name", OdfNamespaces.Text))?.Value
            ?? (_lists.Count > 0 ? _lists[^1].StyleName : null);

        // Seeded one below the level's own start value, so that the first item's increment lands *on* it.
        // Carrying the offset to formatting time instead cannot work once a label shows its ancestors'
        // counters too: those are read straight out of this list, with no level in hand to adjust them by.
        int depth = Math.Min(_lists.Count + 1, MaxListLevels);

        _lists.Add(new OdtOpenList
        {
            StyleName = styleName,
            Counter = (LevelOf(styleName, depth)?.StartValue ?? 1) - 1,
        });
    }

    /// <summary>Leaves the innermost list.</summary>
    private void LeaveList()
    {
        if (_lists.Count > 0) _lists.RemoveAt(_lists.Count - 1);
    }

    /// <summary>Enters a <c>text:list-item</c>: the level's counter advances and a label falls due.</summary>
    /// <remarks>
    /// A <c>text:list-header</c> is deliberately not this. It is an unnumbered heading inside a list, so it
    /// takes the level's indents, shows no label, and does not advance the counter.
    /// </remarks>
    /// <param name="item">The <c>text:list-item</c>, which may restart the count with a start value.</param>
    private void EnterListItem(XElement item)
    {
        if (_lists.Count == 0) return;

        // An item may restart its level's count, which is how a document continues a numbering interrupted by
        // a paragraph between two lists. Set one below, because the increment below is what lands on it.
        if (OdfValue.ParseInt(item.Attribute(XName.Get("start-value", OdfNamespaces.Text))?.Value)
            is { } start)
        {
            _lists[^1].Counter = start - 1;
        }

        _lists[^1].Counter++;
        _labelPending = true;
    }

    /// <summary>
    /// The label the next paragraph takes and the indents it is laid out with, or null when it is not in a
    /// list at all.
    /// </summary>
    private OdtListLabel? PendingLabel()
    {
        if (_lists.Count == 0) return null;

        int depth = Math.Min(_lists.Count, MaxListLevels);
        OdfListLevel? level = LevelOf(_lists[^1].StyleName, depth);
        OdtListGeometry geometry = GeometryOf(level);

        // A paragraph inside an item but not its first still takes the indents, so that a wrapped item and a
        // second paragraph of the same item line up rather than one of them reverting to the margin.
        if (!_labelPending) return new OdtListLabel(null, null, geometry);

        _labelPending = false;

        return new OdtListLabel(
            TextOf(_lists[^1].StyleName, depth, level), level?.TextStyleName, geometry);
    }

    /// <summary>The definition for a nesting depth, falling back to the deepest shallower one.</summary>
    /// <remarks>
    /// The fallback matters: a style commonly defines level one only and a document nests three deep, and
    /// ODF's own rule is that a level with no definition takes the nearest one above it. Without that a
    /// nested list shows no label at all.
    /// </remarks>
    private OdfListLevel? LevelOf(string? styleName, int depth)
    {
        if (styleName is null) return null;
        if (!_styles.ListStyles.TryGetValue(styleName, out OdfListStyle? style)) return null;

        for (int at = depth; at >= 1; at--)
        {
            if (style.GetLevel(at) is { } level) return level;
        }

        return null;
    }

    /// <summary>
    /// The label's text: a bullet character, or the level's counters between its prefix and suffix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rendered by <see cref="OdfListStyle.FormatLabel"/>, which the extraction pass already uses. It is the
    /// one piece of a label that is genuinely shared: <c>text:display-levels</c> means a level's label can
    /// show its ancestors' counters as well as its own — <c>1.2.3</c> rather than <c>3</c> — and each
    /// component takes the <em>format of its own level</em>, so a roman level two under a decimal level one
    /// reads <c>1.ii</c>. A second implementation of that would be a second set of rules to get wrong, and
    /// the walk state this pass keeps is exactly the counter array it wants.
    /// </para>
    /// <para>
    /// A null answer means the level draws nothing — an image label, which needs a decoder, or an empty
    /// <c>style:num-format</c>, which is how an outline level contributes to the hierarchy without appearing.
    /// A bullet level with no character is the one case worth substituting for, since losing it loses the
    /// fact that the paragraph is in a list at all.
    /// </para>
    /// </remarks>
    /// <param name="styleName">The list style in force, inherited from the outermost list that named one.</param>
    /// <param name="depth">The one-based nesting depth, which is the level being labelled.</param>
    /// <param name="level">That level's definition, already resolved.</param>
    private string TextOf(string? styleName, int depth, OdfListLevel? level)
    {
        if (level is null || styleName is null) return DefaultBullet;
        if (!_styles.ListStyles.TryGetValue(styleName, out OdfListStyle? style)) return DefaultBullet;

        // Every open level's counter, outermost first, which is what a display-levels label indexes into.
        return style.FormatLabel(depth, [.. _lists.Select(open => open.Counter)])
               ?? (level.Kind == OdfListLabelKind.Bullet ? DefaultBullet : "");
    }

    /// <summary>The bullet a level with no definition at all uses.</summary>
    /// <remarks>
    /// U+2022, which is what Writer's <c>List Bullet</c> style carries. A list whose style the file did not
    /// write is far likelier than a list meaning to show nothing, and showing nothing loses the fact that it
    /// is a list.
    /// </remarks>
    private const string DefaultBullet = "•";

    /// <summary>
    /// A level's indents, in both of ODF's two spellings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The newer one — <c>text:list-level-position-and-space-mode="label-alignment"</c> with a
    /// <c>style:list-level-label-alignment</c> child — states the same three numbers a paragraph does, and is
    /// what LibreOffice writes. The older one states <c>text:space-before</c> and
    /// <c>text:min-label-width</c> on the level properties directly, where the text's indent is the sum and
    /// the hanging part is the label's width. Both reduce to a start indent, a first-line indent and the
    /// position the text after the label goes to.
    /// </para>
    /// <para>
    /// <c>text:label-followed-by</c> decides that last one: <c>listtab</c> sends the text to
    /// <c>text:list-tab-stop-position</c>, <c>space</c> puts one space after the label, and <c>nothing</c>
    /// runs the text straight on.
    /// </para>
    /// </remarks>
    private static OdtListGeometry GeometryOf(OdfListLevel? level)
    {
        OdfPropertySet? properties = level?.LevelProperties;
        if (properties is null) return default;

        XElement? alignment = properties.Children.FirstOrDefault(child =>
            child.Name == XName.Get("list-level-label-alignment", OdfNamespaces.Style));

        if (alignment is not null)
        {
            return new OdtListGeometry(
                Measure(alignment.Attribute(XName.Get("margin-left", OdfNamespaces.FoCompatible))?.Value),
                Measure(alignment.Attribute(XName.Get("text-indent", OdfNamespaces.FoCompatible))?.Value),
                alignment.Attribute(XName.Get("label-followed-by", OdfNamespaces.Text))?.Value ?? "listtab",
                Measure(
                    alignment.Attribute(XName.Get("list-tab-stop-position", OdfNamespaces.Text))?.Value));
        }

        // The older spelling: the text sits at space-before plus the label's width and the label hangs back
        // by that width — the same shape written as two positive numbers instead of one negative one.
        Length before = Measure(properties.Get(OdfNamespaces.Text, "space-before"));
        Length width = Measure(properties.Get(OdfNamespaces.Text, "min-label-width"));

        return new OdtListGeometry(before + width, Length.Zero - width, "listtab", before + width);
    }

    private static Length Measure(string? value)
        => OdfWriterUnits.ToCore(OdfValue.ParseLength(value)) ?? Length.Zero;

    /// <summary>What goes between the label and the item's text.</summary>
    /// <remarks>
    /// A tab for <c>listtab</c>, which the level's own stop then catches; a space for <c>space</c>; and
    /// nothing at all for <c>nothing</c>, where the text runs straight on from the label.
    /// </remarks>
    private static string Separator(OdtListGeometry geometry) => geometry.LabelFollowedBy switch
    {
        "space" => " ",
        "nothing" => "",
        _ => "\t",
    };

    /// <summary>
    /// A paragraph's format with the list level's indents and tab stop laid over it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The level's indents <em>replace</em> the paragraph's rather than adding to them, which is ODF's rule
    /// for the label-alignment mode: the list is what positions its items, and a paragraph style's own indent
    /// inside a list is overridden. Adding them instead indents a list twice.
    /// </para>
    /// <para>
    /// The tab stop is added rather than replacing the paragraph's own, because a list item can hold tabs of
    /// its own — a two-column list is written exactly that way — and the level's stop is simply the first of
    /// them.
    /// </para>
    /// <para>
    /// It is also <em>converted</em>. <c>text:list-tab-stop-position</c> is measured from the text area's
    /// edge, which is measured rather than assumed — a list whose margin and stop are both 1.27 cm puts its
    /// text at 92.70 pt, the area plus one of them and not the area plus both — while <c>TabRuler</c>
    /// measures a stop from the <em>line's</em> start. So the level's stop has the line's own start taken off
    /// it. Skipping that puts the item's text 18 pt too far right, by exactly the hanging indent.
    /// </para>
    /// </remarks>
    private static ParagraphFormat Listed(ParagraphFormat format, OdtListLabel? list)
    {
        if (list is not { } label) return format;

        OdtListGeometry geometry = label.Geometry;

        List<TabStop> stops = [.. format.TabStops];

        if (geometry.LabelFollowedBy == "listtab" && geometry.TabStop > Length.Zero)
        {
            Length fromLineStart =
                geometry.TabStop - (geometry.StartIndent + geometry.FirstLineIndent);

            if (fromLineStart > Length.Zero)
            {
                stops.Add(new TabStop(fromLineStart));
                stops.Sort((left, right) => left.Position.Emu.CompareTo(right.Position.Emu));
            }
        }

        return format with
        {
            StartIndent = geometry.StartIndent,
            FirstLineIndent = geometry.FirstLineIndent,
            TabStops = stops,
        };
    }
}

/// <summary>One open list level: which style it uses and how many items it has numbered.</summary>
internal sealed class OdtOpenList
{
    /// <summary>The list style's name, inherited from the enclosing list when this one names none.</summary>
    public string? StyleName { get; init; }

    /// <summary>How many items of this level have been numbered.</summary>
    public int Counter { get; set; }
}

/// <summary>A level's indents, and where the text after the label starts.</summary>
/// <param name="StartIndent">How far the item's block is indented.</param>
/// <param name="FirstLineIndent">The first line's extra indent, negative for a hanging label.</param>
/// <param name="LabelFollowedBy">
/// <c>listtab</c>, <c>space</c> or <c>nothing</c> — what separates the label from the text.
/// </param>
/// <param name="TabStop">Where a <c>listtab</c> sends the text.</param>
internal readonly record struct OdtListGeometry(
    Length StartIndent, Length FirstLineIndent, string LabelFollowedBy, Length TabStop);

/// <summary>What a list item's paragraph is laid out with: its label, its style and its indents.</summary>
/// <param name="Text">The label, or null for a paragraph inside an item that is not its first.</param>
/// <param name="StyleName">The character style the label takes, when the level names one.</param>
/// <param name="Geometry">The level's indents.</param>
internal readonly record struct OdtListLabel(
    string? Text, string? StyleName, OdtListGeometry Geometry);
