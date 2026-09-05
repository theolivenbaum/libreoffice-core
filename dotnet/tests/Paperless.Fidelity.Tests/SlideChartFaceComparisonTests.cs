using System.Text.RegularExpressions;
using Paperless.Core.Documents;
using Paperless.Rendering.Pdf;
using Paperless.TestKit;
using Paperless.TestKit.LibreOffice;
using Shouldly;

namespace Paperless.Fidelity.Tests;

/// <summary>
/// A chart's text is set in the theme's minor Latin face, with a stated <c>a:latin</c>
/// overriding it — not in a fixed default.
/// </summary>
/// <remarks>
/// <para>
/// <c>SlideChart</c> set every chart label in Liberation Sans for four rounds, on the evidence
/// that <c>pdffonts</c> reported Liberation Sans in LibreOffice's own PDF of
/// <c>chart-bar-deck.pptx</c>. That deck's chart states <c>&lt;a:latin typeface="Arial"/&gt;</c>
/// eleven times, and fontconfig substitutes Arial with Liberation Sans — so the measurement is
/// equally consistent with a fixed face and with the stated one, and the corpus cannot separate
/// them without also changing the theme, the sizes and the data at once.
/// </para>
/// <para>
/// So the two decks here are that deck with <em>one</em> thing moved. Both set the theme's minor
/// Latin face to <strong>Liberation Mono</strong>, because a monospace is the widest available
/// separation from either proportional candidate and it moves the plot area's left edge as well
/// as the embedded font list. <c>chart-face-theme-minor.pptx</c> then states no <c>a:latin</c> at
/// all and <c>chart-face-stated.pptx</c> states a literal <c>Liberation Serif</c>. LibreOffice
/// 24.2.7.2 embeds LiberationMono in the first and LiberationSerif in the second, which is the
/// pattern only "the theme's minor face, overridden by a stated one" predicts: a fixed face gives
/// two identical answers, and reading only a stated face leaves the first on a fallback.
/// </para>
/// <para>
/// <strong>The face assertion alone would be a metadata test</strong>, and a face that is merely
/// named right moves no ink — the round-nineteen <c>/BaseFont</c> finding is exactly that shape.
/// So the third case measures <em>one digit's advance</em>, taken as the gap between the pen of
/// the value axis' three-digit labels and the pen of its two-digit ones. They are right-aligned
/// on the same edge, so that gap is one digit and nothing else: 6.01 pt in ten-point Liberation
/// Mono against 5.55 in Liberation Sans.
/// </para>
/// <para>
/// <strong>Two more obvious quantities were measured first and both are the wrong thing to
/// assert.</strong> An absolute pen position carries the composition as well as the face: the
/// value axis' labels land 0.36 pt from the reference with this change and 0.96 pt without —
/// better — while the legend lands 2.49 pt out with it and 1.39 pt out without, because the
/// composition has a legend-reservation error of its own that the wrong face was partly
/// cancelling. And a word's ink width carries the two writers' show splitting: the reference
/// positions each digit of "100" separately, so poppler ends its box at the last glyph's ink and
/// reports 17.25 where our single show reports the full 18.03 advance. Neither difference is
/// about the face. The gap between two of the axis' own labels is.
/// </para>
/// </remarks>
// [reference moved 24.2.7.2 -> 26.2.4.2, corrected] `TheThemesFaceDecidesTheValueLabelsAdvances`
// asserted that the *reference's* digit advance is the design metric, and under 26.2.4.2 it is not: the
// face is still Liberation Mono — `pdffonts` reports one font, `BAAAAA+LiberationMono`, and
// `AChartUnstatedTakesTheThemesMinorFace` still passes — but the reference draws its digits 5.839 pt
// apart against 24.2.7.2's 6.010 and the face's own 6.004. It is the same divergence `CLAUDE.md`'s rule
// 3 records, seen here at its cleanest, and it is not about the face. See the assertion's own remarks.
public sealed partial class SlideChartFaceComparisonTests : IDisposable
{
    /// <summary>One digit's advance in ten-point Liberation Mono, in points.</summary>
    /// <remarks>
    /// 0.6009 em at the 10.01 pt the labels are drawn at. Liberation Sans' digit is 0.5560 em, or
    /// 5.55 pt, so the two faces are 0.46 pt apart here — five times the tolerance below, and a
    /// quantity neither renderer's plot rectangle can move.
    /// </remarks>
    private const double MonospacedDigitAdvance = 6.01;

    /// <summary>The same digit's advance in ten-point Liberation Sans, in points.</summary>
    /// <remarks>
    /// The face this test exists to rule out — the fixed one <c>SlideChart</c> used for four rounds.
    /// 0.5560 em at 10.01 pt.
    /// </remarks>
    private const double ProportionalDigitAdvance = 5.56;

    private readonly LibreOfficeRunner _libreOffice = new();
    private readonly string _workDirectory =
        Directory.CreateTempSubdirectory("paperless-chart-face").FullName;

    public void Dispose()
    {
        _libreOffice.Dispose();
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    [Fact]
    public void AChartUnstatedTakesTheThemesMinorFace()
    {
        Assert.SkipUnless(LibreOfficeRunner.IsAvailable, LibreOfficeRunner.UnavailableReason);

        const string deck = "chart-face-theme-minor.pptx";

        Faces(Ours(deck)).ShouldContain("LiberationMono");
        Faces(_libreOffice.ConvertToPdf(Corpus.Require(deck), _workDirectory))
            .ShouldContain("LiberationMono");
    }

    [Fact]
    public void AChartStatingAFaceTakesTheStatedOneInstead()
    {
        Assert.SkipUnless(LibreOfficeRunner.IsAvailable, LibreOfficeRunner.UnavailableReason);

        const string deck = "chart-face-stated.pptx";

        List<string> ours = Faces(Ours(deck));
        ours.ShouldContain("LiberationSerif");
        ours.ShouldNotContain("LiberationMono");

        List<string> theirs = Faces(_libreOffice.ConvertToPdf(Corpus.Require(deck), _workDirectory));
        theirs.ShouldContain("LiberationSerif");
        theirs.ShouldNotContain("LiberationMono");
    }

    /// <summary>
    /// The face is what the labels are <em>measured</em> in, not only what they are named in.
    /// </summary>
    [Fact]
    public void TheThemesFaceDecidesTheValueLabelsAdvances()
    {
        Assert.SkipUnless(LibreOfficeRunner.IsAvailable, LibreOfficeRunner.UnavailableReason);
        Assert.SkipUnless(PdfWords.IsAvailable, "pdftotext is not installed");

        const string deck = "chart-face-theme-minor.pptx";

        double ours = DigitAdvance(PdfWords.Read(Ours(deck)));
        double theirs = DigitAdvance(
            PdfWords.Read(_libreOffice.ConvertToPdf(Corpus.Require(deck), _workDirectory)));

        // Ours against the literal first, so this tests Paperless rather than an agreement.
        ours.ShouldBe(MonospacedDigitAdvance, 0.1, "our digit advance");

        // And the reference's against the two candidates rather than against the design metric, which
        // it no longer draws. Under 24.2.7.2 it drew 6.010 — the face's own 0.6009 em at 10.005 pt, to
        // a thousandth — and under 26.2.4.2 it draws 5.839, from a TJ array that shifts the pen 16/1000
        // em at every inter-glyph position where 24.2.7.2 shifted it 76/1000 once. The face has not
        // changed: `pdffonts` reports the single font `BAAAAA+LiberationMono` for both, and the deck's
        // theme is what puts it there.
        //
        // So the assertion is the one this test is for — the labels are set in the monospaced face and
        // not the proportional one — stated as a comparison the reference's own device metrics cannot
        // reach across. The two candidates are 0.45 pt apart and the reference sits 0.17 from one and
        // 0.28 from the other. The residual is the advance divergence recorded in `CLAUDE.md`'s rule 3,
        // which is a defect of ours and not of this test: we draw the unhinted design advance and the
        // reference draws whatever its own device measures.
        Math.Abs(theirs - MonospacedDigitAdvance).ShouldBeLessThan(
            Math.Abs(theirs - ProportionalDigitAdvance),
            $"the reference's digit advance of {theirs:F3} pt is nearer Liberation Mono's "
            + $"{MonospacedDigitAdvance} than Liberation Sans' {ProportionalDigitAdvance}");
    }

    /// <summary>
    /// One digit's advance: the gap between the pens of the value axis' <c>100</c> and <c>80</c>
    /// labels, which are right-aligned on the same edge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both are found by their text, because a glyph count cannot be compared across the two
    /// writers: <c>PdfTextRuns</c> counts hexadecimal show strings and LibreOffice writes literal
    /// ones, so every reference run reports zero glyphs. A word's own left edge is poppler's
    /// reading of the pen at the start of that word, which is the same quantity on both sides.
    /// </para>
    /// <para>
    /// Measured: 6.009 for ours and 6.010 for the reference against 5.556 for the fixed face this
    /// replaces, so the tolerance below separates them by four times over.
    /// </para>
    /// </remarks>
    private static double DigitAdvance(List<PdfWord> words)
        => Pen(words, "80") - Pen(words, "100");

    private static double Pen(List<PdfWord> words, string label)
    {
        List<PdfWord> found = [.. words.Where(w => w.PageIndex == 0 && w.Text == label)];
        found.Count.ShouldBe(1, $"one label reading {label} on the first slide");
        return found[0].Left;
    }

    /// <summary>Every <c>/BaseFont</c> in the file, subset prefix stripped.</summary>
    private static List<string> Faces(string pdfPath)
    {
        string text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(pdfPath));
        return [.. BaseFont().Matches(text).Select(m => m.Groups[1].Value).Distinct()];
    }

    private string Ours(string deck)
    {
        string source = Corpus.Require(deck);
        string destination = Path.Combine(
            _workDirectory, Path.GetFileNameWithoutExtension(deck) + "-paperless.pdf");

        using IDocument document = PaperlessDocument.Open(source);
        IPageSequence pages = ((IPaginatedDocument)document).Layout();

        using FileStream output = File.Create(destination);
        new PdfRenderer(new PdfRenderOptions
        {
            CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        }).Render(pages, output);

        return destination;
    }

    [GeneratedRegex(@"/BaseFont\s*/(?:[A-Z]{6}\+)?([A-Za-z0-9-]+)")]
    private static partial Regex BaseFont();
}
