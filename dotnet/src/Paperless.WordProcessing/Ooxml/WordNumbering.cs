using System.Text;
using System.Xml.Linq;
using Paperless.Core.Numbering;

namespace Paperless.WordProcessing.Ooxml;

/// <summary>One <c>w:lvl</c>: how a single level of a list is numbered and labelled.</summary>
public sealed class WordNumberingLevel
{
    internal WordNumberingLevel(XElement element)
    {
        Level = Word.Attribute(element, "ilvl") is { } ilvl
                && int.TryParse(ilvl, out int parsed) ? parsed : 0;
        NumberFormat = Word.Value(element, "numFmt") ?? "decimal";
        LevelText = Word.Value(element, "lvlText") ?? string.Empty;
        // An absent `w:start` means zero, not one. Measured rather than read off the schema: four
        // one-level lists differing only in this element render 0/1/2 in LibreOffice when it is
        // omitted and 0/1/2, 1/2/3 and 3/4/5 for `w:val` of 0, 1 and 3 — and we agreed on all three
        // explicit values and disagreed only on the omission. See
        // `probes/numbering-start-default/`.
        //
        // `ABCD-FE-01-00 Flight Envelope` is the corpus document it decides: its heading list
        // (abstractNum 9, level 0) carries no `w:start`, so LibreOffice numbers its sections from
        // zero — "0. Introduction", "1. References" — and its own stored table of contents, which
        // Word wrote, agrees. We numbered every section one higher.
        Start = int.TryParse(Word.Value(element, "start"), out int start) ? start : 0;
        RestartAfterLevel = int.TryParse(Word.Value(element, "lvlRestart"), out int restart)
            ? restart
            : null;
        ParagraphStyleId = Word.Value(element, "pStyle");
        Suffix = Word.Value(element, "suff");
        IsLegalNumbering = Word.Child(element, "isLgl") is not null;
        RunProperties = Word.Child(element, "rPr");
        ParagraphProperties = Word.Child(element, "pPr");
    }

    /// <summary>The zero-based level this definition applies to.</summary>
    public int Level { get; }

    /// <summary>
    /// The <c>w:numFmt</c>: <c>decimal</c>, <c>bullet</c>, <c>lowerRoman</c>, <c>none</c> and
    /// so on.
    /// </summary>
    public string NumberFormat { get; }

    /// <summary>
    /// The <c>w:lvlText</c> template, with <c>%1</c>…<c>%9</c> standing for the counter at each
    /// level: <c>%1.</c> renders as "3." and <c>%1.%2</c> as "3.2".
    /// </summary>
    public string LevelText { get; }

    /// <summary>The value the first item at this level takes.</summary>
    public int Start { get; }

    /// <summary>
    /// The <c>w:lvlRestart</c>: which level's advance restarts this one. Zero means never.
    /// Null means the default, which is to restart whenever any shallower level advances.
    /// </summary>
    public int? RestartAfterLevel { get; }

    /// <summary>
    /// The paragraph style that implies this level, when the list is style-linked. This is how
    /// heading numbering attaches itself: the level names <c>Heading1</c> rather than every
    /// heading naming the list.
    /// </summary>
    public string? ParagraphStyleId { get; }

    /// <summary>
    /// What follows the label: <c>tab</c> (the default), <c>space</c>, or <c>nothing</c>.
    /// </summary>
    public string? Suffix { get; }

    /// <summary>
    /// <c>w:isLgl</c>: render every level of the label in decimal regardless of each level's own
    /// format, which is what legal numbering means.
    /// </summary>
    public bool IsLegalNumbering { get; }

    /// <summary>The label's own character formatting, which carries the bullet's font.</summary>
    public XElement? RunProperties { get; }

    /// <summary>
    /// The level's own <c>w:pPr</c>, which carries where the label and the text it labels sit.
    /// </summary>
    /// <remarks>
    /// Only two things in it are read: <c>w:ind</c>, whose <c>w:start</c> and <c>w:hanging</c> are the
    /// text's indent and the label's hanging distance, and the <c>w:tab w:val="num"</c> the label's own
    /// tab aims at. Extraction needs neither, which is why it was unread until layout wanted it.
    /// </remarks>
    public XElement? ParagraphProperties { get; }
}

/// <summary>
/// The numbering a DOCX declares: <c>numbering.xml</c>'s abstract definitions and the list
/// instances that point at them.
/// </summary>
/// <remarks>
/// <para>
/// WordprocessingML splits list <em>definition</em> from list <em>instance</em>. An
/// <c>w:abstractNum</c> defines nine levels; a <c>w:num</c> gives that definition a
/// <c>w:numId</c> that content refers to, optionally overriding a level or its start value. Two
/// paragraphs sharing a <c>numId</c> share counters; two <c>numId</c>s over one
/// <c>abstractNum</c> count independently. Collapsing the two would make every list in a
/// document continue the previous one's numbering.
/// </para>
/// <para>
/// Unlike ODF, nesting is not expressed in the XML tree: a paragraph states its own level in
/// <c>w:ilvl</c>. So the counters have to be advanced by walking the paragraphs in order, which
/// is what <see cref="Advance"/> does.
/// </para>
/// </remarks>
public sealed class WordNumbering
{
    /// <summary>
    /// The level count WordprocessingML defines. Fixed by the format, not a limit Paperless
    /// chose.
    /// </summary>
    public const int LevelCount = 9;

    private readonly Dictionary<string, Dictionary<int, WordNumberingLevel>> _abstractNumbering =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _instanceToAbstract = new(StringComparer.Ordinal);
    private readonly Dictionary<(string NumId, int Level), WordNumberingLevel> _overrides = [];
    private readonly Dictionary<(string NumId, int Level), int> _startOverrides = [];
    private readonly Dictionary<string, string> _abstractStyleLinks = new(StringComparer.Ordinal);

    /// <summary>
    /// Abstract definitions that define no levels of their own, only the name of one that does.
    /// </summary>
    /// <remarks>
    /// <c>w:numStyleLink</c> is the other half of <c>w:styleLink</c>: one abstract definition declares
    /// itself the numbering of a style, and any number of others say "I am that style's numbering" and
    /// carry nothing else. A reader that takes such a definition at face value finds no levels and draws
    /// no label at all — the whole numbering of the document disappears while its text and its indents
    /// stay exactly right, which is why it shows up as a handful of missing tokens rather than as
    /// anything visibly broken.
    /// </remarks>
    private readonly Dictionary<string, string> _abstractNumStyleLinks = new(StringComparer.Ordinal);

    // Live counters, per list instance and level. Advanced as paragraphs are read, which is the
    // only way to know a level's value: the file records the label nowhere.
    private readonly Dictionary<(string NumId, int Level), int> _counters = [];

    /// <summary>Reads a <c>numbering.xml</c> root element.</summary>
    public void Add(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (XElement abstractNum in Word.Children(root, "abstractNum"))
        {
            string? id = Word.Attribute(abstractNum, "abstractNumId");
            if (id is null) continue;

            Dictionary<int, WordNumberingLevel> levels = [];
            foreach (XElement level in Word.Children(abstractNum, "lvl"))
            {
                WordNumberingLevel parsed = new(level);
                levels[parsed.Level] = parsed;
            }
            _abstractNumbering[id] = levels;

            // A style link makes the abstract definition reachable by style name as well as by
            // instance, which is how "List Number" carries its numbering.
            if (Word.Value(abstractNum, "styleLink") is { Length: > 0 } styleLink)
                _abstractStyleLinks[styleLink] = id;

            if (Word.Value(abstractNum, "numStyleLink") is { Length: > 0 } numStyleLink)
                _abstractNumStyleLinks[id] = numStyleLink;
        }

        foreach (XElement num in Word.Children(root, "num"))
        {
            string? numId = Word.Attribute(num, "numId");
            string? abstractId = Word.Value(num, "abstractNumId");
            if (numId is null || abstractId is null) continue;

            _instanceToAbstract[numId] = abstractId;

            foreach (XElement over in Word.Children(num, "lvlOverride"))
            {
                if (!int.TryParse(Word.Attribute(over, "ilvl"), out int level)) continue;

                if (Word.Value(over, "startOverride") is { } start
                    && int.TryParse(start, out int startValue))
                    _startOverrides[(numId, level)] = startValue;

                if (Word.Child(over, "lvl") is { } replacement)
                    _overrides[(numId, level)] = new WordNumberingLevel(replacement);
            }
        }
    }

    /// <summary>
    /// The level definition in force for a list instance, or null when the instance is unknown.
    /// </summary>
    /// <remarks>
    /// A level override on the instance wins over the abstract definition, and a level the
    /// definition omits falls back to the deepest one above it — real files define only level
    /// zero and rely on it for deeper nesting.
    /// </remarks>
    public WordNumberingLevel? FindLevel(string? numId, int level)
    {
        if (numId is null) return null;
        if (_overrides.TryGetValue((numId, level), out WordNumberingLevel? overridden)) return overridden;
        if (!_instanceToAbstract.TryGetValue(numId, out string? abstractId)) return null;

        abstractId = FollowStyleLink(abstractId);
        if (!_abstractNumbering.TryGetValue(abstractId, out Dictionary<int, WordNumberingLevel>? levels))
            return null;

        for (int candidate = level; candidate >= 0; candidate--)
        {
            if (levels.TryGetValue(candidate, out WordNumberingLevel? found)) return found;
        }
        return null;
    }

    /// <summary>
    /// Follows <c>w:numStyleLink</c> to the abstract definition that actually holds the levels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LibreOffice's <c>ListsManager::GetAbstractList</c>
    /// (<c>sw/source/writerfilter/dmapper/NumberingManager.cxx:1140-1176</c>) tries the named paragraph
    /// style's own list first and then, failing that, scans for the abstract definition whose
    /// <c>w:styleLink</c> names the same style. Only the second is done here: the first needs the style
    /// table, which this class does not hold, and it is the fallback that every file measured takes —
    /// a producer that writes <c>numStyleLink</c> writes the matching <c>styleLink</c> beside it.
    /// </para>
    /// <para>
    /// Walked rather than looked up once, because a chain is legal; bounded, because a file can point
    /// two definitions at each other and neither Word nor this may hang on it.
    /// </para>
    /// </remarks>
    private string FollowStyleLink(string abstractId)
    {
        for (int hop = 0; hop < LevelCount; hop++)
        {
            if (!_abstractNumStyleLinks.TryGetValue(abstractId, out string? styleName)) break;
            if (!_abstractStyleLinks.TryGetValue(styleName, out string? target)) break;
            if (string.Equals(target, abstractId, StringComparison.Ordinal)) break;

            abstractId = target;
        }

        return abstractId;
    }

    /// <summary>
    /// The <c>numId</c> a paragraph style implies, for a style-linked list.
    /// </summary>
    public string? FindInstanceForStyle(string styleId)
    {
        ArgumentException.ThrowIfNullOrEmpty(styleId);

        // A style may be named either by the abstract definition's styleLink or by one of its
        // levels' w:pStyle. Both appear; heading numbering uses the latter.
        if (_abstractStyleLinks.TryGetValue(styleId, out string? abstractId))
        {
            foreach ((string numId, string mapped) in _instanceToAbstract)
            {
                if (string.Equals(mapped, abstractId, StringComparison.Ordinal)) return numId;
            }
        }

        foreach ((string numId, string mapped) in _instanceToAbstract)
        {
            if (!_abstractNumbering.TryGetValue(mapped, out Dictionary<int, WordNumberingLevel>? levels))
                continue;
            foreach (WordNumberingLevel level in levels.Values)
            {
                if (string.Equals(level.ParagraphStyleId, styleId, StringComparison.Ordinal))
                    return numId;
            }
        }
        return null;
    }

    /// <summary>
    /// Advances the counters for a paragraph at a level, and returns its rendered label.
    /// </summary>
    /// <param name="numId">The list instance the paragraph belongs to.</param>
    /// <param name="level">The paragraph's zero-based level.</param>
    /// <returns>
    /// The label as it would be drawn, or null when this level draws none — a level whose
    /// <c>w:numFmt</c> is <c>none</c>, which is how numbered heading styles that show no number
    /// are written.
    /// </returns>
    public string? Advance(string? numId, int level)
    {
        // numId zero is the format's way of saying "not numbered", and is how a continuation
        // paragraph inside a list item is written.
        if (numId is null or "0") return null;

        WordNumberingLevel? definition = FindLevel(numId, level);
        if (definition is null) return null;

        int current = _counters.TryGetValue((numId, level), out int existing)
            ? existing + 1
            : StartOf(numId, level, definition);
        _counters[(numId, level)] = current;

        // A shallower level advancing restarts everything under it, unless a level says
        // otherwise with w:lvlRestart. Without this every sub-list continues the last one.
        for (int deeper = level + 1; deeper < LevelCount; deeper++)
        {
            WordNumberingLevel? deeperDefinition = FindLevel(numId, deeper);
            if (deeperDefinition?.RestartAfterLevel == 0) continue;
            _counters.Remove((numId, deeper));
        }

        SeedLevelsShownBy(numId, level, definition);

        return FormatLabel(numId, level, definition);
    }

    /// <summary>
    /// Gives every shallower level this label shows a counter of its own, if it has none yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A level shown inside a deeper item's number has been used, and the next item at that
    /// level counts on from it.</strong> Without this, <see cref="FormatLabel"/> renders a missing
    /// component from <see cref="StartOf"/> and throws the value away, so the level's first real item
    /// takes the start value a second time and everything under that parent is one too low.
    /// </para>
    /// <para>
    /// Measured on 26.2.4.2 with a four-level <c>multilevel</c> list, no <c>w:start</c> anywhere —
    /// <c>probes/skipped-level-counter/</c>. Levels 0, 2, 1, 1 number
    /// <c>0</c>, <c>0.0.0</c>, <c>0.1</c>, <c>0.2</c>; we gave the third item <c>0.0</c>. Skipping two
    /// levels behaves the same way, and a run with no skip in it is untouched — which is why this had
    /// gone unnoticed: it needs a deeper item to appear before its own parent does.
    /// </para>
    /// <para>
    /// Only the levels the template actually shows are seeded. A level the deeper item does not
    /// display was never rendered, so nothing says it was used, and the corpus holds no case that
    /// separates the two — narrowing it to what was measured is the conservative reading.
    /// </para>
    /// <para>
    /// The corpus document it decides is
    /// <c>OM template for non-complex NCC operators_August 2016.docx</c>, whose <c>0.2</c> section
    /// opens with a <c>Heading4</c> before any <c>Heading3</c>. The reference numbers the headings
    /// that follow <c>0.2.2</c>, <c>0.2.3</c>, <c>0.2.4</c>, and the document's own stored table of
    /// contents — written by Word — agrees; we numbered them one lower throughout.
    /// </para>
    /// </remarks>
    private void SeedLevelsShownBy(string numId, int level, WordNumberingLevel definition)
    {
        ReadOnlySpan<char> template = definition.LevelText;

        for (int at = 0; at + 1 < template.Length; at++)
        {
            if (template[at] != '%' || !char.IsAsciiDigit(template[at + 1])) continue;

            // %1 is level zero: the placeholder is one-based, the level is not.
            int shown = template[at + 1] - '1';
            at++;

            if (shown < 0 || shown >= level) continue;
            if (_counters.ContainsKey((numId, shown))) continue;

            if (FindLevel(numId, shown) is { } component)
            {
                _counters[(numId, shown)] = StartOf(numId, shown, component);
            }
        }
    }

    /// <summary>
    /// Renders a level's label from the current counters, without advancing them.
    /// </summary>
    public string? FormatLabel(string numId, int level, WordNumberingLevel definition)
    {
        ArgumentException.ThrowIfNullOrEmpty(numId);
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.NumberFormat == "none") return null;
        if (definition.LevelText.Length == 0) return null;

        if (definition.NumberFormat == "bullet")
        {
            // The level text *is* the bullet, and it is usually a symbol-font code point that
            // means nothing outside that font.
            return OutlineNumbers.NormaliseBullet(definition.LevelText);
        }

        StringBuilder label = new();
        ReadOnlySpan<char> template = definition.LevelText;

        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] != '%' || i + 1 >= template.Length || !char.IsAsciiDigit(template[i + 1]))
            {
                label.Append(template[i]);
                continue;
            }

            // %1 is level zero: the placeholder is one-based, the level is not.
            int placeholder = template[i + 1] - '1';
            i++;
            if (placeholder is < 0 or >= LevelCount) continue;

            WordNumberingLevel? component = FindLevel(numId, placeholder);
            int value = _counters.TryGetValue((numId, placeholder), out int counter)
                ? counter
                : component is null ? 1 : StartOf(numId, placeholder, component);

            // Legal numbering forces every component to decimal, whatever each level's own
            // format says.
            string format = definition.IsLegalNumbering
                ? "decimal"
                : component?.NumberFormat ?? "decimal";
            label.Append(FormatNumber(value, format));
        }

        return label.ToString();
    }

    /// <summary>
    /// Formats one counter value in a WordprocessingML number format.
    /// </summary>
    /// <remarks>
    /// An unrecognised format falls back to decimal rather than rendering nothing: the format
    /// list is long, mostly locale-specific, and a missing label is more wrong than a
    /// differently-shaped one.
    /// </remarks>
    public static string FormatNumber(int value, string? format) => format switch
    {
        "upperRoman" => OutlineNumbers.Roman(value, upperCase: true),
        "lowerRoman" => OutlineNumbers.Roman(value, upperCase: false),
        // Word's letter formats repeat the letter past twenty-six rather than counting in
        // bijective base 26.
        "upperLetter" => OutlineNumbers.Alphabetic(value, upperCase: true, synchronised: true),
        "lowerLetter" => OutlineNumbers.Alphabetic(value, upperCase: false, synchronised: true),
        "decimalZero" => OutlineNumbers.DigitsWithLeadingZero(value),
        "ordinal" => OutlineNumbers.Ordinal(value),
        _ => OutlineNumbers.Digits(value),
    };

    /// <summary>Resets every counter, so a second body can be read from the same numbering.</summary>
    /// <remarks>
    /// Headers, footnotes and comments are separate flows. Numbering inside them restarts
    /// rather than continuing the body's count, so the reader resets between flows.
    /// </remarks>
    public void ResetCounters() => _counters.Clear();

    private int StartOf(string numId, int level, WordNumberingLevel definition)
        => _startOverrides.TryGetValue((numId, level), out int overridden)
            ? overridden
            : definition.Start;
}
