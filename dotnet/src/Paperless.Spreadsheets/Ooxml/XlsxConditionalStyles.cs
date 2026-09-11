using System.Globalization;
using System.Xml.Linq;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.Spreadsheets.Layout;

namespace Paperless.Spreadsheets.Ooxml;

/// <summary>
/// Reads the conditional formatting rules that name a <c>dxf</c>, and works out which cells each
/// one paints.
/// </summary>
/// <remarks>
/// <para>
/// Beside <see cref="XlsxConditionalFormats"/> rather than inside it because the two answer
/// different questions from opposite ends. A <c>colorScale</c> states no format at all — its
/// colour is computed from the numbers inside its own range — while every rule here names a
/// <c>&lt;dxf&gt;</c> in <c>styles.xml</c> and the whole of the work is deciding, per cell,
/// whether the rule is true. Calc keeps the same separation: a scale is an
/// <c>ScColorScaleFormat</c> that <c>fillinfo.cxx</c> applies last and on its own, and a
/// condition is an <c>ScCondFormatEntry</c> resolved to a cell <em>style</em> through
/// <c>ScDocument::GetCondResult</c>.
/// </para>
/// <para>
/// <strong>Exactly one rule wins a cell.</strong> <c>GetCondResult</c>
/// (<c>sc/source/core/data/documen4.cxx</c>) walks the formats covering the cell and returns the
/// first non-empty style name's item set; <c>ScConditionalFormat::GetCellStyle</c>
/// (<c>conditio.cxx</c>) returns the first matching entry within a format. So the properties of
/// two matching rules are never merged, and a cell takes one <c>dxf</c> or none.
/// </para>
/// <para>
/// <strong>What a <c>dxf</c> is silent about is not overridden.</strong> A conditional style's
/// item set holds only the items the rule states, and everything else falls through to the cell's
/// own pattern — which is why the result is a differential
/// <see cref="SheetConditionalText"/> rather than a whole format.
/// </para>
/// <para>
/// <strong>A <c>dxf</c>'s fill states its colour in <c>bgColor</c>, which is the opposite of a
/// cell's.</strong> <c>Fill::finalizeImport</c> (<c>sc/source/filter/oox/stylesbuffer.cxx</c>)
/// begins <c>if (mbDxf)</c> and, when the fill colour is used and the pattern is absent or solid,
/// moves <c>maFillColor</c> — the <c>bgColor</c> — into <c>maPatternColor</c> and forces the
/// pattern to solid. Reading <c>fgColor</c> here, as a cell's <c>patternFill</c> needs, finds
/// nothing at all on the 2734 corpus <c>dxf</c> fills that state only a background.
/// </para>
/// <para>
/// <strong>What is evaluated is deliberately narrow, and it is where the corpus is.</strong>
/// Censused over the 307 sheets documents, the 601 <c>expression</c> rules reduce to 461 of the
/// single shape <c>&lt;reference&gt; = "&lt;text&gt;"</c> and a further two dozen of the same
/// shape against a number or a second reference; the rest are <c>AND</c>, <c>MOD(ROW())</c>,
/// <c>ISERROR</c>, <c>TODAY</c>, defined names and <c>#REF!</c>, which need a formula
/// interpreter. A rule this cannot evaluate paints nothing, which is the same answer the reader
/// gave before it existed.
/// </para>
/// </remarks>
internal static class XlsxConditionalStyles
{
    /// <summary>
    /// How many cell positions all of a sheet's rules together may be evaluated over.
    /// </summary>
    /// <remarks>
    /// A rule's <c>sqref</c> is routinely a whole column — <c>TK-Syllabus-Comparison-Document-v2</c>
    /// states 413 of them over <c>S1:S1048576</c> — so the ranges are clamped to the rows and
    /// columns the sheet actually states before anything is walked. This is the second guard, for
    /// the sheet that genuinely holds a hundred thousand rows under a hundred rules: past it the
    /// remaining rules are skipped rather than the reader stalling.
    /// </remarks>
    private const long PositionBudget = 8_000_000;

    /// <summary>
    /// Resolves every <c>dxf</c>-naming rule a worksheet states.
    /// </summary>
    /// <param name="formatting">The sheet's decoration, which takes the conditional fills.</param>
    /// <param name="styles">The <c>styleSheet</c> root, or null when the workbook has none.</param>
    /// <param name="theme">The <c>theme</c> root, for colours named by slot.</param>
    /// <param name="worksheet">The sheet's own root.</param>
    /// <param name="shared">The workbook's shared strings, for the cells a rule compares.</param>
    /// <returns>What each matching cell's winning rule changes about its text.</returns>
    public static Dictionary<(int Row, int Column), SheetConditionalText> Apply(
        SheetFormatting formatting,
        XElement? styles,
        XElement? theme,
        XElement? worksheet,
        XlsxSharedStrings shared)
    {
        Dictionary<(int Row, int Column), SheetConditionalText> text = [];
        if (worksheet is null || styles is null) return text;

        XlsxPalette palette = XlsxPalette.Read(styles, theme);
        List<Difference> differences = ReadDifferences(styles, palette);
        if (differences.Count == 0) return text;

        List<Rule> rules = ReadRules(worksheet, differences);
        if (rules.Count == 0) return text;

        Sheet sheet = Sheet.Read(worksheet, shared);
        if (sheet.LastRow < 0) return text;

        // A lower `priority` is the higher priority, and an unnumbered rule sorts last.
        rules.Sort(static (a, b) => a.Priority.CompareTo(b.Priority));

        HashSet<(int Row, int Column)> decided = [];
        long budget = PositionBudget;

        foreach (Rule rule in rules)
        {
            foreach (SheetRange range in rule.Ranges)
            {
                int firstRow = Math.Max(0, range.FirstRow);
                int lastRow = Math.Min(sheet.LastRow, range.LastRow);
                int firstColumn = Math.Max(0, range.FirstColumn);
                int lastColumn = Math.Min(sheet.LastColumn, range.LastColumn);
                if (lastRow < firstRow || lastColumn < firstColumn) continue;

                long span = (long)(lastRow - firstRow + 1) * (lastColumn - firstColumn + 1);
                if (span > budget) return text;
                budget -= span;

                for (int row = firstRow; row <= lastRow; row++)
                {
                    for (int column = firstColumn; column <= lastColumn; column++)
                    {
                        if (!decided.Add((row, column))) continue;
                        if (!rule.Test.Holds(sheet, row, column, rule.AnchorRow, rule.AnchorColumn))
                        {
                            decided.Remove((row, column));
                            continue;
                        }

                        Difference difference = rule.Difference;
                        if (difference.Background is { } fill)
                            formatting.SetConditionalBackground(row, column, fill);
                        if (!difference.Text.IsNone) text[(row, column)] = difference.Text;
                    }
                }
            }
        }

        return text;
    }

    /// <summary>One <c>&lt;dxf&gt;</c>: what it changes about the text, and its fill if it has one.</summary>
    private readonly record struct Difference(SheetConditionalText Text, Colour? Background);

    /// <summary>One <c>cfRule</c> that names a <c>dxf</c>.</summary>
    private sealed record Rule(
        int Priority,
        List<SheetRange> Ranges,
        int AnchorRow,
        int AnchorColumn,
        Difference Difference,
        ICondition Test);

    /// <summary>The <c>dxfs</c> table, in the order <c>dxfId</c> indexes it.</summary>
    private static List<Difference> ReadDifferences(XElement styles, XlsxPalette palette)
    {
        List<Difference> differences = [];

        foreach (XElement dxf in Xlsx.Children(Xlsx.Child(styles, "dxfs"), "dxf"))
        {
            XElement? font = Xlsx.Child(dxf, "font");
            SheetConditionalText text = font is null ? default : new SheetConditionalText
            {
                Colour = palette.Read(Xlsx.Child(font, "color")),
                IsStruckThrough = Toggle(Xlsx.Child(font, "strike")),
                FontWeight = Toggle(Xlsx.Child(font, "b")) switch
                {
                    true => 700,
                    false => 400,
                    null => null,
                },
                IsItalic = Toggle(Xlsx.Child(font, "i")),
                Underline = Xlsx.Child(font, "u") is { } underline
                    ? XlsxCellFormats.UnderlineOf(underline)
                    : null,
                FontSize = Number(Xlsx.Child(font, "sz")) is > 0 and { } points
                    ? Length.FromPoints(points)
                    : null,
                FontFamily = Xlsx.Attribute(Xlsx.Child(font, "name"), "val"),
            };

            differences.Add(new Difference(text, ReadFill(Xlsx.Child(dxf, "fill"), palette)));
        }

        return differences;
    }

    /// <summary>
    /// The colour a <c>dxf</c>'s fill paints, or null when it paints nothing.
    /// </summary>
    /// <remarks>
    /// The <c>mbDxf</c> branch of <c>Fill::finalizeImport</c>, in the order it tests: a stated
    /// background with no pattern or a solid one <em>is</em> the solid colour; a solid pattern
    /// with neither colour stated paints nothing; anything else keeps the foreground as the
    /// pattern colour, which for the corpus's hatches is what shows.
    /// </remarks>
    private static Colour? ReadFill(XElement? fill, XlsxPalette palette)
    {
        XElement? pattern = Xlsx.Child(fill, "patternFill");
        if (pattern is null) return null;

        string? type = Xlsx.Attribute(pattern, "patternType");
        if (string.Equals(type, "none", StringComparison.Ordinal)) return null;

        Colour? background = palette.Read(Xlsx.Child(pattern, "bgColor"));
        Colour? foreground = palette.Read(Xlsx.Child(pattern, "fgColor"));

        if (background is { } stated && type is null or "solid") return stated;
        return type is "solid" && foreground is null ? null : foreground ?? background;
    }

    /// <summary>Every rule that names a <c>dxf</c> and whose condition can be evaluated.</summary>
    private static List<Rule> ReadRules(XElement worksheet, List<Difference> differences)
    {
        List<Rule> rules = [];

        foreach (XElement block in Xlsx.Children(worksheet, "conditionalFormatting"))
        {
            List<SheetRange> ranges = ParseSqref(Xlsx.Attribute(block, "sqref"));
            if (ranges.Count == 0) continue;

            // The formula is written for the top-left cell of the whole `sqref` and is shifted
            // for every other cell in it, which is what `calcext:base-cell-address` states in
            // 26.2.4.2's own view of the file.
            int anchorRow = int.MaxValue;
            int anchorColumn = int.MaxValue;
            foreach (SheetRange range in ranges)
            {
                anchorRow = Math.Min(anchorRow, range.FirstRow);
                anchorColumn = Math.Min(anchorColumn, range.FirstColumn);
            }

            foreach (XElement rule in Xlsx.Children(block, "cfRule"))
            {
                if (Xlsx.Integer(rule, "dxfId") is not { } id) continue;
                if (id < 0 || id >= differences.Count) continue;

                Difference difference = differences[id];
                if (difference.Text.IsNone && difference.Background is null) continue;

                if (ConditionOf(rule) is not { } test) continue;

                rules.Add(new Rule(
                    Xlsx.Integer(rule, "priority") ?? int.MaxValue,
                    ranges, anchorRow, anchorColumn, difference, test));
            }
        }

        return rules;
    }

    /// <summary>The condition one <c>cfRule</c> states, or null when it cannot be evaluated.</summary>
    private static ICondition? ConditionOf(XElement rule)
    {
        List<string> formulas = [.. Xlsx.Children(rule, "formula").Select(static f => f.Value)];

        switch (Xlsx.Attribute(rule, "type"))
        {
            case "expression":
                return formulas.Count == 1 ? Comparison.Parse(formulas[0]) : null;

            case "cellIs":
                return CellIs.Parse(Xlsx.Attribute(rule, "operator"), formulas);

            default:
                return null;
        }
    }

    /// <summary>A condition that can be asked about one cell.</summary>
    private interface ICondition
    {
        /// <summary>Whether the rule holds at one position.</summary>
        /// <param name="sheet">The sheet's values.</param>
        /// <param name="row">The zero-based row being tested.</param>
        /// <param name="column">The zero-based column being tested.</param>
        /// <param name="anchorRow">The row the formula was written for.</param>
        /// <param name="anchorColumn">The column the formula was written for.</param>
        bool Holds(Sheet sheet, int row, int column, int anchorRow, int anchorColumn);
    }

    /// <summary>An <c>expression</c> rule of the shape <c>&lt;operand&gt; &lt;op&gt; &lt;operand&gt;</c>.</summary>
    private sealed record Comparison(Operand Left, string Operator, Operand Right) : ICondition
    {
        /// <summary>Reads one, or null when the formula is anything else.</summary>
        /// <param name="formula">The rule's <c>&lt;formula&gt;</c> text.</param>
        public static Comparison? Parse(string? formula)
        {
            if (string.IsNullOrWhiteSpace(formula)) return null;

            string text = formula.Trim();
            if (text.StartsWith('=')) text = text[1..];

            foreach (string op in Operators)
            {
                int at = IndexOfOperator(text, op);
                if (at < 0) continue;

                if (Operand.Parse(text[..at]) is not { } left) return null;
                if (Operand.Parse(text[(at + op.Length)..]) is not { } right) return null;
                return new Comparison(left, op, right);
            }

            return null;
        }

        /// <inheritdoc/>
        public bool Holds(Sheet sheet, int row, int column, int anchorRow, int anchorColumn)
        {
            int rowShift = row - anchorRow;
            int columnShift = column - anchorColumn;

            Value left = Left.Resolve(sheet, rowShift, columnShift);
            Value right = Right.Resolve(sheet, rowShift, columnShift);
            return Value.Compare(left, right, Operator);
        }

        /// <summary>Longest first, so <c>&lt;=</c> is not read as <c>&lt;</c>.</summary>
        private static readonly string[] Operators = ["<>", "<=", ">=", "=", "<", ">"];

        /// <summary>
        /// Where an operator sits, ignoring the ones inside a quoted string.
        /// </summary>
        /// <remarks>
        /// A single pass rather than a split, because <c>A1="&lt;"</c> is legal and a naive
        /// <c>IndexOf</c> finds the wrong character in it. Parentheses are counted as well: a
        /// formula whose top level is inside a call is not a comparison this understands, and
        /// finding an operator inside one would evaluate half a function.
        /// </remarks>
        private static int IndexOfOperator(string text, string op)
        {
            int depth = 0;
            bool quoted = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"') { quoted = !quoted; continue; }
                if (quoted) continue;
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth--; continue; }
                if (depth != 0) continue;

                if (i + op.Length <= text.Length
                    && string.CompareOrdinal(text, i, op, 0, op.Length) == 0)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// A <c>cellIs</c> rule, which compares the cell being formatted against one or two operands.
    /// </summary>
    private sealed record CellIs(string Operator, Operand First, Operand? Second) : ICondition
    {
        /// <summary>Reads one, or null when the operator or the operands are not understood.</summary>
        /// <param name="op">The rule's <c>operator</c> attribute.</param>
        /// <param name="formulas">Its <c>&lt;formula&gt;</c> children, one or two of them.</param>
        public static CellIs? Parse(string? op, List<string> formulas)
        {
            if (op is null || formulas.Count == 0) return null;

            bool two = op is "between" or "notBetween";
            if (formulas.Count < (two ? 2 : 1)) return null;
            if (Operand.Parse(formulas[0]) is not { } first) return null;

            Operand? second = null;
            if (two)
            {
                if (Operand.Parse(formulas[1]) is not { } parsed) return null;
                second = parsed;
            }

            return op is "equal" or "notEqual" or "greaterThan" or "lessThan"
                       or "greaterThanOrEqual" or "lessThanOrEqual" or "between" or "notBetween"
                ? new CellIs(op, first, second)
                : null;
        }

        /// <inheritdoc/>
        public bool Holds(Sheet sheet, int row, int column, int anchorRow, int anchorColumn)
        {
            int rowShift = row - anchorRow;
            int columnShift = column - anchorColumn;

            Value cell = sheet.At(row, column);
            Value first = First.Resolve(sheet, rowShift, columnShift);

            switch (Operator)
            {
                case "equal": return Value.Compare(cell, first, "=");
                case "notEqual": return Value.Compare(cell, first, "<>");
                case "greaterThan": return Value.Compare(cell, first, ">");
                case "lessThan": return Value.Compare(cell, first, "<");
                case "greaterThanOrEqual": return Value.Compare(cell, first, ">=");
                case "lessThanOrEqual": return Value.Compare(cell, first, "<=");
                default:
                    if (Second is not { } upper) return false;
                    Value second = upper.Resolve(sheet, rowShift, columnShift);
                    bool inside = Value.Compare(cell, first, ">=") && Value.Compare(cell, second, "<=");
                    return Operator is "between" ? inside : !inside;
            }
        }
    }

    /// <summary>One side of a comparison: a cell reference, a number, a string or a boolean.</summary>
    private sealed record Operand(
        bool IsReference,
        int Row,
        int Column,
        bool AbsoluteRow,
        bool AbsoluteColumn,
        Value Literal)
    {
        /// <summary>Reads one, or null when it is anything more than a reference or a literal.</summary>
        /// <param name="text">The operand's text, with any surrounding space.</param>
        public static Operand? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string one = text.Trim();
            if (one.Length == 0) return null;

            if (one[0] == '"')
            {
                if (one.Length < 2 || one[^1] != '"') return null;
                string body = one[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
                if (body.Contains('"', StringComparison.Ordinal)) return null;
                return OfLiteral(Value.OfText(body));
            }

            if (double.TryParse(one, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                return OfLiteral(Value.OfNumber(number));

            if (string.Equals(one, "TRUE", StringComparison.OrdinalIgnoreCase))
                return OfLiteral(Value.OfNumber(1));
            if (string.Equals(one, "FALSE", StringComparison.OrdinalIgnoreCase))
                return OfLiteral(Value.OfNumber(0));

            return Reference(one);
        }

        /// <summary>What this side is worth at a position, given the shift from the anchor.</summary>
        /// <param name="sheet">The sheet's values.</param>
        /// <param name="rowShift">Rows from the cell the formula was written for.</param>
        /// <param name="columnShift">Columns from it.</param>
        public Value Resolve(Sheet sheet, int rowShift, int columnShift)
        {
            if (!IsReference) return Literal;

            int row = AbsoluteRow ? Row : Row + rowShift;
            int column = AbsoluteColumn ? Column : Column + columnShift;
            return row < 0 || column < 0 ? Value.Blank : sheet.At(row, column);
        }

        private static Operand OfLiteral(Value value) => new(false, 0, 0, false, false, value);

        /// <summary>
        /// An A1 reference, with the two <c>$</c> flags kept.
        /// </summary>
        /// <remarks>
        /// A rule's formula is stated for the top-left cell of its range and shifted for the
        /// rest, so which halves are absolute is the whole of what a rule over a column means:
        /// <c>H2="x"</c> on <c>S2:S900</c> tests a different <c>H</c> on every row, and
        /// <c>$H$2="x"</c> would test one cell for all of them. A reference naming a sheet is
        /// refused — nothing here can reach another sheet's values.
        /// </remarks>
        private static Operand? Reference(string text)
        {
            if (text.Contains('!', StringComparison.Ordinal)) return null;

            int at = 0;
            bool absoluteColumn = false;
            if (text[at] == '$') { absoluteColumn = true; at++; }

            int letters = at;
            while (letters < text.Length && char.IsAsciiLetter(text[letters])) letters++;
            if (letters == at || letters - at > 3) return null;

            int digitsStart = letters;
            bool absoluteRow = false;
            if (digitsStart < text.Length && text[digitsStart] == '$') { absoluteRow = true; digitsStart++; }

            if (digitsStart >= text.Length) return null;
            for (int i = digitsStart; i < text.Length; i++)
            {
                if (!char.IsAsciiDigit(text[i])) return null;
            }

            int column = 0;
            for (int i = at; i < letters; i++)
                column = (column * 26) + (char.ToUpperInvariant(text[i]) - 'A' + 1);

            if (!int.TryParse(text[digitsStart..], NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out int row) || row < 1)
            {
                return null;
            }

            return new Operand(true, row - 1, column - 1, absoluteRow, absoluteColumn, Value.Blank);
        }
    }

    /// <summary>What a cell or a literal is worth: a number, a string, or nothing at all.</summary>
    private readonly record struct Value(double? Number, string? Text)
    {
        /// <summary>An empty cell, which equals neither a number nor a non-empty string.</summary>
        public static Value Blank { get; }

        /// <summary>A numeric value.</summary>
        /// <param name="number">The number.</param>
        public static Value OfNumber(double number) => new(number, null);

        /// <summary>A text value.</summary>
        /// <param name="text">The string.</param>
        public static Value OfText(string text) => new(null, text);

        /// <summary>True when the cell holds nothing.</summary>
        public bool IsBlank => Number is null && Text is null;

        /// <summary>
        /// Compares two values the way a condition does.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Text against text is compared without regard to case, which is Calc's default: the
        /// document option <c>IsIgnoreCase</c> is on unless a workbook turns it off
        /// (<c>ScDocOptions</c>), and every corpus rule of this shape tests a one-letter marker.
        /// </para>
        /// <para>
        /// A number and a string are never equal and a number always sorts first, which is the
        /// ordering <c>ScInterpreter::CompareFunc</c> uses. A blank equals a blank, equals the
        /// number zero and equals the empty string, and is otherwise less than anything.
        /// </para>
        /// </remarks>
        /// <param name="left">The left side.</param>
        /// <param name="right">The right side.</param>
        /// <param name="op">One of <c>=</c>, <c>&lt;&gt;</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>.</param>
        public static bool Compare(Value left, Value right, string op)
        {
            int order = Order(left, right);

            return op switch
            {
                "=" => order == 0,
                "<>" => order != 0,
                "<" => order < 0,
                "<=" => order <= 0,
                ">" => order > 0,
                ">=" => order >= 0,
                _ => false,
            };
        }

        private static int Order(Value left, Value right)
        {
            if (left.IsBlank && right.IsBlank) return 0;
            if (left.IsBlank) return right.Number is { } n ? 0.0.CompareTo(n) : right.Text!.Length == 0 ? 0 : -1;
            if (right.IsBlank) return left.Number is { } m ? m.CompareTo(0.0) : left.Text!.Length == 0 ? 0 : 1;

            if (left.Number is { } a && right.Number is { } b) return a.CompareTo(b);
            if (left.Text is { } s && right.Text is { } t)
                return string.Compare(s, t, StringComparison.OrdinalIgnoreCase);

            return left.Number is not null ? -1 : 1;
        }
    }

    /// <summary>A sheet's values, by position, for the rules to be asked against.</summary>
    /// <remarks>
    /// A formula cell's cached <c>v</c> is what is read, on the same terms as every other reader
    /// here: the reference recalculates, so a rule testing a volatile formula is the divergence
    /// this project already records rather than one this introduces.
    /// </remarks>
    private sealed class Sheet
    {
        private readonly Dictionary<(int Row, int Column), Value> _values = [];

        /// <summary>The last row the sheet states anything on, or -1 for an empty sheet.</summary>
        public int LastRow { get; private set; } = -1;

        /// <summary>The last column it states anything on, or -1.</summary>
        public int LastColumn { get; private set; } = -1;

        /// <summary>Widens the walked extent to a rectangle the sheet declares.</summary>
        /// <param name="row">A row the sheet reaches.</param>
        /// <param name="column">A column it reaches.</param>
        private void Reaches(int row, int column)
        {
            if (row > LastRow) LastRow = row;
            if (column > LastColumn) LastColumn = column;
        }

        /// <summary>What one cell holds.</summary>
        /// <param name="row">The zero-based row.</param>
        /// <param name="column">The zero-based column.</param>
        public Value At(int row, int column)
            => _values.TryGetValue((row, column), out Value value) ? value : Value.Blank;

        /// <summary>Reads every stated cell of one worksheet.</summary>
        /// <param name="worksheet">The sheet's own root.</param>
        /// <param name="shared">The workbook's shared strings.</param>
        public static Sheet Read(XElement worksheet, XlsxSharedStrings shared)
        {
            Sheet sheet = new();

            // A bounded `<col>` run as well as the cells the sheet writes. A column that holds no
            // cell at all is still printed when a run formats it, and a rule covering it would
            // otherwise be clamped away — while the sheet's own `dimension` is not used, because a
            // file stating `A1:XFD1048576` would put every rule past the budget below and turn the
            // whole feature off on it.
            foreach (XElement run in Xlsx.Children(Xlsx.Child(worksheet, "cols"), "col"))
            {
                int last = (Xlsx.Integer(run, "max") ?? 0) - 1;
                if (last >= 0 && last < SheetAddress.MaxColumn) sheet.Reaches(-1, last);
            }

            int expectedRow = 0;
            foreach (XElement row in Xlsx.Children(Xlsx.Child(worksheet, "sheetData"), "row"))
            {
                int index = (Xlsx.Integer(row, "r") - 1) ?? expectedRow;
                if (index < 0) index = expectedRow;
                expectedRow = index + 1;

                sheet.Reaches(index, -1);

                int expectedColumn = 0;
                foreach (XElement cell in Xlsx.Children(row, "c"))
                {
                    int at = index;
                    int column;
                    if (Xlsx.TryParseCellReference(Xlsx.Attribute(cell, "r"), out int parsed, out int parsedRow))
                    {
                        column = parsed;
                        at = parsedRow;
                    }
                    else
                    {
                        column = expectedColumn;
                    }

                    expectedColumn = column + 1;

                    if (ValueOf(cell, shared) is { } value && !value.IsBlank)
                        sheet._values[(at, column)] = value;

                    sheet.Reaches(at, column);
                }
            }

            return sheet;
        }

        private static Value? ValueOf(XElement cell, XlsxSharedStrings shared)
        {
            string type = Xlsx.Attribute(cell, "t") ?? "n";

            switch (type)
            {
                case "s":
                    return Xlsx.Child(cell, "v")?.Value is { } key
                           && int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                           out int index)
                        ? Value.OfText(shared[index] ?? string.Empty)
                        : null;

                case "str":
                    return Xlsx.Child(cell, "v")?.Value is { } literal ? Value.OfText(literal) : null;

                case "inlineStr":
                    return Value.OfText(XlsxSharedStrings.ReadRichString(Xlsx.Child(cell, "is")));

                case "e":
                    return null;

                default:
                    return Xlsx.Child(cell, "v")?.Value is { } text
                           && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                                              out double number)
                        ? Value.OfNumber(number)
                        : null;
            }
        }
    }

    /// <summary>A <c>dxf</c> toggle, which is absent when the rule does not change it.</summary>
    /// <remarks>
    /// Three states rather than two, which is what makes a <c>dxf</c> differential: no element
    /// leaves the cell's own answer alone, a bare element turns the property on, and an explicit
    /// zero turns it off. 287 of one corpus document's 413 rules turn a strikethrough off.
    /// </remarks>
    private static bool? Toggle(XElement? element)
    {
        if (element is null) return null;

        string? value = Xlsx.Attribute(element, "val");
        return value is null || (value is not "0" && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase));
    }

    private static double? Number(XElement? element)
        => double.TryParse(Xlsx.Attribute(element, "val"), NumberStyles.Float,
                           CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;

    /// <summary>The ranges an <c>sqref</c> names, which is a space-separated list of A1 ranges.</summary>
    private static List<SheetRange> ParseSqref(string? sqref)
    {
        List<SheetRange> ranges = [];
        if (string.IsNullOrWhiteSpace(sqref)) return ranges;

        foreach (string part in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string one = part.Replace("$", string.Empty, StringComparison.Ordinal);
            int colon = one.IndexOf(':', StringComparison.Ordinal);

            if (colon < 0)
            {
                if (Xlsx.TryParseCellReference(one, out int column, out int row))
                    ranges.Add(new SheetRange(column, row, column, row));
                continue;
            }

            if (Xlsx.TryParseCellReference(one[..colon], out int firstColumn, out int firstRow)
                && Xlsx.TryParseCellReference(one[(colon + 1)..], out int lastColumn, out int lastRow))
            {
                ranges.Add(new SheetRange(
                    Math.Min(firstColumn, lastColumn), Math.Min(firstRow, lastRow),
                    Math.Max(firstColumn, lastColumn), Math.Max(firstRow, lastRow)));
            }
        }

        return ranges;
    }
}
