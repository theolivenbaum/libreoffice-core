using System.Xml.Linq;
using Paperless.Ooxml;
using Shouldly;

namespace Paperless.Spreadsheets.Tests;

/// <summary>
/// Which branch of an <c>mc:AlternateContent</c> Paperless takes, and why the extended-chart
/// case is the one exception to "a choice we cannot read loses to the fallback".
/// </summary>
/// <remarks>
/// <para>
/// Excel writes an extended ("chartex") chart — Pareto, histogram, waterfall, treemap, sunburst,
/// box-and-whisker, funnel — as a <c>Requires="cx1"</c> choice with a generated rectangle beside
/// it whose only text tells the reader their Excel is too old. Taking the fallback, which is
/// otherwise the correct thing for a reader that cannot draw the choice, puts 26 words of English
/// advice on the page where the chart belongs. Measured against LibreOffice 26.2.4.2 on the two
/// corpus witnesses (<c>054_Problem_analysis_with_Pareto_chart</c> and
/// <c>051_Manufacturer_defect_analysis</c>): the reference draws none of that sentence, and
/// suppressing it took both documents from 87/61 and 95/69 extractable words to exact matches.
/// </para>
/// <para>
/// The slicer test below is the one that keeps this honest. A slicer's fallback has the *same*
/// shape — <c>id="0"</c>, an empty name, <c>noTextEdit</c>, a one-EMU outline and an advisory
/// sentence — and there the reference <em>does</em> draw it. So the rule may not be "suppress
/// advisory placeholders"; it has to be keyed on the chartex graphic-data URI and nothing else.
/// </para>
/// </remarks>
public sealed class OoxmlAlternateContentTests
{
    private const string Mce = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>Builds a spreadsheet drawing whose choice holds a graphic frame of one URI.</summary>
    /// <param name="choiceUri">The graphic-data URI the choice's frame carries.</param>
    /// <param name="requires">The prefix the choice's <c>Requires</c> names.</param>
    /// <param name="prefix">The prefix bound to the markup-compatibility namespace.</param>
    /// <param name="requiresUri">
    /// What the <c>Requires</c> prefix is bound to. The default is a namespace nothing here
    /// understands, which is the ordinary "a choice we cannot read" case. Binding it to a
    /// namespace that <em>is</em> understood is what the corpus actually contains for a slicer,
    /// and leaving that untested is how the slicer assertion below passed for four rounds while
    /// the defect it names was live on three documents.
    /// </param>
    private static XElement Drawing(
        string choiceUri, string requires, string prefix = "mc",
        string requiresUri = "urn:the-extension")
        => XElement.Parse($"""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:twoCellAnchor>
                <{prefix}:AlternateContent xmlns:{prefix}="{Mce}">
                  <{prefix}:Choice xmlns:{requires}="{requiresUri}" Requires="{requires}">
                    <xdr:graphicFrame>
                      <a:graphic><a:graphicData uri="{choiceUri}" /></a:graphic>
                    </xdr:graphicFrame>
                  </{prefix}:Choice>
                  <{prefix}:Fallback>
                    <xdr:sp>
                      <xdr:txBody><a:p><a:r><a:t>Your version of Excel is too old.</a:t></a:r></a:p></xdr:txBody>
                    </xdr:sp>
                  </{prefix}:Fallback>
                </{prefix}:AlternateContent>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """);

    private const string ExtendedChartUri = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private const string SlicerUri = "http://schemas.microsoft.com/office/drawing/2010/slicer";

    [Fact]
    public void AnExtendedChartChoiceBeatsItsAdvisoryFallback()
    {
        XElement root = Drawing(ExtendedChartUri, "cx1");
        OoxmlXml.Normalise(root);

        root.Descendants().Any(e => e.Name.LocalName == "graphicFrame").ShouldBeTrue();
        root.Value.ShouldNotContain("too old");
    }

    [Fact]
    public void TheExtendedChartExceptionDoesNotDependOnTheMarkupCompatibilityPrefix()
    {
        // ECMA-376 1st edition bound the namespace to `ve`, and eight corpus documents still do.
        XElement root = Drawing(ExtendedChartUri, "cx1", prefix: "ve");
        OoxmlXml.Normalise(root);

        root.Descendants().Any(e => e.Name.LocalName == "graphicFrame").ShouldBeTrue();
        root.Value.ShouldNotContain("too old");
    }

    [Fact]
    public void ASlicerChoiceStillLosesToItsFallback()
    {
        // The reference draws the slicer placeholder, so this must NOT follow the chartex rule.
        // Here the Requires prefix is bound to a namespace nothing understands, so the fallback
        // wins by the *general* rule; the test below is the one that exercises the slicer key.
        XElement root = Drawing(SlicerUri, "a14");
        OoxmlXml.Normalise(root);

        root.Descendants().Any(e => e.Name.LocalName == "graphicFrame").ShouldBeFalse();
        root.Value.ShouldContain("too old");
    }

    /// <summary>
    /// A slicer choice loses to its fallback even though its <c>Requires</c> names a namespace
    /// Paperless understands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what the corpus contains and what the test above did not reach. All three witnesses
    /// write <c>Requires="a14"</c> with <c>a14</c> bound to
    /// <see cref="OoxmlNamespaces.DrawingML2010"/>, which <strong>is</strong> in
    /// <c>UnderstoodExtensions</c> — so the choice was taken, the slicer frame inside it had no
    /// reader, and the anchor drew nothing at all. LibreOffice 26.2.4.2 draws the fallback
    /// rectangle: measured, its PDF holds the advisory 3 times on
    /// <c>049_Expenses_calculator</c>, 2 on <c>037_Personal_money_tracker</c> and 1 on
    /// <c>DynamicBubbleChart</c>, against 0 in ours.
    /// </para>
    /// <para>
    /// The lesson generalises past this element: <c>Requires</c> names the <em>vocabulary</em> a
    /// choice is written in, not whether its content is something a reader can draw, and for a
    /// slicer the two answers differ.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASlicerChoiceLosesToItsFallbackEvenWhenItsRequiresIsUnderstood()
    {
        XElement root = Drawing(SlicerUri, "a14", requiresUri: OoxmlNamespaces.DrawingML2010);
        OoxmlXml.Normalise(root);

        root.Descendants().Any(e => e.Name.LocalName == "graphicFrame").ShouldBeFalse();
        root.Value.ShouldContain("too old");
    }

    /// <summary>
    /// An understood choice that is <em>not</em> a slicer still wins over its fallback.
    /// </summary>
    /// <remarks>
    /// The guard against widening. 108 word-processing documents write a
    /// <c>Requires="wps"</c> choice with a VML fallback beside it, and every one of them must keep
    /// taking the choice — a rule that made any understood choice with a fallback lose would
    /// silently swap the shape content of a third of the words corpus for its VML twin.
    /// </remarks>
    [Fact]
    public void AnUnderstoodChoiceThatIsNotASlicerStillBeatsItsFallback()
    {
        XElement root = Drawing(
            "http://schemas.microsoft.com/office/word/2010/wordprocessingShape", "wps",
            requiresUri: OoxmlNamespaces.WordShape);
        OoxmlXml.Normalise(root);

        root.Descendants().Any(e => e.Name.LocalName == "graphicFrame").ShouldBeTrue();
        root.Value.ShouldNotContain("too old");
    }

    /// <summary>
    /// A slicer choice with no fallback beside it is still taken.
    /// </summary>
    /// <remarks>
    /// The slicer rule is keyed on there being a fallback to fall back <em>to</em>. Four corpus
    /// spreadsheets state the slicer URI under <c>Requires="sle15"</c>, which is not understood,
    /// and reach their fallback by the general rule already; this pins the remaining shape, where
    /// dropping the choice would lose an anchor rather than gain a placeholder.
    /// </remarks>
    [Fact]
    public void ASlicerChoiceWithNoFallbackBesideItIsStillTaken()
    {
        XElement root = XElement.Parse($"""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <mc:AlternateContent xmlns:mc="{Mce}">
                <mc:Choice xmlns:a14="{OoxmlNamespaces.DrawingML2010}" Requires="a14">
                  <xdr:graphicFrame>
                    <a:graphic><a:graphicData uri="{SlicerUri}" /></a:graphic>
                  </xdr:graphicFrame>
                </mc:Choice>
              </mc:AlternateContent>
            </xdr:wsDr>
            """);
        OoxmlXml.Normalise(root);

        root.Descendants().Any(e => e.Name.LocalName == "graphicFrame").ShouldBeTrue();
    }

    /// <summary>
    /// The guard requires a fallback to exist, so it cannot change the no-fallback case — and the
    /// no-fallback case drops the element, which is what MCE says to do: select the first choice
    /// you understand, else the fallback, else nothing. Asserted because it is *not* obvious, and
    /// because a future widening of the chartex exception must not silently start resurrecting
    /// frames here.
    /// </summary>
    [Fact]
    public void AnUnreadableChoiceWithNoFallbackBesideItIsStillDropped()
    {
        XElement root = XElement.Parse($"""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <mc:AlternateContent xmlns:mc="{Mce}">
                <mc:Choice xmlns:cx1="urn:the-extension" Requires="cx1">
                  <xdr:graphicFrame>
                    <a:graphic><a:graphicData uri="{ExtendedChartUri}" /></a:graphic>
                  </xdr:graphicFrame>
                </mc:Choice>
              </mc:AlternateContent>
            </xdr:wsDr>
            """);
        OoxmlXml.Normalise(root);

        root.Descendants().Any(e => e.Name.LocalName == "graphicFrame").ShouldBeFalse();
        root.Descendants().Any(e => e.Name.NamespaceName == Mce).ShouldBeFalse();
    }
}
