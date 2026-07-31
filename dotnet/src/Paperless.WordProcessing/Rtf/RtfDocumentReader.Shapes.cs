using System.Globalization;
using Paperless.Core.Geometry;
using Paperless.Core.Graphics;
using Paperless.Core.Units;
using Paperless.WordProcessing.Layout;

namespace Paperless.WordProcessing.Rtf;

/// <content>
/// Reading a <c>{\shp}</c> group — RTF's floating shape — into the frame the layout engine takes.
/// </content>
/// <remarks>
/// <para>
/// RTF states a shape in three vocabularies at once, and that is the whole difficulty. Its <em>position</em> is
/// four control words carrying twips; its <em>insets, fill and line</em> are Escher shape properties written as
/// <c>{\sp{\sn name}{\sv value}}</c> pairs whose values are in <strong>EMUs</strong> and whose colours are
/// <strong>BGR</strong> integers; and its <em>text</em> is an ordinary flow inside <c>{\shptxt}</c>. Two units
/// and two colour orders in one group.
/// </para>
/// <para>
/// <strong><c>\shpwr</c>'s numbering is LibreOffice's own, not the specification's.</strong> The spec says
/// 1 = around, 2 = tight, 3 = through, 4 = top-and-bottom, 5 = none. LibreOffice's importer
/// (<c>rtfdispatchvalue.cxx</c>) reads 1 as <em>no text beside</em>, 2 as parallel, 3 as run-through, 4 as
/// parallel with a contour and 5 as run-through — and its exporter is the exact inverse, so it round-trips
/// itself perfectly and disagrees with the specification. Matching LibreOffice means taking LibreOffice's
/// numbering, which is what this does.
/// </para>
/// </remarks>
public sealed partial class RtfDocumentReader
{
    /// <summary>
    /// The shapes whose groups are still open, innermost last.
    /// </summary>
    /// <remarks>
    /// A stack because a shape can hold a text flow which can hold another shape. The depth of each group is
    /// kept with it, so a shape closes when its own brace does rather than when any brace does.
    /// </remarks>
    private readonly List<RtfShapeDraft> _shapes = [];

    /// <summary>The name of the shape property being read, from the <c>{\sn}</c> group just closed.</summary>
    /// <remarks>
    /// Held between the two halves of a <c>{\sp}</c> pair, since RTF writes the name and the value as separate
    /// groups and the value has to be applied to something.
    /// </remarks>
    private string? _shapeProperty;

    /// <summary>Opens a <c>{\shp}</c> group, or ignores it when shapes are nested too deeply.</summary>
    /// <remarks>
    /// The bound is a guard on untrusted input, not a real limit: a shape inside a shape's text is legal and
    /// rare, and a generated file can nest them indefinitely.
    /// </remarks>
    private void BeginShape()
    {
        if (_shapes.Count >= MaxShapeNesting) return;

        _shapes.Add(new RtfShapeDraft { Depth = _groupDepth });
    }

    /// <summary>How deeply shapes may nest before further ones are ignored.</summary>
    private const int MaxShapeNesting = 8;

    /// <summary>The shape being read, or null when no <c>{\shp}</c> group is open.</summary>
    private RtfShapeDraft? OpenShape => _shapes.Count > 0 ? _shapes[^1] : null;

    /// <summary>
    /// Applies one of the shape's positioning control words, or reports that it was none of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The position is four <em>edges</em> in twips rather than an offset and an extent, which is the one place
    /// RTF is friendlier than the other three: a shape's size needs no separate statement.
    /// </para>
    /// <para>
    /// <c>\shpbxignore</c> and <c>\shpbyignore</c> are deliberately not handled. They tell a reader to ignore
    /// the <em>preceding</em> positioning keyword, for the benefit of readers that predate the current pair —
    /// and LibreOffice writes them after every one of them, so honouring them would discard the reference the
    /// document just stated.
    /// </para>
    /// </remarks>
    /// <param name="name">The control word's name, without its backslash.</param>
    /// <param name="parameter">Its numeric parameter, or null when it has none.</param>
    /// <returns>True when the word belonged to the shape.</returns>
    private bool ApplyShapeWord(string name, int? parameter)
    {
        if (OpenShape is not { } shape) return false;

        switch (name)
        {
            case "shpleft": shape.Left = parameter ?? 0; return true;
            case "shptop": shape.Top = parameter ?? 0; return true;
            case "shpright": shape.Right = parameter ?? 0; return true;
            case "shpbottom": shape.Bottom = parameter ?? 0; return true;
            case "shpwr": shape.Wrap = parameter ?? 0; return true;
            case "shpwrk": shape.WrapSide = parameter ?? 0; return true;

            // Which rectangle each axis is measured against. `page` is the whole sheet, `margin` the text area,
            // `column` the column within it — the same three the other formats name differently.
            case "shpbxpage": shape.Horizontal = FrameReference.Page; return true;
            case "shpbxmargin" or "shpbxcolumn": shape.Horizontal = FrameReference.TextArea; return true;
            case "shpbypage": shape.Vertical = FrameReference.Page; return true;
            case "shpbymargin": shape.Vertical = FrameReference.TextArea; return true;
            case "shpbypara": shape.Vertical = FrameReference.Paragraph; return true;

            default: return false;
        }
    }

    /// <summary>
    /// Applies one <c>{\sp{\sn name}{\sv value}}</c> pair.
    /// </summary>
    /// <remarks>
    /// Escher's own property names, and their units are not RTF's: the four text insets and the line width are
    /// in <strong>EMUs</strong> while everything around them is in twips, and the two colours are
    /// <strong>BGR</strong> integers — <c>lineColor</c> of 1974729 is 0x1E21C9, which is #C9211E and not
    /// #1E21C9. The wrap distances are EMUs too, which is why they are not read from the twips beside them.
    /// </remarks>
    /// <param name="name">The property's name.</param>
    /// <param name="value">Its value, as written.</param>
    private void ApplyShapeProperty(string name, string value)
    {
        if (OpenShape is not { } shape) return;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)) return;

        switch (name)
        {
            case "dxTextLeft": shape.InsetLeft = number; break;
            case "dxTextRight": shape.InsetRight = number; break;
            case "dyTextTop": shape.InsetTop = number; break;
            case "dyTextBottom": shape.InsetBottom = number; break;

            case "dxWrapDistLeft": shape.WrapLeft = number; break;
            case "dxWrapDistRight": shape.WrapRight = number; break;
            case "dyWrapDistTop": shape.WrapTop = number; break;
            case "dyWrapDistBottom": shape.WrapBottom = number; break;

            case "fillColor": shape.Fill = FromBgr(number); break;
            case "lineColor": shape.Line = FromBgr(number); break;
            case "lineWidth": shape.LineWidth = number; break;

            // Both are switches, and each beats the colour beside it: a shape stating a fill colour and
            // `fFilled` of zero is transparent, which is how a producer turns a fill off without forgetting
            // which colour it was.
            case "fFilled": shape.IsFilled = number != 0; break;
            case "fLine": shape.HasLine = number != 0; break;

            default: break;
        }
    }

    /// <summary>A colour written as an Escher <c>0x00BBGGRR</c> integer.</summary>
    private static Colour FromBgr(long value)
        => new((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));

    /// <summary>
    /// Closes any shape whose group has ended, handing the finished frame to the paragraph it sits in.
    /// </summary>
    /// <remarks>
    /// To the paragraph rather than to the flow's blocks, because that is what anchors it: a shape group sits
    /// part way through the sentence it belongs to, exactly as a footnote's does, so the paragraph is still
    /// open when the shape finishes and takes it as a pending frame.
    /// </remarks>
    private void CloseShapes()
    {
        while (OpenShape is { } shape && shape.Depth >= _groupDepth)
        {
            _shapes.RemoveAt(_shapes.Count - 1);

            if (shape.Frame() is { } frame) CurrentFlow.PendingFrames.Add(frame);
        }
    }

    /// <summary>One <c>{\shp}</c> group under construction.</summary>
    /// <remarks>
    /// A class rather than a struct because it is filled in over the course of a group — a dozen control words
    /// and as many property pairs, in whatever order the producer wrote them — and copied nowhere.
    /// </remarks>
    private sealed class RtfShapeDraft
    {
        /// <summary>The group depth this shape opened at, which is what closes it.</summary>
        public int Depth { get; init; }

        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }

        /// <summary><c>\shpwr</c>'s value, in LibreOffice's numbering rather than the specification's.</summary>
        public int Wrap { get; set; }

        /// <summary><c>\shpwrk</c>: 0 both sides, 1 left, 2 right, 3 the larger.</summary>
        public int WrapSide { get; set; }

        public FrameReference Horizontal { get; set; } = FrameReference.TextArea;

        public FrameReference Vertical { get; set; } = FrameReference.Paragraph;

        public long InsetLeft { get; set; } = DefaultInset;
        public long InsetRight { get; set; } = DefaultInset;
        public long InsetTop { get; set; } = DefaultEndInset;
        public long InsetBottom { get; set; } = DefaultEndInset;

        public long WrapLeft { get; set; }
        public long WrapRight { get; set; }
        public long WrapTop { get; set; }
        public long WrapBottom { get; set; }

        public Colour? Fill { get; set; }
        public Colour? Line { get; set; }
        public long LineWidth { get; set; }
        public bool IsFilled { get; set; } = true;
        public bool HasLine { get; set; } = true;

        /// <summary>The shape's own text, staged as its <c>{\shptxt}</c> flow closes.</summary>
        public List<RtfLayoutBlock> Blocks { get; } = [];

        /// <summary>
        /// DrawingML's default side inset, which Escher shares: a tenth of an inch in EMUs.
        /// </summary>
        /// <remarks>
        /// The same non-zero, non-symmetrical pair OOXML has, and for the same reason — they are one shape
        /// model written twice. A shape stating none still insets its text.
        /// </remarks>
        private const long DefaultInset = 91440;

        /// <summary>And its default top and bottom inset, a twentieth of an inch.</summary>
        private const long DefaultEndInset = 45720;

        /// <summary>
        /// The frame this shape describes, or null when it has no area to occupy.
        /// </summary>
        /// <remarks>
        /// A shape of no size is dropped rather than placed: RTF writes one for a drawing whose geometry it
        /// states elsewhere, and a frame of no width obstructs nothing while still risking a division by its
        /// own zero further down.
        /// </remarks>
        public RtfLayoutFrame? Frame()
        {
            Length width = Length.FromTwips(Math.Max(0, Right - Left));
            Length height = Length.FromTwips(Math.Max(0, Bottom - Top));

            if (width <= Length.Zero || height <= Length.Zero) return null;

            return new RtfLayoutFrame(
                new DocPoint(Length.FromTwips(Left), Length.FromTwips(Top)),
                new DocSize(width, height),
                WrapOf(Wrap, WrapSide),
                Horizontal,
                Vertical,
                new CellPadding(
                    Length.FromEmu(WrapLeft), Length.FromEmu(WrapRight),
                    Length.FromEmu(WrapTop), Length.FromEmu(WrapBottom)),
                new CellPadding(
                    Length.FromEmu(InsetLeft), Length.FromEmu(InsetRight),
                    Length.FromEmu(InsetTop), Length.FromEmu(InsetBottom)),
                IsFilled ? Fill : null,
                HasLine && LineWidth > 0 && Line is { } colour
                    ? new TableBorder(Length.FromEmu(LineWidth), colour)
                    : default,
                [.. Blocks]);
        }

        /// <summary>
        /// What <c>\shpwr</c> and <c>\shpwrk</c> together mean, in LibreOffice's numbering.
        /// </summary>
        /// <remarks>
        /// <c>\shpwr</c> chooses between no text beside, parallel and run-through; <c>\shpwrk</c> then narrows
        /// a parallel wrap to one side. Zero — which is what a shape stating neither leaves — is treated as
        /// parallel, since a frame that should have moved the text and did not is the more visible error.
        /// </remarks>
        private static TextWrap WrapOf(int wrap, int side) => wrap switch
        {
            1 => TextWrap.None,
            3 or 5 => TextWrap.Through,
            _ => side switch
            {
                1 => TextWrap.Left,
                2 => TextWrap.Right,
                3 => TextWrap.Dynamic,
                _ => TextWrap.Parallel,
            },
        };
    }
}

/// <summary>
/// A floating shape as the RTF reader found it, before any font has been resolved.
/// </summary>
/// <remarks>
/// The intermediate step the other two formats do not need: their layout sources hold the document's fonts and
/// can build a <c>PageFrame</c> outright, while this reader is a token-stream state machine with no faces. So
/// the geometry is finished here and the frame's own blocks are converted once the fonts are known.
/// </remarks>
/// <param name="Offset">Its top-left, relative to whatever the two references name.</param>
/// <param name="Size">How big it is.</param>
/// <param name="Wrap">How text behaves where it is in the way.</param>
/// <param name="HorizontalRelativeTo">What the horizontal offset is measured against.</param>
/// <param name="VerticalRelativeTo">And the vertical.</param>
/// <param name="Margins">The gap kept between the shape and the text beside it.</param>
/// <param name="Padding">The gap between its own edges and its own text.</param>
/// <param name="Background">Its fill, or null when it is transparent.</param>
/// <param name="Border">Its outline, applied to all four sides, or the default when it has none.</param>
/// <param name="Blocks">Its own text, as blocks.</param>
public sealed record RtfLayoutFrame(
    DocPoint Offset,
    DocSize Size,
    TextWrap Wrap,
    FrameReference HorizontalRelativeTo,
    FrameReference VerticalRelativeTo,
    CellPadding Margins,
    CellPadding Padding,
    Colour? Background,
    TableBorder Border,
    IReadOnlyList<RtfLayoutBlock> Blocks);
