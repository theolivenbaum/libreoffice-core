using Paperless.Text.Layout;

namespace Paperless.WordProcessing.Rtf;

/// <summary>
/// The formatting one <c>{\stylesheet}</c> entry states, as read off the entry's own group.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The character half resolves through the whole <c>\sbasedon</c> chain; the paragraph half
/// is three properties and reaches a paragraph only from the chain <em>above</em> the style it
/// names.</strong> That asymmetry is measured — 53 one-page probes in
/// <c>probes/rtf-resid-r80/genmatrix.py</c>, thirteen paragraph properties against 26.2.4.2 — and it
/// is not a quirk of the corpus: it is <c>cloneAndDeduplicateSprm</c>
/// (<c>sw/source/writerfilter/rtftok/rtfsprm.cxx</c>:283-339), whose <em>"not found - try to
/// override style with default"</em> branch writes <c>getDefaultSPRM</c>'s value onto the paragraph
/// as direct formatting for every paragraph property the <em>named</em> style states and the
/// paragraph does not. So the named style's own <c>\sb \sa \li \ri \fi \sl</c> are each reset to
/// RTF's default before they can reach the text, and an ancestor's are not, because
/// <c>lcl_copyFlatten</c> (<c>rtfdocumentimpl.cxx</c>:490-514) flattens only the entry's own
/// <c>pPr</c> into the set that branch walks.
/// </para>
/// <para>
/// Which properties survive follows from the same function's table, and the probes agree with it
/// property for property. <c>getDefaultSPRM</c> (<c>rtfsprm.cxx</c>:154-224) answers a default for
/// <c>ind</c>'s three children and for <c>spacing</c>'s <em>after</em> alone — the
/// <c>LN_CT_PPrBase_spacing</c> case returns a value carrying <c>after = 0</c> and nothing else, and
/// it is taken before the per-attribute recursion, so <em>before</em> is never visited. It answers
/// nothing for <c>jc</c>, <c>keepNext</c> or <c>tabs</c>. Measured, one property per probe:
/// <c>\qc \qr \qj</c> and <c>\sb</c> reach the paragraph from an ancestor and
/// <c>\sa \li \ri \fi \sl</c> reach it from nowhere; <c>\keepn</c> reaches it from the named style
/// as well, which is why it is the one member here resolved over the whole chain.
/// </para>
/// <para>
/// <strong>The earlier reading — "character formatting only" — was right about the effect and wrong
/// about the cause, and the cause is what decides which properties to carry.</strong> It attributed
/// the whole of it to <c>StyleSheetTable::ConvertStyleName</c>
/// (<c>sw/source/writerfilter/dmapper/StyleSheetTable.cxx</c>:1620-1660) mapping an RTF style
/// <em>name</em> onto Writer's built-in style of that name, so that a pool style's own vertical
/// spacing beat the <c>\sbasedon</c> chain. That mapping is real, but it is not what suppresses the
/// spacing: a style called <c>Centered</c> maps onto no pool style at all and its own <c>\sb480</c>
/// is dropped just the same, while the identical <c>\sb480</c> one level up is honoured.
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

    /// <summary>The alignment one of the <c>\q</c> words gave, by page side.</summary>
    /// <remarks>
    /// Carried because <c>LN_CT_PPrBase_jc</c> has no entry in <c>getDefaultSPRM</c>, so nothing
    /// overrides it on the way to the paragraph. Reaches the text from an ancestor only.
    /// </remarks>
    public TextAlignment? Alignment { get; init; }

    /// <summary>The space above the paragraph in twips, from <c>\sb</c>.</summary>
    /// <remarks>
    /// Its sibling <c>\sa</c> is deliberately absent: <c>getDefaultSPRM</c>'s value for the whole
    /// <c>spacing</c> node is <c>after = 0</c>, which is written over any style's space after and
    /// leaves its space before alone. Reaches the text from an ancestor only.
    /// </remarks>
    public int? SpaceBeforeTwips { get; init; }

    /// <summary><c>\keepn</c>.</summary>
    /// <remarks>
    /// The one paragraph property a style states <em>for itself</em> and still reaches the text,
    /// because <c>keepNext</c> is a plain value with no default and no children, so
    /// <c>cloneAndDeduplicateSprm</c>'s not-found branch writes nothing over it.
    /// </remarks>
    public bool? KeepWithNext { get; init; }

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
        Alignment = Alignment ?? parent.Alignment,
        SpaceBeforeTwips = SpaceBeforeTwips ?? parent.SpaceBeforeTwips,
        KeepWithNext = KeepWithNext ?? parent.KeepWithNext,
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
    private readonly Dictionary<int, RtfStyleFormatting> _resolvedContribution = [];

    /// <summary>Records a style definition read from the stylesheet.</summary>
    /// <remarks>
    /// A <c>\sbasedon</c> naming a style the sheet has not reached yet is dropped, because the
    /// importer resolves it to a <em>name</em> where it stands —
    /// <c>pIntValue = new RTFValue(getStyleName(nParam))</c>,
    /// <c>sw/source/writerfilter/rtftok/rtfdispatchvalue.cxx</c>:131-134 — and a style not yet read
    /// has no name to give, so <c>StyleSheetTable</c> never calls <c>setParentStyle</c> for it.
    /// Measured: a child declared before its parent takes nothing from it
    /// (<c>probes/rtf-resid-r80</c>, <c>o-childfirst</c>), where a child declared after it, with an
    /// unrelated entry in between, takes everything (<c>s-gap</c>).
    /// </remarks>
    internal void Add(int id, RtfStyle style)
    {
        if (style.BasedOn is { } parent && !_paragraphStyles.ContainsKey(parent))
        {
            style = style with { BasedOn = null };
        }

        if (style.IsCharacterStyle) _characterStyles[id] = style;
        else _paragraphStyles[id] = style;
        _resolved.Clear();
        _resolvedContribution.Clear();
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
    /// What a paragraph naming this style actually takes from it, which is not the whole
    /// <c>\sbasedon</c> merge <see cref="FormattingOf"/> answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three classes, and which property is in which is measured rather than chosen — see the
    /// table in <see cref="RtfStyleFormatting"/>'s own remarks. Most properties are honoured only
    /// when an <em>ancestor</em> states them, because the named style's own statement is written
    /// back over the paragraph as RTF's default. A few — the ones <c>getDefaultSPRM</c> answers
    /// nothing for — are honoured wherever in the chain they are stated. The font <em>face</em> is
    /// in neither class: no style's face ever reaches a run.
    /// </para>
    /// <para>
    /// A null member means "leave the paragraph where <c>\pard\plain</c> left it", which is the
    /// same value the reset would write, so the two cases need not be told apart downstream.
    /// </para>
    /// </remarks>
    public RtfStyleFormatting ContributionOf(int id)
    {
        if (_resolvedContribution.TryGetValue(id, out RtfStyleFormatting? cached)) return cached;

        bool known = _paragraphStyles.TryGetValue(id, out RtfStyle style);
        RtfStyleFormatting own = known ? style.Formatting : RtfStyleFormatting.Empty;
        RtfStyleFormatting inherited = known && style.BasedOn is { } parent && parent != id
            ? FormattingOf(parent)
            : RtfStyleFormatting.Empty;
        RtfStyleFormatting chain = FormattingOf(id);

        // A property the *named* style states is dropped and the ancestors' value with it, because
        // the reset is written over the paragraph as direct formatting; a property only an ancestor
        // states is honoured. Null therefore means "leave the paragraph at what \pard\plain left",
        // which is the same value the reset writes.
        static T? Inherited<T>(T? own, T? inherited) where T : struct
            => own is null ? inherited : null;

        RtfStyleFormatting result = new()
        {
            // Not the face: no style's font reaches a run at all -- see the type's remarks.
            FontSizeHalfPoints = Inherited(own.FontSizeHalfPoints, inherited.FontSizeHalfPoints),
            Bold = Inherited(own.Bold, inherited.Bold),
            Italic = Inherited(own.Italic, inherited.Italic),
            Underline = Inherited(own.Underline, inherited.Underline),
            ForegroundColourIndex =
                Inherited(own.ForegroundColourIndex, inherited.ForegroundColourIndex),
            Alignment = Inherited(own.Alignment, inherited.Alignment),
            SpaceBeforeTwips = Inherited(own.SpaceBeforeTwips, inherited.SpaceBeforeTwips),

            // The properties `getDefaultSPRM` answers nothing for: the named style's own statement
            // survives, so these resolve over the whole chain.
            Capitals = chain.Capitals,
            SmallCapitals = chain.SmallCapitals,
            Strike = chain.Strike,
            LanguageId = chain.LanguageId,
            KeepWithNext = chain.KeepWithNext,
        };

        _resolvedContribution[id] = result;
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
