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
/// <strong>Of those five, only the colour is cleared, and the reason is that clearing is only
/// half of what the reference does.</strong> <c>ScDPOutput::Output</c> ends with
/// <c>maFormatOutput.apply</c> (<c>dpoutput.cxx</c>:1190), which lays the pivot's own
/// <c>&lt;format&gt;</c>/<c>dxf</c> records back over the generated styles — and those are far
/// from nil: <strong>286 records across 12 of the corpus's 28 pivot parts</strong>
/// (<c>probes/pivot-fmt-r110/format-census.py</c>, which corrects an r108 census that searched
/// for <c>&lt;format&gt;</c> as a direct child of <c>pivotTableDefinition</c> where it is a child
/// of <c>&lt;formats&gt;</c>, and so counted zero). This tree reads none of them, so for a
/// property those records restore, clearing it alone moves <em>away</em> from the reference.
/// </para>
/// <para>
/// Scored cell for cell against 26.2.4.2's own resolved view of the 9878 cells of the corpus's
/// 19 generatable pivot ranges — <c>probes/pivot-fmt-r110/clearfour.py</c>, merge is what this
/// tree shipped before:
/// </para>
/// <list type="table">
/// <item><description>colour — clear <strong>9873</strong>, merge 9804</description></item>
/// <item><description>font identity, the face and its declared generic class together — clear
/// 9750, merge <strong>9835</strong></description></item>
/// <item><description>size — clear 9783, merge <strong>9872</strong></description></item>
/// <item><description>fill — 9789 either way: not one of the 9878 cells states a fill, so there
/// is nothing to clear</description></item>
/// <item><description>border — nil, as r108 measured</description></item>
/// </list>
/// <para>
/// The font identity and the size lose because <c>033_Event_planning_tracker</c>'s <c>dxf</c>
/// records restore a 12 pt <c>Consolas</c>-modern that its own cells happen also to state, on 89
/// of its 91 cells. Reproducing it by not clearing is an accident, but it is an accident that is
/// right 85 more times than clearing is, so clearing waits for the <c>dxf</c> half.
/// </para>
/// <para>
/// <strong>And the colour's own margin is smaller than 9873 against 9804 suggests.</strong> 73 of
/// the 74 cells clearing wins are <c>033</c> body cells carrying
/// <c>style:use-window-font-color="true"</c> over a black <c>dxf</c> fill — the reference is
/// drawing white on a dark ground, and neither model draws either the white or the fill. What is
/// genuinely fixed is one cell of <c>037_Personal_money_tracker</c>, a corner cell the workbook
/// leaves in a 14 pt accent-coloured serif; what is genuinely broken is five cells of
/// <c>033</c>'s row-label column, where a <c>dxf</c> states the black we were reproducing.
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

    /// <summary>
    /// The format the emptied rectangle falls back to, or null for a cell outside one.
    /// </summary>
    /// <remarks>
    /// Not the cell's own and not <c>cellXfs[0]</c>: the <c>Normal</c> <c>cellStyleXf</c>, which
    /// is what Calc calls the <c>Default</c> cell style — see
    /// <c>XlsxCellFormats.NormalStyleXf</c>, which records the probe workbook that settles it.
    /// Only the <em>colour</em> is taken from it; the remarks above say why the face, the
    /// declared class and the size are not.
    /// </remarks>
    public SheetCellFormat? Cleared { get; init; }

    /// <summary>True when the style changes nothing about the text.</summary>
    public bool IsNone
        => FontWeight is null && Horizontal is null && Indent is null && Cleared is null;

    /// <summary>Lays this style over what the cell states.</summary>
    /// <param name="stated">The format the cell resolves to on its own.</param>
    public SheetCellFormat Over(SheetCellFormat stated)
    {
        ArgumentNullException.ThrowIfNull(stated);
        if (IsNone) return stated;

        // One `with`: `At` is called for every drawn cell of every page and a pivot's rectangle
        // can be ten thousand cells, so the cleared base is folded into the same copy the
        // generated style makes rather than allocating a second one.
        return stated with
        {
            Colour = Cleared?.Colour ?? stated.Colour,
            FontWeight = FontWeight ?? stated.FontWeight,
            Horizontal = Horizontal ?? stated.Horizontal,
            Indent = Indent ?? stated.Indent,
        };
    }
}
