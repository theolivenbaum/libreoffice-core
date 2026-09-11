using Paperless.Core.Graphics;

namespace Paperless.Spreadsheets.Layout;

/// <summary>
/// The bar a <c>dataBar</c> conditional format draws over one cell, already resolved to
/// percentages of that cell's own width.
/// </summary>
/// <remarks>
/// <para>
/// The reference splits the same job in the same place. <c>ScDataBarFormat::GetDataBarInfo</c>
/// (<c>sc/source/core/data/colorscale.cxx</c>:968-1090) turns the rule, the cell's value and the
/// numbers in the rule's range into an <c>ScDataBarInfo</c> holding a length and a zero position
/// as percentages and a colour; <c>drawDataBars</c> (<c>sc/source/ui/view/output.cxx</c>:883-950)
/// is the only code that knows where the cell is. Everything here is the first half's output, so
/// the drawing side needs the cell rectangle and nothing else.
/// </para>
/// <para>
/// A conditional <em>bar</em> and not a conditional fill: it covers part of the cell, it is drawn
/// over whatever the cell's own background is rather than replacing it, and — like the colour
/// scale beside it — it must never reach <see cref="SheetDecorationArea"/>, because a rule stated
/// over a million rows would otherwise decide how far the sheet prints.
/// </para>
/// </remarks>
public readonly record struct SheetDataBar
{
    /// <summary>The colour the bar is painted in.</summary>
    /// <remarks>
    /// The rule's positive colour, or its negative one when the cell's value is below zero and
    /// the rule states that a negative colour exists at all — <c>mbNeg</c>, which only an
    /// <c>x14:negativeFillColor</c> sets. With no negative colour stated the bar is
    /// <c>COL_LIGHTRED</c>; <c>GetDataBarInfo</c>:1062-1078.
    /// </remarks>
    public Colour Colour { get; init; }

    /// <summary>
    /// How far the bar reaches from the axis, as a percentage of the cell's usable width.
    /// </summary>
    /// <remarks>
    /// Signed: negative reaches left of <see cref="Zero"/> and positive right of it, and zero
    /// draws nothing at all — <c>drawDataBars</c> returns before painting anything, which is why
    /// a cell sitting exactly on an automatic minimum shows no bar.
    /// </remarks>
    public double Length { get; init; }

    /// <summary>Where the axis sits, as a percentage of the cell's usable width from its left.</summary>
    public double Zero { get; init; }

    /// <summary>The colour of the dashed axis line, drawn only when <see cref="HasAxis"/>.</summary>
    public Colour AxisColour { get; init; }

    /// <summary>Whether the cell's own value is still drawn over the bar.</summary>
    /// <remarks>
    /// <c>showValue="0"</c> on the rule. <c>ScOutputData::DrawStrings</c> clears <c>bDoCell</c>
    /// for such a cell (<c>sc/source/ui/view/output2.cxx</c>:1691-1697), so the number is not
    /// drawn at all — not merely hidden behind the bar.
    /// </remarks>
    public bool ShowValue { get; init; }

    /// <summary>Whether a gradient was asked for, which this tree paints as a solid bar.</summary>
    /// <remarks>
    /// <para>
    /// True is <c>ScDataBarFormatData</c>'s own default and false is what every corpus rule
    /// states, through <c>x14:dataBar/@gradient="0"</c>. The reference's true branch is
    /// <c>DrawGradient</c> with a linear gradient from the bar's colour to <c>COL_TRANSPARENT</c>
    /// and 255 steps; its PDF of <c>sheet-cf-data-bar-lengths.xlsx</c> comes back as 209 slices
    /// fading from <c>#2e75b6</c> to white, a count VCL derives from the device's own pixel width
    /// and which nothing here can reproduce. **0 of the 9 corpus <c>dataBar</c> rules state it**,
    /// so it is recorded rather than modelled and such a bar is painted solid.
    /// </para>
    /// </remarks>
    public bool Gradient { get; init; }

    /// <summary>
    /// Whether the dashed vertical axis is drawn, which needs a zero strictly inside the cell.
    /// </summary>
    /// <remarks>
    /// <c>if(!(mnZero &amp;&amp; mnZero != 100)) return;</c> — a bar whose range holds no negative
    /// value has its zero at the left edge and draws none, and one entirely below zero has it at
    /// the right edge and draws none either.
    /// </remarks>
    public bool HasAxis => Zero != 0 && Zero != 100;
}
