using System.Globalization;
using System.Xml.Linq;

namespace Paperless.WordProcessing.Ooxml;

/// <summary>
/// Which of a table style's conditional layers a table has switched on — <c>w:tblLook</c>.
/// </summary>
/// <remarks>
/// <para>
/// A table style states its formatting in parts: an unconditional one and up to twelve
/// <c>w:tblStylePr</c> layers, each naming a region of the table it applies to. The layers are inert
/// until the table asks for them, and <c>w:tblLook</c> is that request. So the same style makes one
/// table's first row bold and leaves another's plain, and reading the style without reading the look
/// is how a reader ends up applying either all of it or none of it.
/// </para>
/// <para>
/// Both spellings are read because both are in the corpus. Word 2007 wrote a hexadecimal bitmask in
/// <c>w:val</c>; Word 2010 and later write the same bits as named attributes and usually keep the
/// mask beside them. Where a file states the attributes they win, since they are the newer and
/// unambiguous form — which is the precedence <c>DomainMapperTableManager::sprm</c> applies.
/// </para>
/// </remarks>
/// <param name="FirstRow">The heading row takes <c>firstRow</c>.</param>
/// <param name="LastRow">The final row takes <c>lastRow</c>.</param>
/// <param name="FirstColumn">The leading column takes <c>firstCol</c>.</param>
/// <param name="LastColumn">The trailing column takes <c>lastCol</c>.</param>
public readonly record struct WordTableLook(
    bool FirstRow,
    bool LastRow,
    bool FirstColumn,
    bool LastColumn)
{
    /// <summary>What a table stating no <c>w:tblLook</c> at all asks for: nothing.</summary>
    public static WordTableLook None => default;

    /// <summary>Reads the look off a table's <c>w:tblPr</c>.</summary>
    /// <remarks>
    /// The bits are §17.4.56's: <c>0x0020</c> first row, <c>0x0040</c> last row, <c>0x0080</c> first
    /// column, <c>0x0100</c> last column. The two band bits are read by nothing here — see
    /// <see cref="WordTableStyleConditions"/> for why the band layers are not applied.
    /// </remarks>
    /// <param name="tableProperties">The table's <c>w:tblPr</c>, or null.</param>
    public static WordTableLook Read(XElement? tableProperties)
    {
        XElement? look = Word.Child(tableProperties, "tblLook");
        if (look is null) return None;

        int mask = 0;
        string? value = Word.Attribute(look, "val");
        if (value is { Length: > 0 })
        {
            _ = int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask);
        }

        return new WordTableLook(
            Flag(look, "firstRow", mask, 0x0020),
            Flag(look, "lastRow", mask, 0x0040),
            Flag(look, "firstColumn", mask, 0x0080),
            Flag(look, "lastColumn", mask, 0x0100));

        static bool Flag(XElement look, string name, int mask, int bit)
            => Word.Attribute(look, name) switch
            {
                "1" or "true" or "on" => true,
                "0" or "false" or "off" => false,
                _ => (mask & bit) != 0,
            };
    }
}

/// <summary>
/// The <c>w:tblStylePr</c> layers that apply to one cell, in the order they are consulted.
/// </summary>
/// <remarks>
/// <para>
/// §17.7.6 fixes both which layers a cell is in and which of them wins where two disagree. The
/// specification lists them in the order they are <em>applied</em> — whole table, then the bands,
/// then the first and last column, then the first and last row, then the four corner cells — so the
/// last applied is the most specific. <see cref="Names"/> hands them back the other way up, most
/// specific first, because a resolver stops at the first layer that states the property it wants.
/// </para>
/// <para>
/// <b>The band layers are deliberately absent.</b> Row and column banding needs
/// <c>w:tblStyleRowBandSize</c>, the band a row falls in counted with the heading and total rows
/// excluded, and it decides shading far more often than it decides text. Measured over the whole
/// words track: 14 of the 134 DOCX files declare a <c>w:tblStylePr</c> at all, 7 name such a style
/// from a table, and <b>not one of those 7 carries a <c>w:rPr</c> on a band layer</b> — every one of
/// them carries it on <c>firstRow</c>. Implementing the bands here would be reach that cannot be
/// measured, so they are left for a round that has a document to measure them on.
/// </para>
/// <para>
/// <b>Only the run half of a layer is applied</b>, and that too is scope rather than principle: a
/// <c>w:tblStylePr</c> may also carry <c>w:pPr</c>, <c>w:tcPr</c> and <c>w:tblPr</c>. Character
/// formatting is what moves a line break, and a line break is what moves a page. The other three are
/// recorded as not done in <c>dotnet/probes/words-regress-01/results.md</c>.
/// </para>
/// </remarks>
/// <param name="Look">Which layers the table switched on.</param>
/// <param name="IsFirstRow">Whether the cell is in the table's first row.</param>
/// <param name="IsLastRow">Whether the cell is in the table's last row.</param>
/// <param name="IsFirstColumn">Whether the cell is in the leading grid column.</param>
/// <param name="IsLastColumn">Whether the cell is in the trailing grid column.</param>
public readonly record struct WordTableStyleConditions(
    WordTableLook Look,
    bool IsFirstRow,
    bool IsLastRow,
    bool IsFirstColumn,
    bool IsLastColumn)
{
    /// <summary>A cell in no conditional region at all.</summary>
    public static WordTableStyleConditions None => default;

    /// <summary>
    /// The layer names that apply, most specific first.
    /// </summary>
    /// <remarks>
    /// A corner cell's layer is offered only when <em>both</em> of the edges meeting there are switched
    /// on, which is what makes <c>nwCell</c> a refinement of <c>firstRow</c> and <c>firstCol</c> rather
    /// than a third independent thing. <c>wholeTable</c> is last and needs no bit: a style's
    /// unconditional formatting applies to every cell of every table that names it.
    /// </remarks>
    public IReadOnlyList<string> Names
    {
        get
        {
            bool first = Look.FirstRow && IsFirstRow;
            bool last = Look.LastRow && IsLastRow;
            bool leading = Look.FirstColumn && IsFirstColumn;
            bool trailing = Look.LastColumn && IsLastColumn;

            List<string> names = [];

            if (first && leading) names.Add("nwCell");
            if (first && trailing) names.Add("neCell");
            if (last && leading) names.Add("swCell");
            if (last && trailing) names.Add("seCell");

            if (first) names.Add("firstRow");
            if (last) names.Add("lastRow");
            if (leading) names.Add("firstCol");
            if (trailing) names.Add("lastCol");

            names.Add("wholeTable");
            return names;
        }
    }
}
