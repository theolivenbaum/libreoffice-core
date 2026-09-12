using Paperless.Core.Units;

namespace Paperless.Spreadsheets.Layout;

/// <summary>
/// What one of a pivot table's generated cell styles changes about the text under it.
/// </summary>
/// <remarks>
/// <para>
/// A pivot table's cells are not formatted by the file. <c>ScDPOutput::Output</c> clears the
/// whole output range and then applies four styles it creates on demand —
/// <c>lcl_SetStyleById</c>, <c>sc/source/core/data/dpoutput.cxx</c>:264-295 — of which only
/// three carry anything at all: <c>Pivot Table Result</c> and <c>Pivot Table Title</c> are bold,
/// <c>Pivot Table Category</c> and <c>Pivot Table Title</c> are left justified, and
/// <c>Pivot Table Value</c>, <c>Field</c>, <c>Corner</c> and <c>Top</c> state nothing. Beside
/// them one attribute is applied directly: a row field that is not the innermost indents its
/// member cell by <c>13 px</c> per level (<c>:1136-1140</c>), which is 195 twips and the
/// <c>fo:margin-left="0.1354in"</c> the reference's own <c>.fods</c> of such a workbook writes.
/// </para>
/// <para>
/// A differential record for the same reason <see cref="SheetConditionalText"/> is one: the
/// styles state three properties between them and are silent about everything else, so a null
/// here is that silence. What fills that silence over a pivot's own rectangle is <em>not</em>
/// the cell's own format: Calc empties the range first —
/// <c>clearContents(… HARDATTR | STYLES …)</c> at
/// <c>sc/source/filter/oox/pivottablebuffer.cxx</c>:1336 over the stated range and
/// <c>DeleteAreaTab(…, InsertDeleteFlags::ALL)</c> at <c>dpoutput.cxx</c>:1226 over the computed
/// one — so <c>XlsxPivotGrid</c> states all three properties for every cell of that
/// rectangle, taking the sheet's own default format where the generated style says nothing.
/// Measured over the nineteen pivot rectangles of the corpus's eleven pivot-bearing workbooks:
/// 330 of their 9970 cells state an alignment or an indent, merging disagrees with 26.2.4.2's
/// own resolved view on 81 of the alignments and 98 of the indents, and replacing agrees on all
/// 9970 (<c>probes/pivot-res-r108/clearing-census.py</c>).
/// </para>
/// <para>
/// What is <em>still</em> narrower than the reference is everything else the two calls remove: a
/// font face, a size, a colour, a fill and a border on a pivot cell all survive here and none
/// survives there. Borders are nil on this corpus — not one cell of the twenty-eight pivot
/// ranges has a <c>cellXfs</c> entry stating one
/// (<c>probes/pivot-res-r108/statedborders.py</c>) — and the other four are unmeasured.
/// </para>
/// </remarks>
public readonly record struct SheetPivotStyle
{
    /// <summary>The weight on the usual 100–900 scale, or null to keep the cell's own.</summary>
    public int? FontWeight { get; init; }

    /// <summary>How the text sits across the column, or null to keep the cell's own.</summary>
    public SheetHorizontalAlignment? Horizontal { get; init; }

    /// <summary>The indent from the aligned edge, or null to keep the cell's own.</summary>
    public Length? Indent { get; init; }

    /// <summary>True when the style changes nothing about the text.</summary>
    public bool IsNone => FontWeight is null && Horizontal is null && Indent is null;

    /// <summary>Lays this style over what the cell states.</summary>
    /// <param name="stated">The format the cell resolves to on its own.</param>
    public SheetCellFormat Over(SheetCellFormat stated)
    {
        ArgumentNullException.ThrowIfNull(stated);
        if (IsNone) return stated;

        return stated with
        {
            FontWeight = FontWeight ?? stated.FontWeight,
            Horizontal = Horizontal ?? stated.Horizontal,
            Indent = Indent ?? stated.Indent,
        };
    }
}
