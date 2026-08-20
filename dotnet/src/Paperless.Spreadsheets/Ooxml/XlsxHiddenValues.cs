using System.Xml.Linq;
using Paperless.Spreadsheets.Layout;

namespace Paperless.Spreadsheets.Ooxml;

/// <summary>
/// The cells a worksheet's conditional formatting draws an icon or a bar in <em>instead of</em>
/// their text.
/// </summary>
/// <remarks>
/// <para>
/// An icon-set or data-bar rule carries <c>showValue="0"</c>, and Calc honours it by clearing
/// <c>bDoCell</c> before the cell's string is laid out — the icon replaces the number rather than
/// joining it (<c>sc/source/ui/view/output2.cxx:1691-1698</c>). The same code runs for printing,
/// so a hidden value is hidden in the PDF.
/// </para>
/// <para>
/// The trap is that it is <em>not</em> a property of the range. <c>GetIconSetInfo</c> returns
/// nothing at all when the band a cell falls in has a <c>NoIcons</c> entry in a custom icon
/// vector (<c>sc/source/core/data/colorscale.cxx:1231-1239</c>, with the <c>-1</c> put there by
/// <c>IconSetRule::importIcon</c>), and a cell with no icon information keeps its text. A rule
/// whose low bands are <c>NoIcons</c> therefore hides some of its cells and prints the rest —
/// which is exactly what
/// <c>077_Inventory_list_with_highlighting</c> does, drawing thirteen <c>0</c>s and replacing
/// twelve <c>1</c>s with a red flag.
/// </para>
/// <para>
/// Bands are chosen by the <em>last</em> threshold a value satisfies, not the first
/// (<c>colorscale.cxx:1200-1215</c>), the comparison is <c>&gt;=</c> unless the threshold says
/// <c>gte="0"</c>, and <c>percent</c>/<c>percentile</c>/<c>min</c>/<c>max</c> thresholds resolve
/// against the numeric values inside the rule's own range (<c>ScColorFormat::getValues</c>,
/// <c>colorscale.cxx:504-573</c>).
/// </para>
/// <para>
/// Only the value-hiding half of conditional formatting is modelled here. Differential formats
/// (<c>dxf</c> fills, strikethrough, fonts) need the rule's expression evaluated and are not
/// attempted; the icon and the bar themselves are ink and are not drawn either.
/// </para>
/// </remarks>
internal sealed class XlsxHiddenValues
{
    /// <summary>The 2009 extension namespace that carries custom icon sets.</summary>
    private const string X14Namespace
        = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    /// <summary>The namespace the extension writes its ranges and thresholds in.</summary>
    private const string XmNamespace = "http://schemas.microsoft.com/office/excel/2006/main";

    /// <summary>A worksheet with no such rule, which is almost every worksheet.</summary>
    public static readonly XlsxHiddenValues None = new([]);

    private readonly HashSet<(int Row, int Column)> _hidden;

    private XlsxHiddenValues(HashSet<(int Row, int Column)> hidden) => _hidden = hidden;

    /// <summary>True when nothing on the sheet is hidden.</summary>
    public bool IsEmpty => _hidden.Count == 0;

    /// <summary>True when this cell's text is replaced by an icon or a bar.</summary>
    public bool Hides(int row, int column) => _hidden.Contains((row, column));

    /// <summary>
    /// Works out which of a worksheet's cells draw an icon or a bar in place of their text.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="None"/> without walking the sheet when no rule hides anything, so the
    /// cost on a document that states no such rule is one pass over its
    /// <c>conditionalFormatting</c> elements.
    /// </remarks>
    public static XlsxHiddenValues Read(XElement? worksheet)
    {
        if (worksheet is null) return None;

        List<Rule> rules = ReadRules(worksheet);
        if (rules.Count == 0) return None;

        Dictionary<(int Row, int Column), double> numbers = ReadNumbers(worksheet);
        if (numbers.Count == 0) return None;

        HashSet<(int Row, int Column)> hidden = [];
        foreach (Rule rule in rules) rule.Collect(numbers, hidden);
        return hidden.Count == 0 ? None : new XlsxHiddenValues(hidden);
    }

    /// <summary>
    /// Every numeric cell on the sheet, addressed the way the rules address them.
    /// </summary>
    /// <remarks>
    /// "Numeric" is <c>ScRefCellValue::hasNumeric</c>: a value cell or a formula whose cached
    /// result is a number. A shared string, an inline string, a formula's cached <em>string</em>
    /// result and an error are all excluded, and a cell excluded here keeps its text however the
    /// rule is written — <c>GetIconSetInfo</c> and <c>GetDataBarInfo</c> both return nothing for
    /// one. A boolean is a number in Calc and is included, which is why <c>t="b"</c> is here.
    /// </remarks>
    private static Dictionary<(int Row, int Column), double> ReadNumbers(XElement worksheet)
    {
        Dictionary<(int Row, int Column), double> numbers = [];
        int expectedRow = 0;

        foreach (XElement rowElement in Xlsx.Children(Xlsx.Child(worksheet, "sheetData"), "row"))
        {
            int rowIndex = (Xlsx.Integer(rowElement, "r") - 1) ?? expectedRow;
            if (rowIndex < 0) rowIndex = expectedRow;
            expectedRow = rowIndex + 1;

            int expectedColumn = 0;
            foreach (XElement cellElement in Xlsx.Children(rowElement, "c"))
            {
                int column = expectedColumn;
                if (Xlsx.Attribute(cellElement, "r") is { } reference
                    && Xlsx.TryParseCellReference(reference, out int parsed, out _))
                {
                    column = parsed;
                }
                if (column < 0) column = expectedColumn;
                expectedColumn = column + 1;

                string type = Xlsx.Attribute(cellElement, "t") ?? "n";
                if (type is not ("n" or "b")) continue;
                if (Xlsx.Child(cellElement, "v") is not { } value) continue;
                if (Xlsx.Double(value.Value) is not { } number) continue;

                numbers[(rowIndex, column)] = number;
            }
        }

        return numbers;
    }

    private static List<Rule> ReadRules(XElement worksheet)
    {
        List<Rule> rules = [];

        foreach (XElement block in Xlsx.Children(worksheet, "conditionalFormatting"))
        {
            List<SheetRange> ranges = ParseRanges(Xlsx.Attribute(block, "sqref"));
            if (ranges.Count == 0) continue;

            foreach (XElement cfRule in Xlsx.Children(block, "cfRule"))
            {
                switch (Xlsx.Attribute(cfRule, "type"))
                {
                    case "iconSet" when Xlsx.Child(cfRule, "iconSet") is { } iconSet:
                        AddIconSet(rules, ranges, iconSet, Xlsx.Name("cfvo"),
                                   Xlsx.Name("cfIcon"), extension: false);
                        break;
                    case "dataBar" when Xlsx.Child(cfRule, "dataBar") is { } dataBar:
                        AddDataBar(rules, ranges, dataBar, Xlsx.Name("cfvo"), extension: false);
                        break;
                    default:
                        break;
                }
            }
        }

        foreach (XElement block in ExtensionBlocks(worksheet))
        {
            List<SheetRange> ranges =
                ParseRanges(block.Element(XName.Get("sqref", XmNamespace))?.Value);
            if (ranges.Count == 0) continue;

            foreach (XElement cfRule in block.Elements(XName.Get("cfRule", X14Namespace)))
            {
                // The extension's data bar carries no showValue of its own and
                // ExtCfDataBarRule::importDataBar never touches mbOnlyBar
                // (condformatbuffer.cxx:1710-1715), so only the plain element decides for a bar.
                if (Xlsx.Attribute(cfRule, "type") != "iconSet") continue;
                if (cfRule.Element(XName.Get("iconSet", X14Namespace)) is not { } iconSet) continue;

                AddIconSet(rules, ranges, iconSet, XName.Get("cfvo", X14Namespace),
                           XName.Get("cfIcon", X14Namespace), extension: true);
            }
        }

        return rules;
    }

    /// <summary>
    /// The <c>x14:conditionalFormatting</c> blocks a worksheet's <c>extLst</c> carries.
    /// </summary>
    /// <remarks>
    /// Reached by element name rather than by the <c>ext</c> element's <c>uri</c> GUID: the URI
    /// identifies the extension, but a producer is free to put the block under any <c>ext</c> it
    /// declares and the namespace is what actually names the content.
    /// </remarks>
    private static IEnumerable<XElement> ExtensionBlocks(XElement worksheet)
        => Xlsx.Child(worksheet, "extLst") is { } extensions
            ? extensions.Descendants(XName.Get("conditionalFormatting", X14Namespace))
            : [];

    private static void AddIconSet(
        List<Rule> rules,
        IReadOnlyList<SheetRange> ranges,
        XElement iconSet,
        XName cfvoName,
        XName cfIconName,
        bool extension)
    {
        if (Xlsx.Flag(iconSet, "showValue", fallback: true)) return;

        List<Threshold> thresholds = [];
        foreach (XElement cfvo in iconSet.Elements(cfvoName))
        {
            if (ReadThreshold(cfvo, extension) is not { } threshold) return;
            thresholds.Add(threshold);
        }

        // Fewer than three entries is not an icon set Calc will draw from at all
        // (colorscale.cxx:1194-1195), and a cell with no icon information keeps its text.
        if (thresholds.Count < 3) return;

        List<int>? icons = null;
        if (Xlsx.Flag(iconSet, "custom"))
        {
            icons = [];
            foreach (XElement icon in iconSet.Elements(cfIconName))
            {
                // NoIcons is stored as index -1, and an index of -1 is what makes
                // GetIconSetInfo return nothing (condformatbuffer.cxx:459-469).
                icons.Add(Xlsx.Attribute(icon, "iconSet") == "NoIcons"
                    ? -1
                    : Xlsx.Integer(icon, "iconId") ?? -1);
            }
        }

        rules.Add(new Rule(ranges, thresholds, icons, Xlsx.Flag(iconSet, "reverse"), IconSet: true));
    }

    private static void AddDataBar(
        List<Rule> rules, IReadOnlyList<SheetRange> ranges, XElement dataBar, XName cfvoName,
        bool extension)
    {
        if (Xlsx.Flag(dataBar, "showValue", fallback: true)) return;

        List<Threshold> thresholds = [];
        foreach (XElement cfvo in dataBar.Elements(cfvoName))
        {
            if (ReadThreshold(cfvo, extension) is not { } threshold) return;
            thresholds.Add(threshold);
        }

        rules.Add(new Rule(ranges, thresholds, CustomIcons: null, Reverse: false, IconSet: false));
    }

    /// <summary>
    /// One <c>cfvo</c> threshold, or null when it is one this cannot resolve.
    /// </summary>
    /// <remarks>
    /// A threshold stated as a formula would need the formula evaluated against the sheet.
    /// Returning null drops the whole rule, so the cells keep their text — the direction that
    /// leaves the output as it was rather than hiding something on a guess.
    /// </remarks>
    private static Threshold? ReadThreshold(XElement cfvo, bool extension)
    {
        string type = Xlsx.Attribute(cfvo, "type") ?? "num";

        // gte="0" turns the band boundary from >= into > (condformatbuffer.cxx:118-124).
        bool orEqual = Xlsx.Flag(cfvo, "gte", fallback: true);

        ThresholdKind kind = type switch
        {
            "num" => ThresholdKind.Number,
            "percent" => ThresholdKind.Percent,
            "percentile" => ThresholdKind.Percentile,
            "min" or "autoMin" => ThresholdKind.Minimum,
            "max" or "autoMax" => ThresholdKind.Maximum,
            _ => ThresholdKind.Unusable,
        };
        if (kind == ThresholdKind.Unusable) return null;

        if (kind is ThresholdKind.Minimum or ThresholdKind.Maximum)
            return new Threshold(kind, 0, orEqual);

        // The plain element states its value in an attribute; the extension states it as a
        // formula element whose text is, for these types, a literal (importFormula,
        // condformatbuffer.cxx:425-434).
        string? text = extension
            ? cfvo.Element(XName.Get("f", XmNamespace))?.Value
            : Xlsx.Attribute(cfvo, "val");

        return Xlsx.Double(text?.Trim()) is { } value ? new Threshold(kind, value, orEqual) : null;
    }

    private static List<SheetRange> ParseRanges(string? sqref)
    {
        if (string.IsNullOrWhiteSpace(sqref)) return [];

        List<SheetRange> ranges = [];
        foreach (string part in sqref.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Xlsx.TryParseRange(part, out int firstColumn, out int firstRow,
                                    out int lastColumn, out int lastRow))
                continue;
            if (lastColumn < firstColumn || lastRow < firstRow) continue;
            ranges.Add(new SheetRange(firstColumn, firstRow, lastColumn, lastRow));
        }
        return ranges;
    }

    private enum ThresholdKind { Number, Percent, Percentile, Minimum, Maximum, Unusable }

    private readonly record struct Threshold(ThresholdKind Kind, double Value, bool OrEqual);

    private sealed record Rule(
        IReadOnlyList<SheetRange> Ranges,
        IReadOnlyList<Threshold> Thresholds,
        IReadOnlyList<int>? CustomIcons,
        bool Reverse,
        bool IconSet)
    {
        /// <summary>Adds every cell this rule hides to <paramref name="hidden"/>.</summary>
        public void Collect(
            Dictionary<(int Row, int Column), double> numbers, HashSet<(int Row, int Column)> hidden)
        {
            List<(int Row, int Column)> covered = [];
            List<double> values = [];
            foreach (((int Row, int Column) address, double value) in numbers)
            {
                if (!Covers(address.Row, address.Column)) continue;
                covered.Add(address);
                values.Add(value);
            }
            if (covered.Count == 0) return;

            values.Sort();
            double minimum = values[0];
            double maximum = values[^1];

            // A leading or trailing threshold stated as a plain number replaces the range's own
            // extreme, which is what the percent thresholds are then a percentage of
            // (ScIconSetFormat::GetMinValue/GetMaxValue, colorscale.cxx:1312-1334).
            if (Thresholds.Count > 0 && Thresholds[0].Kind == ThresholdKind.Number)
                minimum = Thresholds[0].Value;
            if (Thresholds.Count > 0 && Thresholds[^1].Kind == ThresholdKind.Number)
                maximum = Thresholds[^1].Value;

            double[] resolved = new double[Thresholds.Count];
            for (int i = 0; i < Thresholds.Count; i++)
                resolved[i] = Resolve(Thresholds[i], minimum, maximum, values);

            foreach ((int Row, int Column) address in covered)
            {
                if (HidesValue(numbers[address], resolved)) hidden.Add(address);
            }
        }

        private bool Covers(int row, int column)
        {
            foreach (SheetRange range in Ranges)
            {
                if (row >= range.FirstRow && row <= range.LastRow
                    && column >= range.FirstColumn && column <= range.LastColumn)
                    return true;
            }
            return false;
        }

        private bool HidesValue(double value, double[] resolved)
        {
            // A data bar with the value hidden hides every numeric cell it covers; the bar is
            // drawn for all of them (GetDataBarInfo, colorscale.cxx:968-983).
            if (!IconSet) return true;

            int band = -1;
            for (int i = 0; i < resolved.Length; i++)
            {
                bool matched = Thresholds[i].OrEqual ? value >= resolved[i] : value > resolved[i];
                if (matched) band = i;
            }
            if (band < 0) return false;

            if (Reverse) band = resolved.Length - 1 - band;

            if (CustomIcons is null) return true;
            if (band >= CustomIcons.Count) return true;

            // NoIcons at this band means no icon information at all, and a cell with none keeps
            // its text however the rule's showValue reads.
            return CustomIcons[band] != -1;
        }

        private static double Resolve(
            Threshold threshold, double minimum, double maximum, List<double> sorted)
            => threshold.Kind switch
            {
                ThresholdKind.Percent => minimum + ((maximum - minimum) * (threshold.Value / 100)),
                ThresholdKind.Minimum => minimum,
                ThresholdKind.Maximum => maximum,
                ThresholdKind.Percentile => Percentile(sorted, threshold.Value / 100),
                _ => threshold.Value,
            };

        /// <summary>
        /// The same percentile Calc's own interpreter computes
        /// (<c>ScInterpreter::GetPercentile</c>, <c>interpr3.cxx:3402-3429</c>).
        /// </summary>
        private static double Percentile(List<double> sorted, double fraction)
        {
            if (sorted.Count == 1) return sorted[0];

            double position = fraction * (sorted.Count - 1);
            int index = (int)Math.Floor(position);
            if (index < 0) return sorted[0];
            if (index >= sorted.Count - 1) return sorted[^1];

            double offset = position - index;
            return offset <= 0
                ? sorted[index]
                : sorted[index] + (offset * (sorted[index + 1] - sorted[index]));
        }
    }
}
