using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.Text.Fonts;
using Paperless.Text.Layout;

namespace Paperless.Presentations.Layout;

/// <summary>Where a text body sits inside its shape, vertically.</summary>
/// <remarks>
/// DrawingML's <c>a:bodyPr/@anchor</c> and ODF's <c>draw:textarea-vertical-align</c>, which
/// spell the same three positions differently. Justified anchoring — spreading the paragraphs to
/// fill the shape — is a fourth value both formats have and neither corpus deck uses; it is read
/// as <see cref="Top"/> rather than silently as <see cref="Middle"/>, which is what LibreOffice
/// falls back to for a single paragraph anyway.
/// </remarks>
public enum TextAnchor
{
    /// <summary>The text block starts at the top of the text rectangle.</summary>
    Top = 0,

    /// <summary>It is centred vertically.</summary>
    Middle,

    /// <summary>It ends at the bottom.</summary>
    Bottom,
}

/// <summary>
/// A shape's text body before layout: its paragraphs, its insets, and how it is anchored.
/// </summary>
/// <remarks>
/// The presentation family's equivalent of the word processor's <c>PageParagraph</c> list, and
/// deliberately its own type rather than a reuse: a slide's text is bounded by the shape rather
/// than flowed down a page, so what layout needs to know is the rectangle and the anchor, and
/// none of the pagination properties — widows, keep-with-next, page breaks — mean anything.
/// </remarks>
public sealed record SlideTextBody
{
    /// <summary>The paragraphs, in order.</summary>
    public IReadOnlyList<SlideParagraph> Paragraphs { get; init; } = [];

    /// <summary>
    /// The insets between the shape's text rectangle and the text.
    /// </summary>
    /// <remarks>
    /// Defaulted to DrawingML's own defaults — 0.1 inch left and right, 0.05 inch top and bottom
    /// (<c>a:bodyPr</c>'s <c>lIns</c>, <c>tIns</c>, <c>rIns</c>, <c>bIns</c>) — because a body
    /// that states none gets exactly those, and a reader defaulting them to zero puts every line
    /// of every unstated text box 7.2 pt too far left.
    /// </remarks>
    public Margins Insets { get; init; } = DefaultInsets;

    /// <summary>DrawingML's default text insets: 91440 EMU across, 45720 EMU down.</summary>
    public static Margins DefaultInsets { get; } = new(
        Length.FromEmu(91440), Length.FromEmu(45720),
        Length.FromEmu(91440), Length.FromEmu(45720));

    /// <summary>Where the block sits vertically.</summary>
    public TextAnchor Anchor { get; init; }

    /// <summary>
    /// How far the text is turned inside the shape, clockwise, in radians.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>a:bodyPr/@rot</c>, and what a SmartArt <c>autoTxRot</c> resolves to. It is <em>not</em>
    /// the shape's own rotation: the shape stays where it is and only its text turns, which is
    /// why it belongs to the body rather than to the placement. LibreOffice keeps the two apart
    /// the same way, as <c>TextPreRotateAngle</c> beside <c>RotateAngle</c>.
    /// </para>
    /// <para>
    /// A quarter turn swaps the text rectangle's width and height about its centre, because the
    /// lines then run down the shape rather than across it; a half turn leaves the rectangle
    /// alone. Only multiples of a quarter turn arise: <c>autoTxRot</c> produces nothing else.
    /// </para>
    /// </remarks>
    public double Rotation { get; init; }

    /// <summary>
    /// Whether the text is shrunk until it fits the shape — DrawingML's <c>a:normAutofit</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When this is set the layouter solves the fit itself and <see cref="FontScale"/> is
    /// <em>ignored</em>, because that is what the reference does: LibreOffice 24.2 reads
    /// <c>a:normAutofit/@fontScale</c> into a field
    /// (<c>oox/source/drawingml/textbodypropertiescontext.cxx:236</c>) and never reads that field
    /// again, so the authoring application's stated answer is discarded and
    /// <c>SdrTextObj::autoFitTextForCompatibility</c> searches for its own. See
    /// <see cref="SlideTextLayout"/> for the search and for what it is measured against.
    /// </para>
    /// <para>
    /// <c>a:normAutofit/@lnSpcReduction</c> is modelled nowhere at all, which is deliberate: the
    /// same handler does not read it either — the <c>normAutofit</c> case reads
    /// <c>XML_fontScale</c> and nothing else — so a body carrying one must lay out exactly as a
    /// body that does not. Paperless did apply it, and it was worth 20 per cent of a line on the
    /// one shape in <c>slides/batch-001</c> that states it: the subtitle of
    /// <c>BMFE-06-03 (Gerflor) Smoke Density and Toxicity.pptx</c> shrank its lines, so the
    /// fit search thought the text nearly fitted unshrunk and drew it at 20 pt where the
    /// reference draws 15.
    /// </para>
    /// <para>
    /// This is a text-only fit. <c>a:spAutoFit</c> is the other direction — the shape grows to its
    /// text rather than the text shrinking to the shape — and is not this flag.
    /// </para>
    /// </remarks>
    public bool AutoFit { get; init; }

    /// <summary>
    /// The multiplier <c>a:normAutofit/@fontScale</c> asks for, or one when it states none.
    /// </summary>
    /// <remarks>
    /// Applied to every run's size when <see cref="AutoFit"/> is <em>not</em> set — which after
    /// the fit search means the ODF path and hand-built bodies only. The value in the file is what
    /// the authoring application arrived at when it last shrank the text to fit.
    /// </remarks>
    public double FontScale { get; init; } = 1.0;

    /// <summary>
    /// The WordArt preset the body is warped along — <c>a:bodyPr/a:prstTxWarp/@prst</c> — or
    /// null when it is ordinary text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>textNoShape</c> is normalised to null on read: it is the value that means <em>no</em>
    /// warp, and the reference tests for exactly that
    /// (<c>oox/source/drawingml/textbodypropertiescontext.cxx:215-226</c> and
    /// <c>oox/source/drawingml/shape.cxx:2202-2211</c>) before putting the shape into text-path
    /// mode. So a non-null value here means Fontwork and nothing else does.
    /// </para>
    /// <para>
    /// See <see cref="IsTextPath"/> for what that costs the text layer.
    /// </para>
    /// </remarks>
    public string? WarpPreset { get; init; }

    /// <summary>
    /// Whether the body is Fontwork: drawn as glyph outlines rather than as text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A warped body is not text in the reference's output. <c>putCustomShapeIntoTextPathMode</c>
    /// turns the shape into a Fontwork custom shape, and
    /// <c>svx/source/customshapes/EnhancedCustomShapeFontWork.cxx</c> converts its characters to
    /// <c>tools::PolyPolygon</c> outlines, so what reaches a PDF is filled paths carrying no
    /// glyph and no <c>ToUnicode</c>. Measured on the installed 26.2.4.2 rather than assumed:
    /// the reference's page 13 of <c>FAAAIandtheArtandScienceofV&amp;Vfinal.pptx</c> holds 597
    /// curve operators where ours holds 4, and neither <c>Automation</c> nor <c>Autonomy</c> —
    /// words that exist only inside its warped bodies — appears anywhere in its text layer. The
    /// same is true of <c>prst="textPlain"</c>, which curves nothing:
    /// <c>redac-sas-201403-ppt-portfolio-rev-sim.pptx</c>'s <c>Fractographic Examinations</c>
    /// is absent from the reference too. The test really is "not <c>textNoShape</c>".
    /// </para>
    /// <para>
    /// <strong>Paperless draws nothing for such a body, which is deliberately a partial.</strong>
    /// The arch geometry is not implemented, and until it is, the honest choice is between
    /// leaving unwarped glyphs where they fall and drawing nothing. Measured on that document's
    /// page 13, the four Fontwork outlines the reference draws sit 14 to 40 pt away from where
    /// the unwarped runs land — always outward along the box's own local up for
    /// <c>textArchUp</c> and down for <c>textArchDown</c>, which is the arch's radial
    /// displacement. Ink in the wrong place counts twice in a comparison against the reference
    /// and absent ink counts once, so drawing nothing is the nearer of the two; the measurement
    /// that settles it is in <c>dotnet/probes/slides-extra-01/results.md</c>.
    /// </para>
    /// <para>
    /// Extraction is unaffected. <c>paperless extract</c> reads the body through
    /// <c>DrawingTextBody</c>, which never consults this, so the words stay in the content tree
    /// — as they should: they are the document's own words, and it is only the *rendering* that
    /// turns them into a picture.
    /// </para>
    /// </remarks>
    public bool IsTextPath => WarpPreset is not null;

    /// <summary>
    /// Whether the text wraps at the shape's width.
    /// </summary>
    /// <remarks>
    /// <c>a:bodyPr/@wrap="none"</c> means it does not: the line runs on past the shape and the
    /// shape grows around it. Modelled as an unbounded width rather than as clipping, which is
    /// what makes a `wrap="none"` label come out on one line as its author saw it.
    /// </remarks>
    public bool Wraps { get; init; } = true;

    /// <summary>
    /// Whether the line height comes from the font size rather than from the font's metrics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EditEngine's <c>FixedCellHeight</c>, which ODF spells
    /// <c>style:font-independent-line-spacing</c>. When it is on the ascent is the font height
    /// outright and the line is 1.2 times it, whatever face the text is set in
    /// (<c>editeng/source/editeng/impedit3.cxx:501,3138-3141</c>); when it is off the face's own
    /// ascent and descent decide, as they do in a word processor.
    /// </para>
    /// <para>
    /// True by default because that is what a PPTX gets: the importer sets it on every text body
    /// it reads (<c>oox/source/ppt/pptshapecontext.cxx:186</c>). A natively authored ODP states
    /// it per paragraph style and usually does not, which is why the two paths give the same
    /// deck slightly different baselines and why this is a property of the body rather than a
    /// constant.
    /// </para>
    /// </remarks>
    public bool FontIndependentLineSpacing { get; init; } = true;
}

/// <summary>One paragraph of a shape's text.</summary>
/// <param name="Text">Its text, without a terminating mark.</param>
/// <param name="Runs">
/// Its runs, partitioning the text. Never empty for non-empty text: a paragraph with no stated
/// formatting still carries one run, so that the size an empty line is as tall as is known.
/// </param>
/// <param name="Alignment">How its lines are placed across the text rectangle.</param>
/// <param name="SpaceBefore">The space above it.</param>
/// <param name="SpaceAfter">The space below it.</param>
/// <param name="LineSpacing">Its line-spacing rule.</param>
/// <param name="StartIndent">Its indent from the start edge.</param>
/// <param name="FirstLineIndent">The extra indent on its first line, negative for a hanging one.</param>
/// <param name="Language">A BCP 47 tag, for the language-specific break rules.</param>
/// <param name="Marker">The bullet or number drawn before it, or null when it has none.</param>
public sealed record SlideParagraph(
    string Text,
    IReadOnlyList<SlideTextRun> Runs,
    TextAlignment Alignment = TextAlignment.Start,
    Length SpaceBefore = default,
    Length SpaceAfter = default,
    LineSpacingRule LineSpacing = default,
    Length StartIndent = default,
    Length FirstLineIndent = default,
    string? Language = null,
    SlideMarker? Marker = null)
{
    /// <summary>The slide formats' own default tab distance: one inch.</summary>
    public static Length DefaultTabDistance { get; } = Length.FromEmu(Length.EmuPerInch);

    /// <summary>
    /// How far apart the stops a tab advances to are, when the paragraph states none of its own.
    /// </summary>
    /// <remarks>
    /// <strong>A slide's is an inch, not the half inch a word processor uses.</strong> PowerPoint
    /// stores it as 0x240 master units and DrawingML as <c>a:defTabSz</c> defaulting to 914400
    /// EMU, and both are one inch; <see cref="ParagraphFormat.DefaultTabInterval"/> defaults to
    /// Word's 720 twips because that is what a document is. The difference compounds: a paragraph
    /// positioned by three tabs lands an inch and a half to the left of where it belongs, which on
    /// a ten-inch slide is fifteen per cent of the page.
    /// </remarks>
    public Length DefaultTabInterval { get; init; } = DefaultTabDistance;
}

/// <summary>
/// The bullet or number a paragraph is labelled with.
/// </summary>
/// <remarks>
/// <para>
/// A marker is drawn as its own glyph run at its own pen, in its own face and usually at its own
/// size — LibreOffice writes it as a separate <c>/Lbl</c> block in the PDF, and on
/// <c>deck-features.pptx</c>'s outline that is a 12.6 pt run beside 28 pt text, because
/// <c>a:buSzPct val="45000"</c> says 45%.
/// </para>
/// <para>
/// It is <em>not</em> part of the paragraph's text, which is why it is here rather than prefixed
/// to it: a marker does not wrap, does not participate in the line breaking, and would change
/// every character offset the runs index by if it were spliced in.
/// </para>
/// </remarks>
/// <param name="Text">The characters to draw.</param>
/// <param name="Typeface">The family it is set in, or null for the paragraph's own.</param>
/// <param name="Scale">Its size as a fraction of the first run's, one for the same size.</param>
/// <param name="Colour">Its colour, or null for the first run's.</param>
/// <param name="IsSymbol">
/// Whether it is a fixed character rather than a generated number, which decides where it sits
/// vertically.
/// <para>
/// <strong>The two are placed by different rules and the difference is a point.</strong>
/// <c>Outliner::StripBullet</c> branches on <c>SVX_NUM_CHAR_SPECIAL</c>: a symbol is drawn from
/// the bullet <em>area's</em> bottom, which centres it against the line's text, and anything else
/// is drawn at <c>nFirstLineMaxAscent</c>, which is the text's own baseline
/// (<c>editeng/source/outliner/outliner.cxx:918</c>). Measured on
/// <c>slide-shape-features.pptx</c>, whose list is <c>a:buAutoNum</c>: LibreOffice draws its
/// first number at 89.972 and centring it would put it at 89.036.
/// </para>
/// </param>
public readonly record struct SlideMarker(
    string Text,
    string? Typeface = null,
    double Scale = 1.0,
    Colour? Colour = null,
    bool IsSymbol = true);

/// <summary>
/// A run raised or lowered off its baseline, and shrunk while it is up there.
/// </summary>
/// <remarks>
/// <para>
/// Two numbers rather than one because a slide's formats state two: DrawingML's
/// <c>a:rPr/@baseline</c> gives the offset alone and the importer supplies the size
/// (<c>oox/source/drawingml/textcharacterproperties.cxx:196-199</c>), and a binary PowerPoint's
/// <c>PPT_CharAttr_Escapement</c> does the same
/// (<c>filter/source/msfilter/svdfppt.cxx:5764-5775</c>). Both end as one
/// <c>SvxEscapementItem(nEsc, nProp)</c>, which is this pair.
/// </para>
/// <para>
/// <strong>The percentage is of the em size here, not of the font's height.</strong> That is
/// where a slide differs from a word processor: EditEngine draws the run at
/// <c>GetFontSize().Height() × nEsc / 100</c> above the pen
/// (<c>editeng/source/items/svxfont.cxx:549-558</c>), where Writer's <c>swfont.cxx</c> takes the
/// same percentage of the unshrunk font's ascent-plus-descent. Using the wrong one of the two
/// misplaces a superscript by about a fifth of its rise.
/// </para>
/// <para>
/// The size matters more than the offset does, because it moves line breaks: a 12 pt run set at
/// 58% is 42% narrower, so a line that fits with the shrink wraps without it. Measured on
/// <c>slides/batch-003/pptx/NCW-2024-Guide-.pptx</c>, whose dates are written
/// <c>5<sup>th</sup> March</c>: drawing the ordinals full size wraps one line of a text box that
/// already overflows the slide, which pushes its last paragraph off the bottom edge.
/// </para>
/// </remarks>
/// <param name="Percent">
/// How far the run moves, as a percentage of its em size; positive raises it and negative lowers
/// it.
/// </param>
/// <param name="Proportion">
/// The size the run is set at, as a percentage of the size it would otherwise take. Zero and 100
/// both mean no change, so a default-constructed value is "no escapement at all".
/// </param>
public readonly record struct SlideEscapement(int Percent, int Proportion)
{
    /// <summary>The size an escaped run is set at when the file states only an offset.</summary>
    /// <remarks><c>DFLT_ESC_PROP</c>, <c>include/editeng/escapementitem.hxx:30</c>.</remarks>
    public const int AutomaticProportion = 58;

    /// <summary>Neither moved nor resized.</summary>
    public static SlideEscapement None => default;

    /// <summary>True when the run sits on its baseline at its own size.</summary>
    public bool IsNone => Percent == 0 && Proportion is 0 or 100;

    /// <summary>The size the run is actually set at, given the size it would otherwise take.</summary>
    public Length SizeOf(Length emSize)
        => Proportion is 0 or 100 ? emSize : emSize * (Proportion / 100.0);

    /// <summary>How far the run sits above its baseline, negative for a subscript.</summary>
    /// <param name="emSize">The size the run would take were it not escaped.</param>
    public Length RiseOf(Length emSize)
        => Percent == 0 ? Length.Zero : emSize * (Percent / 100.0);
}

/// <summary>One run of a paragraph: a range of its text with its own face, size and colour.</summary>
/// <param name="Start">The run's first character.</param>
/// <param name="Length">How many characters it covers.</param>
/// <param name="Typeface">The family it asks for, or null for the deck's default.</param>
/// <param name="Size">The em size.</param>
/// <param name="Weight">The weight on the OpenType 1–1000 scale.</param>
/// <param name="IsItalic">Whether it is italic.</param>
/// <param name="Colour">The colour it is drawn in.</param>
/// <param name="Tracking">
/// A fixed distance added between the run's characters — <c>a:rPr/@spc</c>, stated in hundredths
/// of a point and commonly negative. See <see cref="Paperless.Text.Layout.FormattedRun.Tracking"/>
/// for how it is charged.
/// </param>
/// <param name="IsUnderlined">
/// Whether a rule is drawn under it. A decoration rather than a glyph in every format here —
/// <c>a:rPr/@u</c> in DrawingML, bit 2 of a PPT character-property mask — so it moves no line
/// break and is drawn from the face's own <c>post</c> metrics after the text is placed.
/// </param>
/// <param name="IsStruckThrough">Whether a rule is drawn through it.</param>
/// <param name="IsShadowed">
/// Whether the characters cast the legacy per-character drop shadow — bit 4 of a PPT
/// character-property mask. Like the decorations above it moves no line break, because the
/// shadow is the same glyphs drawn a second time at an offset derived from the font's line
/// height rather than from anything the paragraph measured. See
/// <see cref="SlideTextLayout"/>'s <c>ShadowOffset</c> for the rule and the probe behind it.
/// </param>
/// <param name="Escapement">
/// How far off its baseline the run sits and how much it shrinks to sit there — a superscript or
/// a subscript. Unlike the decorations above, this <em>does</em> move line breaks, because the
/// shrink is what makes the run narrower.
/// </param>
/// <param name="SymbolFont">
/// The face the run's <em>private-use</em> characters are drawn from, or null when it names none.
/// <para>
/// DrawingML's <c>a:rPr/a:sym</c>, and a second family rather than a replacement for
/// <see cref="Typeface"/> because it governs only part of the run: LibreOffice switches the face
/// over each maximal stretch of characters satisfying <c>(ch &amp; 0xff00) == 0xf000</c> and
/// restores it after every one (<c>oox/source/drawingml/textrun.cxx:96-135</c>). A run reading
/// "see &#xF0E0; overleaf" is set in its own face except for the arrow.
/// </para>
/// <para>
/// It is resolved by <c>SlideSymbolRuns</c> before anything is measured, because whether the slot
/// is drawn as it stands or recoded into OpenSymbol turns on whether the named face is installed
/// — which a reader cannot know.
/// </para>
/// </param>
public readonly record struct SlideTextRun(
    int Start,
    int Length,
    string? Typeface,
    Length Size,
    int Weight,
    bool IsItalic,
    Colour Colour,
    Length Tracking = default,
    bool IsUnderlined = false,
    bool IsStruckThrough = false,
    bool IsShadowed = false,
    SlideEscapement Escapement = default,
    SlideSymbolFont? SymbolFont = null)
{
    /// <summary>One past the run's last character.</summary>
    public int End => Start + Length;
}

/// <summary>
/// The face a run's private-use characters are drawn from — DrawingML's <c>a:rPr/a:sym</c>.
/// </summary>
/// <remarks>
/// <para>
/// The two halves are one value because the second decides what the first means, and separating
/// them cost a measurement. <c>a:sym</c>'s <c>@charset</c> is what makes the request a
/// <em>symbol-encoded</em> one — <c>TextFont::implGetFontData</c> reports
/// <c>mnCharset == WINDOWS_CHARSET_SYMBOL</c>, which is the value 2, and nothing else
/// (<c>oox/source/drawingml/textfont.cxx:87-94</c>) — and that flag decides which of two entirely
/// different resolutions the face gets.
/// </para>
/// <para>
/// <strong>A symbol-encoded request never reaches fontconfig at all.</strong>
/// <c>FcPreMatchSubstitution::FindFontSubstitute</c> returns false immediately for one
/// (<c>vcl/unx/generic/font/fontsubst.cxx:100-104</c>), so the request falls to
/// <c>VCL.xcu</c>'s own chain, which names <c>opensymbol</c> for Wingdings and its relatives —
/// and the recode follows. A request that is <em>not</em> symbol-encoded is answered by
/// fontconfig first, and fontconfig has no idea the name meant a symbol font: it answers
/// <c>Wingdings</c> with DejaVu Sans, and the slot is then drawn from DejaVu Sans as it stands.
/// </para>
/// <para>
/// Measured against the banked 26.2.4.2 references rather than reasoned from the tree, on the
/// three corpus decks that state the two combinations:
/// </para>
/// <list type="bullet">
/// <item><description><c>Structural Testing.pptx</c> states <c>&lt;a:sym typeface="Symbol"
/// charset="0"/&gt;</c> — <em>not</em> symbol-encoded — and the reference recodes all five of its
/// slots anyway, because fontconfig answers the family "Symbol" with OpenSymbol on its own. Its
/// OpenSymbol glyphs on pages 3, 4, 5, 6 and 26 sit within 0.3 pt of ours.</description></item>
/// <item><description><c>16 - UTM - (NASA).pptx</c> and
/// <c>Stakeholders-v08052017 - v5.pptx</c> both state <c>&lt;a:sym typeface="Wingdings"/&gt;</c>
/// with no charset, and the reference draws those three slots in <b>DejaVu Sans</b> — at
/// (175.9, 94.3) and (189.2, 29.1) on the latter's page 8, where we had put OpenSymbol.
/// </description></item>
/// </list>
/// <para>
/// So the rule is not "the charset decides whether to recode". It is "the charset decides
/// whether fontconfig is consulted", and the recode then follows from where the face actually
/// landed — which is the same rule the bullet path has always had.
/// </para>
/// </remarks>
/// <param name="Typeface">The family <c>a:sym/@typeface</c> names.</param>
/// <param name="IsMicrosoftEncoded">
/// Whether <c>a:sym/@charset</c> is 2, VCL's <c>IsMicrosoftSymbolEncoded</c>. Absent and 0 both
/// mean false; <c>WINDOWS_CHARSET_DEFAULT</c> is 1, so an unstated charset is not symbol-encoded
/// either.
/// </param>
public readonly record struct SlideSymbolFont(string Typeface, bool IsMicrosoftEncoded);

/// <summary>
/// Resolves the faces a slide's text needs, once per distinct request.
/// </summary>
/// <remarks>
/// The same shape as the word processor's cache and for the same reason: a deck has a handful of
/// typefaces and hundreds of runs, and resolving one means walking a substitution chain and
/// reading a font file. Its own type rather than a shared one because the two libraries sit at
/// the same layer and neither may depend on the other.
/// </remarks>
public sealed class SlideFonts
{
    private readonly SystemFontResolver _fonts;
    private readonly Dictionary<(string?, int, bool), (OpenTypeFace? Face, FontReference? Reference)>
        _resolved = [];

    /// <summary>Creates a cache over a resolver, or over the installed fonts.</summary>
    /// <param name="fonts">The resolver to use, or null to build one over the installed fonts.</param>
    public SlideFonts(SystemFontResolver? fonts = null)
        => _fonts = fonts ?? new SystemFontResolver(SystemFontIndex.Build());

    /// <summary>
    /// The pitch the deck declares for a typeface, when its format states one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both binary formats carry it and neither was read until it was measured to matter: PPTX puts
    /// it in the low two bits of <c>pitchFamily</c> on <c>&lt;a:latin&gt;</c>, PPT in
    /// <c>lfPitchAndFamily</c> at the end of each <c>FontEntityAtom</c>. LibreOffice sends it to
    /// fontconfig, and for a family fontconfig files under no generic it is the only thing that
    /// says the text is meant to line up in columns.
    /// </para>
    /// <para>
    /// <strong>Measured, on the corpus and then in isolation.</strong>
    /// <c>airbus-powerpoint-presentation-2019-20…pptx</c> declares <c>Lucida Console</c> with
    /// <c>pitchFamily="49"</c> — fixed pitch, modern family — and 26.2.4.2 draws it in DejaVu Sans
    /// Mono. Re-zipping the same deck with that one attribute removed and nothing else changed, it
    /// draws DejaVu Sans instead, which is fontconfig's answer for a name it files under nothing.
    /// <c>introduction_to_bea_tuxedo.ppt</c> is the same fact in the binary format:
    /// <c>lfPitchAndFamily</c> is <c>0x31</c> for Lucida Console and <c>0x12</c> for Times New
    /// Roman in the same collection.
    /// </para>
    /// <para>
    /// The pitch and not the family class, deliberately. The family bits are in the same byte and
    /// the word processor's equivalent leaves them alone for the same reason — declaring a family
    /// class changes the answer for every name in the deck and has never been measured on a slide,
    /// where a declared *pitch* has now been measured twice.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Settable rather than <c>init</c>-only because a PPT's font collection lives inside the
    /// <c>Environment</c> container, which is not read until layout starts and so is not available
    /// where the cache is constructed. The delegate is wired once, before any request reaches it.
    /// </remarks>
    public Func<string, FontPitch>? DeclaredPitches { get; set; }

    /// <summary>
    /// The face the deck carries for a request, when it carries one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Answers a path to the face, which is what <see cref="FontRequest.EmbeddedFaceKey"/> takes
    /// and what every backend downstream of resolution can open. Null means the deck embeds
    /// nothing usable for this family, which is true of all but three documents in the slides
    /// track, so that is the path that has to stay cheap.
    /// </para>
    /// <para>
    /// The weight and the slant are arguments rather than the family alone, because one
    /// <c>p:embeddedFont</c> carries up to four styles under a single name and a run picks among
    /// them. Beside <see cref="DeclaredPitches"/> and settable for the same reason: the deck's
    /// font list lives on a part the format-specific layout owns.
    /// </para>
    /// </remarks>
    public Func<string, int, bool, string?>? EmbeddedFaces { get; set; }

    /// <summary>
    /// The pitch in a Windows <c>LOGFONT.lfPitchAndFamily</c> byte.
    /// </summary>
    /// <remarks>
    /// Shared by both readers because both formats carry the same byte: PPTX writes it as the
    /// decimal <c>pitchFamily</c> attribute, PPT as the last byte of a <c>FontEntityAtom</c>, and
    /// the WW8 font table as <c>FFN.prq</c>. The low two bits are the pitch and the high four the
    /// family; only the pitch is read here.
    /// </remarks>
    /// <param name="pitchAndFamily">The byte, as written.</param>
    public static FontPitch PitchIn(int pitchAndFamily)
        => (pitchAndFamily & 0x03) switch
        {
            1 => FontPitch.Fixed,
            2 => FontPitch.Variable,
            _ => FontPitch.Unknown,
        };

    /// <summary>The substitutions made so far, which is the first thing a comparison checks.</summary>
    public IReadOnlyList<FontSubstitution> Substitutions => _fonts.Substitutions;

    /// <summary>The face and reference a request resolves to, both null when nothing could be read.</summary>
    public (OpenTypeFace? Face, FontReference? Reference) Resolve(
        string? family, int weight, bool isItalic)
    {
        // The pitch is a property of the typeface rather than of the request, so it adds nothing to
        // the key: two requests naming the same family declare the same pitch.
        (string?, int, bool) key = (family, weight, isItalic);
        if (_resolved.TryGetValue(key, out (OpenTypeFace?, FontReference?) cached)) return cached;

        FontPitch pitch = family is { Length: > 0 } named && DeclaredPitches is { } declared
            ? declared(named)
            : FontPitch.Unknown;

        // The deck's own copy of the face, when it has one. It wins over everything installed and
        // over the whole substitution chain, because it is the face the author measured against —
        // see `FontRequest.EmbeddedFaceKey`.
        string? embedded = family is { Length: > 0 } carried && EmbeddedFaces is { } faces
            ? faces(carried, weight, isItalic)
            : null;

        (OpenTypeFace? Face, FontReference? Reference) resolved = default;
        try
        {
            FontReference reference = _fonts.Resolve(
                new FontRequest(family ?? string.Empty, weight, isItalic, pitch, embedded));
            resolved = (_fonts.LoadOpenType(reference), reference);
        }
        catch (Exception exception) when (exception is Core.MalformedDocumentException
                                             or IOException
                                             or UnauthorizedAccessException)
        {
            // A face that cannot be read costs the shape its text, not the deck its layout.
        }

        _resolved[key] = resolved;
        return resolved;
    }
}
