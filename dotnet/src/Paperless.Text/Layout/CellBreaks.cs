using System.Globalization;

namespace Paperless.Text.Layout;

/// <summary>
/// A stretch of a paragraph that breaks between <em>characters</em> rather than between words.
/// </summary>
/// <remarks>
/// <para>
/// EditEngine's field. A field is one portion of the line whatever its length, and when it is
/// wider than the room left on the line it is not moved down and it is not offered to the break
/// iterator: <c>ImpEditEngine::CreateLines</c>' <c>EE_FEATURE_FIELD</c> branch walks the field's
/// own text with <c>XBreakIterator::nextCharacters(…, CharacterIteratorMode::SKIPCELL, …)</c> and
/// records a break wherever the next <em>cell</em> would overflow
/// (<c>editeng/source/editeng/impedit3.cxx</c>:1101-1200). A cell is a grapheme cluster, so on
/// Latin text that is an opportunity at every character.
/// </para>
/// <para>
/// That is the whole of <em>“the reference breaks a long URL mid-token where we break only at
/// <c>-</c> and <c>/</c>”</em>: it is neither a URL rule nor a hyphenation rule, it is what a
/// field does — and a <c>text:a</c> in a draw shape's text <em>is</em> a field, because
/// <c>txtparai.cxx</c>:1352-1370 picks <c>XMLUrlFieldImportContext</c> whenever the cursor has no
/// <c>HyperLinkURL</c> property, which is every Draw and Impress text.
/// </para>
/// <para>
/// Two consequences for the fill loop, and the second is as important as the first. The
/// opportunities inside such a stretch are <em>added</em> to the break iterator's, so a field
/// that fits is still placed whole and a field that does not is filled to the character rather
/// than pushed onto the next line. And the solidus glue — see
/// <c>LineFiller.GluedAcrossSolidus</c> — must not fire on a break inside one, because that rule
/// belongs to <c>BreakIterator_Unicode::getLineBreak</c> and a field never asks it anything.
/// </para>
/// </remarks>
/// <param name="Start">The stretch's first character.</param>
/// <param name="Length">How many characters it covers.</param>
public readonly record struct CellBrokenSpan(int Start, int Length)
{
    /// <summary>One past the stretch's last character.</summary>
    public int End => Start + Length;

    /// <summary>
    /// Whether a line ending at <paramref name="index"/> would break <em>inside</em> the stretch.
    /// </summary>
    /// <remarks>
    /// Strictly inside: a break at the stretch's own start or end is an ordinary one, decided by
    /// the text around it, and is not the field's rule.
    /// </remarks>
    public bool BreaksInside(int index) => index > Start && index < End;
}

/// <summary>
/// Adds the break opportunities a <see cref="CellBrokenSpan"/> carries to a paragraph's own.
/// </summary>
internal static class CellBreaks
{
    /// <summary>
    /// Whether any of the stretches breaks strictly inside itself at <paramref name="index"/>.
    /// </summary>
    public static bool BreaksInside(IReadOnlyList<CellBrokenSpan>? spans, int index)
    {
        if (spans is null) return false;

        foreach (CellBrokenSpan span in spans)
        {
            if (span.BreaksInside(index)) return true;
        }

        return false;
    }

    /// <summary>
    /// The paragraph's break opportunities with a cell boundary added inside every stretch.
    /// </summary>
    /// <remarks>
    /// The boundaries are grapheme clusters rather than UTF-16 indices, which is what
    /// <c>SKIPCELL</c> means: a surrogate pair, a combining sequence and a regional-indicator pair
    /// are each one cell, so none of them may be split. On the ASCII a URL is made of the two
    /// coincide, which is why this is stated rather than assumed.
    /// </remarks>
    /// <param name="opportunities">What the break iterator offered, ascending.</param>
    /// <param name="spans">The cell-broken stretches, which may overlap or repeat.</param>
    /// <param name="text">The paragraph's text.</param>
    public static IReadOnlyList<int> Merge(
        IReadOnlyList<int> opportunities, IReadOnlyList<CellBrokenSpan> spans, string text)
    {
        ArgumentNullException.ThrowIfNull(opportunities);
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(text);

        bool[] added = new bool[text.Length + 1];
        bool any = false;

        foreach (CellBrokenSpan span in spans)
        {
            int start = Math.Max(0, span.Start);
            int end = Math.Min(text.Length, span.End);
            if (end - start <= 1) continue;

            TextElementEnumerator cells =
                StringInfo.GetTextElementEnumerator(text[start..end]);

            while (cells.MoveNext())
            {
                int at = start + cells.ElementIndex;
                if (at <= start || at >= end) continue;

                added[at] = true;
                any = true;
            }
        }

        if (!any) return opportunities;

        foreach (int at in opportunities)
        {
            if (at >= 0 && at < added.Length) added[at] = true;
        }

        List<int> merged = [];
        for (int at = 0; at < added.Length; at++)
        {
            if (added[at]) merged.Add(at);
        }

        return merged;
    }
}
