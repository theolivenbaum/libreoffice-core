using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Paperless.Core.Documents;
using Paperless.Core.Geometry;
using Paperless.Core.Units;
using Paperless.WordProcessing.Layout;
using Paperless.WordProcessing.Model;
using Paperless.WordProcessing.Ooxml;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// <c>wp:effectExtent</c> on an inline drawing, which is room on the line rather than size on the
/// drawing.
/// </summary>
/// <remarks>
/// <para>
/// A shadow, a glow or a fat stroke paints outside the <c>wp:extent</c> a drawing states, and
/// <c>wp:effectExtent</c> is how much on each side. For a <c>wp:inline</c> LibreOffice adds all four
/// edges straight to the object's own margins — <c>GraphicImport.cxx</c>:1036-1055, guarded by
/// <c>IMPORT_AS_DETECTED_INLINE</c> and a zero rotation, with the comment <em>"EffectExtent contains
/// all needed additional space, including fat stroke and shadow. Simple add it to the margins."</em>
/// Writer then rests the object's rectangle <em>including</em> that spacing on the baseline
/// (<c>SwFlyCntPortion::SetBase</c> sizing itself from
/// <c>SwAsCharAnchoredObjectPosition::GetObjBoundRectInclSpacing</c>), so the extent grows the line.
/// </para>
/// <para>
/// Measured in <c>dotnet/probes/words-inline-effectextent/</c>, against both installed references —
/// 24.2.7.2 and 26.2.4.2, which agree to the twip on every fixture. One 50.4 pt shape between two
/// 12 pt text lines, the gap between those lines measured against a zero-extent control:
/// </para>
/// <list type="table">
///   <item><term><c>l=t=r=b="0"</c></term><description>64.25 pt — the control</description></item>
///   <item><term><c>27432</c> (2.16 pt)</term><description>68.55 pt, <b>+4.30</b></description></item>
///   <item><term><c>91440</c> (7.2 pt)</term><description>78.65 pt, <b>+14.40</b></description></item>
///   <item><term><c>137160</c> (10.8 pt)</term><description>85.85 pt, <b>+21.60</b></description></item>
///   <item><term>top only</term><description>75.05 pt, <b>+10.80</b></description></item>
///   <item><term>bottom only</term><description>75.05 pt, <b>+10.80</b></description></item>
/// </list>
/// <para>
/// So each edge is independent and additive, and the growth is exactly the stated EMUs rounded to the
/// twip — 2.16 pt is 43.2 twips, which lands at 43 and doubles to the 4.30 above rather than to 4.32.
/// </para>
/// <para>
/// The corpus case is <c>WordArt_Shapes_Arrows_Catalog1.docx</c>, 340 unrotated inline shapes all
/// carrying one of those three extents. Without this the document paginated to <b>45 pages against
/// both references' 52</b>; with it, 52, holding the same shapes on every one of them.
/// </para>
/// </remarks>
public sealed class FrameEffectExtentTests
{
    /// <summary>The four edges are read from an inline drawing, each rounded to the twip.</summary>
    /// <remarks>
    /// <para>
    /// The rounding is the reader's shared <c>Emu</c> helper and it is what the reference does too:
    /// 27432 EMU is 2.16 pt, which is 43.2 twips and lands at 43, so a shape carrying that extent on
    /// its top and bottom grows its line by <b>4.30 pt</b> and not by 4.32. That is the measured
    /// figure — see the type's own remarks — so asserting the unrounded EMUs here would pin a value
    /// neither renderer produces. The other two edges divide evenly: 91440 is 144 twips and 137160 is
    /// 216.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFourEdgesAreRead()
    {
        PageFrame frame = Inline("""<wp:effectExtent l="27432" t="91440" r="137160" b="0"/>""");

        frame.EffectExtent.Left.ShouldBe(Length.FromTwips(43));
        frame.EffectExtent.Top.ShouldBe(Length.FromTwips(144));
        frame.EffectExtent.Right.ShouldBe(Length.FromTwips(216));
        frame.EffectExtent.Bottom.ShouldBe(Length.Zero);
    }

    /// <summary>
    /// The catalogue's three extents grow a line by the figures both references were measured at.
    /// </summary>
    /// <remarks>
    /// The whole of the pagination fix in one assertion: these are the +4.30, +14.40 and +21.60 pt
    /// from the probe, as the difference between the drawing's own height and the room it takes.
    /// </remarks>
    [Theory]
    [InlineData(27432, 4.30)]
    [InlineData(91440, 14.40)]
    [InlineData(137160, 21.60)]
    public void TheMeasuredGrowthIsReproduced(int emu, double points)
    {
        PageFrame frame = Inline($"""<wp:effectExtent l="0" t="{emu}" r="0" b="{emu}"/>""");

        (frame.InlineExtent.Height - frame.Size.Height).ShouldBe(Length.FromPoints(points));
    }

    /// <summary>
    /// The extent grows the room the drawing takes on its line and leaves the drawing's own size alone.
    /// </summary>
    /// <remarks>
    /// The split matters because the shape is still painted at the size the file gives it: 10.8 pt on
    /// each edge of a 50.4 pt shape makes a 72 pt line and a 50.4 pt shape, not a 72 pt shape.
    /// </remarks>
    [Fact]
    public void TheExtentGrowsTheLineAndNotTheDrawing()
    {
        PageFrame frame = Inline("""<wp:effectExtent l="137160" t="137160" r="137160" b="137160"/>""");

        frame.Size.Height.ShouldBe(Length.FromEmu(640080));
        frame.InlineExtent.Height.ShouldBe(Length.FromPoints(72));
        frame.InlineExtent.Width.ShouldBe(frame.Size.Width + Length.FromPoints(21.6));
    }

    /// <summary>A drawing stating no extent takes exactly its own size on the line.</summary>
    [Fact]
    public void NoExtentLeavesTheLineAtTheDrawingsOwnSize()
    {
        PageFrame frame = Inline("");

        frame.EffectExtent.ShouldBe(Margins.Zero);
        frame.InlineExtent.Height.ShouldBe(frame.Size.Height);
        frame.InlineExtent.Width.ShouldBe(frame.Size.Width);
    }

    /// <summary>
    /// The <c>dist*</c> attributes beside it are discarded on an inline drawing, not added to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not an omission. LibreOffice zeroes the matching margin merely because the attribute is
    /// present — <c>GraphicImport.cxx</c>:1387-1398 is four cases of
    /// <c>case LN_CT_Inline_distT: m_nTopMargin = 0;</c>, which never reads <c>nIntValue</c> at all.
    /// </para>
    /// <para>
    /// Measured: a fixture stating <c>distT="137160" distB="137160"</c> and no effect extent moves the
    /// line below it by <b>0.00 pt</b> against the zero control, on both installed references. So a
    /// reader that added the two would be 21.6 pt out on every such drawing.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDistanceAttributesDoNotGrowAnInlineDrawing()
    {
        PageFrame frame = Inline("", dist: """distT="137160" distB="137160" distL="137160" distR="137160" """);

        frame.InlineExtent.Height.ShouldBe(frame.Size.Height);
        frame.InlineExtent.Width.ShouldBe(frame.Size.Width);
    }

    /// <summary>
    /// An anchored drawing carries no extent, because LibreOffice reaches it by a different route.
    /// </summary>
    /// <remarks>
    /// A floating drawing's extent goes into its <em>wrap</em> margins, through the much longer
    /// <c>WrapTextMode_PARALLEL</c> branch that needs the shape's own bound rectangle. Reading the four
    /// numbers there would be wrong rather than partial — see the note on
    /// <see cref="PageFrame.Spacing"/> for the measurement that keeps it unread.
    /// </remarks>
    [Fact]
    public void AnAnchoredDrawingTakesNoEffectExtent()
    {
        XElement drawing = XElement.Parse(
            $"""
            <w:drawing xmlns:w="{W}" xmlns:wp="{Wp}" xmlns:a="{A}" xmlns:wps="{Wps}">
              <wp:anchor distT="0" distB="0" distL="0" distR="0">
                <wp:extent cx="1828800" cy="640080"/>
                <wp:effectExtent l="137160" t="137160" r="137160" b="137160"/>
                <wp:wrapNone/>
                <a:graphic><a:graphicData><wps:wsp>
                  <wps:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="1828800" cy="640080"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                  </wps:spPr>
                </wps:wsp></a:graphicData></a:graphic>
              </wp:anchor>
            </w:drawing>
            """);

        PageFrame frame = DocxFrames
            .ReadAll(drawing, null, anchorOffset: 0, pictures: null)
            .ShouldHaveSingleItem();

        frame.EffectExtent.ShouldBe(Margins.Zero);
        frame.InlineExtent.Height.ShouldBe(frame.Size.Height);
    }

    /// <summary>A plain picture takes none of it, because LibreOffice stops treating it as a shape.</summary>
    /// <remarks>
    /// <para>
    /// <c>GraphicImport.cxx</c>:879 gates the whole margin block on <c>if (m_xShape.is())</c>, and
    /// <c>bUseShape = !m_xGraphicObject.is()</c> at :844 — an unrotated, effect-free picture is turned
    /// into a Writer graphic object and its shape disposed, so nothing below it runs.
    /// </para>
    /// <para>
    /// Measured on both references: a <c>pic:pic</c> at <c>137160</c> on all four edges moves the line
    /// below it by <b>0.00 pt</b>, where the same fixture built from a <c>wps:wsp</c> moves it by
    /// 21.60. This is the case that cost <c>gpp-pr-top-7-office-markets-4q-2023.docx</c> 3.35 pt on
    /// everything below its chart, and <c>TE.CAO.00125 … OJT Logbook.docx</c> a whole page off a
    /// 0.75 pt header logo.
    /// </para>
    /// </remarks>
    [Fact]
    public void APlainPictureTakesNoEffectExtent()
    {
        PageFrame frame = Picture("""<wp:effectExtent l="137160" t="137160" r="137160" b="137160"/>""");

        frame.EffectExtent.ShouldBe(Margins.Zero);
        frame.InlineExtent.Height.ShouldBe(frame.Size.Height);
        frame.InlineExtent.Width.ShouldBe(frame.Size.Width);
    }

    /// <summary>
    /// A picture carrying DrawingML effects keeps it, because that refuses the conversion.
    /// </summary>
    /// <remarks>
    /// <c>bContainsEffects</c> (<c>GraphicImport.cxx</c>:820-825) is any of <c>EffectProperties</c>,
    /// <c>3DEffectProperties</c> or <c>ArtisticEffectProperties</c> in the grab bag — written by
    /// <c>oox</c> from <c>a:effectLst</c>, <c>a:scene3d</c>/<c>a:sp3d</c> and the <c>a14</c> artistic
    /// effects. Measured: the same picture with an <c>a:outerShdw</c> moves its line by
    /// <b>+21.65 pt</b>, exactly as a shape does; with the shadow and no extent, by 0.00.
    /// </remarks>
    [Theory]
    [InlineData("""<a:effectLst><a:outerShdw blurRad="50800"><a:srgbClr val="000000"/></a:outerShdw></a:effectLst>""")]
    [InlineData("""<a:scene3d><a:camera prst="orthographicFront"/></a:scene3d>""")]
    [InlineData("""<a:sp3d extrusionH="57150"/>""")]
    public void APictureCarryingEffectsKeepsTheExtent(string effects)
    {
        PageFrame frame = Picture(
            """<wp:effectExtent l="137160" t="137160" r="137160" b="137160"/>""", effects);

        frame.InlineExtent.Height.ShouldBe(frame.Size.Height + Length.FromPoints(21.6));
    }

    /// <summary>
    /// A rotated drawing takes none of it, and the growth it does get is not the extent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rotation sends the import down the other branch of <c>nOOXAngle == 0</c>, which derives the
    /// margins from the rotated snap rectangle. Worked through for a 20 degree, 144 x 50.4 pt drawing
    /// those margins come out negative on both edges and clamp to zero at
    /// <c>GraphicImport.cxx</c>:1248-1252.
    /// </para>
    /// <para>
    /// The measurement settles it rather than the arithmetic: that fixture grows its line by
    /// <b>+46.25 pt</b> with a <c>137160</c> extent, and by <b>+46.25 pt</b> with no extent at all.
    /// The growth is the rotated bounding box — which we do not yet size a rotated inline drawing by,
    /// and which is a separate open defect — and none of it is the effect extent.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARotatedDrawingTakesNoEffectExtent()
    {
        PageFrame frame = Inline(
            """<wp:effectExtent l="137160" t="137160" r="137160" b="137160"/>""", rotation: 1200000);

        frame.EffectExtent.ShouldBe(Margins.Zero);
        frame.InlineExtent.Height.ShouldBe(frame.Size.Height);
    }

    /// <summary>
    /// An inline drawing is placed at the outer left <em>plus</em> the left extent, and at the outer
    /// top with no top extent at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The asymmetry is LibreOffice's, and it is measured rather than reasoned about.
    /// <c>SwAsCharAnchoredObjectPosition::CalcPosition</c> moves the anchor point by both spacings —
    /// <c>sw/source/core/objectpositioning/ascharanchoredobjectposition.cxx</c>:129-133,
    /// <c>aAnchorPos.AdjustX(nLRSpaceLeft)</c> then <c>aAnchorPos.AdjustY(nULSpaceUpper)</c> — but a
    /// shape carrying a <c>wps:txbx</c> is two objects in Writer, and only the vertical half of that
    /// move is lost when its TextBox fails to follow its draw shape.
    /// </para>
    /// <para>
    /// <c>probes/words-inline-effectextent/make-x-fixture.py</c> is the same fixture laid across a
    /// line as <c>LEFT</c> + drawing + <c>RIGHT</c>. Both installed references, identical: a 10.8 pt
    /// <em>left</em> extent moves the shape's own fill band from 103.50 to <b>114.25 pt</b> and the
    /// <c>INSIDE</c> run of its text box from 155.95 to <b>166.75</b> — both halves, by the same
    /// amount — while a 10.8 pt <em>top</em> extent moves neither of them by anything, leaving
    /// <c>INSIDE</c> at 155.95 across and 90.86 down.
    /// </para>
    /// <para>
    /// Before this the corpus catalogue's page 7 read <b>-23 px at 150 dpi</b>, 11.04 pt, against both
    /// references with zero vertical shift and zero ink difference; after it, <b>0 px</b>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLeftExtentMovesTheDrawingAndTheTopExtentDoesNot()
    {
        DocRect none = PlacedInline("""<wp:effectExtent l="0" t="0" r="0" b="0"/>""");
        DocRect left = PlacedInline("""<wp:effectExtent l="137160" t="0" r="0" b="0"/>""");
        DocRect top = PlacedInline("""<wp:effectExtent l="0" t="137160" r="0" b="0"/>""");
        DocRect right = PlacedInline("""<wp:effectExtent l="0" t="0" r="137160" b="0"/>""");

        (left.X - none.X).ShouldBe(Length.FromPoints(10.8));
        (top.Y - none.Y).ShouldBe(Length.Zero);
        (top.X - none.X).ShouldBe(Length.Zero);
        (right.X - none.X).ShouldBe(Length.Zero, "the right edge is room on the line, not a move");
    }

    /// <summary>Where one inline drawing lands on the first page of a one-paragraph document.</summary>
    private static DocRect PlacedInline(string effectExtent)
    {
        string body = $"""
            <w:p>
              <w:r><w:rPr><w:sz w:val="24"/></w:rPr><w:t xml:space="preserve">LEFT </w:t></w:r>
              <w:r><w:rPr><w:sz w:val="24"/></w:rPr>
                <w:drawing>
                  <wp:inline distT="0" distB="0" distL="0" distR="0">
                    <wp:extent cx="1828800" cy="640080"/>
                    {effectExtent}
                    <wp:docPr id="1" name="probe"/>
                    <a:graphic><a:graphicData uri="{Wps}"><wps:wsp>
                      <wps:cNvSpPr/>
                      <wps:spPr>
                        <a:xfrm><a:off x="0" y="0"/><a:ext cx="1828800" cy="640080"/></a:xfrm>
                        <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                        <a:solidFill><a:srgbClr val="000000"/></a:solidFill>
                      </wps:spPr>
                      <wps:bodyPr/>
                    </wps:wsp></a:graphicData></a:graphic>
                  </wp:inline>
                </w:drawing>
              </w:r>
              <w:r><w:rPr><w:sz w:val="24"/></w:rPr><w:t xml:space="preserve"> RIGHT</w:t></w:r>
            </w:p>
            """;

        using MemoryStream package = BuildPackage(body);
        using DocumentSource source = DocumentSource.FromStream(package, "effect-extent.docx");
        using IDocument document = new WordProcessingReader().Read(source);
        WordProcessingPages pages = (WordProcessingPages)((IPaginatedDocument)document).Layout();

        return pages.Pages[0].Frames
            .Where(frame => frame.Frame.Anchor == FrameAnchor.AsCharacter)
            .ShouldHaveSingleItem()
            .Area;
    }

    private static MemoryStream BuildPackage(string body)
    {
        const string ContentTypes = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels"
                       ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml"
                        ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """;

        const string RootRelationships = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Target="word/document.xml"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"/>
            </Relationships>
            """;

        string document = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <w:document xmlns:w="{W}" xmlns:wp="{Wp}" xmlns:a="{A}" xmlns:wps="{Wps}">
              <w:body>
                {body}
                <w:sectPr>
                  <w:pgSz w:w="16838" w:h="11906" w:orient="landscape"/>
                  <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"
                           w:header="0" w:footer="0" w:gutter="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """;

        MemoryStream result = new();
        using (ZipArchive archive = new(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", ContentTypes);
            Write(archive, "_rels/.rels", RootRelationships);
            Write(archive, "word/document.xml", document);
        }

        result.Position = 0;
        return result;

        static void Write(ZipArchive archive, string name, string content)
        {
            using Stream entry = archive.CreateEntry(name).Open();
            entry.Write(Encoding.UTF8.GetBytes(content));
        }
    }

    private static PageFrame Picture(string effectExtent, string effects = "")
    {
        XElement drawing = XElement.Parse(
            $"""
            <w:drawing xmlns:w="{W}" xmlns:wp="{Wp}" xmlns:a="{A}" xmlns:pic="{Pic}"
                       xmlns:r="{R}">
              <wp:inline distT="0" distB="0" distL="0" distR="0">
                <wp:extent cx="1828800" cy="640080"/>
                {effectExtent}
                <a:graphic><a:graphicData uri="{Pic}">
                  <pic:pic>
                    <pic:nvPicPr><pic:cNvPr id="1" name="p"/><pic:cNvPicPr/></pic:nvPicPr>
                    <pic:blipFill><a:blip r:embed="rId1"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                    <pic:spPr>
                      <a:xfrm><a:off x="0" y="0"/><a:ext cx="1828800" cy="640080"/></a:xfrm>
                      <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                      {effects}
                    </pic:spPr>
                  </pic:pic>
                </a:graphicData></a:graphic>
              </wp:inline>
            </w:drawing>
            """);

        return DocxFrames
            .ReadAll(drawing, null, anchorOffset: 0, pictures: null)
            .ShouldHaveSingleItem();
    }

    private static PageFrame Inline(string effectExtent, string dist = "", int rotation = 0)
    {
        XElement drawing = XElement.Parse(
            $"""
            <w:drawing xmlns:w="{W}" xmlns:wp="{Wp}" xmlns:a="{A}" xmlns:wps="{Wps}">
              <wp:inline {dist}>
                <wp:extent cx="1828800" cy="640080"/>
                {effectExtent}
                <a:graphic><a:graphicData><wps:wsp>
                  <wps:spPr>
                    <a:xfrm{(rotation == 0 ? "" : $" rot=\"{rotation}\"")}><a:off x="0" y="0"/><a:ext cx="1828800" cy="640080"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                  </wps:spPr>
                </wps:wsp></a:graphicData></a:graphic>
              </wp:inline>
            </w:drawing>
            """);

        return DocxFrames
            .ReadAll(drawing, null, anchorOffset: 0, pictures: null)
            .ShouldHaveSingleItem();
    }

    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private const string Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
}
