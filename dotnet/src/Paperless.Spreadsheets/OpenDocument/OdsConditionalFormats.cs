using System.Xml.Linq;
using Paperless.OpenDocument;
using Paperless.Spreadsheets.Layout;

namespace Paperless.Spreadsheets.OpenDocument;

/// <summary>
/// The blocks a sheet's conditional formats cover.
/// </summary>
/// <remarks>
/// <para>
/// Only the ranges, because only the ranges decide anything here: a cell whose pattern carries a
/// non-empty <c>ATTR_CONDITIONAL</c> is measured rather than computed when its row height is
/// recovered, and Calc tests the attribute rather than the condition
/// (<c>ScColumn::GetOptimalHeight</c>, <c>sc/source/core/data/column2.cxx:937-941</c>). See
/// <see cref="SheetLayout.ConditionalRanges"/>.
/// </para>
/// <para>
/// <strong>The spelling is the extension namespace's and the specification's is not what the
/// corpus holds.</strong> ODF 1.2 states a conditional format as a <c>style:map</c> on the cell's
/// own style; LibreOffice writes its full model as <c>calcext:conditional-formats</c> inside the
/// <c>table:table</c> instead (<c>sc/source/filter/xml/xmlexprt.cxx</c>'s
/// <c>WriteExternalDataMapping</c> neighbours), and of the 307 converted <c>.ods</c> **95 state
/// the <c>calcext:</c> spelling and one states a <c>style:map</c> in <c>content.xml</c> without
/// it**. That one document is the residual this deliberately does not read: recovering it would
/// mean resolving every automatic cell style back to the cells that name it, and it is worth one
/// row of one track.
/// </para>
/// <para>
/// <c>calcext:target-range-address</c> is a space-separated list in the same OOO syntax
/// <c>table:print-ranges</c> uses, so it goes through the same two helpers.
/// </para>
/// </remarks>
internal static class OdsConditionalFormats
{
    /// <summary>Reads the ranges every conditional format of one sheet covers.</summary>
    /// <param name="table">The <c>table:table</c> element.</param>
    public static IReadOnlyList<SheetRange> ReadRanges(XElement table)
    {
        ArgumentNullException.ThrowIfNull(table);

        XElement? formats =
            table.Element(XName.Get("conditional-formats", OdfNamespaces.CalcExt));
        if (formats is null) return [];

        List<SheetRange> ranges = [];

        foreach (XElement format in
                 formats.Elements(XName.Get("conditional-format", OdfNamespaces.CalcExt)))
        {
            string? stated =
                format.Attribute(XName.Get("target-range-address", OdfNamespaces.CalcExt))?.Value;
            if (string.IsNullOrEmpty(stated)) continue;

            foreach (string part in SheetAddress.SplitList(stated, ' '))
            {
                if (SheetAddress.TryParseRange(part, out SheetRange range)) ranges.Add(range);
            }
        }

        return ranges;
    }
}
