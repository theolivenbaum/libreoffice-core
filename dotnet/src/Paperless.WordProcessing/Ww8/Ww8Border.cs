using System.Buffers.Binary;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.WordProcessing.Layout;

namespace Paperless.WordProcessing.Ww8;

/// <summary>
/// One WW8 border code — a <c>BRC</c> — before it becomes a drawable edge.
/// </summary>
/// <remarks>
/// <para>
/// Kept as the file's own three fields rather than translated on the spot, because a border's <em>type</em>
/// has to survive the read: WW8 distinguishes "this side was never stated" (type 0) from "this side has
/// explicitly no border" (the nil code), and a table's default borders fill in only the former. Collapsing
/// both to a zero width at read time loses that, and then a cell that says "no line here" quietly acquires
/// the table's.
/// </para>
/// <para>
/// Two spellings, and a document can carry either: the four-byte <c>BRC80</c> names its colour by palette
/// index, and the eight-byte <c>BRCVer9</c> states a full <c>COLORREF</c>. Word writes the newer form and
/// keeps the older one alongside for readers that predate it.
/// </para>
/// </remarks>
/// <param name="Type">
/// The <c>brcType</c> code: 0 is "unstated", 1 a single line, 3 double, 6 dotted, and so on up to 27.
/// </param>
/// <param name="WidthEighths">
/// <c>dptLineWidth</c>, in <em>eighths of a point</em> — the same unit as DOCX's <c>w:sz</c>, and the one
/// unit in the family that is neither twips nor half-points.
/// </param>
/// <param name="Colour">The line's colour, with the automatic colour already resolved to black.</param>
public readonly record struct Ww8Border(int Type, int WidthEighths, Colour Colour)
{
    /// <summary>The type code meaning the side was never stated, so a default may fill it in.</summary>
    public const int UnstatedType = 0;

    /// <summary>The type code of the nil border: stated, and explicitly nothing.</summary>
    public const int NilType = 0xFF;

    /// <summary>The nil border — "no line here", which a table default must not override.</summary>
    public static Ww8Border Nil { get; } = new(NilType, 0, Colour.Black);

    /// <summary>True when nothing stated this side, so a table's default border applies to it.</summary>
    public bool IsUnstated => Type == UnstatedType;

    /// <summary>
    /// The width and colour the layout engine wants, or the default when this border draws nothing.
    /// </summary>
    /// <remarks>
    /// The style is dropped, because <see cref="TableBorder"/> carries none — but the <em>width</em> a
    /// style implies is not, since that is what takes space in the layout. A double line really is three
    /// times as thick as the nominal width, and a triple five times, so a table drawn with them and a
    /// reader that ignored the multiplier disagree on every row's position after the first.
    /// </remarks>
    public TableBorder AsTableBorder()
    {
        Length width = DrawnWidth();

        return width <= Length.Zero ? default : new TableBorder(width, Colour);
    }

    /// <summary>
    /// How thick the border is actually drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two conversions in sequence, and both are LibreOffice's:
    /// <c>WW8_BRCVer9::DetermineBorderProperties</c> (<c>ww8scan.cxx</c>) turns the eighths into twips and
    /// corrects the handful of types whose drawn width has nothing to do with their nominal one, and
    /// <c>editeng::ConvertBorderWidthFromWord</c> (<c>borderline.cxx</c>) then applies the multiplier the
    /// style implies. Doing only the first leaves a double border a third of its real thickness.
    /// </para>
    /// <para>
    /// A width of zero means three quarters of a point rather than nothing — LibreOffice's own default for
    /// a border whose width is missing, noted there as fdo#68779.
    /// </para>
    /// </remarks>
    private Length DrawnWidth()
    {
        if (Type is UnstatedType or NilType) return Length.Zero;

        int twips = WidthEighths * 20 / 8;

        twips = Type switch
        {
            // A triple line is five times an ordinary one, except at the two smallest nominal widths,
            // where Word draws it as if it were a wider ordinary line.
            10 => twips switch { 5 => 15, 10 => 45, _ => twips * 5 },

            // The waves are drawn rather than stroked, so their thickness is the wave's, not the pen's.
            20 => twips + 45,
            21 => twips + 90,
            _ => twips,
        };

        if (twips == 0) twips = MissingWidth;

        return Length.FromTwips(StyleWidth(Type, twips));
    }

    /// <summary>The width a border whose <c>dptLineWidth</c> is zero is drawn at: three quarters of a point.</summary>
    private const int MissingWidth = 15;

    /// <summary>
    /// The width the border's style implies, from the nominal width in twips.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two tables of <c>editeng/source/items/borderline.cxx</c> folded into one, since Paperless has no
    /// border-style enumeration to pass between them. The composite styles add fixed line and gap widths
    /// rather than scaling, which is why they are spelled out: a thin-thick border with a small gap is the
    /// nominal width plus a fifteen-twip line and a fifteen-twip gap however thin the nominal line is.
    /// </para>
    /// <para>
    /// Types 26 and 27 — outset and inset — are folded into the two large-gap composites the way
    /// <c>GetLineIndex</c> folds them, because LibreOffice cannot draw either and substitutes those. A type
    /// no arm names draws nothing, which is <c>ConvertBorderStyleFromWord</c>'s default arm.
    /// </para>
    /// </remarks>
    /// <param name="type">The <c>brcType</c> code.</param>
    /// <param name="twips">The nominal width in twips.</param>
    private static int StyleWidth(int type, int twips) => type switch
    {
        // Single lines. A thick line is drawn at twice its nominal width, and a hairline at a whole twip
        // however fine it claims to be — fdo#55526, since a zero-width line is invisible.
        2 => twips * 2,
        5 => Math.Max(twips, 1),
        1 or 20 or 6 or 7 or 8 or 9 => twips,

        // A fine dashed line is drawn at a point at the least, or the dashes merge.
        22 => twips is > 0 and < 20 ? 20 : twips,

        // Double, triple and the shading beams, all drawn as two lines and a gap.
        3 or 10 or 21 or 23 => twips * 3,

        // The composites: a thin line, a gap and a thick one, in one order or the other. Which order does
        // not change the total — a small gap costs fifteen twips of second line and fifteen of gap either
        // way, and a large one forty-five — so the two directions share their arms here.
        11 or 12 or 13 => twips + 15 + 15,
        14 or 15 or 16 or 24 or 25 => twips * 2,
        17 or 18 or 19 or 26 or 27 => twips + 15 + 30,

        _ => 0,
    };

    /// <summary>
    /// Reads the four-byte <c>BRC80</c> form, or null when there are not four bytes to read.
    /// </summary>
    /// <param name="bytes">The operand, positioned at the border code.</param>
    public static Ww8Border? ReadShort(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < ShortLength) return null;

        // Nil is spelled as a width and type of 0xFF rather than by a type code of its own.
        if (bytes[0] == 0xFF && bytes[1] == 0xFF) return Nil;

        return new Ww8Border(bytes[1], bytes[0], Ww8Colours.At(bytes[2]) ?? Colour.Black);
    }

    /// <summary>
    /// Reads the eight-byte <c>BRCVer9</c> form, or null when there are not eight bytes to read.
    /// </summary>
    /// <param name="bytes">The operand, positioned at the border code.</param>
    public static Ww8Border? ReadLong(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < LongLength) return null;

        // Here nil is the whole second word set, which is why the type cannot be tested on its own.
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) == 0xFFFFFFFF) return Nil;

        uint reference = BinaryPrimitives.ReadUInt32LittleEndian(bytes);

        return new Ww8Border(
            bytes[5], bytes[4], Ww8Colours.FromColorRef(reference) ?? Colour.Black);
    }

    /// <summary>How many bytes the older border code occupies.</summary>
    public const int ShortLength = 4;

    /// <summary>How many bytes the newer one occupies.</summary>
    public const int LongLength = 8;
}

/// <summary>
/// The four border codes a table's cell descriptor carries, in WW8's own order.
/// </summary>
/// <remarks>
/// Top, left, bottom, right — neither the order the sides are usually written in nor OOXML's, and the same
/// trap the cell padding sprms set. A named record rather than an array so the order cannot be misread at
/// the point of use, whatever it was in the file.
/// </remarks>
/// <param name="Top">Its top edge.</param>
/// <param name="Left">Its left edge.</param>
/// <param name="Bottom">Its bottom edge.</param>
/// <param name="Right">Its right edge.</param>
public readonly record struct Ww8CellBorders(
    Ww8Border Top, Ww8Border Left, Ww8Border Bottom, Ww8Border Right)
{
    /// <summary>Which sides a <c>sprmTSetBrc</c> flag byte asks to change.</summary>
    public const int TopFlag = 0x01;

    /// <summary>The left side's flag.</summary>
    public const int LeftFlag = 0x02;

    /// <summary>The bottom side's flag.</summary>
    public const int BottomFlag = 0x04;

    /// <summary>The right side's flag.</summary>
    public const int RightFlag = 0x08;

    /// <summary>
    /// This set with the sides a flag byte names replaced by a border.
    /// </summary>
    /// <param name="sides">The flag byte, whose low four bits name the sides.</param>
    /// <param name="border">The border to put on each named side.</param>
    /// <remarks>
    /// A replacement rather than a merge, and unconditional: <c>sprmTSetBrc</c> overwrites the cell
    /// descriptor's own code in LibreOffice's <c>ProcessSprmTSetBRC</c>, so a sprm setting a side to the nil
    /// border removes whatever the descriptor said. That is how a document turns one edge of one cell off.
    /// </remarks>
    public Ww8CellBorders With(int sides, Ww8Border border) => new(
        (sides & TopFlag) != 0 ? border : Top,
        (sides & LeftFlag) != 0 ? border : Left,
        (sides & BottomFlag) != 0 ? border : Bottom,
        (sides & RightFlag) != 0 ? border : Right);

    /// <summary>The four edges as the layout engine wants them.</summary>
    public CellBorders AsCellBorders() => new(
        Left.AsTableBorder(),
        Right.AsTableBorder(),
        Top.AsTableBorder(),
        Bottom.AsTableBorder());
}

/// <summary>
/// A table's six default border codes, from <c>sprmTTableBorders</c>.
/// </summary>
/// <remarks>
/// <para>
/// Six rather than four, because a table states its outer edges and its interior lines separately — which is
/// how Word's table dialogue expresses "a box around it and a grid inside", and why the defaults cannot
/// simply be laid over a cell: which of the six a cell's top takes depends on whether the cell is in the
/// table's first row, and which its left takes on whether it is in the row's first column.
/// </para>
/// <para>
/// They are <em>defaults</em>, not overrides. A cell that states a side keeps it; only a side whose type is
/// zero — unstated, as distinct from the nil border, which is a stated absence — is filled in. That is the
/// whole of <c>ww8par2.cxx</c>'s third pass over the bands, and it is what makes a table that carries nothing
/// but these draw at all.
/// </para>
/// <para>
/// Not written by LibreOffice's own DOC export, which states every edge per cell instead, so a corpus
/// document cannot be produced by round-tripping one. Word writes it constantly.
/// </para>
/// </remarks>
/// <param name="Top">The table's outer top edge.</param>
/// <param name="Left">Its outer left edge.</param>
/// <param name="Bottom">Its outer bottom edge.</param>
/// <param name="Right">Its outer right edge.</param>
/// <param name="Horizontal">The line between two rows.</param>
/// <param name="Vertical">The line between two columns.</param>
public readonly record struct Ww8TableBorders(
    Ww8Border Top,
    Ww8Border Left,
    Ww8Border Bottom,
    Ww8Border Right,
    Ww8Border Horizontal,
    Ww8Border Vertical)
{
    /// <summary>How many codes the operand holds.</summary>
    public const int Count = 6;

    /// <summary>
    /// Reads the operand: six border codes end to end, in the same order the cell descriptor uses plus the
    /// two interior lines.
    /// </summary>
    /// <param name="operand">The sprm's operand.</param>
    /// <param name="isLongForm">
    /// True for <c>sprmTTableBorders</c> (0xD613), whose codes are the eight-byte <c>BRCVer9</c>; false for
    /// <c>sprmTTableBorders80</c> (0xD605), whose codes are the four-byte <c>BRC80</c>. Word writes both, the
    /// newer after the older, so the newer must be applied last — which it is, since a grpprl is walked in
    /// order.
    /// </param>
    /// <returns>The six codes, or null when the operand is too short to hold them.</returns>
    public static Ww8TableBorders? Read(ReadOnlySpan<byte> operand, bool isLongForm)
    {
        int size = isLongForm ? Ww8Border.LongLength : Ww8Border.ShortLength;
        if (operand.Length < size * Count) return null;

        Ww8Border[] sides = new Ww8Border[Count];
        for (int i = 0; i < Count; i++)
        {
            ReadOnlySpan<byte> code = operand.Slice(i * size, size);
            sides[i] = (isLongForm ? Ww8Border.ReadLong(code) : Ww8Border.ReadShort(code))
                       ?? default;
        }

        return new Ww8TableBorders(sides[0], sides[1], sides[2], sides[3], sides[4], sides[5]);
    }

    /// <summary>
    /// A cell's four edges with the sides it did not state filled in from these defaults.
    /// </summary>
    /// <remarks>
    /// The choice per side is positional, and the position that matters is the cell's place in the
    /// <em>unmerged</em> row and the row's place in the table: a top edge is the table's outer top only in the
    /// first row and the interior horizontal line everywhere else, and a left edge is the outer left only in
    /// the first column. Reading the four outer codes onto every cell draws a box round each one instead of
    /// round the table.
    /// </remarks>
    /// <param name="stated">What the cell descriptor and its overrides said.</param>
    /// <param name="isFirstRow">True when the cell is in the table's first row.</param>
    /// <param name="isLastRow">True when it is in the last.</param>
    /// <param name="isFirstColumn">True when it is the first of its row.</param>
    /// <param name="isLastColumn">True when it is the last of its row.</param>
    public Ww8CellBorders FillIn(
        Ww8CellBorders stated,
        bool isFirstRow,
        bool isLastRow,
        bool isFirstColumn,
        bool isLastColumn)
        => new(
            stated.Top.IsUnstated ? (isFirstRow ? Top : Horizontal) : stated.Top,
            stated.Left.IsUnstated ? (isFirstColumn ? Left : Vertical) : stated.Left,
            stated.Bottom.IsUnstated ? (isLastRow ? Bottom : Horizontal) : stated.Bottom,
            stated.Right.IsUnstated ? (isLastColumn ? Right : Vertical) : stated.Right);
}
