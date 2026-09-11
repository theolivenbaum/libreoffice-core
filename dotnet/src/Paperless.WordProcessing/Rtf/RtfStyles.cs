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
/// (<c>sw/source/writerfilter/rtftok/rtfsprm.cxx</c>:290-340), whose <em>"not found - try to
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

    /// <summary>The space below the paragraph in twips.</summary>
    /// <remarks>
    /// No RTF <c>\sa</c> ever reaches this — <c>getDefaultSPRM</c>'s value for the whole
    /// <c>spacing</c> node is <c>after = 0</c>, which is written over any style's space after.
    /// It carries one thing only: the space below that Writer's own <em>Heading</em> pool style
    /// gives a style whose <c>\sbasedon</c> did not resolve. That is not an RTF sprm at all, so
    /// nothing in <c>cloneAndDeduplicateSprm</c>'s table can overwrite it. See
    /// <see cref="RtfStyles.PoolParentOf"/>.
    /// </remarks>
    public int? SpaceAfterTwips { get; init; }

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
        SpaceAfterTwips = SpaceAfterTwips ?? parent.SpaceAfterTwips,
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
/// What Writer's style pool gives an RTF style whose <c>\sbasedon</c> did not resolve.
/// </summary>
/// <remarks>
/// The two answers are not the same kind of thing, which is why this is an enumeration rather than
/// a nullable formatting. <see cref="Heading"/> is a constant — <c>COLL_HEADLINE_BASE</c>'s own
/// 14 pt, 12/6 and keep-with-next. <see cref="Standard"/> is a *reference*: what the paragraph
/// inherits is whatever the document's own <c>Normal</c> entry says, which is why a probe that
/// measures one <c>Normal</c> size cannot tell the two apart. See
/// <see cref="RtfStyles.PoolParentOf"/>.
/// </remarks>
internal enum RtfPoolParent
{
    /// <summary>The name is not one Writer already has a style for.</summary>
    None,

    /// <summary>Writer's <em>Heading</em>: the nine headings, <c>Title</c> and <c>Subtitle</c>.</summary>
    Heading,

    /// <summary>Writer's <em>Standard</em>, which is the document's own <c>Normal</c> entry.</summary>
    Standard,
}

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

    /// <summary>
    /// What Writer's own style pool gives a paragraph style whose <c>\sbasedon</c> did not
    /// resolve, or null when the name is not one the pool answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A style whose name is one of Word's is not created: Writer's existing style of that
    /// name is reused, and it keeps its own parent when the entry states no usable
    /// <c>\sbasedon</c>.</strong> <c>StyleSheetTable::ApplyStyleSheets</c>
    /// (<c>sw/source/writerfilter/dmapper/StyleSheetTable.cxx</c>:1099-1121) converts the entry's
    /// name through <c>ConvertStyleName</c> (<c>:1620-1660</c>, whose map holds
    /// <c>heading 1</c>…<c>heading 9</c> and <c>Heading 1</c>…<c>Heading 9</c>), and where
    /// <c>xStyles->hasByName</c> answers yes it takes that style, resets its own properties and
    /// leaves the parent alone — the one branch that would clear it needs
    /// <c>m_bHasImportedDefaultParaProps</c>, which only an OOXML <c>w:docDefaults</c> sets
    /// (<c>:653-667</c>), so for RTF it is never true. A resolvable <c>\sbasedon</c> replaces the
    /// parent instead (<c>:1156-1169</c>), which is why this is a fallback and not an override.
    /// </para>
    /// <para>
    /// <strong>Three families of name reach this, and only one of them is here.</strong>
    /// <c>heading 1</c>…<c>heading 9</c>, <c>Title</c> and <c>Subtitle</c> all map to a pool style
    /// under <c>COLL_DOC_BITS</c>, whose parent is <c>COLL_HEADLINE_BASE</c> unless it <em>is</em>
    /// that style (<c>GetPoolParent</c>, <c>sw/source/core/doc/poolfmt.cxx</c>:279-289) — so all
    /// eleven answer <em>Heading</em>, and none of them answers the 28 pt bold centring
    /// <c>COLL_DOC_TITLE</c> states for itself (<c>DocumentStylePoolManager.cxx</c>:1365-1374) or
    /// the 18 pt of <c>COLL_DOC_SUBTITLE</c> (<c>:1376-1387</c>), because the entry's own
    /// properties are what the import resets. Measured: <c>Title</c> and <c>Subtitle</c> reproduce
    /// <c>heading 4</c>'s size, space above and space below to the hundredth of a point.
    /// </para>
    /// <para>
    /// <c>Body Text</c> and <c>caption</c> are the second family and answer
    /// <see cref="RtfPoolParent.Standard"/>. Their pool styles' parent is <c>COLL_STANDARD</c>
    /// (<c>poolfmt.cxx</c>:201-204, :229-235), so what they inherit is the document's own
    /// <c>Normal</c> entry rather than a constant: the same probe with
    /// <c>{\s0 … \fs20 Normal;}</c> draws them at 10 pt and with <c>\fs28</c> at 14, while
    /// <c>heading 4</c>, <c>Title</c> and <c>Subtitle</c> answer 14 to both. Neither
    /// <c>COLL_TEXT</c>'s 0/7 pt nor <c>COLL_LABEL</c>'s italic 6/6 survives the reset, so the
    /// pool entry a reader would be tempted to copy is the wrong half of it — what carries is the
    /// <em>parent</em>, and modelling it is following the chain into style 0 rather than folding
    /// a constant in. <c>Caption</c> is the map's other spelling of the same style
    /// (<c>StyleSheetTable.cxx</c>:1715-1716).
    /// </para>
    /// <para>
    /// <b>Only those three names, and the boundary is measured rather than cautious.</b>
    /// <c>probes/words-close-r95/standard-census.py</c> lists every paragraph style the 338
    /// converted <c>.rtf</c> apply without a resolvable <c>\sbasedon</c> — 84 distinct names —
    /// and the ones whose converted name reaches a Writer style with <c>COLL_STANDARD</c>
    /// <em>somewhere</em> above it are <c>Body Text</c> (2 documents), <c>header</c> and
    /// <c>footer</c> (5 each), <c>toc 1</c>…<c>toc 3</c> (2, 1, 1), a bare <c>Heading</c> (1) and
    /// <c>Figure</c> (1); <c>caption</c> and <c>Caption</c> are <b>0 of 338</b>. The six that are
    /// left out are left out because their pool parent is <em>not</em> <c>COLL_STANDARD</c>
    /// directly — <c>Footer</c>'s is <c>COLL_HEADERFOOTER</c> and <c>Contents 1</c>'s is
    /// <c>COLL_REGISTER_BASE</c> (<c>poolfmt.cxx</c>:206-256), both of which are pool styles with
    /// properties of their own that the import does <em>not</em> reset, so each needs its own
    /// measurement rather than this branch.
    /// </para>
    /// <para>
    /// The third family is every name the map answers nothing for, <c>Quote</c>,
    /// <c>Normal (Web)</c> and <c>List Paragraph</c> among them (<c>:1794</c>, <c>:1883-1884</c>),
    /// which keeps <c>\pard\plain</c>'s twelve points and is the control this rule must not move.
    /// </para>
    /// <para>
    /// Every heading maps to a pool style whose parent is <em>Heading</em>, and
    /// <c>SwPoolFormatId::COLL_HEADLINE_BASE</c>
    /// (<c>sw/source/core/doc/DocumentStylePoolManager.cxx</c>:768-819) is where the four values
    /// below come from: <c>SvxFontHeightItem aFntSize(PT_14, …)</c> at <c>:809</c>,
    /// <c>SvxULSpaceItem aUL(PT_12, PT_6, …)</c> at <c>:810</c> and
    /// <c>SvxFormatKeepItem(true, RES_KEEP)</c> at <c>:814</c>. The per-level percentages in
    /// <c>aHeadlineSizes</c> (<c>:107-115</c>) do <em>not</em> arrive with them: the import resets
    /// the level style's own properties, so <c>heading 1</c> through <c>heading 9</c> all answer
    /// 14 pt. Measured on the reference, nine levels and four properties —
    /// <c>probes/rtf-holdover-r87/</c>.
    /// </para>
    /// </remarks>
    /// <returns>Which pool style the name inherits from, or <see cref="RtfPoolParent.None"/>.</returns>
    internal static RtfPoolParent PoolParentOf(string name)
    {
        // Word strips whitespace around style names before the name is looked up
        // (rtfdocumentimpl.cxx:1594), and ConvertStyleName's map is case-sensitive with an entry
        // for each of the two spellings a file actually uses.
        string trimmed = name.Trim();

        // `Body Text` is `Text body`, `COLL_TEXT`, and both spellings of `caption` are `Caption`,
        // `COLL_LABEL` (`StyleSheetTable.cxx`:1715-1716, :1761) -- and both have `COLL_STANDARD`
        // for a pool parent, so what they inherit is the document's own `Normal`.
        if (string.Equals(trimmed, "Body Text", StringComparison.Ordinal)
            || string.Equals(trimmed, "caption", StringComparison.Ordinal)
            || string.Equals(trimmed, "Caption", StringComparison.Ordinal))
        {
            return RtfPoolParent.Standard;
        }

        // `Title` and `Subtitle` have one spelling each in the map; the nine headings have two.
        if (string.Equals(trimmed, "Title", StringComparison.Ordinal)
            || string.Equals(trimmed, "Subtitle", StringComparison.Ordinal))
        {
            return RtfPoolParent.Heading;
        }

        if (trimmed.Length != 9) return RtfPoolParent.None;
        if (trimmed[0] is not ('h' or 'H')) return RtfPoolParent.None;
        if (!trimmed.AsSpan(1, 7).SequenceEqual("eading ")) return RtfPoolParent.None;
        if (trimmed[8] is < '1' or > '9') return RtfPoolParent.None;

        return RtfPoolParent.Heading;
    }

    /// <summary>
    /// What a style whose <c>\sbasedon</c> did not resolve inherits from its pool parent.
    /// </summary>
    /// <param name="name">The style's own name, as the stylesheet entry states it.</param>
    /// <param name="id">Its own <c>\s</c> id, so that <c>Normal</c> cannot inherit from itself.</param>
    private RtfStyleFormatting PoolInheritance(string name, int id) => PoolParentOf(name) switch
    {
        RtfPoolParent.Heading => HeadingPool,
        RtfPoolParent.Standard when id != DefaultStyleId => FormattingOf(DefaultStyleId),
        _ => RtfStyleFormatting.Empty,
    };

    /// <summary>
    /// The <c>\s</c> id of the document's own default paragraph style, which RTF fixes at zero.
    /// </summary>
    /// <remarks>
    /// <c>rtfdispatchflag.cxx</c>:600-614 — <em>"By default the style with index 0 is applied"</em>
    /// — so <c>Standard</c> is this entry and not a constant.
    /// </remarks>
    private const int DefaultStyleId = 0;

    /// <summary>Writer's <em>Heading</em> pool style, as an RTF style's inherited half.</summary>
    private static readonly RtfStyleFormatting HeadingPool = new()
    {
        FontSizeHalfPoints = 28,
        SpaceBeforeTwips = 240,
        SpaceAfterTwips = 120,
        KeepWithNext = true,
    };

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

            // The style at the top of the chain named no parent this sheet could resolve, so
            // Writer's own style of that name keeps the pool parent it came with. `Heading` is a
            // constant; `Standard` is the *document's* own `Normal`, which is style 0 -- and the
            // walk continues into it rather than folding a constant in, which is what makes
            // `\fs20 Normal` and `\fs28 Normal` give different answers.
            switch (style.BasedOn is null ? PoolParentOf(style.Name) : RtfPoolParent.None)
            {
                case RtfPoolParent.Heading:
                    chain.Add(HeadingPool);
                    break;
                case RtfPoolParent.Standard:
                    current = DefaultStyleId;
                    break;
                default:
                    break;
            }
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
            : known ? PoolInheritance(style.Name, id)
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

            // Only ever the pool's, and the paragraph's own `\sa` still overrides it.
            SpaceAfterTwips = inherited.SpaceAfterTwips,

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
