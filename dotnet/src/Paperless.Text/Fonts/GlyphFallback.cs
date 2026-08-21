namespace Paperless.Text.Fonts;

/// <summary>
/// Chooses a face for a character the face in force cannot draw.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IFontResolver"/> because it answers a different question. A resolver
/// answers "what did the author mean by <em>Calibri</em>"; this answers "the face I have has no
/// glyph for this character, what does". The first is asked once per run, the second only for the
/// characters a run cannot show, and a document that needs neither should pay for neither.
/// </para>
/// <para>
/// LibreOffice asks the platform first — fontconfig's <c>FcFontMatch</c> with the missing characters
/// as a charset, in <c>vcl/unx/generic/fontmanager/fontconfig.cxx</c> — and falls back to a
/// hard-coded list of families when that fails. Paperless takes the second half as its main path,
/// deliberately: going through fontconfig for substitution would add a second source of truth rather
/// than the missing one. It reads the platform's configuration for one thing only — the order in
/// which faces are tried once <em>nothing</em> on that hard-coded list is installed, where there is
/// no other source of truth to compete with. See <see cref="FontconfigPreferences"/>, which records
/// the measurement showing that this last step's answer cannot be derived from the font files.
/// </para>
/// </remarks>
public interface IGlyphFallbackResolver
{
    /// <summary>
    /// A face that can draw a character, or null when nothing installed can.
    /// </summary>
    /// <param name="codePoint">The character the primary face has no glyph for.</param>
    /// <param name="weight">The weight to match, on the OpenType 1-1000 scale.</param>
    /// <param name="isItalic">Whether an italic face is wanted.</param>
    OpenTypeFace? FallbackFor(int codePoint, int weight = 400, bool isItalic = false);

    /// <summary>
    /// The reference naming a face this resolver returned, or null when it did not return it.
    /// </summary>
    /// <remarks>
    /// A face on its own is enough to <em>measure</em> and to <em>shape</em>, and not enough to
    /// <em>embed</em>: a PDF writer loads the font program through the reference's face key, so a
    /// fallback face named only by its family reaches the writer with no program behind it and is
    /// announced in the file without being embedded. Measured on <c>手机免提系统TSB.doc</c>, whose
    /// three fallback faces all came out <c>emb no</c> — which the corpus gate scores as a failure,
    /// correctly, because a reader without those fonts installed sees nothing.
    /// <para>
    /// Defaulted to null so an implementation that only answers coverage questions stays valid; the
    /// caller then falls back to naming the face, which is what it did before this existed.
    /// </para>
    /// </remarks>
    Core.Graphics.FontReference? ReferenceFor(OpenTypeFace face) => null;

    /// <summary>
    /// The same reference, carrying the lean a fallback face has no italic of its own to give.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ReferenceFor(OpenTypeFace)"/> is a reverse lookup from a face and says so: it has
    /// <em>no request to compare against</em>, so it cannot answer
    /// <c>LogicalFontInstance::NeedsArtificialItalic()</c> — <em>italic was asked for and the face
    /// that answered has none</em> — and it never has. Every face reached through
    /// <see cref="Itemisation.FontItemiser"/> therefore arrived at the page upright however italic
    /// the run around it was, which is synthetic oblique lost a second time and at a different seat
    /// from the one round 56 closed in the four word-processing readers.
    /// </para>
    /// <para>
    /// The request is the caller's to supply, so it is a parameter rather than something inferred:
    /// a fallback face is by definition not the face the run asked for, and its own
    /// <c>IsItalic</c> is a fact about the substitute rather than about the document. The rule
    /// itself lives here, once, because all three tracks reach it — <c>PageDrawing.ByFace</c>,
    /// <c>SlideTextLayout.Block.FontFor</c> and <c>SheetFonts.ForFallback</c>.
    /// </para>
    /// <para>
    /// Measured against LibreOffice <b>26.2.4.2</b> on 41 authored two-run packages over
    /// <em>six</em> filters — <c>.docx</c>, <c>.fodt</c>, <c>.fodp</c>, <c>.fods</c>, <c>.pptx</c>
    /// and <c>.xlsx</c> — in <c>probes/words-r58/fallback-oblique.py</c> and
    /// <c>fallback-oblique-ooxml.py</c>. The format is varied deliberately, because the one earlier
    /// round that got a fallback question wrong here got it wrong by holding the format fixed. Every
    /// italic case shears on the reference and none did here: CJK to WenQuanYi Zen Hei 6 of 6,
    /// symbols and Hebrew to DejaVu Sans 4 of 4, the same under a bold request, and the same when
    /// the primary face is itself only synthetically oblique. Four negative controls — italic Latin
    /// in a family whose italic <em>is</em> installed, and the identical fallback text with no
    /// italic asked for — are nought on both sides in all six formats.
    /// </para>
    /// <para>
    /// <strong>The reference does not go looking for an italic face, and that is what makes this the
    /// whole of the fix.</strong> Hebrew from an italic <c>Carlito</c> run is covered by DejaVu Sans,
    /// which has no italic here, and by Liberation Sans, which does. 26.2.4.2 draws it in
    /// <b>DejaVu Sans, sheared</b> — its fallback order wins over the slant, exactly as
    /// <see cref="SystemFontResolver.FallbackFor"/> already ranks family above slant. So the face
    /// this interface picks was already right and only the lean was missing.
    /// </para>
    /// </remarks>
    /// <param name="face">A face <see cref="FallbackFor"/> returned.</param>
    /// <param name="isItalicRequested">
    /// Whether the run this face is standing in for asked for italic. That is <em>not</em> the
    /// primary face's <c>IsItalic</c> alone: a primary that is itself being sheared asked for italic
    /// too and has no italic face to prove it with, and the reference shears the fallback in that
    /// case as well.
    /// </param>
    Core.Graphics.FontReference? ReferenceFor(OpenTypeFace face, bool isItalicRequested)
        => ReferenceFor(face) is { } reference
            ? reference with { SyntheticOblique = isItalicRequested && !face.IsItalic }
            : null;
}

/// <summary>One mid-run fallback: a stretch the run's own face could not show.</summary>
/// <remarks>
/// Reported rather than applied silently, for the same reason a family substitution is. A fallback
/// face is almost never metric-compatible with the one it replaces, so the run it lands in measures
/// differently and every line after it can break somewhere else — and a caller comparing against a
/// reference renderer otherwise has no way to tell that from a layout bug. One entry per contiguous
/// stretch rather than per character, so a paragraph in a script the face does not cover leaves one
/// line in the list and not a thousand.
/// </remarks>
/// <param name="CodePoint">The first character of the stretch that was missing.</param>
/// <param name="FromFamily">The family the run was set in.</param>
/// <param name="ToFamily">The family that drew it, or null when nothing could.</param>
public readonly record struct GlyphFallback(int CodePoint, string? FromFamily, string? ToFamily)
{
    /// <summary>True when a face was found; false means the character draws as a missing-glyph box.</summary>
    public bool IsResolved => ToFamily is not null;

    /// <inheritdoc/>
    public override string ToString()
        => IsResolved
            ? $"U+{CodePoint:X4} not in {FromFamily}, drawn from {ToFamily}"
            : $"U+{CodePoint:X4} not in {FromFamily}, and in nothing installed";
}

/// <summary>
/// The generic glyph-fallback list LibreOffice carries, in its own order.
/// </summary>
/// <remarks>
/// Ported verbatim from <c>ImplInitGenericGlyphFallback</c> in
/// <c>vcl/source/font/PhysicalFontCollection.cxx</c>. It is grouped: each group holds families that
/// cover roughly the same characters, so the first installed member of a group is as good as any
/// other and the groups are tried in turn. Porting it rather than inventing an order matters because
/// which face draws a missing character decides its advance width, and therefore where the line
/// holding it breaks.
/// </remarks>
public static class GlyphFallbackFamilies
{
    /// <summary>The families to try, in order, as normalised names.</summary>
    public static IReadOnlyList<string> InOrder { get; } =
    [
        "eudc",
        "arialunicodems", "cyberbit", "code2000",
        "andalesansui",
        "starsymbol", "opensymbol",
        "msmincho", "fzmingti", "fzheiti", "ipamincho", "sazanamimincho", "kochimincho",
        "sunbatang", "sundotum", "baekmukdotum", "gulim", "batang", "dotum",
        "hgmincholightj", "msunglightsc", "msunglighttc", "hymyeongjolightk",
        "tahoma", "dejavusans", "timesnewroman", "liberationsans",
        "shree", "mangal",
        "raavi", "shruti", "tunga",
        "latha", "gautami", "kartika", "vrinda",
        "shayyalmt", "naskmt", "scheherazade",
        "david", "nachlieli", "lucidagrande",
        "norasi", "angsanaupc",
        "khmerossystem",
        "muktinarrow",
        "phetsarathot",
        "padauk", "pinlonmyanmar",
        "iskoolapota", "lklug",
    ];
}
