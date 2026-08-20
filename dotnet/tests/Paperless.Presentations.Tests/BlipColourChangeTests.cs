using System.Xml.Linq;
using Paperless.Core.Graphics;
using Paperless.Ooxml.DrawingML;
using Shouldly;

namespace Paperless.Presentations.Tests;

/// <summary>
/// <c>a:blip/a:clrChange</c> — PowerPoint's <em>Set Transparent Color</em> — is read, and
/// <c>from == to</c> with a transparent destination is a knockout rather than a no-op.
/// </summary>
/// <remarks>
/// <para>
/// This was a route, not a rule. <see cref="ColourKnockout"/>, its per-channel box match, its
/// binary alpha and the decoder that applies it all already existed, and the binary <c>.ppt</c>
/// path had populated them from Escher property 263 for rounds. Nothing in the tree read
/// <c>a:clrChange</c>, so every OOXML picture was drawn with its stored pixels.
/// </para>
/// <para>
/// <c>social-media-app-bulletin-january.pptx</c> page 3 is the corpus instance, and it was found
/// by a reviewer looking at a page of a document that <strong>passes every gate column</strong>.
/// Its wordmark is a 450 × 95 PNG, <strong>91.6% pure #000000</strong>, colour type 2 with no
/// alpha channel and no <c>tRNS</c>, under
/// <c>&lt;a:clrChange&gt;&lt;a:clrFrom&gt;&lt;a:srgbClr val="000000"/&gt;…&lt;a:alpha val="0"/&gt;</c>.
/// Drawn as stored it is an opaque black slab covering the words <em>Social Media</em> in the
/// title — and the title is still in the text layer, so the word count never moves and the gate
/// reports the document correct.
/// </para>
/// <para>
/// The values pinned here are read from LibreOffice's own
/// <c>lclCheckAndApplyChangeColorTransform</c>,
/// <c>oox/source/drawingml/fillproperties.cxx</c>:236-276.
/// </para>
/// </remarks>
public class BlipColourChangeTests
{
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>A <c>p:blipFill</c> whose blip carries the stated colour change.</summary>
    private static XElement BlipFill(string from, string to, string? alpha)
    {
        XElement destination = new(XName.Get("srgbClr", A), new XAttribute("val", to));
        if (alpha is not null)
        {
            destination.Add(new XElement(XName.Get("alpha", A), new XAttribute("val", alpha)));
        }

        return new XElement(
            XName.Get("blipFill", A),
            new XElement(
                XName.Get("blip", A),
                new XAttribute(XName.Get("embed", R), "rId2"),
                new XElement(
                    XName.Get("clrChange", A),
                    new XElement(
                        XName.Get("clrFrom", A),
                        new XElement(XName.Get("srgbClr", A), new XAttribute("val", from))),
                    new XElement(XName.Get("clrTo", A), destination))));
    }

    [Fact]
    public void AClrChangeIsRead()
    {
        DrawingColourChange change = DrawingFill.ReadBlip(BlipFill("000000", "000000", "0"))
            !.ColourChange.ShouldNotBeNull();

        change.From.Resolve(theme: null).ShouldBe(new Colour(0, 0, 0));

        // The destination's a:alpha rides inside the colour as an ordinary DrawingML transform,
        // which is why nothing here parses the attribute a second time.
        change.To.Resolve(theme: null).ShouldBe(new Colour(0, 0, 0, 0));
    }

    [Fact]
    public void ABlipWithoutAClrChangeStatesNone()
        => DrawingFill.ReadBlip(new XElement(
               XName.Get("blipFill", A),
               new XElement(XName.Get("blip", A))))!.ColourChange.ShouldBeNull();

    /// <summary>
    /// The rule the whole fix turns on, and the one an "obvious" reading gets backwards.
    /// </summary>
    /// <remarks>
    /// <c>fillproperties.cxx</c>:240 applies the transform when
    /// <c>(nFromColor != nToColor) || maColorChangeTo.hasTransparency()</c>. Every one of the 93
    /// occurrences in this corpus is <c>from == to</c>, so a reader that skips equal colours
    /// implements exactly nothing while looking correct.
    /// </remarks>
    [Fact]
    public void EqualColoursStillKnockOutWhenTheDestinationIsTransparent()
        => DrawingPictureEffects.Knockout(
               DrawingFill.ReadBlip(BlipFill("000000", "000000", "0")),
               theme: null, Png).ShouldNotBeNull();

    /// <summary>Equal colours and an opaque destination ask for nothing.</summary>
    [Fact]
    public void EqualColoursWithNoAlphaProduceNoKnockout()
        => DrawingPictureEffects.Knockout(
               DrawingFill.ReadBlip(BlipFill("FF0000", "FF0000", null)),
               theme: null, Png).ShouldBeNull();

    /// <summary>
    /// An opaque recolour produces no knockout, because <see cref="ColourKnockout"/> cannot
    /// express one and drawing the picture as stored is what we did before.
    /// </summary>
    [Fact]
    public void AnOpaqueRecolourProducesNoKnockout()
        => DrawingPictureEffects.Knockout(
               DrawingFill.ReadBlip(BlipFill("FFFFFF", "00FF00", null)),
               theme: null, Png).ShouldBeNull();

    [Fact]
    public void AKnockoutResolvesToItsFromColour()
    {
        ColourKnockout knockout = DrawingPictureEffects.Knockout(
            DrawingFill.ReadBlip(BlipFill("000000", "000000", "0")), theme: null, Png)
            .ShouldNotBeNull();

        knockout.Colour.ShouldBe(new Colour(0, 0, 0));
        knockout.Matches(0, 0, 0).ShouldBeTrue();
    }

    /// <summary>
    /// The tolerance is chosen by the stored format, and it is not one number.
    /// </summary>
    /// <remarks>
    /// <c>fillproperties.cxx</c>:245-264, the fix for tdf#149670. A lossy JPEG smears a flat
    /// background into a cloud of near-matches, so an exact match leaves a halo; a lossless PNG
    /// stores it exactly, so a wide tolerance eats real picture content. Note 15 and 1 are both
    /// different from the 9 the binary Escher call site passes, which is
    /// <see cref="ColourKnockout.DefaultTolerance"/> and is also correct — for that call site.
    /// </remarks>
    [Theory]
    [InlineData(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 }, 1)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, 15)]
    [InlineData(new byte[] { (byte)'B', (byte)'M', 0, 0 }, 0)]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 0x2A, 0x00 }, 1)]
    [InlineData(new byte[] { (byte)'M', (byte)'M', 0x00, 0x2A }, 1)]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8' }, 9)]
    public void TheToleranceIsChosenByTheStoredFormat(byte[] encoded, int expected)
        => DrawingPictureEffects.ToleranceFor(encoded).ShouldBe(expected);

    /// <summary>
    /// The knockout the corpus actually asks for, end to end, at the tolerance a PNG gets.
    /// </summary>
    /// <remarks>
    /// Tolerance 1 on a PNG means a pixel one step off pure black still goes — and one two steps
    /// off does not. That is the boundary tdf#149670 moved, and pinning it is what stops a
    /// future change quietly restoring the flat 9, which on this deck's anti-aliased wordmark
    /// would eat the dark blue of the letter <em>chat</em>.
    /// </remarks>
    [Fact]
    public void APngKnockoutMatchesOneStepOffButNotTwo()
    {
        ColourKnockout knockout = DrawingPictureEffects.Knockout(
            DrawingFill.ReadBlip(BlipFill("000000", "000000", "0")), theme: null, Png)
            .ShouldNotBeNull();

        knockout.Tolerance.ShouldBe(1);
        knockout.Matches(1, 1, 1).ShouldBeTrue();
        knockout.Matches(2, 0, 0).ShouldBeFalse();
    }

    private static ReadOnlySpan<byte> Png => [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10];
}
