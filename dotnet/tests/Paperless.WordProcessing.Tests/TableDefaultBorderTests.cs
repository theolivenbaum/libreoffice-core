using Paperless.Core.Graphics;
using Paperless.WordProcessing.Ww8;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// Checks the six table-level default border codes a DOC can state instead of stating each cell's.
/// </summary>
/// <remarks>
/// <para>
/// Tested against hand-built operands rather than against LibreOffice, and that is a deliberate exception
/// worth explaining. <c>sprmTTableBorders</c> is a sprm LibreOffice <em>reads</em> and never
/// <em>writes</em> — its own DOC export states all four edges on every cell descriptor instead — so no
/// round trip through <c>soffice</c> can produce a corpus document that carries one. Word writes it
/// constantly, which is why it is worth reading at all.
/// </para>
/// <para>
/// So the rule is transcribed from <c>sw/source/filter/ww8/ww8par2.cxx</c> — <c>ProcessSprmTTableBorders</c>
/// for the operand and the third pass over the bands for the fill-in — and what is pinned here is that
/// transcription: the order of the six codes, and which of them each side of each cell takes. Both are
/// positional, and a transposition in either is silent.
/// </para>
/// </remarks>
public sealed class TableDefaultBorderTests
{
    /// <summary>The <c>brcType</c> for a single line, which is what these operands all state.</summary>
    private const byte SingleLine = 1;

    [Fact]
    public void TheSixCodesAreReadInWordsOwnOrder()
    {
        // Six four-byte BRC80s, each distinguishable only by its width: top, left, bottom, right, then the
        // two interior lines. Any transposition swaps two lines of a rendered table and nothing else, so the
        // widths are what the assertions read.
        Ww8TableBorders borders = Ww8TableBorders.Read(Short([8, 16, 24, 32, 40, 48]), isLongForm: false)
            .ShouldNotBeNull();

        borders.Top.WidthEighths.ShouldBe(8);
        borders.Left.WidthEighths.ShouldBe(16);
        borders.Bottom.WidthEighths.ShouldBe(24);
        borders.Right.WidthEighths.ShouldBe(32);
        borders.Horizontal.WidthEighths.ShouldBe(40);
        borders.Vertical.WidthEighths.ShouldBe(48);

        // And every one of them is a real code rather than the unstated blank a short operand would leave.
        borders.Vertical.IsUnstated.ShouldBeFalse();
    }

    [Fact]
    public void TheNewerFormReadsTheSameSixFromEightByteCodes()
    {
        // The same six in the `BRCVer9` form, whose difference is not only the size: the colour is a
        // four-byte BGR reference rather than a palette index, so a reader that took the older layout would
        // read a colour byte as a width and get the six codes in a plausible-looking mess.
        Ww8TableBorders borders = Ww8TableBorders.Read(Long([8, 16, 24, 32, 40, 48]), isLongForm: true)
            .ShouldNotBeNull();

        borders.Top.WidthEighths.ShouldBe(8);
        borders.Left.WidthEighths.ShouldBe(16);
        borders.Bottom.WidthEighths.ShouldBe(24);
        borders.Right.WidthEighths.ShouldBe(32);
        borders.Horizontal.WidthEighths.ShouldBe(40);
        borders.Vertical.WidthEighths.ShouldBe(48);

        // 0x00BBGGRR little-endian, so the bytes arrive red, green, blue — the same swap the run colour
        // sprm needs, and the reason the colour is worth asserting at all.
        borders.Top.Colour.ShouldBe(new Colour(0x11, 0x22, 0x33));
    }

    [Fact]
    public void AnOperandTooShortForSixCodesIsRejectedWhole()
    {
        // Rejected rather than half-read, because the caller keeps whatever an earlier sprm — or an earlier
        // row, since the defaults carry forward — already said. Five real codes and one blank would be worse
        // than none.
        Ww8TableBorders.Read(Short([8, 16, 24, 32, 40]), isLongForm: false).ShouldBeNull();
        Ww8TableBorders.Read(Short([8, 16, 24, 32, 40, 48]), isLongForm: true).ShouldBeNull();
    }

    [Fact]
    public void ACornerCellTakesTheOuterCodesOnItsOuterSidesOnly()
    {
        Ww8TableBorders defaults = Defaults();

        // The top-left cell of a table with more than one row and column: outer top and outer left, but the
        // interior lines on the two sides that face other cells. Taking all four outer codes here — the
        // mistake that looks right until the table has two of anything — draws a box round the cell.
        Ww8CellBorders topLeft = defaults.FillIn(
            default, isFirstRow: true, isLastRow: false, isFirstColumn: true, isLastColumn: false);

        topLeft.Top.WidthEighths.ShouldBe(8);
        topLeft.Left.WidthEighths.ShouldBe(16);
        topLeft.Bottom.WidthEighths.ShouldBe(40, "a row that is not the last meets the horizontal line");
        topLeft.Right.WidthEighths.ShouldBe(48, "a column that is not the last meets the vertical line");

        Ww8CellBorders bottomRight = defaults.FillIn(
            default, isFirstRow: false, isLastRow: true, isFirstColumn: false, isLastColumn: true);

        bottomRight.Top.WidthEighths.ShouldBe(40);
        bottomRight.Left.WidthEighths.ShouldBe(48);
        bottomRight.Bottom.WidthEighths.ShouldBe(24);
        bottomRight.Right.WidthEighths.ShouldBe(32);
    }

    [Fact]
    public void AOneCellTableTakesAllFourOuterCodes()
    {
        // The degenerate case, and the one where the four outer codes are right — which is why it cannot be
        // the case a reader is tested on.
        Ww8CellBorders only = Defaults().FillIn(
            default, isFirstRow: true, isLastRow: true, isFirstColumn: true, isLastColumn: true);

        only.Top.WidthEighths.ShouldBe(8);
        only.Left.WidthEighths.ShouldBe(16);
        only.Bottom.WidthEighths.ShouldBe(24);
        only.Right.WidthEighths.ShouldBe(32);
    }

    [Fact]
    public void ASideTheCellStatedKeepsWhatItStated()
    {
        // Defaults, not overrides. A cell that names a side keeps it, which is how a table with a grid can
        // still have one thick line in the middle of it.
        Ww8Border stated = new(SingleLine, 96, Colour.Black);

        Ww8CellBorders filled = Defaults().FillIn(
            new Ww8CellBorders(stated, default, default, default),
            isFirstRow: true, isLastRow: false, isFirstColumn: true, isLastColumn: false);

        filled.Top.WidthEighths.ShouldBe(96);
        filled.Left.WidthEighths.ShouldBe(16, "the sides it said nothing about are still filled in");
    }

    [Fact]
    public void ANilSideIsAStatedAbsenceAndSurvives()
    {
        // The distinction the whole fill-in turns on: an *unstated* side has type zero and is filled in, while
        // the nil border is a type of its own and means "this edge is deliberately absent". Treating nil as
        // unstated puts the table's grid back over the very edges a document turned off.
        Ww8CellBorders filled = Defaults().FillIn(
            new Ww8CellBorders(Ww8Border.Nil, default, default, default),
            isFirstRow: true, isLastRow: true, isFirstColumn: true, isLastColumn: true);

        filled.Top.ShouldBe(Ww8Border.Nil);
        filled.Top.AsTableBorder().ShouldBe(default, "and it draws nothing");
        filled.Left.WidthEighths.ShouldBe(16);
    }

    // ------------------------------------------------------------------------- the operands

    /// <summary>Six four-byte <c>BRC80</c> codes, distinguished by width.</summary>
    private static byte[] Short(byte[] widths)
    {
        byte[] operand = new byte[widths.Length * Ww8Border.ShortLength];

        for (int i = 0; i < widths.Length; i++)
        {
            operand[i * Ww8Border.ShortLength] = widths[i];
            operand[(i * Ww8Border.ShortLength) + 1] = SingleLine;
        }

        return operand;
    }

    /// <summary>Six eight-byte <c>BRCVer9</c> codes: a BGR colour reference, then the width and type.</summary>
    private static byte[] Long(byte[] widths)
    {
        byte[] operand = new byte[widths.Length * Ww8Border.LongLength];

        for (int i = 0; i < widths.Length; i++)
        {
            int at = i * Ww8Border.LongLength;

            operand[at] = 0x11;
            operand[at + 1] = 0x22;
            operand[at + 2] = 0x33;
            operand[at + 3] = 0x00;
            operand[at + 4] = widths[i];
            operand[at + 5] = SingleLine;
        }

        return operand;
    }

    private static Ww8TableBorders Defaults()
        => Ww8TableBorders.Read(Short([8, 16, 24, 32, 40, 48]), isLongForm: false)!.Value;
}
