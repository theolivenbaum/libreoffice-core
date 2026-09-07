namespace Paperless.WordProcessing.Rtf;

/// <summary>
/// The formatting one <c>{\stylesheet}</c> entry states, as read off the entry's own group.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Character formatting only, and that is measured rather than chosen.</strong> A style's
/// paragraph formatting cannot be resolved from the <c>\sbasedon</c> chain, because dmapper maps an
/// RTF style <em>name</em> onto Writer's built-in style of that name —
/// <c>StyleSheetTable::ConvertStyleName</c>, <c>sw/source/writerfilter/dmapper/StyleSheetTable.cxx</c>:1620-1660,
/// <c>{ "heading 5", "Heading 5" }</c> — and a pool style brings its own vertical spacing, which
/// <c>setParentStyle</c> (:1156-1170) does not override. On <c>DEP2008-1900.rtf</c> the RTF chain
/// gives <c>\s5 heading 5</c> the <c>\sa160</c> its <c>\sbasedon0</c> parent states and 26.2.4.2
/// draws no space after the heading at all. Over the converted corpus's 336 scoreable <c>.rtf</c>,
/// carrying the paragraph half as well scores <strong>247</strong> where carrying the character
/// half alone scores <strong>254</strong>, against a base of 243.
/// </para>
/// <para>
/// Every member is nullable, and that is the whole point of the type: RTF writes a style as the
/// <em>difference</em> from its <c>\sbasedon</c> parent, so "the style said nothing" has to stay
/// distinguishable from "the style said the default". A toggle cannot express that on its own —
/// <c>\b0</c> and an absent <c>\b</c> both leave the group state not-bold — so which control words
/// the entry actually stated is recorded while it is read and consulted here.
/// </para>
/// </remarks>
public sealed record RtfStyleFormatting
{
    /// <summary>The <c>\f</c> index into the font table.</summary>
    public int? FontIndex { get; init; }

    /// <summary>The <c>\fs</c> size in half-points.</summary>
    public int? FontSizeHalfPoints { get; init; }

    /// <summary><c>\b</c>.</summary>
    public bool? Bold { get; init; }

    /// <summary><c>\i</c>.</summary>
    public bool? Italic { get; init; }

    /// <summary><c>\ul</c> and its siblings.</summary>
    public bool? Underline { get; init; }

    /// <summary><c>\strike</c>.</summary>
    public bool? Strike { get; init; }

    /// <summary><c>\caps</c>.</summary>
    public bool? Capitals { get; init; }

    /// <summary><c>\scaps</c>.</summary>
    public bool? SmallCapitals { get; init; }

    /// <summary><c>\cf</c>, as an index into the colour table.</summary>
    public int? ForegroundColourIndex { get; init; }

    /// <summary><c>\lang</c>.</summary>
    public int? LanguageId { get; init; }

    /// <summary>True when the entry stated nothing this type carries.</summary>
    public bool IsEmpty => this == Empty;

    /// <summary>A style that states nothing.</summary>
    public static RtfStyleFormatting Empty { get; } = new();

    /// <summary>
    /// This style's formatting laid over <paramref name="parent"/>'s: what the child states wins,
    /// and what it leaves unstated is inherited.
    /// </summary>
    public RtfStyleFormatting Over(RtfStyleFormatting parent) => new()
    {
        FontIndex = FontIndex ?? parent.FontIndex,
        FontSizeHalfPoints = FontSizeHalfPoints ?? parent.FontSizeHalfPoints,
        Bold = Bold ?? parent.Bold,
        Italic = Italic ?? parent.Italic,
        Underline = Underline ?? parent.Underline,
        Strike = Strike ?? parent.Strike,
        Capitals = Capitals ?? parent.Capitals,
        SmallCapitals = SmallCapitals ?? parent.SmallCapitals,
        ForegroundColourIndex = ForegroundColourIndex ?? parent.ForegroundColourIndex,
        LanguageId = LanguageId ?? parent.LanguageId,
    };
}

/// <summary>One entry from an RTF <c>{\stylesheet}</c> group.</summary>
/// <param name="Name">The style's name, as the trailing text of its definition.</param>
/// <param name="BasedOn">The style this one is based on, or null.</param>
/// <param name="OutlineLevel">
/// The zero-based outline level the style gives its paragraphs, or null for body text.
/// </param>
/// <param name="IsCharacterStyle">True for a <c>\cs</c> character style.</param>
/// <param name="Formatting">The formatting the entry states, relative to its parent.</param>
public readonly record struct RtfStyle(
    string Name,
    int? BasedOn,
    int? OutlineLevel,
    bool IsCharacterStyle,
    RtfStyleFormatting Formatting);

/// <summary>
/// The styles an RTF document declares, and the formatting they carry.
/// </summary>
/// <remarks>
/// <para>
/// It is often said that RTF's stylesheet is decorative — that a writer must restate a
/// paragraph's <em>effective</em> formatting inline after the <c>\s</c> that names its style, so a
/// reader has no cascade to resolve. That is what this class used to say, and it is false for the
/// files this project actually reads.
/// </para>
/// <para>
/// LibreOffice's own RTF export writes the <em>difference</em> from the style and nothing more, so
/// a paragraph in a style that already names the right font names no font at all:
/// <c>\pard\plain \s24\li85\lin85\intbl{\cf8\fs20\b TC Holder}</c> is a whole table cell whose
/// face is stated only by <c>\s24</c>, through <c>\sbasedon0</c>, in the stylesheet. Its importer
/// resolves that cascade — <c>RTFDocumentImpl::getProperties</c>
/// (<c>sw/source/writerfilter/rtftok/rtfdocumentimpl.cxx</c>:534-637) merges the style's
/// properties under the paragraph's own, with the comment <em>"Take paragraph style into account
/// for character properties as well, as paragraph style may contain character properties"</em> at
/// :616-618 — and <c>\pard</c> selects style 0 when the paragraph names none
/// (<c>rtfdispatchflag.cxx</c>:600-614, <em>"By default the style with index 0 is applied"</em>).
/// </para>
/// <para>
/// Reading it the other way put every such run in font 0, which is <c>Times New Roman</c> in every
/// file LibreOffice writes: on <c>Annex-10…GCAA.rtf</c> the reference lays the whole 148-page table
/// out in Carlito and this tree laid it out in Liberation Serif, which is wide enough to wrap
/// cells the reference fits, and the document paginated to 169 pages.
/// </para>
/// </remarks>
public sealed class RtfStyles
{
    /// <summary>How deep a <c>\sbasedon</c> chain is followed.</summary>
    /// <remarks>A guard on untrusted input; a real chain is two or three deep.</remarks>
    private const int MaxBasedOnDepth = 32;

    private readonly Dictionary<int, RtfStyle> _paragraphStyles = [];
    private readonly Dictionary<int, RtfStyle> _characterStyles = [];
    private readonly Dictionary<int, RtfStyleFormatting> _resolved = [];

    /// <summary>Records a style definition read from the stylesheet.</summary>
    internal void Add(int id, RtfStyle style)
    {
        if (style.IsCharacterStyle) _characterStyles[id] = style;
        else _paragraphStyles[id] = style;
        _resolved.Clear();
    }

    /// <summary>The paragraph style with this <c>\s</c> id, or null.</summary>
    public RtfStyle? ParagraphStyle(int id)
        => _paragraphStyles.TryGetValue(id, out RtfStyle style) ? style : null;

    /// <summary>The character style with this <c>\cs</c> id, or null.</summary>
    public RtfStyle? CharacterStyle(int id)
        => _characterStyles.TryGetValue(id, out RtfStyle style) ? style : null;

    /// <summary>
    /// The formatting a paragraph in this style carries, with every <c>\sbasedon</c> ancestor
    /// merged under it.
    /// </summary>
    /// <remarks>
    /// Cycle-guarded and depth-bounded: a <c>\sbasedon</c> loop is malformed but appears in files
    /// written by converters, and this walks a chain over untrusted input.
    /// </remarks>
    public RtfStyleFormatting FormattingOf(int id)
    {
        if (_resolved.TryGetValue(id, out RtfStyleFormatting? cached)) return cached;

        RtfStyleFormatting result = RtfStyleFormatting.Empty;
        HashSet<int> visited = [];
        int? current = id;
        List<RtfStyleFormatting> chain = [];

        while (current is { } styleId && visited.Add(styleId) && chain.Count < MaxBasedOnDepth)
        {
            if (!_paragraphStyles.TryGetValue(styleId, out RtfStyle style)) break;
            chain.Add(style.Formatting);
            current = style.BasedOn;
        }

        // Nearest ancestor last, so each generation lays its own statements over its parent's.
        for (int i = chain.Count - 1; i >= 0; i--) result = chain[i].Over(result);

        _resolved[id] = result;
        return result;
    }

    /// <summary>
    /// The outline level a paragraph style gives, following <c>\sbasedon</c> when the style
    /// itself does not say.
    /// </summary>
    /// <remarks>
    /// Cycle-guarded: a <c>\sbasedon</c> loop is malformed but appears in files written by
    /// converters, and this walks a chain over untrusted input.
    /// </remarks>
    public int? OutlineLevelOf(int id)
    {
        HashSet<int> visited = [];
        int? current = id;

        while (current is { } styleId && visited.Add(styleId))
        {
            if (_paragraphStyles.TryGetValue(styleId, out RtfStyle style))
            {
                if (style.OutlineLevel is { } level) return level;
                current = style.BasedOn;
            }
            else
            {
                return null;
            }
        }
        return null;
    }
}
