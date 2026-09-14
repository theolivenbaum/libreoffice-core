using System.Xml.Linq;
using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.Text.Fonts;
using Paperless.Text.Layout;
using Paperless.WordProcessing.Layout;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// The rules an underlined or struck-through run draws under and through itself.
/// </summary>
/// <remarks>
/// <para>
/// <c>w:u</c> and <c>w:strike</c> in a DOCX, <c>sprmCKul</c> and <c>sprmCFStrike</c> in a DOC,
/// <c>\ul</c> and <c>\strike</c> in an RTF, <c>style:text-underline-style</c> in an ODT. Every reader
/// read past them into extraction — which emits <c>&lt;u&gt;</c> correctly — and
/// <see cref="PageRun"/> had no field to put them in, so the word-processing rendering model could not
/// express an underline at all and no document in the corpus had one drawn.
/// </para>
/// <para>
/// Checked at the drawing pass, like the highlight band beside it, because that is where the two halves
/// meet: the flag comes from the run and the rectangle from the face's own metrics and the pen position
/// the tab stops left behind.
/// </para>
/// </remarks>
public sealed class UnderlineTests
{
    private static readonly Length Size = Length.FromPoints(12);

    [Fact]
    public void AnUnderlinedRunDrawsARuleUnderItsOwnGlyphs()
    {
        (List<(GlyphRun Run, Colour Colour)> runs, List<(DocRect Area, Colour Colour)> rules) =
            Draw(Decorated("plain and lined", from: 10, underline: true, strike: false));

        rules.Count.ShouldBe(1);

        // Under the decorated run and only it: the rule starts where that run's glyphs start, and it sits
        // below the baseline rather than on it.
        GlyphRun lined = runs[^1].Run;
        rules[0].Area.X.ShouldBe(lined.Origin.X);
        rules[0].Area.Width.ShouldBeGreaterThan(Length.Zero);
        rules[0].Area.Y.ShouldBeGreaterThan(lined.Origin.Y);
        rules[0].Area.Height.ShouldBeGreaterThan(Length.Zero);
    }

    [Fact]
    public void AStruckRunDrawsARuleThroughItRatherThanUnderIt()
    {
        (List<(GlyphRun Run, Colour Colour)> runs, List<(DocRect Area, Colour Colour)> rules) =
            Draw(Decorated("plain and struck", from: 10, underline: false, strike: true));

        rules.Count.ShouldBe(1);

        // A strikethrough sits *above* the baseline, which is the sign that distinguishes it from an
        // underline and the one an OS/2 table records positively.
        rules[0].Area.Y.ShouldBeLessThan(runs[^1].Run.Origin.Y);
    }

    [Fact]
    public void ARunCarryingBothDrawsBoth()
    {
        (_, List<(DocRect Area, Colour Colour)> rules) =
            Draw(Decorated("plain and both", from: 10, underline: true, strike: true));

        rules.Count.ShouldBe(2);
    }

    [Fact]
    public void APlainParagraphDrawsNoRule()
    {
        (_, List<(DocRect Area, Colour Colour)> rules) =
            Draw(Decorated("plain and plain", from: 10, underline: false, strike: false));

        rules.ShouldBeEmpty();
    }

    [Fact]
    public void ARuleTakesTheColourOfTheTextItDecorates()
    {
        PageParagraph paragraph = new()
        {
            Text = "red and underlined",
            Face = Face,
            EmSize = Size,
            Runs =
            [
                new PageRun(
                    0, 18, Face, Size, Colour: Colour.FromRgb(0xFF0000), Underline: TextUnderline.SingleLine),
            ],
        };

        Draw(paragraph).Rules[0].Colour.ShouldBe(Colour.FromRgb(0xFF0000));
    }

    /// <summary>
    /// The rule is placed from the face's descent, not from its <c>post</c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Liberation Serif is on LibreOffice's shipped <c>FontsDontUseUnderlineMetrics</c> list because its
    /// <c>post</c> table is not to be believed, so the rule has to come from the descent instead. The two
    /// answers are close, which is exactly why this asserts narrowly: at 12 pt this face's
    /// <c>post</c> gives 0.721 pt below the baseline and 0.600 pt thick, and the descent gives 0.973 pt
    /// and 0.700 pt. A band wide enough to hold both would pass either way and prove nothing — a first
    /// draft of this test did precisely that.
    /// </para>
    /// <para>
    /// <b>The thickness is no longer a fraction of the em and the band here is the reference's own
    /// measurement.</b> A rule's weight is a whole number of the PDF writer's 720 dpi pixels
    /// rounded to a whole unit of the map mode the page is painted in, which on a Writer page is
    /// the twip — so this face at 12 pt is drawn at exactly <b>14 twips, 0.700 pt</b>, read off
    /// 26.2.4.2's own PDF (<c>probes/quantise-r120/</c>, the Writer table in
    /// <c>Paperless.Text.Tests.RuleWidthTests</c>). The <c>post</c> branch would give 6 pixels,
    /// 12 twips, 0.600 pt, so the two candidate answers are still a tenth of a point apart and
    /// this still discriminates between them.
    /// </para>
    /// <para>
    /// <b>And the OFFSET is now the reference's own too, which is what the band used to hide.</b>
    /// It was a range because the offset was a fraction of the em and only approximately right;
    /// round 123 took it off the same device chain as the thickness, so it is a constant. This
    /// face at 12 pt has its descent rounded to 26 pixels, <c>nUnderlineOffset = 26/2 + 1</c> = 14
    /// and <c>nLineHeight/2</c> = 3, so the stroke's centre is <c>HCONV(14 - 3 + 3)</c> = <b>28
    /// twips</b> below the baseline and its top edge is 28 − 7 = <b>21 twips, 1.05 pt</b> — read
    /// off 26.2.4.2's own PDF, where the old band's 0.94-1.01 does not reach.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRuleIgnoresAPostTableLibreOfficeRefusesToBelieve()
    {
        (List<(GlyphRun Run, Colour Colour)> runs, List<(DocRect Area, Colour Colour)> rules) =
            Draw(Decorated("underlined", from: 0, underline: true, strike: false));

        // Half a twip, because a stroke's centre is a whole twip and a fill's top edge is that
        // less half its thickness.
        (rules[0].Area.Y - runs[^1].Run.Origin.Y).ShouldBe(Length.FromTwips(28) - Length.FromTwips(7));

        // 14 twips, exactly, because the device answers a whole number of them.
        rules[0].Area.Height.ShouldBe(Length.FromTwips(14));
    }

    /// <summary>
    /// A double underline is two thinner lines, and where the second sits is not a guess.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read off 26.2.4.2's own rendering, and the numbers are not fitted. Liberation Serif is on the
    /// <c>FontsDontUseUnderlineMetrics</c> list so it takes the descent branch: at 12 pt its em is
    /// 120 device pixels at the PDF writer's 720 dpi, its descent rounds to 26, so
    /// <c>n2LineHeight = ((26 × 16) + 50) / 100</c> = 4 pixels and <c>n2LineDY</c> is
    /// <c>max(4, 1 + 720/150)</c> = <b>5</b> — the floor binds, which is why this size was chosen.
    /// <c>nUnderlineOffset</c> is <c>26/2 + 1</c> = 14, so the two tops are at 14 − 2 − 4 = 8 and
    /// 8 + 5 + 4 = 17 pixels, and <c>drawStraightTextLine</c> centres them at
    /// <c>HCONV(8 + 2)</c> = 20 twips and <c>HCONV(17 + 2) + HCONV(4)</c> = 38 + 8 = 46 twips.
    /// </para>
    /// <para>
    /// So the drawn bands are 20 − 4 = <b>16</b> to 24 twips and 46 − 4 = <b>42</b> to 50 twips, each
    /// 8 twips thick. <b>The gap between them is 18 twips, more than twice the thickness</b> — a
    /// reader that put the second line one thickness below the first, which is what the metric alone
    /// suggests and what this tree drew for a Calc cell, would put it at 32 twips.
    /// </para>
    /// <para>
    /// The whole probe behind this is <c>probes/dblunder-r123/</c>: six faces × seven sizes × three
    /// kinds authored as a <c>.fodt</c> and a <c>.fods</c>, rendered twice each, scored
    /// <b>126 of 126 and 126 of 126</b> on thickness and position together.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADoubleUnderlineIsTwoThinnerLinesThreeThicknessesApart()
    {
        (List<(GlyphRun Run, Colour Colour)> runs, List<(DocRect Area, Colour Colour)> rules) =
            Draw(Doubled("double underlined"));

        rules.Count.ShouldBe(2);

        Length baseline = runs[^1].Run.Origin.Y;

        rules[0].Area.Height.ShouldBe(Length.FromTwips(8));
        rules[1].Area.Height.ShouldBe(Length.FromTwips(8));
        (rules[0].Area.Y - baseline).ShouldBe(Length.FromTwips(16));
        (rules[1].Area.Y - baseline).ShouldBe(Length.FromTwips(42));
    }

    /// <summary>
    /// And each of its lines is thinner than the single underline of the same face and size.
    /// </summary>
    /// <remarks>
    /// The half of the register's O68 that was right: both branches make a double line thinner, a
    /// quarter of the descent against sixteen hundredths on this one. Asserted as a comparison
    /// rather than as a second constant, because the constant is already pinned above.
    /// </remarks>
    [Fact]
    public void EachLineOfADoubleUnderlineIsThinnerThanASingleOne()
    {
        (_, List<(DocRect Area, Colour Colour)> single) =
            Draw(Decorated("underlined", from: 0, underline: true, strike: false));
        (_, List<(DocRect Area, Colour Colour)> doubled) = Draw(Doubled("underlined"));

        doubled[0].Area.Height.ShouldBeLessThan(single[0].Area.Height);
    }

    /// <summary>
    /// A decoration must not move a line break.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The invariant the whole change rests on. A reader has to keep a paragraph's runs when any of them
    /// carries a decoration — a property dropped by the uniform-paragraph shortcut is a property never
    /// drawn — and keeping them puts the paragraph on the multi-run measuring path, where a shaper called
    /// once per run loses the kern pair straddling each boundary. So underlining a sentence would make it
    /// fractionally wider, and a paragraph near a line's end would break differently.
    /// </para>
    /// <para>
    /// <see cref="PageParagraph.Measure"/> joins adjacent runs whose measurement halves are equal, which
    /// is what makes this hold. Reintroducing the bug — dropping that join — makes this test fail on a
    /// face with kerning, which Liberation Serif is.
    /// </para>
    /// </remarks>
    [Fact]
    public void UnderliningAParagraphChangesNoWidth()
    {
        // "AV" and "To" are kern pairs in this face, and they straddle the run boundary below.
        const string Text = "AVAVAVA TToToTo AWAY";

        Length plain = Width(Split(Text, at: 7, underline: false));
        Length lined = Width(Split(Text, at: 7, underline: true));

        lined.ShouldBe(plain);

        // And the split itself must be what a single run measures, or the invariant is vacuous.
        Length whole = Width(new PageParagraph
        {
            Text = Text,
            Face = Face,
            EmSize = Size,
            Runs = [new PageRun(0, Text.Length, Face, Size, Colour: Colour.Black)],
        });

        lined.ShouldBe(whole);
    }

    /// <summary>One paragraph, double-underlined end to end.</summary>
    private static PageParagraph Doubled(string text)
        => new()
        {
            Text = text,
            Face = Face,
            EmSize = Size,
            Runs =
            [
                new PageRun(
                    0, text.Length, Face, Size, Colour: Colour.Black,
                    Underline: TextUnderline.DoubleLine),
            ],
        };

    /// <summary>One line or none, for the tests whose variable is only whether there is a rule.</summary>
    private static TextUnderline Ruled(bool underline)
        => underline ? TextUnderline.SingleLine : TextUnderline.None;

    private static Length Width(PageParagraph paragraph)
        => paragraph.Measure().WidthBetween(0, paragraph.Text.Length);

    /// <summary>The same text as two runs, differing only in a decoration.</summary>
    private static PageParagraph Split(string text, int at, bool underline)
        => new()
        {
            Text = text,
            Face = Face,
            EmSize = Size,
            Runs =
            [
                new PageRun(0, at, Face, Size, Colour: Colour.Black, Underline: Ruled(underline)),
                new PageRun(
                    at, text.Length - at, Face, Size,
                    Colour: Colour.Black, Underline: Ruled(underline)),
            ],
        };

    /// <summary>
    /// <c>w:u</c> carries a line style rather than a switch, so <c>none</c> is off and the rest are on.
    /// </summary>
    /// <remarks>
    /// The trap is that <c>w:u w:val="none"</c> is how a run turns off an underline its style set, and
    /// reading the element as an ordinary on/off toggle — which is what every other decoration in
    /// <c>w:rPr</c> is — underlines it instead.
    /// </remarks>
    [Theory]
    [InlineData("single", TextUnderline.SingleLine)]
    [InlineData("dotDotDash", TextUnderline.SingleLine)]
    [InlineData("none", TextUnderline.None)]
    // The two of ST_Underline's eighteen values that draw two lines. `wavyDouble` is
    // `LINESTYLE_DOUBLEWAVE`, and this engine draws no wave, so it comes out as a double line.
    [InlineData("double", TextUnderline.DoubleLine)]
    [InlineData("wavyDouble", TextUnderline.DoubleLine)]
    // `thick` and the six heavy forms are one line at about twice the weight.
    [InlineData("thick", TextUnderline.BoldLine)]
    [InlineData("wavyHeavy", TextUnderline.BoldLine)]
    [InlineData("dashDotDotHeavy", TextUnderline.BoldLine)]
    public void AWordUnderlineIsReadAsAStyleAndNotAsASwitch(string value, TextUnderline expected)
        => Resolved("u", value).Underline.ShouldBe(expected);

    [Theory]
    [InlineData("strike", null, true)]
    [InlineData("dstrike", null, true)]
    [InlineData("strike", "false", false)]
    public void BothOfWordsStrikeElementsFoldOntoOneRule(string name, string? value, bool expected)
        => Resolved(name, value).IsStruckThrough.ShouldBe(expected);

    /// <summary>
    /// <c>sprmCKul</c> is a <c>kul</c> naming a line style, and three of its values name no line.
    /// </summary>
    /// <remarks>
    /// The WW8 spelling of the same trap: nought is "none", 255 cancels what the style set, and 5
    /// ("hidden") and 8 (a dot style Word never writes) have no case in
    /// <c>SwWW8ImplReader::Read_Underline</c>'s switch and so fall through to
    /// <c>LINESTYLE_NONE</c>. Reading the byte as non-zero underlines all three.
    /// </remarks>
    [Theory]
    [InlineData(0, TextUnderline.None)]
    [InlineData(1, TextUnderline.SingleLine)]
    [InlineData(2, TextUnderline.SingleLine)]
    [InlineData(5, TextUnderline.None)]
    // 6 is `LINESTYLE_BOLD` and 27 `LINESTYLE_BOLDWAVE`; both draw one heavy line.
    [InlineData(6, TextUnderline.BoldLine)]
    [InlineData(27, TextUnderline.BoldLine)]
    [InlineData(8, TextUnderline.None)]
    [InlineData(11, TextUnderline.SingleLine)]
    [InlineData(255, TextUnderline.None)]
    // 3 is `LINESTYLE_DOUBLE` and 43 `LINESTYLE_DOUBLEWAVE` (`ww8par6.cxx`:3605, :3619). Operand 3
    // is the corpus's only word-processing double underline, on `RobertQ_Service.doc`'s `Heading`
    // and `Subtitle` styles — 1 of 66 `.doc`, twice.
    [InlineData(3, TextUnderline.DoubleLine)]
    [InlineData(43, TextUnderline.DoubleLine)]
    public void AWordBinaryUnderlineIsAStyleAndThreeStylesAreNoLine(int kul, TextUnderline expected)
        => Ww8.Ww8DocumentReader.UnderlineOf(kul).ShouldBe(expected);

    /// <summary>The character formatting of a run whose <c>w:rPr</c> names one element.</summary>
    private static Ooxml.WordTextStyle Resolved(string name, string? value)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        XElement element = new(w + name);
        if (value is not null) element.Add(new XAttribute(w + "val", value));

        return Ooxml.WordParagraphFormats.ResolveRun(
            new Ooxml.WordStyles(), null, new XElement(w + "rPr", element));
    }

    private static (List<(GlyphRun Run, Colour Colour)> Runs, List<(DocRect Area, Colour Colour)> Rules)
        Draw(PageParagraph paragraph)
    {
        DocRect area = new(Length.Zero, Length.Zero, Length.FromPoints(400), Length.FromPoints(400));
        List<(DocRect Area, Colour Colour)> rules = [];

        List<(GlyphRun Run, Colour Colour)> runs =
            PageDrawing.RunsIn(area, Line(paragraph), paragraph, highlights: null, rules: rules);

        return (runs, rules);
    }

    /// <summary>A paragraph whose text from <paramref name="from"/> onwards carries a decoration.</summary>
    private static PageParagraph Decorated(string text, int from, bool underline, bool strike)
        => new()
        {
            Text = text,
            Face = Face,
            EmSize = Size,
            Runs = from > 0
                ?
                [
                    new PageRun(0, from, Face, Size, Colour: Colour.Black),
                    new PageRun(
                        from, text.Length - from, Face, Size, Colour: Colour.Black,
                        Underline: Ruled(underline), IsStruckThrough: strike),
                ]
                :
                [
                    new PageRun(
                        0, text.Length, Face, Size, Colour: Colour.Black,
                        Underline: Ruled(underline), IsStruckThrough: strike),
                ],
        };

    private static PlacedLine Line(PageParagraph paragraph)
        => new(
            ParagraphIndex: 0,
            LineIndex: 0,
            Box: new LineBox(
                new TextLine(
                    0, paragraph.Text.Length, paragraph.Text.Length, Length.Zero, EndsParagraph: true),
                Length.Zero,
                Length.Zero,
                Length.FromPoints(14),
                Length.FromPoints(11),
                Length.Zero),
            Top: Length.Zero);

    /// <summary>A real face, since a rule's offset and thickness are measurements rather than constants.</summary>
    private static OpenTypeFace Face { get; } = Resolve();

    private static OpenTypeFace Resolve()
    {
        SystemFontResolver resolver = new(SystemFontIndex.Build());
        return resolver.LoadOpenType(
            resolver.Resolve(new FontRequest("Liberation Serif", 400, false)));
    }
}
