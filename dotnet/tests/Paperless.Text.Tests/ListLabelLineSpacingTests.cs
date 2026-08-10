using Paperless.Core.Units;
using Paperless.Text.Fonts;
using Paperless.Text.Layout;
using Shouldly;

namespace Paperless.Text.Tests;

/// <summary>
/// A list label raises the height proportional line spacing takes its percentage of; an
/// as-character object does not.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="InlineObjectLineSpacingTests"/> establishes the other half — that a picture raises the
/// line and takes no share. Round 46 recorded, from the citation, that a list label "is the same
/// rule", because Writer's label is a <c>SwNumberPortion</c> and
/// <c>SwLinePortion::IsUsedToCalcLineSpacingHeight</c> (<c>porlin.cxx</c>:324) is true only for
/// <c>PortionType::Text</c>. <strong>The installed 24.2.7.2 says the opposite</strong>, and the
/// binary is what made the reference renderings.
/// </para>
/// <para>
/// Writer's own arrangement puts the paragraph's proportional share in the space after it:
/// <c>SwTextFrame::GetLineSpace</c> is <c>(prop − 100)%</c> of <c>GetHeightOfLastLine()</c>, and
/// <c>SwTextFrame::CalcHeightOfLastLine</c> (<c>txtfrm.cxx</c>:3952-3957) takes that from
/// <c>MaxAscentDescent(…, bNoFlyCnt = true)</c> — <em>"i#47162 — suppress consideration of fly
/// content portions and the line portion"</em>. A fly is suppressed. A number portion is not.
/// </para>
/// <para>
/// Measured, since the citation is only the hypothesis.
/// <c>dotnet/probes/words-r47/list-label-line-height.py</c> sets a 14, 20 and 28 pt numbering level
/// over 12 pt Liberation Serif at 100, 150 and 200%, with an unlabelled control at each percentage.
/// LibreOffice's extension is <c>(prop − 100)%</c> of the <em>label's</em> box every time: at 200%
/// and a 28 pt level the gap grows 32.20 pt, which is that level's whole line box, against the
/// item's 13.80. <c>label-and-picture.py</c> then puts a 100 pt picture on the same line and the
/// extension stays at 32.20 rather than becoming the line's 114 — so the base is the tallest portion
/// that is <em>not</em> a fly, which is one rule covering both halves.
/// </para>
/// </remarks>
public class ListLabelLineSpacingTests
{
    private static readonly Length Twelve = Length.FromPoints(12);
    private static readonly Length Label = Length.FromPoints(32.2);
    private static readonly Length Picture = Length.FromPoints(150);

    /// <summary>The rule: a taller label raises both the line and the base.</summary>
    [Fact]
    public void ALabelTallerThanItsItemRaisesTheTextHeightTo()
    {
        (Length height, _, Length text) = Line(
            new InlineObject(0, Length.Zero, Label, Ascent: null, RaisesTextHeight: true));

        height.ShouldBeGreaterThanOrEqualTo(Label);
        text.ShouldBe(Label);
    }

    /// <summary>
    /// The refuted alternative, pinned: the same object without the flag leaves the base alone.
    /// </summary>
    /// <remarks>
    /// This is round 46's reading — a label behaving exactly like an as-character picture — expressed
    /// as the one bit that separates them. Removing the flag from the label makes this test and
    /// <see cref="ALabelTallerThanItsItemRaisesTheTextHeightTo"/> disagree.
    /// </remarks>
    [Fact]
    public void AnAsCharacterObjectOfTheSameHeightDoesNot()
    {
        (Length height, _, Length text) = Line(new InlineObject(0, Length.Zero, Label));

        height.ShouldBeGreaterThanOrEqualTo(Label);
        text.ShouldBeLessThan(Length.FromPoints(20));
    }

    /// <summary>A label no taller than its item changes nothing, which is nearly every list.</summary>
    [Fact]
    public void ALabelShorterThanItsItemCannotLowerTheTextHeight()
    {
        (Length _, Length _, Length withLabel) = Line(
            new InlineObject(0, Length.Zero, Length.FromPoints(4), Ascent: null,
                             RaisesTextHeight: true));

        (Length _, Length _, Length plain) = Line();

        withLabel.ShouldBe(plain);
    }

    /// <summary>
    /// A picture and a taller label on one line: the extension is the label's, not the line's.
    /// </summary>
    /// <remarks>
    /// The row <c>label-and-picture.py</c> exists for. Both a rule of "the base is the whole line"
    /// and a rule of "a label counts" fit a label-only line, and only this separates them: measured,
    /// LibreOffice extends by 32.20 pt with the picture there and 32.20 without.
    /// </remarks>
    [Fact]
    public void APictureBesideATallerLabelTakesNoShareOfThePercentage()
    {
        (Length natural, _, Length text) = Line(
            new InlineObject(0, Length.Zero, Label, Ascent: null, RaisesTextHeight: true),
            new InlineObject(1, Length.Zero, Picture));

        natural.ShouldBeGreaterThanOrEqualTo(Picture);
        text.ShouldBe(Label);

        LineSpacingRule.Multiple(2.0).Apply(natural, text).ShouldBe(natural + Label);
    }

    /// <summary>
    /// And the layouter uses it, which is what a page break feels.
    /// </summary>
    [Fact]
    public void TheLayouterGivesALabelledLineTheLabelsShareOfThePercentage()
    {
        OpenTypeFace face = Carlito();
        const string Text = "xy";

        MeasuredParagraph measured = MeasuredParagraph.Measure(
            Text,
            [new FormattedRun(0, Text.Length, face, Twelve)],
            objects: [new InlineObject(0, Length.Zero, Label, Ascent: null, RaisesTextHeight: true)]);

        (Length natural, _, Length text) = measured.MeasureLine(0, Text.Length);

        LaidOutParagraph laid = new ParagraphLayouter(face).Layout(
            measured,
            ParagraphFormat.Default with { LineSpacing = LineSpacingRule.Multiple(2.0) },
            Length.FromMillimetres(170));

        laid.Lines.Count.ShouldBe(1);
        laid.Lines[0].Height.ShouldBe(LineSpacingRule.Multiple(2.0).Apply(natural, text));
        laid.Lines[0].Height.ShouldBe(natural + Label);
    }

    private static (Length Height, Length Ascent, Length Text) Line(params InlineObject?[] objects)
    {
        OpenTypeFace face = Carlito();
        const string Text = "xy";

        InlineObject[] present = [.. objects.Where(o => o is not null).Select(o => o!.Value)];

        return MeasuredParagraph
            .Measure(
                Text,
                [new FormattedRun(0, Text.Length, face, Twelve)],
                objects: present.Length == 0 ? null : present)
            .MeasureLine(0, Text.Length);
    }

    private static OpenTypeFace Carlito()
    {
        string? path = FindFont("Carlito-Regular.ttf");
        Assert.SkipWhen(path is null, "Carlito is not installed; see check-env.sh");
        return OpenTypeFace.ReadFile(path!).ShouldNotBeNull();
    }

    private static string? FindFont(string fileName)
    {
        foreach (string root in new[]
                 {
                     "/usr/share/fonts", "/usr/local/share/fonts",
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                  ".fonts"),
                 })
        {
            if (!Directory.Exists(root)) continue;
            string? hit = Directory
                .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (hit is not null) return hit;
        }

        return null;
    }
}
