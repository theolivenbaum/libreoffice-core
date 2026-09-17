using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Paperless.Core.Numbers;

namespace Paperless.OpenDocument.Styles;

/// <summary>
/// Compiles an ODF <c>number:*-style</c> into the format code the number engine parses.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a translation rather than a second formatter.</strong> ODF states a number format
/// as a tree of elements — <c>number:number number:decimal-places="2" number:grouping="true"</c>
/// — where OOXML states it as the string <c>#,##0.00</c>. The two describe the same thing and
/// LibreOffice keeps exactly one formatter for both: <c>SvNumberformat</c>, which ODF's import
/// reaches by <em>building a format string</em> from the elements
/// (<c>xmloff/source/style/xmlnumfi.cxx</c>'s <c>SvXMLNumFormatContext::CreateAndInsert</c>, which
/// assembles <c>aFormatCode</c> piece by piece and hands it to <c>SvNumberFormatter</c>). This is
/// that assembly, and it is the reason <c>Paperless.Core.Numbers</c> needs no ODF path at all.
/// </para>
/// <para>
/// <strong>What it is for.</strong> An ODF chart's axis names a data style through
/// <c>style:data-style-name</c>, and that is the only statement of how its ticks are written —
/// there is no cached text on an axis the way there is on a cell, so a percentage axis draws
/// <c>0.05</c> instead of <c>5%</c> without this.
/// </para>
/// <para>
/// <strong>The trap, named.</strong> <c>number:minutes</c> and <c>number:month</c> both compile
/// to <c>M</c>, which is the same ambiguity the format-code language has and resolves the same
/// way — an <c>M</c> between an hour and a second is minutes. So the pieces must be emitted in
/// document order and not gathered by kind; sorting them, or emitting the date part before the
/// time part regardless of what the style says, turns <c>13:45</c> into month 45 of year 13.
/// </para>
/// </remarks>
public static class OdfNumberFormat
{
    /// <summary>
    /// The format code a data style states, or null when the style is not one this compiles.
    /// </summary>
    /// <param name="style">The <c>number:*-style</c> element.</param>
    /// <param name="resolve">
    /// How to find another data style by name, or null to compile this element alone. Supplying it
    /// is what assembles a multi-section format — see the remarks on <see cref="Sections"/>.
    /// </param>
    public static string? Code(XElement? style, Func<string, XElement?>? resolve = null)
        => Code(style, resolve, []);

    /// <summary>The parsed code a data style states, or null.</summary>
    /// <param name="style">The <c>number:*-style</c> element.</param>
    /// <param name="resolve">How to find another data style by name, or null.</param>
    public static NumberFormatCode? Parse(XElement? style, Func<string, XElement?>? resolve = null)
    {
        if (Code(style, resolve) is not { Length: > 0 } code) return null;

        NumberFormatCode parsed = NumberFormatCode.Parse(code);
        return parsed.IsGeneral ? null : parsed;
    }

    private static string? Code(XElement? style, Func<string, XElement?>? resolve, List<XElement> stack)
    {
        if (style is null) return null;

        // `CreateAndInsert` keeps the styles it is already building on a stack and refuses a
        // `style:map` that names one of them — "invalid style:map references containing style",
        // xmloff/source/style/xmlnumfi.cxx:1592-1596. Without it a file that maps a style to
        // itself is a stack overflow rather than a diagnostic.
        if (stack.Contains(style)) return null;
        stack.Add(style);

        try
        {
            // Which literals need quoting is decided per format type, and the type is what the
            // style's own element name states.
            string kind = style.Name.LocalName;

            StringBuilder conditions = new();
            if (resolve is not null) Sections(conditions, style, kind, resolve, stack);

            StringBuilder code = new();
            foreach (XElement piece in style.Elements())
            {
                if (piece.Name.NamespaceName != OdfNamespaces.Number) continue;
                Append(code, piece, kind);
            }

            if (Colour(style) is { } colour) code.Insert(0, colour);

            // `:1605-1610` — an empty format is inserted as `""`, and the check is made before the
            // conditions are prepended so that a mapped style with nothing of its own still holds
            // a section. Only done where there ARE conditions: a style element that compiles to
            // nothing at all still answers null, which is what every caller before this expected.
            if (code.Length == 0 && conditions.Length > 0) code.Append("\"\"");

            string built = conditions.Append(code).ToString();
            return built.Length == 0 ? null : built;
        }
        finally
        {
            stack.RemoveAt(stack.Count - 1);
        }
    }

    /// <summary>
    /// The sections a style's <c>style:map</c> children contribute, in document order, each
    /// closed with a semicolon — so that prepending them to the style's own body gives the whole
    /// format code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>ODF does not write a multi-section format as one element.</strong> It writes one
    /// <c>number:*-style</c> per section and links them from the one the cell names, whose own
    /// body is the <em>last</em> section: <c>N142</c> carries <c>(</c>, the number and <c>)</c>
    /// and a <c>&lt;style:map style:condition="value()&gt;=0"
    /// style:apply-style-name="N142P0"/&gt;</c>, with the positive form in the volatile
    /// <c>N142P0</c>. Reading the named element alone therefore compiles <em>one</em> section and
    /// applies it to every value — a negative drew <c>(100)</c> for us where the reference draws
    /// it and a positive drew <c>(100)</c> too.
    /// </para>
    /// <para>
    /// This is <c>SvXMLNumFormatContext::CreateAndInsert</c> and <c>AddCondition</c>
    /// (<c>xmloff/source/style/xmlnumfi.cxx</c>:1588-1602 and :2130-2186) in the order they run.
    /// Four details decide it and three of them are not in the specification's prose:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <strong>The condition must begin <c>value()</c></strong> and only what follows is the
    /// comparison (<c>:2137-2140</c>). One that does not, or that names a style nothing resolves,
    /// contributes <b>nothing at all</b> — not even an empty section — so the sections that remain
    /// close up.
    /// </description></item>
    /// <item><description>
    /// <strong>A single <c>value()&gt;=0</c> map states no condition in the code</strong>
    /// (<c>:2149-2150</c>): it is the ordinary <em>positive;negative</em> pair, and bracketing it
    /// would make both sections conditional and leave nothing to fall through to.
    /// </description></item>
    /// <item><description>
    /// <strong>In a <c>number:text-style</c> the last map is unconditional too</strong>
    /// (<c>:2152-2156</c>, whose comment says the last condition "can only be all other numbers").
    /// That is how an accounting format arrives: the owner is the <em>text</em> style holding
    /// <c>@</c>, and its three maps are positive, negative and zero — so the assembled code is the
    /// familiar four sections in the familiar order, with the third bare.
    /// </description></item>
    /// <item><description>
    /// <strong><c>!=</c> is rewritten to <c>&lt;&gt;</c></strong> (<c>:2161-2166</c>), once. The
    /// decimal separator is localised in the same place and this compiles as en-US throughout, so
    /// that half is a no-op here.
    /// </description></item>
    /// </list>
    /// </remarks>
    private static void Sections(
        StringBuilder conditions,
        XElement style,
        string kind,
        Func<string, XElement?> resolve,
        List<XElement> stack)
    {
        List<XElement> maps = [.. style.Elements(XName.Get("map", OdfNamespaces.Style))];

        for (int index = 0; index < maps.Count; index++)
        {
            XElement map = maps[index];

            if (map.Attribute(XName.Get("apply-style-name", OdfNamespaces.Style))?.Value
                is not { Length: > 0 } applied) continue;
            if (map.Attribute(XName.Get("condition", OdfNamespaces.Style))?.Value
                is not { } condition) continue;
            if (!condition.StartsWith(ValuePrefix, StringComparison.Ordinal)) continue;

            if (Code(resolve(applied), resolve, stack) is not { Length: > 0 } section) continue;

            bool bare = (conditions.Length == 0 && maps.Count == 1
                         && condition.AsSpan(ValuePrefix.Length) is ">=0")
                        || (kind == TextStyle && index == maps.Count - 1);

            if (!bare)
            {
                string comparison = condition[ValuePrefix.Length..];
                int inequality = comparison.IndexOf("!=", StringComparison.Ordinal);
                if (inequality >= 0) comparison = comparison.Remove(inequality, 2).Insert(inequality, "<>");

                conditions.Append('[').Append(comparison).Append(']');
            }

            conditions.Append(section).Append(';');
        }
    }

    /// <summary>What a data style's <c>fo:color</c> contributes to its section, or null.</summary>
    /// <remarks>
    /// <para>
    /// ODF states a section's colour as a <c>style:text-properties</c> child carrying an RGB
    /// value, where the format-code language states it as a keyword — so <c>AddColor</c>
    /// (<c>xmlnumfi.cxx</c>:2196-2216) turns the value back into the keyword and
    /// <strong>inserts it at the front of the code</strong>, wherever in the element the property
    /// sat.
    /// </para>
    /// <para>
    /// <strong>Only ten values survive the round trip, and that is the reference's own behaviour
    /// rather than a shortcut here.</strong> <c>aNumFmtStdColors</c> (<c>:212-226</c>) is the same
    /// ten in the same order as <c>ImpSvNumberformatScan::StandardColor</c> — the table
    /// <c>NumberFormatSection</c> already reads the other way — and a colour that is not one of
    /// them matches nothing, leaves <c>aColName</c> empty and is dropped. So a section stating
    /// <c>#262626</c> is black on both sides, and a reader that resolved arbitrary RGB here would
    /// paint ink 26.2.4.2 does not.
    /// </para>
    /// </remarks>
    private static string? Colour(XElement style)
    {
        XElement? properties = style.Element(XName.Get("text-properties", OdfNamespaces.Style));

        string? value = properties?.Attribute(XName.Get("color", OdfNamespaces.FoCompatible))?.Value;
        if (value is not { Length: 7 } || value[0] != '#') return null;

        if (!uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                           out uint rgb))
        {
            return null;
        }

        return rgb switch
        {
            0x000000 => "[BLACK]",
            0x0000FF => "[BLUE]",
            0x00FF00 => "[GREEN]",
            0x00FFFF => "[CYAN]",
            0xFF0000 => "[RED]",
            0xFF00FF => "[MAGENTA]",
            0x808000 => "[BROWN]",
            0x808080 => "[GREY]",
            0xFFFF00 => "[YELLOW]",
            0xFFFFFF => "[WHITE]",
            _ => null,
        };
    }

    /// <summary>What every <c>style:condition</c> this reads begins with.</summary>
    private const string ValuePrefix = "value()";

    private static void Append(StringBuilder code, XElement piece, string kind)
    {
        switch (piece.Name.LocalName)
        {
            case "number": Number(code, piece); break;
            case "scientific-number": Scientific(code, piece); break;
            case "fraction": Fraction(code, piece); break;

            // A literal. ODF writes the per cent sign, the currency symbol and every separator as
            // one of these, so quoting is what keeps a stray "d" or "m" in a suffix out of the
            // date vocabulary.
            case "text" or "currency-symbol": Literal(code, piece.Value, kind); break;
            case "text-content": code.Append('@'); break;

            case "year": code.Append(Long(piece) ? "YYYY" : "YY"); break;
            case "month":
                code.Append(Flag(piece, "textual") == true
                    ? (Long(piece) ? "MMMM" : "MMM")
                    : (Long(piece) ? "MM" : "M"));
                break;
            case "day": code.Append(Long(piece) ? "DD" : "D"); break;
            // NN is the short day name and NNN the long one; NNNN is the long one with the
            // locale's day-of-week separator, and `xmloff` never emits it from the element
            // alone — `AddNfKeyword` rewrites NNNN to NNN and only restores the separator when
            // a following <number:text> holds exactly it (xmloff/source/style/xmlnumfi.cxx:2037
            // and :955-970). Measured on both binaries with a hand-built flat ODS
            // (dotnet/probes/numfmt-r68/dow.fods): a short day-of-week draws `Sun` and a long
            // one draws `Sunday`, with no trailing comma.
            case "day-of-week": code.Append(Long(piece) ? "NNN" : "NN"); break;
            case "quarter": code.Append(Long(piece) ? "QQ" : "Q"); break;
            case "week-of-year": code.Append("WW"); break;
            case "era": code.Append(Long(piece) ? "GGG" : "G"); break;

            case "hours": code.Append(Long(piece) ? "HH" : "H"); break;
            case "minutes": code.Append(Long(piece) ? "MM" : "M"); break;
            case "seconds": Seconds(code, piece); break;
            case "am-pm": code.Append("AM/PM"); break;

            case "boolean": code.Append("BOOLEAN"); break;

            default: break;
        }
    }

    private static void Number(StringBuilder code, XElement piece)
    {
        int integers = Integer(piece, "min-integer-digits") ?? 1;
        int decimals = Integer(piece, "decimal-places") ?? 0;
        int minimum = Integer(piece, "min-decimal-places") ?? decimals;
        bool grouping = Flag(piece, "grouping") == true;

        // The integer part is grouped by writing the group separator into it, which is what the
        // format-code language means by "#,##0": one hash-comma-hash-hash before the digits.
        if (grouping) code.Append("#,##");

        code.Append(integers <= 0 ? "#" : new string('0', integers));

        if (decimals <= 0) return;

        code.Append('.');
        code.Append('0', Math.Clamp(minimum, 0, decimals));
        code.Append('#', decimals - Math.Clamp(minimum, 0, decimals));
    }

    private static void Scientific(StringBuilder code, XElement piece)
    {
        int integers = Integer(piece, "min-integer-digits") ?? 1;
        int decimals = Integer(piece, "decimal-places") ?? 0;
        int exponent = Integer(piece, "min-exponent-digits") ?? 2;

        code.Append(integers <= 0 ? "#" : new string('0', integers));
        if (decimals > 0) code.Append('.').Append('0', decimals);
        code.Append("E+").Append('0', Math.Max(exponent, 1));
    }

    private static void Fraction(StringBuilder code, XElement piece)
    {
        int integers = Integer(piece, "min-integer-digits") ?? 0;
        int numerator = Integer(piece, "min-numerator-digits") ?? 1;
        int denominator = Integer(piece, "min-denominator-digits") ?? 1;
        int value = Integer(piece, "denominator-value") ?? 0;

        if (integers > 0) code.Append('#', integers).Append(' ');
        code.Append('?', Math.Max(numerator, 1)).Append('/');

        // A stated denominator is written as itself — "?/8" rather than "?/?" — which is what
        // makes eighths eighths rather than the nearest single-digit fraction.
        if (value > 0) code.Append(value.ToString(CultureInfo.InvariantCulture));
        else code.Append('?', Math.Max(denominator, 1));
    }

    private static void Seconds(StringBuilder code, XElement piece)
    {
        code.Append(Long(piece) ? "SS" : "S");

        int decimals = Integer(piece, "decimal-places") ?? 0;
        if (decimals > 0) code.Append('.').Append('0', decimals);
    }

    /// <summary>
    /// A literal, quoted so that its letters are not read as directives — but only where the
    /// reference quotes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>lcl_EnquoteIfNecessary</c> (<c>xmloff/source/style/xmlnumfi.cxx</c>:533-560) leaves a
    /// literal bare when it is one character the format type can carry unquoted, or two of which
    /// the second is a space, or the pair <c>" -"</c>. Everything longer is quoted, which is what
    /// keeps a stray <c>d</c> or <c>m</c> in a suffix out of the date vocabulary.
    /// </para>
    /// <para>
    /// <strong>Quoting too eagerly is not cosmetic: on a percentage it costs a factor of a
    /// hundred.</strong> ODF has no per cent directive — a <c>number:percentage-style</c> is a
    /// <c>number:number</c> followed by a <c>number:text</c> holding the sign, and it is that sign
    /// in the compiled code that tells the engine to multiply. The common <c>0.00 %</c> is written
    /// as the two-character literal <c>" %"</c>, which quoting whole renders <c>0.05</c> as
    /// <c>0.05 %</c> instead of <c>5.00 %</c>. Hence the percentage arm below, which quotes around
    /// the sign rather than over it (<c>:552-587</c>).
    /// </para>
    /// </remarks>
    private static void Literal(StringBuilder code, string? text, string kind)
    {
        if (text is not { Length: > 0 } literal) return;

        if (!NeedsQuotes(literal, kind))
        {
            code.Append(literal);
            return;
        }

        int sign = kind == PercentageStyle && literal.Length > 1 ? literal.IndexOf('%') : -1;

        if (sign < 0)
        {
            Quote(code, literal);
            return;
        }

        // Each side of the sign is quoted on its own, leaving the sign bare so the engine still
        // reads it as the directive. A single character either side that needs no quotes stays
        // bare, so that " %" compiles to " %" rather than to "\" \"%".
        if (sign > 0)
        {
            if (sign == 1 && Bare(literal[0], kind)) code.Append(literal[0]);
            else Quote(code, literal[..sign]);
        }

        code.Append('%');

        if (sign + 1 >= literal.Length) return;

        if (sign + 2 == literal.Length && Bare(literal[sign + 1], kind)) code.Append(literal[sign + 1]);
        else Quote(code, literal[(sign + 1)..]);
    }

    /// <summary>Whether a literal has to be quoted at all.</summary>
    private static bool NeedsQuotes(string text, string kind)
    {
        if (kind == BooleanStyle) return true;

        return text.Length switch
        {
            1 => !Bare(text[0], kind),
            2 => !(text is " -" || (text[1] == ' ' && Bare(text[0], kind))),
            _ => true,
        };
    }

    /// <summary>
    /// Whether one character can stand in the code unquoted, which depends on the format type.
    /// </summary>
    /// <remarks>
    /// <c>lcl_ValidChar</c> (<c>xmlnumfi.cxx</c>:480-531). A separator is bare only where the type
    /// can carry one: <c>/</c> is a date separator in a date style and a fraction bar in a number
    /// style, so it is quoted in the latter. The thousands separator is quoted wherever a number
    /// can appear so that an extra one is not read as a display factor — the separator is the
    /// reader's locale's, and this compiles as en-US throughout, matching the 1033 the HTML export
    /// writes.
    /// </remarks>
    private static bool Bare(char character, string kind)
    {
        if (character == ',' && kind is NumberStyle or CurrencyStyle or PercentageStyle) return false;

        return character switch
        {
            '-' => kind != BooleanStyle,
            ' ' or '/' or '.' or ',' or ':' or '\'' => kind is CurrencyStyle or DateStyle or TimeStyle,
            '%' => kind == PercentageStyle,
            '(' or ')' => kind is NumberStyle or CurrencyStyle or PercentageStyle,
            _ => false,
        };
    }

    private static void Quote(StringBuilder code, string text)
    {
        code.Append('"');
        foreach (char character in text)
        {
            // A quote inside quoted text closes it, escapes itself and reopens (`:592-606`).
            if (character == '"') code.Append("\"\\\"\"");
            else code.Append(character);
        }

        code.Append('"');
    }

    private const string NumberStyle     = "number-style";
    private const string CurrencyStyle   = "currency-style";
    private const string PercentageStyle = "percentage-style";
    private const string DateStyle       = "date-style";
    private const string TimeStyle       = "time-style";
    private const string BooleanStyle    = "boolean-style";
    private const string TextStyle       = "text-style";

    private static bool Long(XElement piece)
        => piece.Attribute(XName.Get("style", OdfNamespaces.Number))?.Value == "long";

    private static bool? Flag(XElement piece, string name)
        => piece.Attribute(XName.Get(name, OdfNamespaces.Number))?.Value switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        };

    private static int? Integer(XElement piece, string name)
        => int.TryParse(
            piece.Attribute(XName.Get(name, OdfNamespaces.Number))?.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsed)
            ? parsed
            : null;
}
