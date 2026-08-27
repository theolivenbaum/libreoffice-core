using Paperless.Core.Graphics;
using Paperless.Text.Fonts;
using Shouldly;

namespace Paperless.Text.Tests;

/// <summary>
/// Whether a slant has to be drawn is decided by font resolution, because it is the one place that
/// holds the request and the answer at the same time.
/// </summary>
/// <remarks>
/// <para>
/// <c>LogicalFontInstance::NeedsArtificialItalic()</c> is <em>the request is italic and the face is
/// not</em>, and nothing else. Two rival rules were refuted against the installed 26.2.4.2 on an
/// authored five-family deck, three sizes each: that the answer follows the family a document
/// <em>states</em> (it does not — <c>Verdana</c> is not installed, substitutes onto DejaVu Sans and
/// takes DejaVu Sans's answer), and that a real italic face is sheared too (it is not —
/// <c>Liberation Sans</c> and <c>Liberation Serif</c> both have installed italics and neither
/// leans). 15 of 15.
/// </para>
/// <para>
/// The cases below are stated in terms of <em>what is installed here</em> and skip when the
/// premise does not hold, rather than hard-coding a machine's font list into an assertion.
/// </para>
/// </remarks>
public class SyntheticObliqueResolutionTests
{
    [Theory]
    [InlineData("DejaVu Sans")]
    [InlineData("DejaVu Serif")]
    [InlineData("Verdana")]        // not installed; substitutes onto a family with no italic
    public void AFamilyWithNoInstalledItalicHasItsSlantDrawn(string family)
    {
        SystemFontResolver resolver = SystemFontResolver.Build();

        FontReference italic = resolver.Resolve(new FontRequest(family) { IsItalic = true });
        Assert.SkipWhen(italic.FaceKey.Length == 0, $"nothing resolves for {family}");
        Assert.SkipWhen(italic.IsItalic, $"an italic face for {family} is installed here");

        italic.SyntheticOblique.ShouldBeTrue();

        // And the roman half of the same family does not lean, which is what says the flag is
        // about the request rather than about the face.
        resolver.Resolve(new FontRequest(family)).SyntheticOblique.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Liberation Sans")]
    [InlineData("Liberation Serif")]
    public void AFamilyWithAnInstalledItalicUsesIt(string family)
    {
        SystemFontResolver resolver = SystemFontResolver.Build();

        FontReference italic = resolver.Resolve(new FontRequest(family) { IsItalic = true });
        Assert.SkipUnless(italic.IsItalic, $"no italic face for {family} is installed here");

        italic.SyntheticOblique.ShouldBeFalse();
    }

    [Fact]
    public void TheShearIsTheReferencesOwnAndNotAThird()
    {
        // `ARTIFICIAL_ITALIC_SKEW` is float((1<<16)/3)/(1<<16) = 0.3333333432674408, and
        // `Matrix3::skew` takes it as an *angle*, so the number that reaches a page is its tangent.
        // A shear of exactly one third would be wrong in the fourth decimal — which is the decimal
        // a PDF number keeps — so this is not a pedantic distinction.
        FontReference.SyntheticObliqueShear.ShouldBe(
            Math.Tan(0.3333333432674408), tolerance: 5e-10);

        Math.Round(FontReference.SyntheticObliqueShear, 4)
            .ShouldNotBe(Math.Round(1.0 / 3.0, 4));
    }
}
