using Paperless.Core.Graphics;

namespace Paperless.Text.Fonts;

/// <summary>
/// Turns a font request from a document into a concrete face that exists on this
/// machine.
/// </summary>
/// <remarks>
/// <para>
/// This is the single largest source of divergence between Paperless output and any
/// reference renderer, so it is deliberately pluggable and deliberately explicit
/// about what it did.
/// </para>
/// <para>
/// To match LibreOffice, a resolver must reproduce its substitution order: the
/// document's own font table, then LibreOffice's built-in substitution tables, then
/// the platform's (fontconfig on Linux), then a last-resort default. The
/// metric-compatible pairs matter most in practice — Calibri to Carlito, Cambria to
/// Caladea, Arial to Liberation Sans, Times New Roman to Liberation Serif — because
/// those substitutions preserve advance widths and so preserve line breaks. A
/// non-metric-compatible substitution reflows the text and every subsequent page
/// diverges. See <c>dotnet/research/06-rendering.md</c> section B.
/// </para>
/// </remarks>
public interface IFontResolver
{
    /// <summary>
    /// Resolves a request to an available face, substituting when necessary. Never
    /// returns null: a last-resort fallback is always chosen so rendering can proceed.
    /// </summary>
    FontReference Resolve(FontRequest request);

    /// <summary>Loads the face data behind a resolved reference.</summary>
    IFontFace LoadFace(FontReference reference);
}

/// <summary>A font as a document asks for it.</summary>
/// <param name="FamilyName">The requested family name.</param>
/// <param name="Weight">Requested weight on the OpenType 1-1000 scale.</param>
/// <param name="IsItalic">Whether italic was requested.</param>
/// <param name="Pitch">The requested pitch, used as a substitution hint.</param>
/// <param name="EmbeddedFaceKey">
/// A key into the document's own embedded fonts, when it embeds one for this request.
/// Embedded faces always win: they are what the author saw.
/// </param>
/// <param name="DeclaredFamily">
/// The family class the document stated beside the name, which decides the substitute when the name
/// itself is not installed. See <see cref="DeclaredFontFamily"/>.
/// </param>
public readonly record struct FontRequest(
    string FamilyName,
    int Weight = 400,
    bool IsItalic = false,
    FontPitch Pitch = FontPitch.Unknown,
    string? EmbeddedFaceKey = null,
    DeclaredFontFamily DeclaredFamily = DeclaredFontFamily.Unknown);

/// <summary>
/// The family class a document states beside a font's name.
/// </summary>
/// <remarks>
/// <para>
/// Every format carries it — WW8's <c>FFN.ff</c>, OOXML's <c>w:font/w:family</c>, ODF's
/// <c>style:font-family-generic</c>, RTF's <c>\froman</c> and friends — and it is not decoration. It is
/// what LibreOffice hands fontconfig as a <em>second</em> family when the named one is absent:
/// <c>FontConfigManager::Substitute</c> (<c>vcl/unx/generic/font/fontconfig.cxx</c>) adds the requested
/// name as <c>FC_FAMILY</c> and then appends <c>"serif"</c> for <see cref="Roman"/> and <c>"sans"</c> for
/// <see cref="Swiss"/> — and nothing at all for any other value, which is why the rest are distinguished
/// here but behave alike.
/// </para>
/// <para>
/// The consequence is measurable and large: a document naming <c>Times</c> with no class renders in
/// Liberation Serif, and the same document naming <c>Times</c> as a roman renders in DejaVu Serif, whose
/// glyphs are wider — about 11% fewer characters to the line, and a page more over four pages.
/// </para>
/// </remarks>
public enum DeclaredFontFamily
{
    /// <summary>The document states no class, or states one this reader does not recognise.</summary>
    Unknown = 0,

    /// <summary>A serif face: WW8 <c>FF_ROMAN</c>, ODF <c>roman</c>, RTF <c>\froman</c>.</summary>
    Roman,

    /// <summary>A grotesque: WW8 <c>FF_SWISS</c>, ODF <c>swiss</c>, RTF <c>\fswiss</c>.</summary>
    Swiss,

    /// <summary>A monospaced face: WW8 <c>FF_MODERN</c>, ODF <c>modern</c>, RTF <c>\fmodern</c>.</summary>
    /// <remarks>
    /// Recorded and deliberately inert. LibreOffice appends no generic family for it — measured, a
    /// document naming <c>Times</c> as <c>modern</c> still renders in Liberation Serif — so treating it
    /// as monospaced would invent a substitution the reference does not make.
    /// </remarks>
    Modern,

    /// <summary>A script face: WW8 <c>FF_SCRIPT</c>, ODF <c>script</c>. Inert, as <see cref="Modern"/>.</summary>
    Script,

    /// <summary>A display face: WW8 <c>FF_DECORATIVE</c>. Inert, as <see cref="Modern"/>.</summary>
    Decorative,
}

/// <summary>Whether a font is proportional or fixed-width.</summary>
public enum FontPitch
{
    /// <summary>Not stated by the document.</summary>
    Unknown = 0,

    /// <summary>Proportionally spaced.</summary>
    Variable,

    /// <summary>Fixed-width.</summary>
    Fixed,
}

/// <summary>A loaded font face: metrics, character coverage and glyph outlines.</summary>
public interface IFontFace : IDisposable
{
    /// <summary>The reference this face was loaded from.</summary>
    FontReference Reference { get; }

    /// <summary>
    /// Design units per em, from the font's <c>head</c> table. Glyph metrics are
    /// expressed in these units and scale linearly with the em size.
    /// </summary>
    int UnitsPerEm { get; }

    /// <summary>
    /// The vertical metrics used to derive line height.
    /// </summary>
    /// <remarks>
    /// Which of a font's several competing metric sets to believe is not obvious, and
    /// getting it wrong shifts every baseline on the page. LibreOffice's precedence
    /// rules — hhea versus OS/2 <c>usWin*</c> versus OS/2 typo metrics, plus
    /// per-font overrides — are documented in
    /// <c>dotnet/research/06-rendering.md</c> section B and must be reproduced here.
    /// </remarks>
    FontVerticalMetrics VerticalMetrics { get; }

    /// <summary>True when the face has a glyph for the given Unicode scalar value.</summary>
    bool HasGlyphFor(int codePoint);
}

/// <summary>
/// The vertical metrics that determine baseline placement and line height, in font
/// design units.
/// </summary>
/// <param name="Ascent">Distance above the baseline.</param>
/// <param name="Descent">Distance below the baseline, as a positive value.</param>
/// <param name="LineGap">Extra leading between lines.</param>
/// <param name="UnderlinePosition">Underline offset from the baseline, negative below.</param>
/// <param name="UnderlineThickness">Underline stroke width.</param>
/// <param name="StrikeoutPosition">Strikethrough offset from the baseline.</param>
/// <param name="StrikeoutThickness">Strikethrough stroke width.</param>
public readonly record struct FontVerticalMetrics(
    int Ascent,
    int Descent,
    int LineGap,
    int UnderlinePosition,
    int UnderlineThickness,
    int StrikeoutPosition,
    int StrikeoutThickness);
