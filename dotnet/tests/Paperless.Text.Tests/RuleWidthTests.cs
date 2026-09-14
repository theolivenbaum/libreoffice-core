using System.Globalization;

using Paperless.Core.Units;
using Paperless.Text.Fonts;
using Shouldly;

namespace Paperless.Text.Tests;

/// <summary>
/// How thick 26.2.4.2 draws an underline, a double underline and a strikethrough.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every expectation here was read off the reference's own PDF</b>, not computed from the rule
/// under test. <c>probes/quantise-r120/</c> authors one line per (face, size, kind) three times
/// over — as <c>.fodt</c>, <c>.fods</c> and <c>.fodp</c>, so the module is a variable — renders
/// each with 26.2.4.2 twice (C11) and reads the drawn thickness back out of the content stream.
/// The two tables below are those measurements: 270 rules from Calc in whole hundredths of a
/// millimetre and 270 from Writer in whole twips. The Impress leg agrees with the Calc one on all
/// 270 and is not repeated.
/// </para>
/// <para>
/// The rule that reproduces them is <see cref="LineSpacing.ResolveRuleWidths(OpenTypeFace,
/// LineMetrics, Length, MetricGrid)"/>, and what makes it a rule rather than a fit is that it has
/// no free parameter and covers both branches of <c>FontMetricData::ImplInitTextLineSize</c>: the
/// three Liberation faces take the descent branch (Mono's descent trips #i55341's clamp), and
/// Carlito, Caladea and DejaVu Sans take the HarfBuzz one.
/// </para>
/// <para>
/// A face's own numbers are read from the installed file rather than restated, because the point
/// of the table is the <em>quantisation</em> and a restated metric would let a wrong one pass.
/// </para>
/// </remarks>
public class RuleWidthTests
{
    /// <summary>Measured off 26.2.4.2's PDF of `sweep-fods.fods`: whole hundredths of a millimetre.</summary>
    /// <remarks>Each cell is <c>size=single/double/strikethrough</c>.</remarks>
    private static readonly string[] Calc =
    [
        "Liberation Sans: 6=11/7/11 7=14/7/14 8=14/11/14 9=18/11/18 10=18/11/18 11=21/14/21 12=21/14/21 14=28/18/28 16=32/18/32 18=35/21/35 20=39/25/39 24=46/28/46 28=53/32/53 36=67/42/67 48=92/56/92",
        "Liberation Serif: 6=11/7/11 7=14/7/14 8=14/11/14 9=18/11/18 10=21/14/21 11=21/14/21 12=25/14/25 14=28/18/28 16=32/21/32 18=35/21/35 20=39/25/39 24=46/28/46 28=53/35/53 36=71/42/71 48=92/60/92",
        "Liberation Mono: 6=14/11/14 7=18/11/18 8=21/14/21 9=21/14/21 10=25/14/25 11=28/18/28 12=28/18/28 14=35/21/35 16=39/25/39 18=46/28/46 20=49/32/49 24=60/39/60 28=67/42/67 36=88/56/88 48=116/74/116",
        "Carlito: 6=21/14/14 7=25/18/18 8=28/21/21 9=32/21/21 10=35/25/25 11=39/25/28 12=42/28/28 14=49/32/35 16=56/39/39 18=64/42/42 20=67/46/49 24=81/56/56 28=95/64/67 36=123/81/85 48=162/109/113",
        "Caladea: 6=11/7/11 7=14/11/14 8=14/11/14 9=18/11/18 10=18/14/18 11=21/14/21 12=21/14/21 14=28/18/28 16=28/21/28 18=32/21/32 20=35/25/35 24=42/28/42 28=53/35/53 36=64/42/64 48=85/56/85",
        "DejaVu Sans: 6=11/7/11 7=14/11/14 8=14/11/14 9=14/11/18 10=18/11/18 11=18/14/21 12=21/14/21 14=25/18/25 16=28/18/28 18=28/21/32 20=32/21/35 24=39/28/42 28=46/32/49 36=56/39/64 48=78/53/85",
    ];

    /// <summary>The same grid measured off `sweep-fodt.fodt`: whole twips.</summary>
    /// <remarks>
    /// A twip is exactly two of the writer's 720 dpi pixels, so every one of these is even and a
    /// Writer rule is a whole tenth of a point. That the unit differs from Calc's is the half of
    /// this the source does not say — see <see cref="MetricGrid.TextLine"/>.
    /// </remarks>
    private static readonly string[] Writer =
    [
        "Liberation Sans: 6=6/4/6 7=8/4/8 8=8/6/8 9=10/6/10 10=10/6/10 11=12/8/12 12=12/8/12 14=16/10/16 16=18/10/18 18=20/12/20 20=22/14/22 24=26/16/26 28=30/18/30 36=38/24/38 48=52/32/52",
        "Liberation Serif: 6=6/4/6 7=8/4/8 8=8/6/8 9=10/6/10 10=12/8/12 11=12/8/12 12=14/8/14 14=16/10/16 16=18/12/18 18=20/12/20 20=22/14/22 24=26/16/26 28=30/20/30 36=40/24/40 48=52/34/52",
        "Liberation Mono: 6=8/6/8 7=10/6/10 8=12/8/12 9=12/8/12 10=14/8/14 11=16/10/16 12=16/10/16 14=20/12/20 16=22/14/22 18=26/16/26 20=28/18/28 24=34/22/34 28=38/24/38 36=50/32/50 48=66/42/66",
        "Carlito: 6=12/8/8 7=14/10/10 8=16/12/12 9=18/12/12 10=20/14/14 11=22/14/16 12=24/16/16 14=28/18/20 16=32/22/22 18=36/24/24 20=38/26/28 24=46/32/32 28=54/36/38 36=70/46/48 48=92/62/64",
        "Caladea: 6=6/4/6 7=8/6/8 8=8/6/8 9=10/6/10 10=10/8/10 11=12/8/12 12=12/8/12 14=16/10/16 16=16/12/16 18=18/12/18 20=20/14/20 24=24/16/24 28=30/20/30 36=36/24/36 48=48/32/48",
        "DejaVu Sans: 6=6/4/6 7=8/6/8 8=8/6/8 9=8/6/10 10=10/6/10 11=10/8/12 12=12/8/12 14=14/10/14 16=16/10/16 18=16/12/18 20=18/12/20 24=22/16/24 28=26/18/28 36=32/22/36 48=44/30/48",
    ];

    private static readonly Dictionary<string, string> Files = new()
    {
        ["Liberation Sans"] = "LiberationSans-Regular.ttf",
        ["Liberation Serif"] = "LiberationSerif-Regular.ttf",
        ["Liberation Mono"] = "LiberationMono-Regular.ttf",
        ["Carlito"] = "Carlito-Regular.ttf",
        ["Caladea"] = "Caladea-Regular.ttf",
        ["DejaVu Sans"] = "DejaVuSans.ttf",
    };

    private static readonly string[] SearchDirectories =
    [
        "/usr/share/fonts/truetype/crosextra",
        "/usr/share/fonts/truetype/liberation",
        "/usr/share/fonts/truetype/liberation2",
        "/usr/share/fonts/truetype/dejavu",
        "/usr/share/fonts",
    ];

    private static string? Find(string fileName)
    {
        foreach (string directory in SearchDirectories)
        {
            if (!Directory.Exists(directory)) continue;

            string direct = Path.Combine(directory, fileName);
            if (File.Exists(direct)) return direct;

            string[] found = Directory.GetFiles(directory, fileName, SearchOption.AllDirectories);
            if (found.Length > 0) return found[0];
        }
        return null;
    }

    private static OpenTypeFace Require(string family)
    {
        string? path = Find(Files[family]);
        Assert.SkipWhen(path is null, $"{Files[family]} is not installed; see check-env.sh");

        OpenTypeFace? face = OpenTypeFace.ReadFile(path!);
        face.ShouldNotBeNull($"{path} should be readable as a font");
        return face!;
    }

    private static IEnumerable<(string Family, int Size, int[] Units)> Table(string[] rows)
    {
        foreach (string row in rows)
        {
            string[] halves = row.Split(": ", 2);
            foreach (string cell in halves[1].Split(' '))
            {
                string[] sides = cell.Split('=');
                yield return (halves[0],
                              int.Parse(sides[0], CultureInfo.InvariantCulture),
                              sides[1].Split('/')
                                      .Select(v => int.Parse(v, CultureInfo.InvariantCulture))
                                      .ToArray());
            }
        }
    }

    private static void Check(string[] rows, MetricGrid grid, Func<long, Length> unit)
    {
        int cases = 0;

        foreach ((string family, int size, int[] want) in Table(rows))
        {
            OpenTypeFace face = Require(family);
            LineSpacing.RuleWidths got = LineSpacing.ResolveRuleWidths(
                face, LineSpacing.Resolve(face), Length.FromPoints(size), grid);

            string where = $"{family} at {size} pt";
            got.Underline.ShouldBe(unit(want[0]), $"{where}, single underline");
            got.DoubleUnderline.ShouldBe(unit(want[1]), $"{where}, double underline");
            got.Strikeout.ShouldBe(unit(want[2]), $"{where}, strikethrough");
            cases++;
        }

        cases.ShouldBe(90, "six faces at fifteen sizes");
    }

    /// <summary>
    /// Every one of the 90 (face, size) pairs Calc draws, in the whole hundredth of a millimetre
    /// the reference drew it at.
    /// </summary>
    [Fact]
    public void EveryMeasuredCalcRuleIsAWholeHundredthOfAMillimetreAndTheRuleFindsIt()
        => Check(Calc, MetricGrid.TextLine, u => Length.FromMm100(u));

    /// <summary>
    /// The same 90 pairs on a word-processing page, where the map unit is the twip and the answers
    /// are therefore different numbers of the same 720 dpi pixels.
    /// </summary>
    [Fact]
    public void EveryMeasuredWriterRuleIsAWholeTwipAndTheRuleFindsIt()
        => Check(Writer, MetricGrid.WriterTextLine, u => Length.FromTwips(u));

    /// <summary>
    /// The two grids really do disagree, so the test above is not passing on a coincidence.
    /// </summary>
    /// <remarks>
    /// 21 hundredths of a millimetre is 0.5953 pt and 12 twips is 0.6 pt: the same six pixels
    /// rounded onto two units. If the words path took Calc's grid this would be the error it made.
    /// </remarks>
    [Fact]
    public void TheSamePixelCountComesOutDifferentlyOnTheTwoGrids()
    {
        OpenTypeFace face = Require("Liberation Sans");
        LineMetrics line = LineSpacing.Resolve(face);
        Length size = Length.FromPoints(12);

        LineSpacing.ResolveRuleWidths(face, line, size, MetricGrid.TextLine)
                   .Underline.ShouldBe(Length.FromMm100(21));
        LineSpacing.ResolveRuleWidths(face, line, size, MetricGrid.WriterTextLine)
                   .Underline.ShouldBe(Length.FromTwips(12));
    }

    /// <summary>
    /// The seven pairs O64 was opened on, restated as the (face, size, kind) they came from.
    /// </summary>
    /// <remarks>
    /// Round 118 measured seven thicknesses over four spreadsheets and recorded them as 0.51,
    /// 0.595, 0.51, 0.595, 1.389, 0.794 and 0.397 pt, which are 18, 21, 18, 21, 49, 28 and 14
    /// hundredths of a millimetre. Four of the seven are the two rows above; the two Carlito ones
    /// and the double are the sizes those documents happen to use, so they are asserted here in
    /// the form the register states them.
    /// </remarks>
    [Theory]
    [InlineData("Liberation Sans", 10, "single", 18)]   // the invoice's Arial hyperlinks
    [InlineData("Liberation Sans", 12, "single", 21)]   // and the fixture's
    [InlineData("Carlito", 14, "single", 49)]           // 002_Contextures' Calibri 14 `<u/>` cells
    [InlineData("Carlito", 11, "strike", 28)]           // TK-Syllabus' Calibri 11 strikethroughs
    [InlineData("Liberation Sans", 12, "double", 14)]   // the fixture's double underline
    public void TheSevenPairsO64WasOpenedOnAreReproduced(
        string family, int size, string kind, int hundredths)
    {
        OpenTypeFace face = Require(family);
        LineSpacing.RuleWidths widths = LineSpacing.ResolveRuleWidths(
            face, LineSpacing.Resolve(face), Length.FromPoints(size), MetricGrid.TextLine);

        Length got = kind switch
        {
            "single" => widths.Underline,
            "double" => widths.DoubleUnderline,
            _ => widths.Strikeout,
        };

        got.ShouldBe(Length.FromMm100(hundredths));
    }

    /// <summary>
    /// A double underline is thinner than a single one, on both branches.
    /// </summary>
    /// <remarks>
    /// The arm a design-unit model cannot have at all, because it drew both at one thickness:
    /// <c>mnDUnderlineSize</c> is two thirds of the single size on the HarfBuzz branch and
    /// <c>((descent × 16) + 50) / 100</c> against the single rule's 25 on the other.
    /// </remarks>
    [Theory]
    [InlineData("Liberation Sans")]
    [InlineData("Carlito")]
    public void ADoubleUnderlineIsThinnerThanASingleOne(string family)
    {
        OpenTypeFace face = Require(family);
        LineSpacing.RuleWidths widths = LineSpacing.ResolveRuleWidths(
            face, LineSpacing.Resolve(face), Length.FromPoints(12), MetricGrid.TextLine);

        widths.DoubleUnderline.ShouldBeLessThan(widths.Underline);
    }

    /// <summary>
    /// The thickness is a staircase in the size and not a fraction of the em.
    /// </summary>
    /// <remarks>
    /// The property that separates this from every design-unit answer, and the reason O64 could
    /// not be closed by a scale factor: the reference draws 10 pt and 11 pt Liberation Sans at
    /// 18 and 21 hundredths of a millimetre, a 17 % step for a 10 % rise, and 9 pt and 10 pt at
    /// the same 18. A model proportional to the size cannot produce a repeat and a jump.
    /// </remarks>
    [Fact]
    public void TheThicknessStepsRatherThanScaling()
    {
        OpenTypeFace face = Require("Liberation Sans");
        LineMetrics line = LineSpacing.Resolve(face);

        Length At(double points) => LineSpacing
            .ResolveRuleWidths(face, line, Length.FromPoints(points), MetricGrid.TextLine)
            .Underline;

        At(9).ShouldBe(At(10));
        At(11).ShouldBeGreaterThan(At(10));
        At(11).ShouldBe(At(12));
    }

    /// <summary>
    /// A face LibreOffice will read the metrics of and one it will not answer differently at the
    /// same size, and the discriminator is the name alone.
    /// </summary>
    /// <remarks>
    /// The control on the branch, and it is worth having as a rule rather than as a table row:
    /// Liberation Sans' <c>post</c> would give <c>ceil(150 × 100 / 2048)</c> = 8 pixels at 10 pt,
    /// which is 28 hundredths of a millimetre, and the reference draws 18. Spelling the family
    /// anything else takes the other branch.
    /// </remarks>
    [Fact]
    public void TheBlacklistIsWhatChoosesTheBranchAndItIsCheckedByName()
    {
        OpenTypeFace face = Require("Liberation Sans");
        LineMetrics line = LineSpacing.Resolve(face);
        Length size = Length.FromPoints(10);

        LineSpacing.ResolveRuleWidths(
            "Liberation Sans", face.Post, face.Os2, line, size, MetricGrid.TextLine)
            .Underline.ShouldBe(Length.FromMm100(18));

        LineSpacing.ResolveRuleWidths(
            "Not Liberation Sans", face.Post, face.Os2, line, size, MetricGrid.TextLine)
            .Underline.ShouldBe(Length.FromMm100(28));
    }
}
