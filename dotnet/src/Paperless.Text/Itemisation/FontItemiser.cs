using Paperless.Text.Fonts;

namespace Paperless.Text.Itemisation;

/// <summary>A stretch of text that one face can draw.</summary>
/// <param name="Start">Its first character, as an index into the text.</param>
/// <param name="Length">How many UTF-16 code units it covers.</param>
/// <param name="Face">The face that will draw it.</param>
/// <param name="IsFallback">True when this is not the face the run asked for.</param>
public readonly record struct FaceRun(int Start, int Length, OpenTypeFace Face, bool IsFallback)
{
    /// <summary>One past the run's last character.</summary>
    public int End => Start + Length;
}

/// <summary>
/// Splits a run further where its own face has no glyph for what it contains.
/// </summary>
/// <remarks>
/// <para>
/// The half of font fallback that was missing: coverage has been queryable since the OpenType reader
/// was written, and choosing the replacement and cutting the run at it is what turns a query into a
/// rendered page. Without it a run set in a face that lacks, say, Hebrew draws a row of
/// missing-glyph boxes at whatever width that face gives <c>.notdef</c> — which is both visibly
/// wrong and, because the width is wrong too, breaks the line in the wrong place.
/// </para>
/// <para>
/// LibreOffice does this after shaping rather than before: it lays the run out in the primary face,
/// collects the characters that came back as <c>.notdef</c>, and lays those out again in a fallback
/// face, stacking up to <c>MAX_FALLBACK</c> layouts in a <c>MultiSalLayout</c>
/// (<c>OutputDevice::ImplGlyphFallbackLayout</c>, <c>vcl/source/outdev/font.cxx</c>). Splitting
/// beforehand on the <c>cmap</c> gives the same partition for everything but a font whose
/// <c>cmap</c> claims a character its outlines do not have, and it costs one pass instead of two.
/// </para>
/// <para>
/// A non-spacing mark is kept with what it is attached to wherever it can be. A base and its mark
/// shaped in two different faces is not a mark on that base — it is a mark on nothing, positioned by
/// a font that never saw the letter — so the mark follows the preceding face whenever that face can
/// draw it at all.
/// </para>
/// </remarks>
public static class FontItemiser
{
    /// <summary>
    /// Splits a range of text into the faces that can draw it.
    /// </summary>
    /// <param name="text">The text the range indexes into.</param>
    /// <param name="start">The range's first character.</param>
    /// <param name="length">How many characters it covers.</param>
    /// <param name="primary">The face the run asked for.</param>
    /// <param name="fallback">Where to look when the primary face has no glyph, or null to not look.</param>
    /// <param name="report">Called once per contiguous stretch that needed a fallback, resolved or not.</param>
    public static List<FaceRun> Split(
        ReadOnlySpan<char> text,
        int start,
        int length,
        OpenTypeFace primary,
        IGlyphFallbackResolver? fallback,
        Action<GlyphFallback>? report = null)
    {
        ArgumentNullException.ThrowIfNull(primary);

        List<FaceRun> runs = [];
        if (length <= 0) return runs;

        if (fallback is null)
        {
            runs.Add(new FaceRun(start, length, primary, IsFallback: false));
            return runs;
        }

        // A pi face is never handed to fontconfig, so a character it lacks is sought only on
        // LibreOffice's own generic list -- see IGlyphFallbackResolver.SymbolFallbackFor, which
        // holds the mechanism and the measurements. Decided once per run rather than per character.
        bool isPiFace = SymbolFontRecode.IsSubstituteFamily(primary.FamilyName);

        int end = start + length;
        int runStart = start;
        OpenTypeFace runFace = primary;
        bool runIsFallback = false;
        bool started = false;
        bool wasMissing = false;
        OpenTypeFace? lastMissingFace = null;

        for (int at = start; at < end;)
        {
            int width = 1;
            int codePoint = text[at];
            if (char.IsHighSurrogate(text[at]) && at + 1 < end && char.IsLowSurrogate(text[at + 1]))
            {
                codePoint = char.ConvertToUtf32(text[at], text[at + 1]);
                width = 2;
            }

            OpenTypeFace face = primary;
            bool isFallback = false;
            bool missing = false;

            if (!primary.HasGlyphFor(codePoint) && !IsNeverDrawn(codePoint))
            {
                // A mark the current face can draw stays with the base it is attached to, whatever
                // the primary face says: the mark is positioned against the base's outline.
                bool markOnCurrent = started
                    && BidiProperties.ClassOf(codePoint) == BidiClass.NSM
                    && runFace.HasGlyphFor(codePoint);

                if (markOnCurrent)
                {
                    face = runFace;
                    isFallback = runIsFallback;
                }
                else if (Sought(fallback, codePoint, primary, isPiFace) is { } found)
                {
                    face = found;
                    isFallback = true;
                    missing = true;
                }
                else
                {
                    // Nothing installed has it. The primary face draws its missing-glyph box, which
                    // is what LibreOffice does too once its fallback chain is exhausted — but the
                    // caller is told, because a box on a page is worth explaining.
                    missing = true;
                }
            }

            // Reported once per stretch rather than once per character, so a paragraph in a script
            // the face does not cover leaves one entry and not a thousand. The character named is the
            // first of the stretch, which is what a caller needs to find it in the text.
            if (missing && (!wasMissing || !ReferenceEquals(face, lastMissingFace)))
            {
                report?.Invoke(new GlyphFallback(
                    codePoint, primary.FamilyName, isFallback ? face.FamilyName : null));
            }

            wasMissing = missing;
            lastMissingFace = missing && isFallback ? face : null;

            if (!started)
            {
                runFace = face;
                runIsFallback = isFallback;
                started = true;
            }
            else if (!ReferenceEquals(face, runFace))
            {
                runs.Add(new FaceRun(runStart, at - runStart, runFace, runIsFallback));
                runStart = at;
                runFace = face;
                runIsFallback = isFallback;
            }

            at += width;
        }

        runs.Add(new FaceRun(runStart, end - runStart, runFace, runIsFallback));
        return runs;
    }

    /// <summary>
    /// True when a face cannot draw everything in a range, so <see cref="Split"/> would cut it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Split"/>'s question without <see cref="Split"/>'s list, and it exists because
    /// measuring and drawing have to answer it the same way. The drawing pass cuts <em>every</em>
    /// paragraph by face; measurement reaches this class only through <c>MeasuredParagraph</c>, which
    /// a paragraph of uniform formatting deliberately skips. That shortcut is sound only while the
    /// paragraph's own face can draw its own text — and when it cannot, the paragraph was drawn in a
    /// fallback face and measured in a face that has no glyph for a character of it, at that face's
    /// <c>.notdef</c> width.
    /// </para>
    /// <para>
    /// Measured on <c>手机免提系统TSB.doc</c>: 106 Chinese characters measured through Liberation
    /// Serif's <c>.notdef</c>, 1593 of 2048 units, are 0.778 em each against the em apiece WenQuanYi
    /// Zen Hei actually drew — so a line filled to 44 characters where 34 fit, running 6.8 pt past
    /// the page's own edge. Asking this first is what keeps the uniform shortcut a shortcut and lets
    /// it be taken only where it is equivalent.
    /// </para>
    /// </remarks>
    /// <summary>Where a missing character is looked for, which depends on the face it is missing from.</summary>
    private static OpenTypeFace? Sought(
        IGlyphFallbackResolver fallback, int codePoint, OpenTypeFace primary, bool isPiFace)
        => isPiFace
            ? fallback.SymbolFallbackFor(codePoint, primary.Weight, primary.IsItalic)
            : fallback.FallbackFor(codePoint, primary.Weight, primary.IsItalic);

    /// <param name="text">The text to check.</param>
    /// <param name="primary">The face the run asked for.</param>
    public static bool NeedsFallback(ReadOnlySpan<char> text, OpenTypeFace primary)
    {
        ArgumentNullException.ThrowIfNull(primary);

        for (int at = 0; at < text.Length;)
        {
            int width = 1;
            int codePoint = text[at];
            if (char.IsHighSurrogate(text[at]) && at + 1 < text.Length
                && char.IsLowSurrogate(text[at + 1]))
            {
                codePoint = char.ConvertToUtf32(text[at], text[at + 1]);
                width = 2;
            }

            if (!primary.HasGlyphFor(codePoint) && !IsNeverDrawn(codePoint)) return true;

            at += width;
        }

        return false;
    }

    /// <summary>
    /// True for a character no font is expected to have a glyph for, and no layout draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A tab is not in any font's <c>cmap</c>.</strong> Liberation Sans maps U+0020 and not
    /// U+0009, U+000A or U+000D, so a coverage check run over ordinary prose reports every tab and
    /// every line break as missing — and the search then finds *some* installed face whose cmap does
    /// map the control range, splits the run there, and measures the two halves without the shaping
    /// context that joined them. Measured: wiring fallback in without this test turned
    /// <c>johnson_hall_service_log.pdf.docx</c>, a one-page form holding 36 tabs, into two pages,
    /// while leaving every glyph on page one in exactly the position it had before. It is the only
    /// document of the words track's 200 that moved adversely, and it moved for a character that is
    /// never drawn at all.
    /// </para>
    /// <para>
    /// LibreOffice never asks the question, because it falls back <em>after</em> shaping — by then
    /// the layout has turned a tab into a tab portion and a break into a line, so neither is in the
    /// text handed to a shaper. Splitting on the cmap beforehand is a whole pass cheaper and this is
    /// the one place the two orders disagree.
    /// </para>
    /// <para>
    /// Format characters (Cf) join the control ones: a zero-width joiner or a bidi mark is an
    /// instruction to the shaper rather than a mark on the page, and a face that lacks it should not
    /// pull the text around it into a different font.
    /// </para>
    /// </remarks>
    private static bool IsNeverDrawn(int codePoint)
        => codePoint <= 0x10FFFF
            && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(codePoint)
                is System.Globalization.UnicodeCategory.Control
                or System.Globalization.UnicodeCategory.Format
                or System.Globalization.UnicodeCategory.LineSeparator
                or System.Globalization.UnicodeCategory.ParagraphSeparator;
}
