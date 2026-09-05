using Paperless.Core.Units;

namespace Paperless.Text.Layout;

/// <summary>
/// How far a justified line may squeeze its blanks below their natural width.
/// </summary>
/// <remarks>
/// <para>
/// A justified line has always been able to <em>stretch</em> its blanks; Word 2013 also lets it
/// <em>compress</em> them, which means a line can hold text that does not fit it at natural widths. The
/// consequence is not cosmetic: the same text sets in fewer lines, so a document paginates shorter.
/// LibreOffice states it in as many words where it turns the behaviour on —
/// <c>"new paragraph justification has been introduced in version 15, breaking text layout
/// interoperability: new line shrinking needs less space i.e. it typesets the same text with less lines
/// and pages"</c>, <c>sw/source/writerfilter/dmapper/DomainMapper_Impl.cxx:10172</c>, which sets
/// <c>JustifyLinesWithShrinking</c> for every file whose <c>compatibilityMode</c> is 15 or more.
/// </para>
/// <para>
/// The rule is applied in <c>SwTextPortion::Format_</c>
/// (<c>sw/source/core/text/portxt.cxx:545</c>): having guessed a break the ordinary way, a full
/// justified line is guessed again with its blanks at a <em>minimum word spacing</em>, and the longer
/// guess taken. For a file that says nothing more than <c>compatibilityMode</c> — every Word 2013 file,
/// which is what <c>bOldInterop</c> names there — that minimum is
/// <see cref="MinimumBlankProportion">75%</see>.
/// </para>
/// <para>
/// Stated as an allowance rather than as a second break attempt, because a greedy filler already walks
/// the break opportunities in order: a candidate line fits when its natural width is within its room plus
/// <see cref="AllowanceFor"/>, which is exactly "every blank on it can be squeezed to 75% and it then
/// fits". That formulation is self-consistent — the line finally chosen satisfies the constraint it was
/// measured against — where the reference's is an estimate, since it has to guess the blank count of a
/// line it has not broken yet and adds one for the space the new word brings with it.
/// </para>
/// <para>
/// Measured on <c>BID_ACKNOWLEDGEMENT_FORM_FOR_A320.docx</c>, whose first justified line holds sixteen
/// words summing to 417.63 pt in a 468.0 pt column: we set fifteen gaps at 3.358 pt and the reference
/// seventeen words with sixteen gaps at 1.894 pt, against a natural Carlito space of 2.26 pt. That is a
/// blank at 83.8% of its natural width — inside the 75% floor, and the reason the reference fits a word
/// we do not.
/// </para>
/// <para>
/// <b>This is 24.2.7.2's rule and 26.2.4.2 no longer takes the maximum shrink.</b> The floor is still
/// 75% for a <c>bOldInterop</c> file, but `portxt.cxx`:531-812 is now a weighted decision rather than
/// "guess again and take the longer": having guessed at the minimum spacing it searches forward for a
/// break point <em>nearer to normal spacing</em> (the <c>bNewBreak</c> loop at :720), and it only
/// prefers the shrunk guess when the un-shrunk one would stretch past the maximum word spacing or when
/// the shrunk spacing is better on a weight of <c>1/1.7</c>. The rewrite carries tdf#158776, tdf#158436
/// and tdf#164499 and brings word-spacing minimum, maximum and desired plus a hyphenation-zone level
/// with it.
/// </para>
/// <para>
/// Measured on the corpus pair, both documents through both binaries: 24.2.7.2 sets
/// <c>justify-shrink-2013.docx</c> in <b>4</b> lines and <c>justify-shrink-2007.docx</c> in 5;
/// 26.2.4.2 sets <b>both in 5</b>, with the mode-15 document's last line ending at 113.70 pt against
/// the mode-12 one's 164.92 — so shrinking still carries 51 pt more text into the lines above and no
/// longer saves the line. We reproduce 24.2.7.2 exactly (4 and 5). Following 26.2.4.2 means porting
/// that decision, which is a body of work rather than a constant, and it is open.
/// </para>
/// </remarks>
public static class JustificationShrink
{
    /// <summary>
    /// The narrowest a blank may be squeezed to, as a proportion of its natural width.
    /// </summary>
    /// <remarks>
    /// Seventy-five per cent — <c>nMinimum = bOldInterop ? 75 : …</c> in
    /// <c>SwTextPortion::Format_</c> (<c>sw/source/core/text/portxt.cxx</c>), where
    /// <c>bOldInterop</c> is the file that asks for shrinking and states no word-spacing bounds of its
    /// own, which is every Word 2013 and later document.
    /// </remarks>
    public const double MinimumBlankProportion = 0.75;

    /// <summary>
    /// How much wider than its room a line may be, given the blanks it holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A quarter of the natural width of the line's blanks, which is what squeezing each of them to
    /// <see cref="MinimumBlankProportion"/> recovers. Blanks are measured one at a time out of the
    /// paragraph's own prefix widths rather than from a nominal space width, so a line mixing sizes or
    /// faces is charged what its own blanks are worth. LibreOffice measures ten spaces in the line's
    /// current font and divides by ten, which is the same quantity for the uniform line and an
    /// approximation for the mixed one.
    /// </para>
    /// <para>
    /// A line holding a tab gets nothing. Writer refuses to shrink a tabulated line outright
    /// (<c>tdf#164499</c>, the <c>InTabGrp</c> test at <c>portxt.cxx</c>:571), and it has to be refused
    /// here as well as where the line is justified: a line admitted on an allowance it is then not
    /// squeezed by would simply run past the margin.
    /// </para>
    /// </remarks>
    /// <param name="text">The paragraph's text.</param>
    /// <param name="start">Where the line starts.</param>
    /// <param name="end">Where its visible text ends.</param>
    /// <param name="widthBetween">The natural width of a range of the paragraph.</param>
    public static Length AllowanceFor(
        string text, int start, int end, Func<int, int, Length> widthBetween)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(widthBetween);

        if (end > text.Length) end = text.Length;
        if (end <= start) return Length.Zero;
        if (TabRuler.HasTab(text, start, end)) return Length.Zero;

        long blanks = 0;

        for (int at = start; at < end; at++)
        {
            if (text[at] == ' ') blanks += widthBetween(at, at + 1).Emu;
        }

        return Length.FromEmu((long)(blanks * (1.0 - MinimumBlankProportion)));
    }
}
