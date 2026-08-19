using Paperless.Core.Units;
using Paperless.Text.Layout;
using Shouldly;

namespace Paperless.Text.Tests;

/// <summary>
/// Checks where a tab advances to, and where the text after it sits.
/// </summary>
/// <remarks>
/// Against a measurement of one point per character rather than against a font, because what is under test
/// is the arithmetic of the stops and not the shaping. That makes every expected number legible: a stretch
/// of five characters is five points wide, so a right stop at 100 pt puts it at 95.
/// </remarks>
public sealed class TabRulerTests
{
    /// <summary>A character is a point wide, so a stretch's width is its length.</summary>
    private static Length Measure(int from, int to) => Length.FromPoints(Math.Max(to - from, 0));

    private static ParagraphFormat With(params TabStop[] stops) => new()
    {
        TabStops = stops,
        DefaultTabInterval = Length.FromPoints(10),
    };

    [Fact]
    public void ATabWithoutStopsLandsOnTheNextMultipleOfTheInterval()
    {
        ParagraphFormat format = With();

        // "abc" is 3 pt wide, so the tab at 3 pt goes to 10; "de" then ends at 12, and the next tab to 20.
        List<TabbedSegment> segments = TabRuler.Segments("abc\tde\tf", 0, 8, format, Measure);

        segments.Count.ShouldBe(3);
        segments[0].Left.ShouldBe(Length.Zero);
        segments[1].Left.ShouldBe(Length.FromPoints(10));
        segments[2].Left.ShouldBe(Length.FromPoints(20));
    }

    [Fact]
    public void ATabLandingExactlyOnAStopAdvancesToTheNextOne()
    {
        // A tab always moves. "abcdefghij" is exactly ten points, so the tab after it sits on the first
        // default stop — and must go to the second, or a tab would take no room and a table would collapse.
        List<TabbedSegment> segments =
            TabRuler.Segments("abcdefghij\tx", 0, 12, With(), Measure);

        segments[1].Left.ShouldBe(Length.FromPoints(20));
    }

    [Fact]
    public void AnExplicitStopBeatsTheInterval()
    {
        List<TabbedSegment> segments =
            TabRuler.Segments("ab\tcd", 0, 5, With(new TabStop(Length.FromPoints(7))), Measure);

        segments[1].Left.ShouldBe(Length.FromPoints(7));
    }

    [Fact]
    public void ARightStopPutsTheStretchsEndOnIt()
    {
        List<TabbedSegment> segments = TabRuler.Segments(
            "ab\tcdef",
            0,
            7,
            With(new TabStop(Length.FromPoints(30), TabAlignment.Right)),
            Measure);

        // Four characters, so four points: the stretch starts at 26 and ends on the stop.
        segments[1].Left.ShouldBe(Length.FromPoints(26));
        segments[1].Right.ShouldBe(Length.FromPoints(30));
    }

    [Fact]
    public void ACentreStopPutsTheStretchsMiddleOnIt()
    {
        List<TabbedSegment> segments = TabRuler.Segments(
            "ab\tcdef",
            0,
            7,
            With(new TabStop(Length.FromPoints(30), TabAlignment.Centre)),
            Measure);

        segments[1].Left.ShouldBe(Length.FromPoints(28));
        segments[1].Right.ShouldBe(Length.FromPoints(32));
    }

    [Fact]
    public void ADecimalStopPutsTheSeparatorOnIt()
    {
        List<TabbedSegment> segments = TabRuler.Segments(
            "ab\t12.75",
            0,
            8,
            With(new TabStop(Length.FromPoints(30), TabAlignment.DecimalSeparator)),
            Measure);

        // Two digits before the point, so the stretch starts two points before the stop — and the digits
        // after it hang past, which is the whole point of the alignment.
        segments[1].Left.ShouldBe(Length.FromPoints(28));
        segments[1].Right.ShouldBe(Length.FromPoints(33));
    }

    [Fact]
    public void ADecimalStopWithNoSeparatorAlignsOnTheEnd()
    {
        // Which is what lines a column of whole numbers up with a column of fractional ones.
        List<TabbedSegment> segments = TabRuler.Segments(
            "ab\t125",
            0,
            6,
            With(new TabStop(Length.FromPoints(30), TabAlignment.DecimalSeparator)),
            Measure);

        segments[1].Right.ShouldBe(Length.FromPoints(30));
    }

    [Fact]
    public void AStopThatCannotHoldItsTextDoesNotDrawBackwards()
    {
        // A right stop at 12 pt with five points of text would start at 7 — behind the ten points already
        // set. The text continues from the pen instead, because the alternative is drawing over the column
        // before it. A stop behind the pen never even arises: the lookup only returns stops beyond it.
        List<TabbedSegment> segments = TabRuler.Segments(
            "abcdefghij\tabcde",
            0,
            16,
            With(new TabStop(Length.FromPoints(12), TabAlignment.Right)),
            Measure);

        segments[1].Left.ShouldBe(Length.FromPoints(10));
    }

    [Fact]
    public void TheWidthOfALineIsWhereItsLastStretchEnds()
    {
        TabRuler.WidthOf("ab\tcd", 0, 5, With(), Measure).ShouldBe(Length.FromPoints(12));
    }

    [Fact]
    public void ALineWithoutTabsNeedsNoneOfIt()
    {
        TabRuler.HasTab("plain text", 0, 10).ShouldBeFalse();
        TabRuler.HasTab("plain\ttext", 0, 10).ShouldBeTrue();

        // The range matters, not the string: a tab past the line's end is the next line's problem.
        TabRuler.HasTab("plain\ttext", 0, 5).ShouldBeFalse();
    }

    [Fact]
    public void AStretchCarriesTheBlankItsTabAdvancedAcross()
    {
        List<TabbedSegment> segments = TabRuler.Segments(
            "ab\tcd", 0, 5, With(new TabStop(Length.FromPoints(30), Leader: '.')), Measure);

        // The first stretch was placed by no tab at all, so it has no blank before it and no leader.
        segments[0].GapLeft.ShouldBe(segments[0].Left);
        segments[0].HasLeader.ShouldBeFalse();

        // The second was: the tab began where "ab" ended and carried the pen to the stop, so the blank
        // is [2, 30) — which is what a dot leader has to fill.
        segments[1].GapLeft.ShouldBe(Length.FromPoints(2));
        segments[1].Left.ShouldBe(Length.FromPoints(30));
        segments[1].GapWidth.ShouldBe(Length.FromPoints(28));
        segments[1].Leader.ShouldBe('.');
        segments[1].HasLeader.ShouldBeTrue();
    }

    [Fact]
    public void ARightStopPutsTheBlankBeforeTheTextRatherThanBeforeTheStop()
    {
        // The leader of a contents line runs up to where the page number starts, not up to the stop the
        // number's *end* sits on — so the blank has to be measured against the placed text.
        List<TabbedSegment> segments = TabRuler.Segments(
            "ab\tcd",
            0,
            5,
            With(new TabStop(Length.FromPoints(30), TabAlignment.Right, '.')),
            Measure);

        segments[1].Left.ShouldBe(Length.FromPoints(28));
        segments[1].GapWidth.ShouldBe(Length.FromPoints(26));
    }

    [Fact]
    public void ADefaultStopHasNoLeaderAndASpaceFillIsNoneAtAll()
    {
        // Only an explicit stop can carry a fill: Writer sets cFill to 0 for the stops it synthesises on
        // the default grid (sw/source/core/text/txttab.cxx:218).
        TabRuler.Segments("ab\tcd", 0, 5, With(), Measure)[1].HasLeader.ShouldBeFalse();

        // And both formats spell "no leader" as a space on a stop that has the attribute at all.
        TabRuler.Segments(
                "ab\tcd", 0, 5, With(new TabStop(Length.FromPoints(30), Leader: ' ')), Measure)[1]
            .HasLeader.ShouldBeFalse();
    }

    [Fact]
    public void ARightStopPastTheLineEdgeIsHonouredAtTheEdge()
    {
        // `nRight = std::min(GetTabPos(), rInf.Width())` — SwTabPortion::PostFormat,
        // sw/source/core/text/txttab.cxx:503. Without it "cd" ends at 30 pt on a line 20 pt wide, the
        // line does not fit, and a contents entry breaks into one line per stretch.
        ParagraphFormat format = With(new TabStop(Length.FromPoints(30), TabAlignment.Right, '.'));

        TabRuler.Segments("ab\tcd", 0, 5, format, Measure)[1]
            .Left.ShouldBe(Length.FromPoints(28));

        TabRuler.Segments("ab\tcd", 0, 5, format, Measure, rightEdge: Length.FromPoints(20))[1]
            .Left.ShouldBe(Length.FromPoints(18));

        TabRuler.WidthOf("ab\tcd", 0, 5, format, Measure, rightEdge: Length.FromPoints(20))
            .ShouldBe(Length.FromPoints(20));
    }

    [Fact]
    public void ACentredStopPastTheLineEdgeCentresOnTheEdge()
    {
        ParagraphFormat format = With(new TabStop(Length.FromPoints(40), TabAlignment.Centre));

        // "cd" is 2 pt, so centring it on 20 pt puts its left edge at 19.
        TabRuler.Segments("ab\tcd", 0, 5, format, Measure, rightEdge: Length.FromPoints(20))[1]
            .Left.ShouldBe(Length.FromPoints(19));
    }

    [Fact]
    public void ALeftStopPastTheLineEdgeIsNotClamped()
    {
        // Writer breaks the line at such a tab rather than pulling it back — PreFormat's `bFull`, same
        // file — so the ruler must leave it where it was declared and let the filler decide.
        ParagraphFormat format = With(new TabStop(Length.FromPoints(30)));

        TabRuler.Segments("ab\tcd", 0, 5, format, Measure, rightEdge: Length.FromPoints(20))[1]
            .Left.ShouldBe(Length.FromPoints(30));
    }

    [Fact]
    public void ATabEndingTheParagraphPastTheEdgeDoesNotWidenTheLineItIsFittedBy()
    {
        // `bFull = false` where `bTabCompat && bAtParaEnd && GetTabPos() >= nTextFrameWidth` —
        // SwTabPortion::PreFormat, sw/source/core/text/txttab.cxx:448-458. The tab is the paragraph's
        // last character, so nothing follows it that a second line could hold, and Writer keeps the line.
        // Measured on 150_5300_13_chg10.doc, whose footer "Chap 4\t\t<page>\t" broke after its second tab
        // and put the page number on a line of its own at the left margin.
        ParagraphFormat format = With(new TabStop(Length.FromPoints(20), TabAlignment.Right));

        // "ab" ends at 2, the tab takes the right stop at the edge and "cd" ends on it at 20. The trailing
        // tab has no stop left, takes the next default interval at 30, and overruns.
        TabRuler.WidthOf(
                "ab\tcd\t", 0, 6, format, Measure, rightEdge: Length.FromPoints(20),
                countsDeferredStretch: false)
            .ShouldBe(Length.FromPoints(20));

        // The drawn width is Writer's own: PreFormat sets the portion's width before it takes the break
        // back and does not take that back with it.
        TabRuler.WidthOf("ab\tcd\t", 0, 6, format, Measure, rightEdge: Length.FromPoints(20))
            .ShouldBe(Length.FromPoints(30));
    }

    [Fact]
    public void ATabInsideTheParagraphPastTheEdgeStillBreaksTheLine()
    {
        // The other half of `bAtParaEnd`: a tab with text after it has somewhere to break to, so the
        // forgiveness must not reach it. "ef" follows the trailing tab here and the line is full.
        ParagraphFormat format = With(new TabStop(Length.FromPoints(20), TabAlignment.Right));

        TabRuler.WidthOf(
                "ab\tcd\tef", 0, 8, format, Measure, rightEdge: Length.FromPoints(20),
                countsDeferredStretch: false)
            .ShouldBe(Length.FromPoints(32));
    }

    [Fact]
    public void TabOverSpacingBreaksTheLineAtThatTabInstead()
    {
        // The branch above the rescue — txttab.cxx:429-440 — returns `bFull = true` for a left stop at
        // or past the frame and never falls through, so a file writerfilter read never reaches it. The
        // same footer therefore keeps its page number on one line in a .doc and not in a .docx.
        ParagraphFormat format = With(new TabStop(Length.FromPoints(20), TabAlignment.Right)) with
        {
            TabsOverSpacing = true,
        };

        TabRuler.WidthOf(
                "ab\tcd\t", 0, 6, format, Measure, rightEdge: Length.FromPoints(20),
                countsDeferredStretch: false)
            .ShouldBe(Length.FromPoints(30));
    }

    [Fact]
    public void ATabReachingTheLineEdgeEndsTheLineAtItself()
    {
        // `SwTabPortion::PreFormat` runs once per tab portion, and a tab that finds itself at or past
        // the line's boundary sets bFull, zeroes itself and drops the rest of the chain
        // (txttab.cxx:462-476) — so the line ends in front of the tab and not at the last break
        // opportunity behind it. "ab" ends at 2, the tabs take 10, 20 and 30; the one landing on 20
        // reaches the edge and is not the paragraph's last character.
        TabRuler.BreakAt(
                "ab\t\t\t", 0, With(), Measure, isFirstLine: true,
                lineEdge: Length.FromPoints(20), rightEdge: Length.FromPoints(20))
            .ShouldBe(3);
    }

    [Fact]
    public void TheLastTabOfTheParagraphStillEndsNoLine()
    {
        // The same three tabs against a wider line: only the last of them reaches the edge, and
        // `bAtParaEnd` forgives exactly that one.
        TabRuler.BreakAt(
                "ab\t\t\t", 0, With(), Measure, isFirstLine: true,
                lineEdge: Length.FromPoints(30), rightEdge: Length.FromPoints(30))
            .ShouldBeNull();
    }

    [Fact]
    public void AnAlignedStopNeverEndsTheLine()
    {
        // A right, centred or decimal stop is settled in PostFormat with the text after it already
        // fitted, and never sets bFull.
        TabRuler.BreakAt(
                "ab\tcd", 0, With(new TabStop(Length.FromPoints(20), TabAlignment.Right)), Measure,
                isFirstLine: true, lineEdge: Length.FromPoints(20), rightEdge: Length.FromPoints(20))
            .ShouldBeNull();
    }

    [Fact]
    public void ATabAtTheLineStartIsFilledRatherThanBrokenFor()
    {
        // `if (rInf.GetIdx() == rInf.GetLineStart())` — PreFormat fills the line with the tab instead
        // of opening an empty one, and a rule that broke there would not terminate.
        TabRuler.BreakAt(
                "ab\t\t\t", 3, With(), Measure, isFirstLine: false,
                lineEdge: Length.FromPoints(10), rightEdge: Length.FromPoints(10))
            .ShouldBeNull();
    }
}
