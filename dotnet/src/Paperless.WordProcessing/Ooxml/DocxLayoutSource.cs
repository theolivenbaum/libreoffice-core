using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.Ooxml.DrawingML;
using Paperless.Text.Fonts;
using Paperless.Text.Layout;
using Paperless.Text.Shaping;
using Paperless.WordProcessing.Layout;
using Paperless.WordProcessing.Model;

namespace Paperless.WordProcessing.Ooxml;

/// <summary>
/// Turns a DOCX body into the paragraphs the paginator takes.
/// </summary>
/// <remarks>
/// <para>
/// A second walk over <c>document.xml</c>, for the same reason ODF has one: extraction discards the font
/// sizes, indents and spacing layout needs, and making it carry them would charge every caller for a
/// feature most never use.
/// </para>
/// <para>
/// The same gap as the ODF source: per-run font sizes are not honoured, so a paragraph is measured
/// wholly in the font its paragraph mark carries. The tallest run on a line sets that line's height, so
/// a paragraph mixing sizes lays out slightly short until the runs are walked.
/// </para>
/// </remarks>
public sealed partial class DocxLayoutSource
{
    /// <summary>How many paragraphs are read before the rest are ignored.</summary>
    public const int MaxParagraphs = 200000;

    /// <summary>
    /// The character an anchor occupies: a field result, a note reference, an inline drawing.
    /// </summary>
    /// <remarks>
    /// The same one the document model and the ODF source use, so an offset means the same thing
    /// wherever it was counted.
    /// </remarks>
    private const char AnchorCharacter = '\u0001';

    /// <summary>
    /// The character a <c>w:br</c> becomes.
    /// </summary>
    /// <remarks>
    /// U+2028, whose UAX #14 class is a mandatory break, so the break iterator honours it without layout
    /// special-casing anything. A newline would break the same way but would read as the end of a
    /// paragraph to anything that later scans the text, which a break inside one is not.
    /// </remarks>
    private const char LineSeparator = '\u2028';

    private readonly WordStyles _styles;
    private readonly SystemFontResolver _fonts;
    private readonly Length _defaultTabInterval;
    private readonly int _compatibilityMode;

    /// <summary>What <c>w:beforeAutospacing</c> and <c>w:afterAutospacing</c> stand for here.</summary>
    private readonly Length _autoSpacing;

    /// <summary>The device grid every font metric is rounded onto.</summary>
    /// <remarks>
    /// Always one or the other, never nothing: <see cref="MetricGrid.Reference"/> is the 8640 dpi
    /// virtual device Writer formats against by default. <c>w:usePrinterMetrics</c> swaps it for a real
    /// printer's — writerfilter turns that flag into <c>PrinterIndependentLayout::DISABLED</c> at
    /// <c>sw/source/writerfilter/dmapper/DomainMapper_Impl.cxx:10173</c>, the same state
    /// <c>WW8Dop::fUsePrinterMetrics</c> puts a DOC into, and the same printer grid
    /// <see cref="Ww8.DocReader"/> already passes.
    /// </remarks>
    private readonly MetricGrid _metrics;

    /// <summary>
    /// <c>word/fontTable.xml</c>, for the shape it declares for each family it names.
    /// </summary>
    /// <remarks>
    /// Layout does not measure with it and never asks it for a face. What it settles is the one
    /// question the family name alone cannot answer — whether a family nobody has installed is a
    /// roman or a grotesque — and getting that wrong renders a serif document in DejaVu Sans where
    /// LibreOffice renders it in DejaVu Serif, which moves every line break in it.
    /// </remarks>
    private readonly WordFontTable _fontTable;

    /// <summary>What a <c>FILENAME</c> or <c>TITLE</c> field evaluates to.</summary>
    private readonly ConstantFields _constants;

    private readonly DrawingTheme? _theme;
    private readonly Dictionary<(string? Family, int Weight, bool Italic, FontFamilyClass Class),
        OpenTypeFace?> _faces = [];
    private readonly Dictionary<(string? Family, int Weight, bool Italic, FontFamilyClass Class),
        FontReference> _references = [];

    /// <summary>Creates a source over a document's styles and settings.</summary>
    /// <param name="styles">The document's styles, including its <c>w:docDefaults</c>.</param>
    /// <param name="settings">The document's <c>w:settings</c> root, or null.</param>
    /// <param name="fonts">The font resolver, or null to build one over the installed fonts.</param>
    /// <param name="footnotes">The footnote bodies by <c>w:id</c>, or null for a document with none.</param>
    /// <param name="endnotes">The endnote bodies by <c>w:id</c>.</param>
    /// <param name="theme">The document's theme, for themed run colours, or null.</param>
    /// <param name="pictures">
    /// How to reach the bytes an <c>a:blip</c> names, or null to lay the document out with its picture
    /// frames empty — which is what a caller who wants only measurements should pay for.
    /// </param>
    /// <param name="numbering">
    /// The document's <c>numbering.xml</c>, or null for a document with no lists. Its counters are
    /// advanced by this walk, which is why <see cref="Read"/> and <see cref="ReadFlow"/> reset them: a
    /// caller sharing one instance with the extraction pass must not have the two interleave.
    /// </param>
    /// <param name="fontTable">
    /// <c>word/fontTable.xml</c>, or null for a document without one. Nothing is measured with it —
    /// it settles which shape a family nobody has installed falls back to, which the family name on
    /// its own cannot say.
    /// </param>
    /// <param name="constants">
    /// What a <c>FILENAME</c> or <c>TITLE</c> field evaluates to, or null to leave both at their
    /// cached results — see <see cref="ConstantFields"/>.
    /// </param>
    public DocxLayoutSource(
        WordStyles styles,
        XElement? settings = null,
        SystemFontResolver? fonts = null,
        IReadOnlyDictionary<string, XElement>? footnotes = null,
        IReadOnlyDictionary<string, XElement>? endnotes = null,
        DrawingTheme? theme = null,
        DocxPictures? pictures = null,
        WordNumbering? numbering = null,
        WordFontTable? fontTable = null,
        ConstantFields? constants = null)
    {
        _constants = constants ?? default;
        ArgumentNullException.ThrowIfNull(styles);
        _styles = styles;
        _fontTable = fontTable ?? WordFontTable.Empty;
        _numbering = numbering ?? new WordNumbering();
        Pictures = pictures;
        _theme = theme;
        _fonts = fonts ?? new SystemFontResolver(SystemFontIndex.Build());
        _defaultTabInterval = TabInterval(settings);
        _compatibilityMode = CompatibilityMode(settings);
        WordCompatibility compatibility = WordCompatibility.Read(settings);
        _autoSpacing = AutoSpacing(settings, compatibility);
        // `AsWordDocument` is the MS_WORD_COMP_GRID_METRICS compatibility flag, which the Word
        // filters set and ODF's does not. See MetricGrid.AsWordDocument.
        _metrics = (compatibility.UsesPrinterMetrics ? MetricGrid.Printer : MetricGrid.Reference)
            .AsWordDocument();
        _footnotes = footnotes ?? new Dictionary<string, XElement>(StringComparer.Ordinal);
        _endnotes = endnotes ?? new Dictionary<string, XElement>(StringComparer.Ordinal);
        _footnoteNumbering = NumberingIn(settings, "footnotePr", NoteNumbering.Footnotes);
        _endnoteNumbering = NumberingIn(settings, "endnotePr", NoteNumbering.Endnotes);
    }

    /// <summary>
    /// How a picture's bytes are reached, or null when this source was built without a package.
    /// </summary>
    /// <remarks>
    /// Exposed rather than private because its <see cref="DocxPictures.Scope"/> has to follow the walk:
    /// relationship ids are numbered from one in every part, so whoever hands this source a header to
    /// read must say which part it came from first.
    /// </remarks>
    public DocxPictures? Pictures { get; }

    /// <summary>The footnote bodies by <c>w:id</c>, from <c>footnotes.xml</c>.</summary>
    /// <remarks>
    /// The parts rather than the whole package, because that is all layout needs of it — and passing the
    /// package would let this reach for things the extraction pass owns.
    /// </remarks>
    private readonly IReadOnlyDictionary<string, XElement> _footnotes;

    /// <summary>The endnote bodies by <c>w:id</c>.</summary>
    private readonly IReadOnlyDictionary<string, XElement> _endnotes;

    /// <summary>How the document's footnotes are numbered.</summary>
    private readonly NoteNumbering _footnoteNumbering;

    /// <summary>How its endnotes are numbered, which is a separate sequence in a separate format.</summary>
    private readonly NoteNumbering _endnoteNumbering;

    /// <summary>
    /// The numbering one class of note declares in the document's settings, or the class's default.
    /// </summary>
    /// <remarks>
    /// <c>w:footnotePr</c> and <c>w:endnotePr</c> in <c>w:settings</c>, whose <c>w:numStart</c> is the first
    /// note's number outright — one-based, unlike ODF's <c>text:start-value</c>, which is an offset. A
    /// <em>section</em> can carry the same two elements and override the document's; that is not read, and a
    /// document doing it is numbered by the document-wide values instead.
    /// </remarks>
    /// <param name="settings">The <c>w:settings</c> root, or null.</param>
    /// <param name="element">Which of the two elements to read.</param>
    /// <param name="fallback">The class's default, for whatever the file leaves unsaid.</param>
    private static NoteNumbering NumberingIn(
        XElement? settings, string element, NoteNumbering fallback)
    {
        XElement? properties = Word.Child(settings, element);
        if (properties is null) return fallback;

        NoteNumberFormat format =
            NoteNumbering.Parse(Word.Attribute(Word.Child(properties, "numFmt"), "val"))
            ?? fallback.Format;

        int start = int.TryParse(
            Word.Attribute(Word.Child(properties, "numStart"), "val"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int stated)
            ? stated
            : fallback.StartAt;

        // Where the class collects. `w:pos` means different things for the two elements and only the endnote
        // one matters here: a footnote's `beneathText` is still the foot of its page, while an endnote's
        // `sectEnd` moves it into the note area of the section's last page.
        NotePlacement placement = Word.Attribute(Word.Child(properties, "pos"), "val") switch
        {
            "sectEnd" => NotePlacement.SectionEnd,
            "docEnd" => NotePlacement.DocumentEnd,
            _ => fallback.Placement,
        };

        // Where the count begins again. `eachSect` is not a third kind of restart: Writer has no per-section
        // one, so its chapter restart is what OOXML's `eachSect` both exports from and reads back as.
        NoteRestart restart =
            NoteNumbering.ParseRestart(Word.Attribute(Word.Child(properties, "numRestart"), "val"))
            ?? fallback.Restart;

        return new NoteNumbering(format, start) { Placement = placement, Restart = restart };
    }

    /// <summary>The substitutions made while resolving the document's fonts.</summary>
    public IReadOnlyList<FontSubstitution> Substitutions => _fonts.Substitutions;

    /// <summary>
    /// The room Word leaves above a page's notes: the default paragraph style's line height.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>sw::FootnoteSeparatorHeight</c> takes this branch for every document a Word filter opened
    /// (<c>sw/source/core/layout/ftnfrm.cxx</c>:257-272), and its helper is explicit about the source:
    /// <em>"the height of the line that hosts the separator line (the top margin of the container),
    /// based on the default paragraph style"</em>, read as <c>SwFont::GetHeight</c> of that style's
    /// font (<c>:57-77</c>). Not the note's font, and not the body paragraph's — the <em>default
    /// style's</em>, which in a DOCX is what a paragraph naming no <c>w:pStyle</c> resolves to.
    /// </para>
    /// <para>
    /// So it is asked of the same chain and the same faces every other run goes through, and measured
    /// with <see cref="Paperless.Text.Fonts.LineSpacing"/> on the document's own device grid: this has
    /// to be the height a line of that text would be, or it is a different number that happens to be
    /// close.
    /// </para>
    /// <para>
    /// Zero when no face can be read at all, which the paginator reads as "no reservation of this
    /// kind"; a document whose default font is missing has larger problems than its note separator.
    /// </para>
    /// </remarks>
    public Length DefaultParagraphLineHeight
    {
        get
        {
            WordTextStyle text = WordParagraphFormats.ResolveText(_styles, null, _theme, fontTable: _fontTable);
            if (Face(text) is not { } face) return Length.Zero;

            return LineSpacing.Resolve(face, _metrics, WriterLineBox.LeadingAboveText)
                .ScaledLineHeight(text.Size);
        }
    }

    /// <summary>
    /// The document's margin line numbering, or null when it asks for none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Word states this per section and Writer holds one per document, so the <em>last</em>
    /// <c>w:lnNumType</c> in document order is the one that stands: the importer writes each into the
    /// global settings as it meets it (<c>sw/source/writerfilter/dmapper/DomainMapper.cxx</c>:1213 and
    /// :2795), and a section that says nothing does not put the previous one back. A <c>w:countBy</c> of
    /// nought is how a later section turns numbering off, which is the same rule read the other way —
    /// <c>if (aSettings.nInterval == 0) … PROP_IS_ON false</c>.
    /// </para>
    /// <para>
    /// The numbers take the document's <em>default</em> character formatting rather than any paragraph's,
    /// because Writer's <c>Line Numbering</c> character style declares nothing at all; see
    /// <see cref="LineNumbering"/>, where the four probes that establish it are recorded.
    /// </para>
    /// </remarks>
    /// <param name="body">The <c>w:body</c> element.</param>
    public LineNumbering? LineNumbers(XElement body)
    {
        ArgumentNullException.ThrowIfNull(body);

        XElement? stated = body
            .Descendants(Word.Name("sectPr"))
            .Select(section => Word.Child(section, "lnNumType"))
            .LastOrDefault(numbering => numbering is not null);

        if (stated is null) return null;

        if (!Word.Integer(Word.Attribute(stated, "countBy"), out int countBy) || countBy <= 0)
        {
            return null;
        }

        WordProperty size = _styles.ResolveInDocumentDefaults(runProperty: true, "sz");
        WordProperty fonts = _styles.ResolveInDocumentDefaults(runProperty: true, "rFonts");

        Length emSize = size.IntegerValue is { } halfPoints && halfPoints > 0
            ? Length.FromPoints(halfPoints / 2.0)
            : LineNumbering.DefaultEmSize;

        WordTextStyle style = new(
            Word.Attribute(fonts.Element, "ascii"), emSize, Weight: 400, IsItalic: false, Language: null,
            DeclaredClass: WordParagraphFormats.StatedClass(
                fonts.Element is { } element ? [element] : [], _fontTable));

        if (Face(style) is not { } face) return null;

        return new LineNumbering
        {
            Face = face,
            Font = _references.GetValueOrDefault(style.FaceKey),
            EmSize = emSize,
            CountBy = countBy,
            Start = Word.Integer(Word.Attribute(stated, "start"), out int start) && start > 0
                ? start
                : 1,
            Distance = Word.Integer(Word.Attribute(stated, "distance"), out int distance)
                       && distance > 0
                ? Length.FromTwips(distance)
                : LineNumbering.DefaultDistance,

            // `newSection` maps to false beside `continuous`, because Writer has no per-section restart
            // to map it onto — see the importer's own line, cited above.
            RestartsEachPage =
                string.Equals(Word.Attribute(stated, "restart"), "newPage", StringComparison.Ordinal),
        };
    }

    /// <summary>Reads the body's blocks — its paragraphs and its tables — in document order.</summary>
    /// <param name="body">The <c>w:body</c> element.</param>
    public List<PageBlock> Read(XElement body)
    {
        ArgumentNullException.ThrowIfNull(body);

        _sectionIndex = 0;
        _blocksInSection = 0;
        _pendingBelowTarget = -1;

        // The body is where the document's lists start counting. Reset rather than assumed clean,
        // because the numbering may be the same instance the extraction pass already walked.
        _numbering.ResetCounters();

        List<PageBlock> blocks = [];
        Walk(body, blocks, depth: 0);
        ParagraphBorderJoin.Apply(blocks);
        return blocks;
    }

    /// <summary>
    /// Reads a table cell's blocks, tables included.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ReadFlow"/>, which is for a header or a footer, a cell keeps its tables: a table
    /// inside a cell is how a nested table is written, and <see cref="FlowLayouter"/> lays one out. The two
    /// differ only in the list they fill, which is what the generic walk is for.
    /// </remarks>
    /// <param name="element">The cell element.</param>
    public List<PageBlock> ReadCell(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        List<PageBlock> blocks = [];
        Walk(element, blocks, depth: 0);
        DropNestedTableFiller(blocks);
        SuppressAutoSpacingInCell(blocks);
        ParagraphBorderJoin.Apply(blocks);
        return blocks;
    }

    /// <summary>
    /// Drops the empty paragraph OOXML makes mandatory after a nested table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>w:tc</c> may not end with a <c>w:tbl</c>, so Word writes an empty paragraph after every
    /// nested table whether or not the author put one there. LibreOffice does not lay it out, and the
    /// difference is a whole line per nested table — on <c>UG.CAO.00133</c>'s header the row is
    /// 26.35 pt in the reference against our 36.65.
    /// </para>
    /// <para>
    /// <strong>The rule is stated from the reference rather than from the specification</strong>, by
    /// varying what one real cell holds and reading the drawn cell edges back out of both PDFs
    /// (<c>dotnet/probes/words-r44/header-row-mutations.py</c>). Seven variants, one rule fitting all
    /// of them to a tenth of a point:
    /// </para>
    /// <list type="bullet">
    /// <item>table then one empty paragraph — the paragraph is not laid out;</item>
    /// <item>table then a paragraph with text — laid out, so it is emptiness that decides;</item>
    /// <item>table then <em>two</em> empty paragraphs — <strong>both</strong> laid out, because the last
    /// one follows a paragraph rather than a table;</item>
    /// <item>empty paragraph, table, empty paragraph — the leading one is laid out and the trailing one
    /// is not, so it is neither "the cell's first" nor "every empty paragraph".</item>
    /// </list>
    /// <para>
    /// Hence the two conditions together: the <em>last</em> block, and the block before it a table.
    /// Applied in the DOCX reader rather than in the layout because the shape is OOXML's own — an ODF
    /// cell may end with a table, so an empty paragraph after one there is the author's.
    /// </para>
    /// </remarks>
    private static void DropNestedTableFiller(List<PageBlock> blocks)
    {
        if (blocks.Count < 2) return;
        if (blocks[^1] is not PageParagraph last || blocks[^2] is not PageTable) return;
        if (last.Text.Length > 0 || last.Frames.Count > 0 || last.Notes.Count > 0) return;

        blocks.RemoveAt(blocks.Count - 1);
    }

    /// <summary>
    /// Drops the HTML auto margin at a cell's top and bottom edges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>w:beforeAutospacing</c> means fourteen points in a body paragraph and <em>nothing</em> on the
    /// first paragraph of a table cell; <c>w:afterAutospacing</c> likewise on the last. LibreOffice
    /// applies both — the first in <c>DomainMapper_Impl::finishParagraph</c>
    /// (<c>sw/source/writerfilter/dmapper/DomainMapper_Impl.cxx:2458-2470</c>, where
    /// <c>bFirstParagraphInCell</c> at matching table depth forces the margin to zero) and the second in
    /// <c>ClearPreviousParagraph</c> (<c>:5457-5468</c>, called from <c>TableManager::closeCell</c>).
    /// Without it every row of a table whose style carries the flag is fourteen points taller than the
    /// document asks for, which on a form of thirty single-line rows is seven pages of invented height.
    /// </para>
    /// <para>
    /// A <em>stated</em> <c>w:before</c> survives, which is why this asks how the margin was arrived at
    /// rather than merely whether it is fourteen points: the suppression is of the auto rule, not of
    /// paragraph spacing in cells.
    /// </para>
    /// <para>
    /// The bottom rule spares a numbered paragraph, exactly as <c>ClearPreviousParagraph</c> does — it
    /// reads the paragraph's numbering rules and leaves the margin alone when it has any.
    /// </para>
    /// <para>
    /// <b>Not done:</b> the same <c>if</c> in <c>finishParagraph</c> also zeroes the top margin of the
    /// first paragraph of a <em>shape</em> and of the first paragraph of the document's first section.
    /// Both are the same rule and both are unimplemented here, because neither was measured — a cell is
    /// where the corpus showed it, and the other two move the body flow, which is not free to change on
    /// an argument from symmetry alone.
    /// </para>
    /// </remarks>
    private void SuppressAutoSpacingInCell(List<PageBlock> blocks)
    {
        // Only a paragraph at the very edge is affected; a nested table there shields whatever follows,
        // because the rule is about the cell's own first and last paragraph.
        if (blocks.Count > 0 && blocks[0] is PageParagraph first
            && WordParagraphFormats.IsAutoSpaced(
                _styles, Word.Child(first.Source as XElement, "pPr"), _tableStyle, before: true))
        {
            blocks[0] = first with { Format = first.Format with { SpaceBefore = Length.Zero } };
        }

        if (blocks.Count > 0 && blocks[^1] is PageParagraph last
            && last.Label is null
            && WordParagraphFormats.IsAutoSpaced(
                _styles, Word.Child(last.Source as XElement, "pPr"), _tableStyle, before: false))
        {
            blocks[^1] = last with { Format = last.Format with { SpaceAfter = Length.Zero } };
        }
    }

    /// <summary>
    /// Reads a flow's blocks: a header's or a footer's.
    /// </summary>
    /// <remarks>
    /// The same walk a cell takes, tables included, because a table is how a two-part running head is usually
    /// laid out — one cell hard left, another hard right — and <see cref="FlowLayouter"/> places one either
    /// way. Dropping the table instead is not the harmless simplification it looks like: its paragraphs would
    /// stack as loose lines, giving the header a height no table has and pushing the body text down by the
    /// difference on every page.
    /// </remarks>
    /// <param name="element">The element whose block-level children to read.</param>
    public List<PageBlock> ReadFlow(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        // Each flow numbers its own lists: a numbered paragraph in a footer does not continue the
        // body's count, which is the same rule the extraction reader applies between flows.
        _numbering.ResetCounters();

        List<PageBlock> blocks = [];
        Walk(element, blocks, depth: 0);
        ParagraphBorderJoin.Apply(blocks);
        return blocks;
    }

    /// <summary>
    /// Which section the walk is in, advanced by each paragraph that closes one.
    /// </summary>
    /// <remarks>
    /// A field rather than a walk parameter because the walk recurses through content controls and tracked
    /// insertions, and a section can end inside one — so the count has to survive returning from a nested
    /// call rather than being restored with it.
    /// </remarks>
    private int _sectionIndex;

    /// <summary>
    /// How many blocks the current section has already contributed, reset when a section closes.
    /// </summary>
    /// <remarks>
    /// Only <see cref="IsSectionMarkOnly"/> reads it, and only to answer "is this section mark the whole
    /// section". Counted for every paragraph and table the walk passes rather than for the ones it keeps,
    /// which is what Writer's <c>bIsFirstParaInSection</c> counts — a paragraph the reader could not
    /// resolve a face for still separates the section mark from the start of its section.
    /// </remarks>
    private int _blocksInSection;

    /// <summary>
    /// Whether a paragraph that closes a section is nothing but the section mark, and so is not laid out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Word stores a section break as a paragraph mark carrying <c>w:sectPr</c>, and that mark is not a
    /// paragraph: it takes no line and no spacing. Writer's DOCX importer says so directly — "if the
    /// paragraph contains only the section properties and it has no runs, we should not create a paragraph
    /// for it in Writer, unless that would remove the whole section"
    /// (<c>writerfilter/dmapper/DomainMapper.cxx</c>:4840, the <c>bRemove</c> expression) — and it is the
    /// <c>!bSingleParagraphAfterRedline</c> term there that spells the exception: a mark that is both the
    /// first and the last paragraph of its section is kept, because dropping it would leave the section
    /// with no content to hang a page on.
    /// </para>
    /// <para>
    /// Measured on <c>easa-form-1.docx</c>, whose first section ends with an ordinary empty paragraph and
    /// then a section mark: laying the mark out overflowed the page by one line and produced a sixth page
    /// carrying nothing but the section's footer. LibreOffice's own flat-ODF export of that document holds
    /// one empty paragraph where the DOCX has two, which is this rule visible in its output.
    /// </para>
    /// <para>
    /// "Nothing but the mark" is read from what the paragraph produced rather than from its markup, so a
    /// mark that anchors a frame or cites a note keeps its paragraph: each of those is content the page
    /// would otherwise lose, and Writer guards them individually (<c>HasTopAnchoredObjects</c>,
    /// <c>IsParaWithInlineObject</c>).
    /// </para>
    /// <para>
    /// <strong>A page break does not save the mark, and used to.</strong> The guard list in
    /// <c>bRemove</c> protects a <em>column</em> break — <c>bIsColumnBreak</c>, built from
    /// <c>BreakType_COLUMN_BEFORE</c>/<c>_AFTER</c>/<c>_BOTH</c> a dozen lines above it — and names no
    /// page break at all, so a mark carrying one is removed and the break dies with the paragraph it was
    /// attached to. Measured on the installed 26.2.4.2 by four authored routes onto the same flag, each a
    /// three-paragraph section ending in an empty mark followed by a landscape section: a preceding
    /// <c>&lt;w:br w:type="page"/&gt;</c>, two of them, a <c>w:pageBreakBefore</c> on the mark itself, and
    /// the same on a section already filling its page. LibreOffice emits one portrait page for the first
    /// three and two for the fourth; taking the break at its word emits one more in every case.
    /// </para>
    /// <para>
    /// The corpus case is <c>1_tpr_template__from_fy14_.docx</c>, whose first section ends with a
    /// page-break paragraph and then an empty mark. Its first two pages are word for word and line for
    /// line the reference's; page three held one word, the footer's page number, and pushed the landscape
    /// section that follows onto page four. Nine of the corpus's 200 documents change with this rule.
    /// </para>
    /// <para>
    /// Column breaks are not guarded here because the reader does not model one: only
    /// <c>w:br w:type="page"</c> reaches <see cref="ParagraphFormat.StartsNewPage"/>, so the flag this
    /// test used to read can never have meant a column break. That is a real gap in the other direction —
    /// 26.2.4.2 keeps a mark carrying a column break and gives it a page, and we drop it — and it is
    /// recorded in <c>dotnet/probes/words-e-01/results.md</c> rather than guessed at here.
    /// </para>
    /// </remarks>
    private static bool IsSectionMarkOnly(PageParagraph paragraph)
        => paragraph.Text.Length == 0
           && paragraph.Frames.Count == 0
           && paragraph.Notes.Count == 0;

    /// <summary>
    /// The break each section begins with, indexed as <see cref="_sectionIndex"/> counts them.
    /// </summary>
    /// <remarks>
    /// Only <see cref="HandOnBelowSpacing{T}"/> reads it, and only to tell a continuous break from a
    /// page-starting one. Empty when the caller states nothing, which reads every section as
    /// page-starting — the case the rule was written for, and the one a hand-built source is testing.
    /// </remarks>
    public IReadOnlyList<SectionBreak> SectionBreaks { get; init; } = [];

    /// <summary>
    /// The space-after of a dropped section mark, waiting for the section after it to claim it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A section mark is not laid out — see <see cref="IsSectionMarkOnly"/> — but the space-after it
    /// declares is still the space that ends its section, and Word consolidates it against the space-before
    /// of whatever opens the next one. Writer reproduces that by moving the value onto the last real
    /// paragraph of the closing section before discarding the mark:
    /// <c>DomainMapper_Impl::handleSectPrBeforeRemoval</c> stashes it
    /// (<c>writerfilter/dmapper/DomainMapper_Impl.cxx</c>:10487) and
    /// <c>SectionPropertyMap::EmulateSectPrBelowSpacing</c> writes it to the paragraph before the break
    /// (<c>PropertyMap.cxx</c>:1576), whose own comment is "MS Word is excessively consistent about
    /// consolidating paragraph top and bottom spacing. They even consolidate spacing between section
    /// breaks!"
    /// </para>
    /// <para>
    /// It <em>replaces</em> the previous paragraph's own space-after rather than adding to it, which
    /// Writer justifies with "below spacing before a page break normally has no relevance". Both of
    /// Writer's exclusions are reproduced: a table on either side of the break takes no hand-on, because
    /// a table has neither of the two spacings to consolidate; and a continuous break hands nothing on
    /// unless the paragraph opening the section carries a page break of its own, because without one the
    /// mark's space-after never reaches the layout at all.
    /// </para>
    /// <para>
    /// Measured on <c>03_Technical_Report_(progress)_template.docx</c>, whose landscape section opens with
    /// an 18 pt space-before heading after a mark declaring 6 pt: the reference puts that heading's
    /// baseline 12 pt below where a probe with the space-before removed puts it, which is the consolidated
    /// 18 − 6 and not the 18 the same document's earlier section — whose mark declares no space-after at
    /// all — moves by. Without this the two sections both moved by 18 and the document paginated to
    /// eleven pages against the reference's ten.
    /// </para>
    /// </remarks>
    private Length _pendingBelowSpacing;

    /// <summary>Which block the pending space-after belongs on, or −1 when nothing is pending.</summary>
    private int _pendingBelowTarget = -1;

    /// <summary>
    /// Gives a dropped section mark's space-after to the last paragraph of the section it closed.
    /// </summary>
    /// <param name="into">The blocks read so far.</param>
    /// <param name="startsNewPage">
    /// Whether the paragraph claiming it carries a page break, which is what lets a continuous break hand
    /// anything on.
    /// </param>
    private void HandOnBelowSpacing<T>(List<T> into, bool startsNewPage)
        where T : PageBlock
    {
        int target = _pendingBelowTarget;
        _pendingBelowTarget = -1;

        if (target < 0 || target >= into.Count) return;

        // `_sectionIndex` has already advanced past the mark, so it names the section being opened.
        if (!startsNewPage
            && _sectionIndex < SectionBreaks.Count
            && SectionBreaks[_sectionIndex] == SectionBreak.Continuous)
        {
            return;
        }

        // A table before the break has no space-after to replace, which is Writer's other `FindTableNode`.
        if (into[target] is not PageParagraph last) return;

        if (last with { Format = last.DeclaredFormat with { SpaceAfter = _pendingBelowSpacing } } is T
            replaced)
        {
            into[target] = replaced;
        }
    }

    /// <summary>
    /// How many tables enclose the one being read, counted while its rows are walked.
    /// </summary>
    /// <remarks>
    /// A field for the same reason <see cref="_sectionIndex"/> is one: a cell's blocks are read by the walk
    /// that reads a paragraph's, so a nested table is found several calls deep rather than through a
    /// parameter somebody could pass wrongly. Only the table's own left edge depends on it.
    /// </remarks>
    private int _tableDepth;

    /// <summary>
    /// The <c>w:pPr</c> chain of the table style enclosing the paragraph being read, or null in the body.
    /// </summary>
    /// <remarks>
    /// A field rather than a parameter because a cell's content is read by the same recursive walk the
    /// body uses, and threading it through every overload would touch a dozen signatures for one value
    /// that changes only when a table is entered. Saved and restored around each table, so a nested table
    /// takes its own style and the outer one resumes after it.
    /// </remarks>
    private IReadOnlyList<XElement>? _tableStyle;

    /// <summary>
    /// The table style's <c>w:rPr</c> layers for the cell being read, most specific first, or null in
    /// the body.
    /// </summary>
    /// <remarks>
    /// The companion of <see cref="_tableStyle"/> and a field for the same reason, but it changes per
    /// <em>cell</em> rather than per table: conditional formatting is a property of where the cell sits.
    /// See <see cref="WordTableStyleConditions"/>.
    /// </remarks>
    private IReadOnlyList<XElement>? _tableStyleRun;

    /// <summary>
    /// Walks the body's block-level children.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>w:sdt</c> — a structured-document tag, which is what a content control is — wraps ordinary
    /// content inside a <c>w:sdtContent</c>, so a walk that stopped at it would lose every paragraph in
    /// a form.
    /// </para>
    /// <para>
    /// Generic in what it fills, which is how one walk serves both the body and a flow. A body takes
    /// <see cref="PageBlock"/> and so keeps the tables; a header, a footer or a cell takes
    /// <see cref="PageParagraph"/>, and a table simply does not fit in the list — so it is dropped by the
    /// type rather than by a flag that could be passed the wrong way round.
    /// </para>
    /// </remarks>
    private void Walk<T>(XElement element, List<T> into, int depth)
        where T : PageBlock
    {
        if (depth > 64 || into.Count >= MaxParagraphs) return;

        foreach (XElement child in element.Elements())
        {
            if (into.Count >= MaxParagraphs) return;

            if (Word.Is(child, "p"))
            {
                bool endsSection = Word.Child(Word.Child(child, "pPr"), "sectPr") is not null;

                // Read it either way: even a paragraph that is only a section mark can leave a page break
                // behind for the paragraph after it, and that bookkeeping lives in `Paragraph`.
                if (Paragraph(child) is { } paragraph)
                {
                    if (endsSection && _blocksInSection > 0 && IsSectionMarkOnly(paragraph))
                    {
                        // Dropped — but its space-after is not dropped with it. See BelowSpacing.
                        _pendingBelowSpacing = paragraph.DeclaredFormat.SpaceAfter;
                        _pendingBelowTarget = into.Count - 1;
                    }
                    else if (paragraph is T block)
                    {
                        HandOnBelowSpacing(into, paragraph.Format.StartsNewPage);
                        into.Add(block);
                    }
                }

                _blocksInSection++;

                // A DOCX states a section's properties at its *end*: the w:sectPr inside a paragraph's
                // properties closes the section that paragraph finishes. So the counter advances after the
                // paragraph, which is what puts that paragraph in the section it ends rather than the next.
                if (endsSection)
                {
                    _sectionIndex++;
                    _blocksInSection = 0;
                }

                continue;
            }

            if (Word.Is(child, "tbl"))
            {
                // A section that starts with a table takes no hand-on: a table has no space-above for the
                // mark's space-after to consolidate against, which is the `FindTableNode` arm of
                // `SectionPropertyMap::EmulateSectPrBelowSpacing`.
                _pendingBelowTarget = -1;

                if (Table(child) is { } table && table is T grid) into.Add(grid);
                _blocksInSection++;
                continue;
            }

            if (Word.Is(child, "sdt") || Word.Is(child, "sdtContent"))
            {
                Walk(child, into, depth + 1);
            }
        }
    }

    /// <summary>
    /// Reads one paragraph, with an optional prefix its own text does not contain.
    /// </summary>
    /// <param name="element">The <c>w:p</c>.</param>
    /// <param name="citation">
    /// The number a <c>w:footnoteRef</c> in this paragraph stands for, or null when it is not a note's. The
    /// number is not in the file: Word marks the place and counts the notes itself.
    /// </param>
    private PageParagraph? Paragraph(XElement element, string? citation = null)
    {
        XElement? properties = Word.Child(element, "pPr");

        // Two character styles, because `w:pPr/w:rPr` is the *paragraph mark's* formatting and not
        // the paragraph's. ECMA-376 names it "Run Properties for the Paragraph Mark", Word applies
        // it to the pilcrow, and LibreOffice agrees: its flat-ODF export puts it on
        // `loext:marker-style-name` and leaves the text in the paragraph style. Measured on an
        // authored probe against LibreOffice 24.2.7.2 — a bold style whose mark says
        // `<w:b w:val="0"/>` still draws its text in Liberation Sans Bold, and an unstyled
        // paragraph whose mark says `<w:b/><w:sz w:val="48"/>` still draws 10 pt upright.
        //
        // `mark` is not dead weight: an empty paragraph has nothing *but* its mark, and its height
        // is the mark's. Same probe: the mark alone carrying `w:sz w:val="72"` gives the empty
        // paragraph 36 pt of height in the reference.
        WordTextStyle mark =
            WordParagraphFormats.ResolveText(_styles, properties, _theme, _tableStyleRun, _fontTable);
        WordTextStyle body =
            WordParagraphFormats.ResolveRun(_styles, properties, null, _theme, _tableStyleRun, _fontTable);

        // Both are resolved, not only the one this paragraph draws its text in, because `Face` is
        // also what fills `_references` — and a `FontReference` is the only thing a PDF can turn
        // back into an *embedded* font program. Resolving just the body's face left the list label,
        // which takes the mark's style, with no reference to be embedded through: nine documents
        // went from `match` to `unembedded` on the corpus sweep with the layout otherwise identical.
        OpenTypeFace? face = Face(body);
        OpenTypeFace? markFace = Face(mark);
        if (face is null) return null;

        // Taken before the walk and put back after it, because the walk can set a *new* one. What is
        // read here was left by the paragraph before this one; what the walk leaves belongs to the
        // paragraph after.
        bool breaksPage = _pageBreakPending;
        _pageBreakPending = false;

        RunWalker walker = new(
            CitationOf, Symbol, _constants, _footnoteNumber, _endnoteNumber, StyleReferenceText);
        walker.Walk(element, citation);

        // Where the note's own number landed, for a renumbering pass that has to find it again. A field
        // rather than an out parameter because this method reads an ordinary paragraph and a note's first
        // paragraph alike, and only the call that supplied a citation can have produced one.
        if (citation is not null) _noteCitationOffset = walker.CitationOffset;

        // Notes are numbered across the document, so the counters advance by however many this paragraph
        // referenced — and the bodies are read after the walk, since reading one recurses into this method
        // and would otherwise renumber from the middle of the paragraph that references it.
        _footnoteNumber += walker.FootnotesSeen;
        _endnoteNumber += walker.EndnotesSeen;

        ParagraphFormat format =
            WordParagraphFormats.Resolve(
                _styles, properties, _defaultTabInterval, _autoSpacing, _tableStyle,
                _compatibilityMode >= 15);

        // After the walk, because reading a note body or a text box re-enters this method and a list
        // counter advanced from inside a nested flow would number the paragraph after it wrongly.
        //
        // The mark's style rather than the body's, because a list label takes the formatting of the
        // paragraph mark — which is what `w:pPr/w:rPr` is for, and the one place it is visible.
        (PageLabel? label, format) = ListFormatting(properties, format, mark, markFace ?? face);

        // The runs first, then the text they map: `Apply` rewrites both together, and the offsets it
        // preserves are the ones the notes and frames below were recorded against.
        List<PageRun> runs = RunsOf(walker.Ranges, properties, body, face);
        string mapped = CaseMapping.Apply(walker.Text, runs);

        // A paragraph with nothing in it is its mark, so that is what sizes it; one with text is
        // sized by the text, and its mark formats a pilcrow nobody draws.
        bool empty = walker.Text.Length == 0 || HoldsOnlyFloatingFrames(walker);
        WordTextStyle text = empty ? mark : body;
        if (empty) face = markFace ?? face;

        PageParagraph read = new()
        {
            SectionIndex = _sectionIndex,
            Text = mapped,
            Face = face,
            Font = _references.GetValueOrDefault(text.FaceKey),
            Colour = text.Colour ?? Colour.Black,
            Shading = ShadeColour(WordParagraphFormats.ShadingOf(_styles, properties)),
            Borders = ParagraphBorders(properties),
            Format = breaksPage || walker.BreaksPageHere
                ? format with { StartsNewPage = true }
                : format,
            Label = label,

            // #i3952#: a tab or a run of spaces does not raise a line's height in a Word document, and a
            // DOCX imports with the setting on. See PageParagraph.BlanksAreTransparentToHeight.
            BlanksAreTransparentToHeight = true,

            // A toggle in the schema, so `<w:suppressLineNumbers/>` and `w:val="1"` both mean on and
            // `w:val="0"` — which a style commonly writes to undo an inherited one — means off.
            SuppressesLineNumbers = _styles
                .ResolveParagraphProperty(
                    "suppressLineNumbers",
                    properties,
                    Word.Value(properties, "pStyle") ?? _styles.DefaultStyleId(WordStyleType.Paragraph))
                .IsOn,
            Metrics = _metrics,
            Fallback = _fonts,

            // Word's "add space between Asian and Western text". See ScriptSpacing; the DOC reader
            // sets it for the same reason and the ODF one does not.
            AddsScriptSpace = true,
            EmSize = text.Size,
            Language = text.Language,
            Shaping = new ShapingOptions(
                Language: text.Language, DisableKerning: !text.AutoKerning),
            Tracking = text.Tracking,
            Runs = runs,
            Fields = walker.Fields,
            Notes = NotesOf(walker.Notes),
            Frames = FramesOf(walker.Frames),
            Source = element,
        };

        // After the note bodies and the text boxes above, which recurse into this method and share the
        // field. Writer ignores a page break inside either — `DomainMapper.cxx:4376` applies a deferred
        // one only when it is not in a footnote, a shape or a comment — and overwriting here is what
        // makes that true here too: whatever a nested flow left behind is replaced by this paragraph's
        // own answer, so a break inside a caption cannot push the paragraph after the caption's frame.
        _pageBreakPending = walker.BreaksPage;

        // After the walk, so a STYLEREF in *this* paragraph still quotes the one before it. Word's own
        // search runs backwards from the field, and a heading whose own text is what a caption inside it
        // would quote does not arise.
        if (mapped.Length > 0 && Word.Value(properties, "pStyle") is { Length: > 0 } styled)
        {
            _styleText[styled] = mapped;
        }

        return read;
    }

    /// <summary>
    /// The text last read in each paragraph style, which is what a <c>STYLEREF</c> quotes.
    /// </summary>
    /// <remarks>
    /// The most recent one rather than all of them, because the search is backwards from the field —
    /// <c>SwGetRefFieldType::FindAnchorRefStyleOther</c> scans from the field's own node towards the
    /// start and takes the first match. Keeping the last text seen per style answers that in one lookup
    /// and costs one dictionary entry per style the document actually uses.
    /// </remarks>
    private readonly Dictionary<string, string> _styleText = [];

    /// <summary>
    /// What a <c>STYLEREF</c> naming a style quotes, or null when nothing in that style has been read.
    /// </summary>
    /// <remarks>
    /// The name a field states is a style <em>name</em> — or Word's undocumented bare digit for a
    /// built-in heading level — while a paragraph names a style <em>id</em>, so the two are matched
    /// through the style table rather than directly. Null when the style is unknown or nothing precedes
    /// the field in it, which leaves the producer's cached result in place: a wrong substitution is
    /// worse than a stale one.
    /// </remarks>
    /// <param name="name">The style the field named.</param>
    private string? StyleReferenceText(string name)
    {
        if (_styleText.Count == 0) return null;
        if (_styleText.TryGetValue(name, out string? byId)) return byId;

        // "1" through "9" mean the built-in heading of that level, which is a style whose *name* is
        // "heading N" whatever the document calls its id.
        string wanted = name.Length == 1 && name[0] is >= '1' and <= '9'
            ? "heading " + name
            : name;

        foreach (WordStyle style in _styles.All)
        {
            if (style.Type != WordStyleType.Paragraph) continue;
            if (!string.Equals(style.Name, wanted, StringComparison.OrdinalIgnoreCase)) continue;
            if (_styleText.TryGetValue(style.StyleId, out string? byName)) return byName;
        }

        return null;
    }

    /// <summary>Whether the paragraph read next begins a page, because the one before ended with a break.</summary>
    private bool _pageBreakPending;

    /// <summary>How many footnotes the walk has passed, counted across the document.</summary>
    private int _footnoteNumber;

    /// <summary>
    /// Where the last note body's own citation was emitted, or −1 when it emitted none.
    /// </summary>
    /// <remarks>
    /// A DOCX marks the place: a <c>w:footnoteRef</c> in the note's first paragraph, which a note beginning
    /// with a tab puts at one rather than at nought. Recorded so that a renumbering pass can rewrite the
    /// number at the head of the note as well as the one in the sentence.
    /// </remarks>
    private int _noteCitationOffset = -1;

    /// <summary>
    /// The number the next endnote is cited by, counted separately from the footnotes.
    /// </summary>
    /// <remarks>
    /// Its own counter because the two sequences are independent — a document with two footnotes and two
    /// endnotes cites 1, 2, i and ii, not 1, 2, iii and iv — and because they are formatted differently.
    /// </remarks>
    private int _endnoteNumber;

    /// <summary>
    /// Reads each referenced note's body from the document's notes part.
    /// </summary>
    /// <remarks>
    /// By <c>w:id</c>, which is what a DOCX gives instead of putting the body at the reference: the note
    /// lives in <c>footnotes.xml</c> and the sentence holds only its number. The citation is placed at the
    /// head of the note's first paragraph, which is where Word draws it and where the part does not have it.
    /// </remarks>
    private List<PageNote> NotesOf(List<NoteAnchor> anchors)
    {
        if (anchors.Count == 0) return [];

        List<PageNote> notes = new(anchors.Count);

        foreach (NoteAnchor anchor in anchors)
        {
            if (anchor.Id is null) continue;

            IReadOnlyDictionary<string, XElement> part =
                anchor.IsEndnote ? _endnotes : _footnotes;

            if (!part.TryGetValue(anchor.Id, out XElement? body)) continue;

            List<PageBlock> blocks = ReadNoteBody(body, anchor.Citation, out int bodyOffset);
            if (blocks.Count == 0) continue;

            notes.Add(new PageNote
            {
                Blocks = blocks,
                Offset = anchor.Offset,
                IsEndnote = anchor.IsEndnote,
                Placement =
                    (anchor.IsEndnote ? _endnoteNumbering : _footnoteNumbering).Placement,
                Restart = (anchor.IsEndnote ? _endnoteNumbering : _footnoteNumbering).Restart,
                Numbering = anchor.IsEndnote ? _endnoteNumbering : _footnoteNumbering,
                Citation = anchor.Citation,
                BodyOffset = bodyOffset,
            });
        }

        return notes;
    }

    /// <summary>
    /// Reads a note's body, putting the citation at the head of its first paragraph.
    /// </summary>
    /// <remarks>
    /// Its own walk rather than <see cref="ReadCell"/>'s, only because the first paragraph takes the
    /// citation and the rest do not — everything else about it is the same, tables included.
    /// </remarks>
    private List<PageBlock> ReadNoteBody(XElement body, string citation, out int citationOffset)
    {
        List<PageBlock> blocks = [];
        bool first = true;

        _noteCitationOffset = -1;

        foreach (XElement child in body.Elements())
        {
            if (Word.Is(child, "p"))
            {
                PageParagraph? paragraph = first ? Paragraph(child, citation) : Paragraph(child);

                if (paragraph is not null)
                {
                    blocks.Add(paragraph);
                    first = false;
                }

                continue;
            }

            Walk(child, blocks, depth: 0);
        }

        // Nought when the part marks no place for its number, which is what a note whose first paragraph
        // holds no `w:footnoteRef` is: the citation was never emitted and there is nothing to rewrite.
        citationOffset = Math.Max(0, _noteCitationOffset);
        ParagraphBorderJoin.Apply(blocks);
        return blocks;
    }

    /// <summary>
    /// The paragraph's runs, or nothing when every one of them is the paragraph's own formatting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returning an empty list for a uniform paragraph is not only an optimisation: it puts plain prose
    /// back on the single-face path, which shapes the whole paragraph in one call. A run boundary also
    /// breaks shaping context, so a paragraph split into runs it does not need loses a kern pair at each
    /// boundary and measures very slightly wide — and a DOCX splits runs for reasons that have nothing to
    /// do with formatting, a spell-check marker or a revision id being enough.
    /// </para>
    /// <para>
    /// A range whose font cannot be loaded falls back to the paragraph's face rather than being dropped:
    /// its text is still part of the paragraph, and losing it would silently shorten the document.
    /// </para>
    /// </remarks>
    private List<PageRun> RunsOf(
        IReadOnlyList<StyledRange> ranges,
        XElement? paragraphProperties,
        WordTextStyle paragraph,
        OpenTypeFace paragraphFace)
    {
        List<PageRun> runs = new(ranges.Count);
        bool varies = false;

        foreach (StyledRange range in ranges)
        {
            WordTextStyle style = range.RunProperties is null
                ? paragraph
                : WordParagraphFormats.ResolveRun(
                    _styles, paragraphProperties, range.RunProperties, _theme, _tableStyleRun,
                    _fontTable);

            if (range.IsCitation) style = AsCitation(style);

            // A `w:sym` names its own face for one character, and it was resolved when the character was
            // chosen. Everything else about the run — its size, its colour, its escapement — still comes
            // from the run, so only the face is taken from the symbol.
            OpenTypeFace face = range.Symbol?.Face ?? Face(style) ?? paragraphFace;

            // The escapement is resolved here rather than where it was read, because its rise is a fraction
            // of the face's height and the face is only known now.
            Length size = style.Escapement.SizeOf(style.Size);
            Length rise = style.Escapement.RiseOf(face, style.Size);

            if (face != paragraphFace
                // A symbol's face is its own even when it happens to equal the paragraph's: losing the
                // runs here would draw its code point out of whatever the paragraph is set in, which for
                // a Private Use Area slot is .notdef.
                || range.Symbol is not null
                || size != paragraph.Size
                || style.Colour != paragraph.Colour
                || style.Language != paragraph.Language
                || rise != Length.Zero
                // A case map has to survive the uniform-paragraph shortcut: it is the one property here
                // that changes the *characters*, so dropping the runs would draw the text as stored.
                || style.CaseMap != PageCaseMap.None
                // So does a highlight: the paragraph carries none of its own, so a paragraph highlighted
                // end to end is uniform by every other test and would lose its band entirely.
                || style.Highlight is not null
                // And so do the two rules, for the same reason: neither changes a width, so a paragraph
                // underlined end to end is uniform by every measurement test and would be drawn plain.
                || style.IsUnderlined
                || style.IsStruckThrough
                // Kerning, unlike the two rules, does change a measurement — so a run that kerns
                // inside a paragraph that does not has to survive the shortcut or its width is the
                // paragraph's answer rather than its own.
                || style.AutoKerning != paragraph.AutoKerning
                // And tracking, for the same reason and more sharply: it is a distance per character,
                // so a run that disagrees with its paragraph mark is wrong by its own length.
                || style.Tracking != paragraph.Tracking)
            {
                varies = true;
            }

            runs.Add(new PageRun(
                range.Start,
                range.Length,
                face,
                size,
                range.Symbol is { } symbol ? symbol.Font : _references.GetValueOrDefault(style.FaceKey),
                style.Colour ?? paragraph.Colour ?? Colour.Black,
                new ShapingOptions(Language: style.Language, DisableKerning: !style.AutoKerning),
                rise,
                style.CaseMap,
                Highlight: style.Highlight ?? default,
                IsUnderlined: style.IsUnderlined,
                IsStruckThrough: style.IsStruckThrough,
                Tracking: style.Tracking));
        }

        return varies ? runs : [];
    }

    /// <summary>
    /// A stretch of a paragraph's text and the run properties in force over it.
    /// </summary>
    /// <param name="Start">Its first character, as an index into the paragraph's text.</param>
    /// <param name="Length">How many characters it covers.</param>
    /// <param name="RunProperties">
    /// The enclosing <c>w:r</c>'s <c>w:rPr</c>, or null when the run states none — in which case the
    /// paragraph mark's own formatting applies.
    /// </param>
    /// <param name="IsCitation">
    /// True for a note's citation, which Word draws superscript whether the run says so or not.
    /// </param>
    /// <param name="Symbol">
    /// The face a <c>w:sym</c> resolved to, or null for ordinary text. Carried already resolved because
    /// the same decision picks the character: a slot recoded into OpenSymbol is a different code point
    /// from the one the file states, so the face cannot be chosen after the text is built.
    /// </param>
    private readonly record struct StyledRange(
        int Start,
        int Length,
        XElement? RunProperties,
        bool IsCitation = false,
        (OpenTypeFace Face, FontReference? Font)? Symbol = null);

    /// <summary>A note found while walking a paragraph, before its body has been read.</summary>
    /// <param name="Offset">Where its citation sits in the paragraph's text.</param>
    /// <param name="Id">The <c>w:id</c> naming its body in the notes part.</param>
    /// <param name="IsEndnote">True for an endnote, whose body lives in a different part.</param>
    /// <param name="Citation">The number it is cited by, counted rather than read, and already formatted.</param>
    private readonly record struct NoteAnchor(int Offset, string? Id, bool IsEndnote, string Citation);

    /// <summary>One floating frame in a paragraph, with the character offset it is anchored at.</summary>
    private readonly record struct FrameAnchor(int Offset, XElement Element);

    /// <summary>
    /// Whether the paragraph produced nothing but the anchor characters of <em>floating</em> drawings —
    /// in which case it is an empty paragraph, and its mark is what sizes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk emits one <see cref="AnchorCharacter"/> per drawing, floating or inline, because an
    /// offset has to mean the same thing wherever it was counted. That is right for the frame list and
    /// wrong for the height: a <c>wp:inline</c> genuinely occupies its line, a <c>wp:anchor</c> does not
    /// — Writer's import puts it in a fly and the paragraph it was written in is left empty, so the
    /// paragraph's height is its mark's. Reading the anchor character as text instead takes the *body*
    /// style, and where the mark states a smaller size than the document default the paragraph comes out
    /// several times too tall.
    /// </para>
    /// <para>
    /// Deliberately strict on all three counts, because the failure mode of getting this wrong is a
    /// paragraph that silently loses height:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>every character must be the anchor character, so a caption beside a picture
    ///   still measures its text;</description></item>
    ///   <item><description>there must be exactly one frame anchor per character, so a field result, a
    ///   note citation or a <c>w:commentReference</c> — which emit the same character and register no
    ///   frame — keeps the paragraph text-bearing;</description></item>
    ///   <item><description>and every anchor must be a floating <c>w:drawing</c>. A <c>w:pict</c> or
    ///   <c>w:object</c> is excluded even when it floats: <c>DocxVmlFrames</c> already returns nothing
    ///   for a floating VML shape, so its anchor character stands for something this reader never sized
    ///   in the first place, and changing it would be a second rule with no measurement behind
    ///   it.</description></item>
    /// </list>
    /// <para>
    /// Reach over the corpus: 37 of 271 DOCX-family documents hold at least one such paragraph, 17 of
    /// them with an explicit <c>w:pPr/w:rPr/w:sz</c> on it. Only those where the mark resolves to a
    /// different height from the body can move at all.
    /// </para>
    /// </remarks>
    private static bool HoldsOnlyFloatingFrames(RunWalker walker)
    {
        string text = walker.Text;
        if (text.Length == 0 || walker.Frames.Count != text.Length) return false;

        foreach (char character in text)
        {
            if (character != AnchorCharacter) return false;
        }

        foreach (FrameAnchor anchor in walker.Frames)
        {
            if (anchor.Element.Name.LocalName != "drawing") return false;
            if (!DocxFrames.IsFloating(anchor.Element)) return false;
        }

        return true;
    }

    /// <summary>
    /// How deeply a frame's own text may hold further frames before the innermost is dropped.
    /// </summary>
    /// <remarks>
    /// A guard on untrusted input: a text frame holds paragraphs, a paragraph holds drawings, and a file
    /// claiming a hundred levels would read the same walk a hundred deep. Real documents nest one.
    /// </remarks>
    private const int MaxFrameNesting = 8;

    /// <summary>How many frames enclose the paragraph currently being read.</summary>
    private int _frameDepth;

    /// <summary>
    /// Whether the walk is inside a header or a footer part.
    /// </summary>
    /// <remarks>
    /// Set by the reader around its <see cref="ReadFlow"/> call and restored afterwards, the same shape
    /// as <c>DocxPictures.Scope</c> beside it and for the same reason: the part is what knows, and the
    /// walk cannot tell — a header's body is an ordinary block sequence. It decides one thing, the paint
    /// order of the drawings it anchors (<see cref="PageFrame.BehindText"/>), so a caller that forgets
    /// to set it gets the body's answer rather than a wrong one.
    /// </remarks>
    public bool InHeaderFooter { get; set; }

    /// <summary>
    /// Reads the frames a paragraph anchors, with their own text laid out inside them.
    /// </summary>
    /// <remarks>
    /// A frame's content goes through <see cref="ReadFlow"/> — the same walk a header takes — so a frame
    /// containing a table or a list needs nothing of its own. The reader therefore re-enters itself, which
    /// is why the depth is counted.
    /// </remarks>
    private List<PageFrame> FramesOf(List<FrameAnchor> anchors)
    {
        if (anchors.Count == 0) return [];

        List<PageFrame> frames = [];

        foreach (FrameAnchor anchor in anchors)
        {
            Func<XElement, IReadOnlyList<PageBlock>>? content =
                _frameDepth < MaxFrameNesting ? Content : null;

            // VML states its geometry differently from DrawingML and most of it reserves nothing, so
            // it has a reader of its own rather than a branch inside `DocxFrames`.
            if (anchor.Element.Name.LocalName is "pict" or "object")
            {
                frames.AddRange(
                    DocxVmlFrames.ReadAll(anchor.Element, anchor.Offset, Pictures, content));
                continue;
            }

            frames.AddRange(DocxFrames.ReadAll(
                anchor.Element, content, anchor.Offset, Pictures,
                new DocxFrameContext(_theme, InHeaderFooter, _compatibilityMode)));
        }

        return frames;

        IReadOnlyList<PageBlock> Content(XElement box)
        {
            _frameDepth++;
            try
            {
                return ReadFlow(box);
            }
            finally
            {
                _frameDepth--;
            }
        }
    }

    /// <summary>
    /// Walks a paragraph, building the text as laid out and the ranges its runs divide it into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things have to be got right or the measurement is of the wrong string. A <c>w:del</c> holds
    /// text a tracked change removed, and it is still in the file — measuring it lays out words the
    /// document does not show. And a field's instruction lives in the same run sequence as its result,
    /// bracketed by <c>w:fldChar</c> markers, so a reader that takes every <c>w:t</c> lays out
    /// <c>PAGE \* Arabic</c> in the middle of a sentence.
    /// </para>
    /// <para>
    /// <c>w:tab</c> and <c>w:br</c> are elements rather than characters, as in ODF, and dropping them
    /// silently closes up the space they occupy.
    /// </para>
    /// <para>
    /// The ranges come from the same walk rather than from a second pass, because they are offsets into
    /// that text and the text is not a concatenation of the paragraph's <c>w:t</c> values — every tab,
    /// break and anchor shifts everything after it, and every skipped deletion shifts it back.
    /// </para>
    /// </remarks>
    private sealed class RunWalker
    {
        /// <summary>Creates a walker.</summary>
        /// <param name="citation">How a note of a class and an index is cited.</param>
        /// <param name="footnote">
        /// How many footnotes came before this paragraph. Passed in because notes are numbered across the
        /// document rather than within a paragraph, so the counters belong to the source.
        /// </param>
        /// <param name="symbol">
        /// How a <c>w:sym</c>'s face and slot resolve to something drawable, or null when nothing can
        /// draw it. Supplied by the source for the same reason the citation is: font resolution is the
        /// source's, and the answer decides the *characters* — a recoded slot is a different code point
        /// from the one the file states — so it cannot be deferred to the pass that styles the ranges.
        /// </param>
        /// <param name="endnote">How many endnotes came before it, counted separately.</param>
        /// <param name="constants">What a <c>FILENAME</c> or <c>TITLE</c> field evaluates to.</param>
        /// <param name="styleReference">
        /// What a <c>STYLEREF</c> naming a style quotes, or null when nothing has been read in that
        /// style yet. Supplied by the source because the answer is a paragraph this walker never sees.
        /// </param>
        internal RunWalker(
            Func<bool, int, string> citation,
            Func<string?, char, (string Text, OpenTypeFace Face, FontReference? Font)?> symbol,
            ConstantFields constants = default,
            int footnote = 0,
            int endnote = 0,
            Func<string, string?>? styleReference = null)
        {
            _citationOf = citation;
            _symbolOf = symbol;
            _constants = constants;
            _footnote = footnote;
            _endnote = endnote;
            _styleReference = styleReference;
        }

        /// <summary>What a <c>STYLEREF</c> quotes, or null when the source cannot answer.</summary>
        private readonly Func<string, string?>? _styleReference;

        /// <summary>What a <c>FILENAME</c> or <c>TITLE</c> field evaluates to.</summary>
        private readonly ConstantFields _constants;

        /// <summary>How a note of a class and an index is cited, which the source resolves.</summary>
        /// <remarks>
        /// A delegate because the walker is nested but not owned: the numbering comes from the document's
        /// settings, which the source read, and a walker is built per paragraph.
        /// </remarks>
        private readonly Func<bool, int, string> _citationOf;

        /// <summary>How a <c>w:sym</c> resolves to a drawable character and face.</summary>
        private readonly Func<string?, char, (string Text, OpenTypeFace Face, FontReference? Font)?> _symbolOf;

        /// <summary>The face a <c>w:sym</c> resolved to, in force for exactly the character it emits.</summary>
        private (OpenTypeFace Face, FontReference? Font)? _symbolFace;

        /// <summary>How deep a paragraph's element nesting is followed.</summary>
        /// <remarks>
        /// Hyperlinks, content controls, smart tags and change regions all wrap runs and do nest, but a
        /// generated file can nest indefinitely and this recurses on untrusted input.
        /// </remarks>
        private const int MaxDepth = 64;

        private readonly StringBuilder _builder = new();
        private readonly List<StyledRange> _ranges = [];
        private readonly List<NoteAnchor> _notes = [];
        private readonly List<FrameAnchor> _frames = [];
        private int _footnote;
        private int _endnote;
        private XElement? _runProperties;

        /// <summary>How many footnotes this paragraph cited, which advances the source's counter.</summary>
        internal int FootnotesSeen { get; private set; }

        /// <summary>How many endnotes it cited.</summary>
        internal int EndnotesSeen { get; private set; }

        /// <summary>Where in the text the last <c>w:br w:type="page"</c> fell, or −1 for none.</summary>
        private int _pageBreakAt = -1;

        /// <summary>
        /// True when a <c>w:br w:type="page"</c> ended the paragraph, so the <em>next</em> one starts a page.
        /// </summary>
        /// <remarks>
        /// A page break is written at the point in the text where the page ends, and the layout model says
        /// "this paragraph starts a page" — the same shape Writer's <c>BreakType_PAGE_BEFORE</c> has, and
        /// the same shape the DOC and RTF forms state directly. Which paragraph it lands on is decided by
        /// what follows the break rather than by the paragraph boundary: LibreOffice defers the break and
        /// applies it at the next run of text (<c>DomainMapper::lcl_utext</c>, which calls
        /// <c>deferBreak(PAGE_BREAK)</c> for U+000C and inserts <c>BreakType_PAGE_BEFORE</c> into the
        /// <em>current</em> paragraph context on the next text it sees). So a break with text after it in
        /// the same paragraph breaks before that paragraph, and only one with nothing after it carries over.
        /// </remarks>
        internal bool BreaksPage => _pageBreakAt >= 0 && _pageBreakAt >= _builder.Length;

        /// <summary>True when the break fell before this paragraph's own text, so this one starts a page.</summary>
        internal bool BreaksPageHere => _pageBreakAt >= 0 && _pageBreakAt < _builder.Length;

        private bool _inInstruction;

        /// <summary>
        /// How many open fields have had their value written by this walk, so their cached results are
        /// not drawn.
        /// </summary>
        /// <remarks>
        /// A counter rather than a flag because fields nest: a <c>FILENAME</c> inside a <c>HYPERLINK</c>
        /// closes before the link does, and a flag cleared at the inner end would let the outer field's
        /// remaining result through as if it too had been substituted.
        /// </remarks>
        private int _hidden;

        /// <summary>True while nothing the walk reads is drawn.</summary>
        private bool Suppressed => _inInstruction || _hidden > 0;

        /// <summary>The fields whose begin has been seen and whose end has not, innermost last.</summary>
        /// <remarks>
        /// A stack because fields nest and Word writes them nested — a hyperlink around a cross-reference
        /// is two, and a <c>PAGE</c> inside a <c>SEQ</c> is another. Only the fields this walk can
        /// substitute are recorded when they close.
        /// </remarks>
        private readonly Stack<OpenField> _fields = new();

        /// <summary>The page-sensitive fields this paragraph carries, with the spans their results own.</summary>
        internal List<PageFieldSpan> Fields { get; } = [];

        /// <summary>A field between its begin and its end.</summary>
        private sealed class OpenField
        {
            /// <summary>The instruction, accumulated across however many <c>w:instrText</c> carry it.</summary>
            /// <remarks>
            /// Word splits an instruction across runs freely — <c>PAGE  \* MERGE</c> and <c>FORMAT</c> in
            /// two <c>w:instrText</c> is ordinary — so a reader looking at one element at a time sees a
            /// name that is not the field's.
            /// </remarks>
            internal StringBuilder Instruction { get; } = new();

            /// <summary>Where the result began, or −1 for a field with no separator and so no result.</summary>
            internal int ResultAt { get; set; } = -1;

            /// <summary>
            /// True when this walk wrote the field's value itself, so its cached result is hidden.
            /// </summary>
            internal bool Substituted { get; set; }

            /// <summary>
            /// The run properties in force where the instruction was written.
            /// </summary>
            /// <remarks>
            /// A field with no separator has no result runs to take formatting from, and the run holding
            /// its <c>fldChar end</c> is usually bare — so the instruction's own run is the nearest thing
            /// to what the producer meant the value to look like. Word writes the two with the same
            /// <c>w:rPr</c>, which is what makes this the right guess rather than merely a guess.
            /// </remarks>
            internal XElement? InstructionProperties { get; set; }
        }

        /// <summary>The value a constant field evaluates to, or null when nothing can be computed.</summary>
        private string? ValueOf(string instruction)
        {
            if (FieldInstructions.StyleReferenceName(instruction) is { } style)
            {
                return _styleReference?.Invoke(style) is { Length: > 0 } quoted ? quoted : null;
            }

            return FieldInstructions.ConstantFieldOf(instruction) switch
            {
                ConstantField.FileName => _constants.FileName is { Length: > 0 } name ? name : null,
                ConstantField.Title => _constants.Title is { Length: > 0 } title ? title : null,
                _ => null,
            };
        }

        /// <summary>Writes a constant field's value in place of its cached result.</summary>
        /// <param name="field">The field, whose instruction has been read in full.</param>
        /// <param name="properties">The run properties to draw it under, or null for the paragraph's.</param>
        /// <returns>True when a value was written, so the cached result must be hidden.</returns>
        private bool Substitute(OpenField field, XElement? properties)
        {
            if (ValueOf(field.Instruction.ToString()) is not { } value) return false;

            XElement? outer = _runProperties;
            _runProperties = properties;
            Emit(value);
            _runProperties = outer;

            field.Substituted = true;
            _hidden++;
            return true;
        }

        /// <summary>
        /// Records a field that has just ended, when it is one whose value pagination decides.
        /// </summary>
        private void CloseField(OpenField field)
        {
            if (field.ResultAt < 0) return;

            if (FieldInstructions.PageFieldOf(field.Instruction.ToString()) is not { } page) return;

            Fields.Add(new PageFieldSpan(
                field.ResultAt, _builder.Length - field.ResultAt, page.Kind, page.Format));
        }

        /// <summary>The paragraph's text, as laid out.</summary>
        internal string Text => _builder.ToString();

        /// <summary>The ranges, in order, partitioning the text.</summary>
        internal IReadOnlyList<StyledRange> Ranges => _ranges;

        /// <summary>The notes referenced in the paragraph, with the offsets their citations occupy.</summary>
        internal List<NoteAnchor> Notes => _notes;

        /// <summary>The floating frames anchored in the paragraph, with the offsets they sit at.</summary>
        internal List<FrameAnchor> Frames => _frames;

        /// <summary>Walks a <c>w:p</c>.</summary>
        /// <param name="paragraph">The paragraph element.</param>
        /// <param name="citation">
        /// The number a <c>w:footnoteRef</c> in this paragraph stands for, or null when the paragraph is not
        /// a note's. Unlike ODF, a DOCX marks the place its citation goes: the note's own first paragraph
        /// contains a <c>w:footnoteRef</c>, inside a run whose character style is what makes the number
        /// superscript. So the citation is emitted where the file says rather than prepended.
        /// </param>
        internal void Walk(XElement paragraph, string? citation = null)
        {
            _citation = citation;
            Append(paragraph, depth: 0);
        }

        /// <summary>The number a <c>w:footnoteRef</c> stands for, when this paragraph is a note's.</summary>
        private string? _citation;

        /// <summary>Where that number was emitted, or −1 when the paragraph marked no place for one.</summary>
        internal int CitationOffset { get; private set; } = -1;

        private void Append(XElement element, int depth)
        {
            if (depth > MaxDepth) return;

            foreach (XElement child in element.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "del" or "delText":
                        // Deleted text is in the file and not on the page.
                        break;

                    case "instrText":
                        // Not drawn — but it is the only statement of what the field computes, so it is
                        // accumulated for the innermost open field rather than merely skipped.
                        if (_fields.Count > 0)
                        {
                            OpenField open = _fields.Peek();
                            open.Instruction.Append(child.Value);
                            open.InstructionProperties ??= _runProperties;
                        }

                        break;

                    case "fldChar":
                        // "separate" ends the instruction and starts the result; "end" closes the field.
                        string? type = Word.Attribute(child, "fldCharType");
                        switch (type)
                        {
                            case "begin":
                                _inInstruction = true;
                                if (_fields.Count < MaxDepth) _fields.Push(new OpenField());
                                break;

                            case "separate":
                                _inInstruction = false;
                                if (_fields.Count > 0)
                                {
                                    OpenField open = _fields.Peek();
                                    open.ResultAt = _builder.Length;

                                    // The one point at which a field this walk computes can be written:
                                    // the instruction has been read in full, so the field's name is
                                    // known, and the cached result it replaces starts here.
                                    if (_hidden == 0) Substitute(open, _runProperties);
                                }

                                break;

                            case "end":
                                _inInstruction = false;
                                if (_fields.Count > 0)
                                {
                                    OpenField closing = _fields.Pop();

                                    // A field with no separator has no cached result at all, and
                                    // LibreOffice still draws its value — measured on the second
                                    // FILENAME in `CRIF …`'s footer, which the reference draws and this
                                    // walk would otherwise pass over in silence.
                                    if (closing.ResultAt < 0 && _hidden == 0)
                                    {
                                        int at = _builder.Length;
                                        if (Substitute(closing, closing.InstructionProperties))
                                        {
                                            closing.ResultAt = at;
                                            _hidden--;
                                        }
                                    }
                                    else if (closing.Substituted)
                                    {
                                        _hidden--;
                                    }

                                    CloseField(closing);
                                }

                                break;
                        }

                        break;

                    // The compact form: the instruction is an attribute and the result is the children.
                    // Word writes it for a field with no nested state, which a page number often is.
                    case "fldSimple":
                    {
                        OpenField simple = new() { ResultAt = _builder.Length };
                        simple.Instruction.Append(Word.Attribute(child, "instr") ?? "");

                        // The cached result's own runs carry the formatting the producer gave the field —
                        // here the whole field is one element, so the first of them is taken rather than
                        // the paragraph's, which would draw the value in the style's weight instead.
                        if (_hidden == 0
                            && Substitute(simple, Word.Child(Word.Child(child, "r"), "rPr")))
                        {
                            _hidden--;
                        }
                        else
                        {
                            Append(child, depth + 1);
                        }

                        CloseField(simple);
                        break;
                    }

                    case "t" when !Suppressed:
                        Emit(child.Value);
                        break;

                    case "tab" when !Suppressed:
                        Emit("\t");
                        break;

                    // A character named by slot in a face of its own, and the only run-level element
                    // that overrides the run's font for one character. LibreOffice sets
                    // PROP_CHAR_FONT_NAME to `w:font` with charset SYMBOL and appends the raw `w:char`
                    // (`DomainMapper::sprmWithProps`, `LN_EG_RunInnerContent_sym`), so the face travels
                    // with the character rather than with the run.
                    case "sym" when !Suppressed:
                        EmitSymbol(child);
                        break;

                    // Word states it as an element rather than a character, and dropping it closes up
                    // the space it occupies exactly as dropping a `w:tab` would.
                    //
                    // An ordinary hyphen, not U+2011, and that is measured rather than assumed. The
                    // import carries it as U+2011 (`OOXMLFastContextHandler.cxx:54`, `uNoBreakHyphen`)
                    // and the *layout* then swaps the character out:
                    // `case CHAR_HARDHYPHEN: pPor = new SwBlankPortion('-')`
                    // (`sw/source/core/text/itrform2.cxx:1881-1882`). The reference PDF agrees — the
                    // text layer of `Company-profile-2022-EN.docx` reads `the -600 series` with a
                    // U+002D — and it has to, because U+2011 is in neither Carlito nor any Liberation
                    // face, so keeping it would draw a fallback face's glyph beside text in neither.
                    //
                    // What this does not reproduce is the half of the name that says "no break": a
                    // `SwBlankPortion` cannot be broken and a U+002D is UAX #14 class HY, which is a
                    // break opportunity. Drawing the hyphen in the right face is worth more than the
                    // breaking, which only differs when a line ends exactly there.
                    case "noBreakHyphen" when !Suppressed:
                        Emit("-");
                        break;

                    // A `w:br` is three things wearing one name and only one of them is a line break.
                    // `w:type="page"` moves everything after it to the next page and contributes no
                    // character at all: LibreOffice turns it back into the DOC's own U+000C
                    // (`OOXMLBreakHandler::~OOXMLBreakHandler`, `writerfilter/ooxml/Handler.cxx:246`)
                    // and then *defers* it, applying it to the paragraph that follows as
                    // `BreakType_PAGE_BEFORE` (`dmapper/DomainMapper.cxx:4379`).
                    case "br" when !Suppressed:
                        if (Word.Attribute(child, "type") == "page") _pageBreakAt = _builder.Length;
                        else Emit(LineSeparator.ToString());
                        break;

                    case "footnoteReference" or "endnoteReference":
                    {
                        // A note reference carries its citation, which Word draws in the sentence as a
                        // superscript and again at the head of the note. The style comes from the run this
                        // reference sits in, which is what carries w:vertAlign="superscript".
                        bool isEndnote = Word.Is(child, "endnoteReference");
                        string number = _citationOf(isEndnote, isEndnote ? _endnote : _footnote);

                        _notes.Add(new NoteAnchor(
                            _builder.Length, Word.Attribute(child, "id"), isEndnote, number));

                        _inCitation = true;
                        Emit(number);
                        _inCitation = false;

                        if (isEndnote)
                        {
                            _endnote++;
                            EndnotesSeen++;
                        }
                        else
                        {
                            _footnote++;
                            FootnotesSeen++;
                        }

                        break;
                    }

                    case "footnoteRef" or "endnoteRef":
                        // The note's own citation, at the place the file marks for it. Marked as a citation
                        // so that it falls back to superscript, because the style that should supply it
                        // usually does not: LibreOffice exports its built-in `Footnote Characters` as an
                        // *empty* w:rPr and relies on the importer knowing what that style is. A reader
                        // taking the file at its word draws the number full size on the baseline, where it
                        // fuses with the note's first word.
                        if (_citation is not null)
                        {
                            CitationOffset = _builder.Length;
                            _inCitation = true;
                            Emit(_citation);
                            _inCitation = false;
                        }

                        break;

                    // A floating frame occupies a position in the paragraph and is not part of it: its
                    // own text belongs to a rectangle of its own. Recorded with the offset it sits at,
                    // which is what an anchor is measured in; the anchor character stands for it, as it
                    // does for every other thing that takes a position and is not text.
                    case "drawing":
                        _frames.Add(new FrameAnchor(_builder.Length, child));
                        Emit(AnchorCharacter.ToString());
                        break;

                    // A `w:pict` or a `w:object` is a VML shape. It states its size in a CSS `style`
                    // rather than in a `wp:extent`, and only an inline one takes room on the line —
                    // see `DocxVmlFrames`, which decides both and returns nothing for a floating
                    // shape, leaving the anchor character to stand for it as it did before.
                    case "pict" or "object":
                        _frames.Add(new FrameAnchor(_builder.Length, child));
                        Emit(AnchorCharacter.ToString());
                        break;

                    case "commentReference":
                        Emit(AnchorCharacter.ToString());
                        break;

                    case "pPr" or "bookmarkStart" or "bookmarkEnd" or "proofErr" or "rPr":
                        break;

                    case "r":
                        // The one element that carries character formatting. Runs do not nest, but this
                        // saves and restores anyway so that a malformed file cannot lose the outer state.
                        XElement? outer = _runProperties;
                        _runProperties = Word.Child(child, "rPr");
                        Append(child, depth + 1);
                        _runProperties = outer;
                        break;

                    default:
                        Append(child, depth + 1);
                        break;
                }
            }
        }

        /// <summary>
        /// Emits a <c>w:sym</c>: one character, in the face the element names rather than the run's.
        /// </summary>
        /// <remarks>
        /// The slot is passed on exactly as the file states it — <c>00DE</c> stays <c>U+00DE</c> and
        /// <c>F0B7</c> stays <c>U+F0B7</c> — because the recode tables accept a symbol slot in both its
        /// plain and its Private Use Area spelling and LibreOffice hands them the value unaltered.
        /// Nothing is emitted when the resolver declines: the code point means nothing outside the face
        /// that is missing, so drawing it in the paragraph's own face would put a <c>.notdef</c> box
        /// where the document asked for a picture.
        /// </remarks>
        private void EmitSymbol(XElement symbol)
        {
            string? code = Word.Attribute(symbol, "char");
            if (code is null) return;
            if (!ushort.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort slot))
                return;

            if (_symbolOf(Word.Attribute(symbol, "font"), (char)slot) is not { } resolved) return;

            _symbolFace = (resolved.Face, resolved.Font);
            Emit(resolved.Text);
            _symbolFace = null;
        }

        /// <summary>Appends text under the run properties currently in force.</summary>
        private void Emit(string text)
        {
            if (text.Length == 0) return;

            _builder.Append(text);

            // Adjacent runs with the same properties merge, which matters because a DOCX splits runs for
            // reasons that are not formatting: a proofing error, a revision id, a bookmark boundary.
            // A symbol never merges: its face is its own and the character beside it is not in it.
            if (_ranges.Count > 0
                && _symbolFace is null
                && _ranges[^1].Symbol is null
                && _ranges[^1].IsCitation == _inCitation
                && _ranges[^1].RunProperties == _runProperties)
            {
                _ranges[^1] = _ranges[^1] with { Length = _ranges[^1].Length + text.Length };
                return;
            }

            _ranges.Add(new StyledRange(
                _builder.Length - text.Length, text.Length, _runProperties, _inCitation, _symbolFace));
        }

        /// <summary>True while a note's citation is being emitted.</summary>
        private bool _inCitation;
    }

    /// <summary>
    /// A citation's style, defaulted to superscript when the run does not say so.
    /// </summary>
    /// <remarks>
    /// Word's own <c>FootnoteReference</c> character style sets <c>w:vertAlign="superscript"</c>, and a
    /// document that has it is read correctly without this. LibreOffice's DOCX export does not always write
    /// it, and a document whose notes were added by something else may not either — so the default matches
    /// what Word draws rather than what the file happens to state. Applied only when nothing has been said,
    /// so a run that does state a shift keeps it.
    /// </remarks>
    private static WordTextStyle AsCitation(WordTextStyle style)
        => style.Escapement.IsNone
            ? style with { Escapement = Layout.Escapement.Superscript }
            : style;

    /// <summary>
    /// How a note of each class is cited, which is not the same for the two.
    /// </summary>
    /// <remarks>
    /// Two sequences in two formats, from the document's <c>w:footnotePr</c> and <c>w:endnotePr</c> where it
    /// has them and from LibreOffice's own defaults where it does not — footnotes 1, 2, 3 and endnotes
    /// i, ii, iii, which is measured rather than assumed.
    /// </remarks>
    /// <param name="isEndnote">True for an endnote.</param>
    /// <param name="index">How many notes of the class came before, counted from zero.</param>
    private string CitationOf(bool isEndnote, int index)
        => (isEndnote ? _endnoteNumbering : _footnoteNumbering).Citation(index);

    /// <summary>
    /// What <c>w:beforeAutospacing</c> and <c>w:afterAutospacing</c> stand for in this document.
    /// </summary>
    /// <remarks>
    /// Three answers, in LibreOffice's own order of precedence
    /// (<c>DomainMapper.cxx</c>:916-953). <c>w:doNotUseHTMLParagraphAutoSpacing</c> wins outright and
    /// means five points; otherwise a document saved in <strong>web view</strong> gets 49 twips and
    /// every other document 280. The web branch is easy to miss because <c>w:view</c> is not a
    /// compatibility flag — it sits directly under <c>w:settings</c>, beside the zoom and the ruler,
    /// and reads like a preference rather than a measurement.
    /// </remarks>
    private static Length AutoSpacing(XElement? settings, WordCompatibility compatibility)
    {
        if (compatibility.DoNotUseHtmlParagraphAutoSpacing) return WordParagraphFormats.WordAutoSpacing;

        return Word.Attribute(Word.Child(settings, "view"), "val") == "web"
            ? WordParagraphFormats.WebAutoSpacing
            : WordParagraphFormats.HtmlAutoSpacing;
    }

    /// <summary>
    /// The document's default tab interval.
    /// </summary>
    /// <remarks>
    /// Half an inch when the document does not say, which is what Word uses. A zero would make a tab
    /// advance nowhere, so it is treated as absent rather than honoured.
    /// </remarks>
    private static Length TabInterval(XElement? settings)
        => Word.Attribute(Word.Child(settings, "defaultTabStop"), "val") is { } text
           && Word.Long(text, out long twips)
           && twips > 0
            ? Length.FromTwips(twips)
            : Length.FromTwips(720);

    /// <summary>
    /// Which version of Word wrote the file, from <c>w:compat</c>, or <c>-1</c> when it does not say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single number covering a decade of behaviour changes: 12 is Word 2007, 14 is 2010 and 15 is 2013
    /// and after. Word writes it as a <c>w:compatSetting</c> named <c>compatibilityMode</c> in the
    /// Microsoft namespace, which is a URI rather than the <c>w:</c> one — so the name and the URI both
    /// have to match, and a setting from another vendor's namespace is not this one.
    /// </para>
    /// <para>
    /// Absent stays <c>-1</c> rather than defaulting to 12, following
    /// <c>SettingsTable::GetWordCompatibilityMode</c>: everything that consults it asks whether the mode is
    /// <em>below</em> 15, and −1 is, so a file that says nothing gets the older behaviour without the
    /// reader having to invent a version for it.
    /// </para>
    /// </remarks>
    private static int CompatibilityMode(XElement? settings)
    {
        const string wordUri = "http://schemas.microsoft.com/office/word";

        foreach (XElement setting in Word.Children(Word.Child(settings, "compat"), "compatSetting"))
        {
            if (Word.Attribute(setting, "name") != "compatibilityMode") continue;
            if (Word.Attribute(setting, "uri") != wordUri) continue;

            if (int.TryParse(
                    Word.Attribute(setting, "val"), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int mode))
            {
                return mode;
            }
        }

        return -1;
    }

    private OpenTypeFace? Face(WordTextStyle text)
    {
        (string? Family, int Weight, bool Italic, FontFamilyClass Class) key = text.FaceKey;
        if (_faces.TryGetValue(key, out OpenTypeFace? cached)) return cached;

        OpenTypeFace? face = null;
        try
        {
            // The declared family only. The table declares a pitch too and LibreOffice's DOCX filter
            // does not act on it: probed on 26.2.4.2 with a one-run package, `Garamond` declared
            // `swiss` moves the fallback from DejaVu Serif to DejaVu Sans while `Garamond` declared
            // `fixed` — and `MS Mincho` declared `modern`+`fixed` — leaves it exactly where it was.
            // Its ODF filter *does* honour `style:font-pitch`, so this is a difference between the
            // two importers rather than a property of the resolver, and passing the pitch here put
            // one corpus document into DejaVu Sans Mono that the reference sets in DejaVu Sans.
            //
            // And a family the table says nothing about is not undeclared as far as the *filter* is
            // concerned: it takes Writer's roman default, which is why an unrecognised family drawn
            // through this filter comes out DejaVu Serif where the same name through the ODF filter
            // comes out whatever fontconfig files it under. See `WordFallbackClass`, and
            // `probes/words-r54/font-fallback-rule.py` for the 98 files that measure it.
            FontFamilyClass declared = WordFallbackClass.ForDeclared(
                text.FamilyName, text.DeclaredClass);

            FontReference reference = _fonts.Resolve(
                new FontRequest(
                    text.FamilyName ?? string.Empty, text.Weight, text.IsItalic,
                    DeclaredClass: declared));

            face = _fonts.LoadOpenType(reference);
            _references[key] = reference;
        }
        catch (Exception exception) when (exception is Core.MalformedDocumentException
                                             or IOException
                                             or UnauthorizedAccessException)
        {
            // Nothing to measure the paragraph with. Dropping it gives a shorter document rather than
            // an exception out of the middle of a layout.
        }

        _faces[key] = face;
        return face;
    }
}
