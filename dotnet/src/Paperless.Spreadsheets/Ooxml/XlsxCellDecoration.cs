using System.Globalization;
using System.Xml.Linq;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.Ooxml;
using Paperless.Spreadsheets.Layout;

namespace Paperless.Spreadsheets.Ooxml;

/// <summary>
/// Reads the fills and borders SpreadsheetML keeps in <c>styles.xml</c>, and which cells use them.
/// </summary>
/// <remarks>
/// <para>
/// Beside <see cref="XlsxStyles"/> rather than inside it, because the two answer different
/// questions for different callers: extraction needs the number format a cell's <c>xf</c> names
/// and nothing else, and giving it the fills and borders as well would make every extraction
/// pay for a rendering. This reads the same part for the other half.
/// </para>
/// <para>
/// The indirection is three deep and every step matters. A cell's <c>s</c> attribute indexes
/// <c>cellXfs</c>; the <c>xf</c> there names a <c>fillId</c> and a <c>borderId</c>, and may
/// defer to a <c>cellStyleXfs</c> entry through <c>xfId</c> when its <c>applyFill</c> or
/// <c>applyBorder</c> flag is off. Then the fill itself is a <em>pattern</em>: a solid fill
/// writes its colour in <c>fgColor</c> and not in <c>bgColor</c>, which is the trap — a reader
/// taking <c>bgColor</c> paints every solid-filled cell the wrong colour, and LibreOffice's own
/// export writes a different colour in each of the two.
/// </para>
/// </remarks>
internal static class XlsxCellDecoration
{
    /// <summary>How far one <c>&lt;col&gt;</c> run is honoured.</summary>
    /// <remarks>
    /// A <c>&lt;col min="1" max="16384" style="3"/&gt;</c> is ordinary and is stored as a run,
    /// so this bounds nothing that matters — it only stops a <c>max</c> outside the format's own
    /// limit from being taken at its word.
    /// </remarks>
    private const int MaxColumn = 16383;

    /// <summary>Reads one sheet's decoration.</summary>
    /// <param name="styles">The <c>styleSheet</c> root, or null when the workbook has none.</param>
    /// <param name="theme">The <c>theme</c> root, for colours named by index.</param>
    /// <param name="worksheet">The sheet's own root.</param>
    public static SheetFormatting Read(XElement? styles, XElement? theme, XElement? worksheet)
    {
        if (worksheet is null) return SheetFormatting.Empty;

        XlsxPalette palette = XlsxPalette.Read(styles, theme);
        List<SheetCellDecoration> formats = ReadCellFormats(styles, palette);

        SheetFormatting formatting = new();

        // The conditional formats go on last and are asked for first, because a colour scale
        // beats the fill a cell states. They are applied even when the workbook names no cell
        // formats at all, which is why this no longer returns early on an empty `cellXfs`: a
        // sheet whose only decoration is a scale still has decoration.
        if (formats.Count == 0)
        {
            XlsxConditionalFormats.Apply(formatting, styles, theme, worksheet);
            return formatting.IsEmpty ? SheetFormatting.Empty : formatting;
        }

        int[] handles = new int[formats.Count];
        for (int i = 0; i < formats.Count; i++)
        {
            handles[i] = formats[i].IsNone ? 0 : formatting.Intern(formats[i]);
        }

        int Handle(int? index)
            => index is { } at && at >= 0 && at < handles.Length ? handles[at] : 0;

        foreach (XElement column in Xlsx.Children(Xlsx.Child(worksheet, "cols"), "col"))
        {
            int handle = Handle(Xlsx.Integer(column, "style"));
            if (handle == 0) continue;

            int first = (Xlsx.Integer(column, "min") ?? 1) - 1;
            int last = (Xlsx.Integer(column, "max") ?? 1) - 1;
            formatting.SetColumns(Math.Max(0, first), Math.Min(MaxColumn, last), handle);
        }

        int expectedRow = 0;
        foreach (XElement row in Xlsx.Children(Xlsx.Child(worksheet, "sheetData"), "row"))
        {
            int index = (Xlsx.Integer(row, "r") - 1) ?? expectedRow;
            if (index < 0) index = expectedRow;
            expectedRow = index + 1;

            // customFormat is what makes a row's own s attribute mean anything: without it the
            // attribute is there but inert, and honouring it anyway paints rows Excel does not.
            if (Xlsx.Flag(row, "customFormat")) formatting.SetRow(index, Handle(Xlsx.Integer(row, "s")));

            foreach (XElement cell in Xlsx.Children(row, "c"))
            {
                int handle = Handle(Xlsx.Integer(cell, "s"));
                if (handle == 0) continue;

                if (Xlsx.TryParseCellReference(Xlsx.Attribute(cell, "r"), out int column, out int at))
                    formatting.SetCell(at, column, handle);
            }
        }

        XlsxConditionalFormats.Apply(formatting, styles, theme, worksheet);

        return formatting.IsEmpty ? SheetFormatting.Empty : formatting;
    }

    /// <summary>One entry per <c>cellXfs</c> index, in that order.</summary>
    private static List<SheetCellDecoration> ReadCellFormats(XElement? styles, XlsxPalette palette)
    {
        List<SheetCellDecoration> formats = [];
        if (styles is null) return formats;

        List<Colour?> fills = [.. Xlsx.Children(Xlsx.Child(styles, "fills"), "fill")
            .Select(fill => ReadFill(fill, palette))];
        List<SheetCellBorders> borders = [.. Xlsx.Children(Xlsx.Child(styles, "borders"), "border")
            .Select(border => ReadBorders(border, palette))];

        List<XElement> styleXfs = [.. Xlsx.Children(Xlsx.Child(styles, "cellStyleXfs"), "xf")];

        foreach (XElement xf in Xlsx.Children(Xlsx.Child(styles, "cellXfs"), "xf"))
        {
            // applyFill/applyBorder off means "take the named style's", which is what xfId
            // points at. Absent, the attribute defaults to on for a cell xf.
            XElement? parent = Xlsx.Integer(xf, "xfId") is { } id && id >= 0 && id < styleXfs.Count
                ? styleXfs[id]
                : null;

            XElement fillFrom = Xlsx.Flag(xf, "applyFill", true) || parent is null ? xf : parent;
            XElement borderFrom = Xlsx.Flag(xf, "applyBorder", true) || parent is null ? xf : parent;

            Colour? fill = At(fills, Xlsx.Integer(fillFrom, "fillId"));
            SheetCellBorders border = At(borders, Xlsx.Integer(borderFrom, "borderId"));

            formats.Add(fill is null && border.IsNone
                ? SheetCellDecoration.None
                : new SheetCellDecoration(fill, border));
        }

        return formats;

        static T? At<T>(List<T> list, int? index)
            => index is { } at && at >= 0 && at < list.Count ? list[at] : default;
    }

    /// <summary>
    /// The colour a <c>fill</c> paints, or null when it paints nothing.
    /// </summary>
    /// <remarks>
    /// Only <c>solid</c> is a colour. <c>none</c> is transparent, and the eighteen hatch
    /// patterns are a foreground drawn over a background, which is not something a single
    /// colour can stand for — they are reported as their <em>background</em> colour, which is
    /// what Calc falls back to when it cannot hatch (<c>XclImpCellArea</c>,
    /// <c>sc/source/filter/excel/xistyle.cxx:1075</c>), and recorded in the module's TODO.
    /// </remarks>
    private static Colour? ReadFill(XElement fill, XlsxPalette palette)
    {
        XElement? pattern = Xlsx.Child(fill, "patternFill");
        if (pattern is null) return null;

        string type = Xlsx.Attribute(pattern, "patternType") ?? "none";
        if (string.Equals(type, "none", StringComparison.Ordinal)) return null;

        // A solid fill's colour is its foreground; a hatch's visible mass is its background.
        return string.Equals(type, "solid", StringComparison.Ordinal)
            ? palette.Read(Xlsx.Child(pattern, "fgColor"))
            : palette.Read(Xlsx.Child(pattern, "bgColor"))
              ?? palette.Read(Xlsx.Child(pattern, "fgColor"));
    }

    private static SheetCellBorders ReadBorders(XElement border, XlsxPalette palette)
        => new(
            Edge(Xlsx.Child(border, "left"), palette),
            Edge(Xlsx.Child(border, "right"), palette),
            Edge(Xlsx.Child(border, "top"), palette),
            Edge(Xlsx.Child(border, "bottom"), palette));

    /// <summary>
    /// One edge, from the fourteen style names SpreadsheetML allows.
    /// </summary>
    /// <remarks>
    /// The widths are LibreOffice's, in twips: hair 1, thin 15, medium 35 and thick 50
    /// (<c>API_LINE_*</c>, <c>sc/source/filter/inc/stylesbuffer.hxx:63-67</c>), assigned by the
    /// same switch this mirrors (<c>stylesbuffer.cxx:1700-1748</c>). They are not what the names
    /// suggest: <c>thin</c> draws at 0.75 pt and <c>hair</c> at a twentieth of a point.
    /// </remarks>
    private static SheetBorder Edge(XElement? edge, XlsxPalette palette)
    {
        if (edge is null) return SheetBorder.None;

        string style = Xlsx.Attribute(edge, "style") ?? "none";
        Colour colour = palette.Read(Xlsx.Child(edge, "color")) ?? Colour.Black;

        (int twips, SheetBorderPattern pattern, bool doubled) = style switch
        {
            "hair" => (1, SheetBorderPattern.Solid, false),
            "thin" => (15, SheetBorderPattern.Solid, false),
            "medium" => (35, SheetBorderPattern.Solid, false),
            "thick" => (50, SheetBorderPattern.Solid, false),
            "double" => (50, SheetBorderPattern.Solid, true),
            "dotted" => (15, SheetBorderPattern.Dotted, false),
            "dashed" => (15, SheetBorderPattern.FineDashed, false),
            "dashDot" => (15, SheetBorderPattern.DashDot, false),
            "dashDotDot" => (15, SheetBorderPattern.DashDotDot, false),
            "mediumDashed" => (35, SheetBorderPattern.FineDashed, false),
            "mediumDashDot" => (35, SheetBorderPattern.DashDot, false),
            "mediumDashDotDot" => (35, SheetBorderPattern.DashDotDot, false),
            "slantDashDot" => (35, SheetBorderPattern.DashDot, false),
            _ => (0, SheetBorderPattern.Solid, false),
        };

        if (twips == 0) return SheetBorder.None;

        Length width = Length.FromTwips(twips);
        if (!doubled) return SheetBorder.Line(width, colour, pattern);

        Length line = width / 3;
        return new SheetBorder(line, width - line - line, line, colour, pattern);
    }
}
