using System.Text;
using Paperless.Text.Fonts;
using Paperless.WordProcessing.Ww8;
using Shouldly;

namespace Paperless.WordProcessing.Tests;

/// <summary>
/// A DOC's font table declares a family <em>class</em> beside each name, and it decides the substitute.
/// </summary>
/// <remarks>
/// <para>
/// The <c>ff</c> field of an <c>FFN</c>'s first byte. It changes nothing while the named family is
/// installed, and when it is not it decides which face the document is drawn in — LibreOffice hands the
/// class to fontconfig as a second <c>FC_FAMILY</c>, <c>"serif"</c> for <c>FF_ROMAN</c> and <c>"sans"</c>
/// for <c>FF_SWISS</c> (<c>FontConfigManager::Substitute</c>,
/// <c>vcl/unx/generic/font/fontconfig.cxx</c>), and the generic then beats every weak alias.
/// </para>
/// <para>
/// Measured, not inferred: <c>words/batch-004/doc/1447.doc</c> sets its body in a family called
/// <c>Times</c> declared <c>FF_ROMAN</c>. LibreOffice 26.2.4.2 draws it in DejaVu Serif and we drew it in
/// Liberation Serif, whose glyphs are narrower — the reference fits about 11% fewer characters to the
/// line and takes nine lines over the paragraph where we took seven. With the class read, every line
/// break on the document's first page matches the reference exactly.
/// </para>
/// <para>
/// The bytes are built here rather than taken from a corpus document because the whole point is the
/// <em>flags</em> byte, and no committable fixture varies it: LibreOffice's own DOC export writes
/// <c>FF_DONTCARE</c> for the faces it knows. See <c>dotnet/probes/words-pages-01/results.md</c>.
/// </para>
/// </remarks>
public sealed class DocFontFamilyClassTests
{
    /// <summary>Where an entry's name starts, after the flags, weight, charset, alt index, PANOSE and fs.</summary>
    private const int NameOffset = 1 + 2 + 1 + 1 + 10 + 24;

    /// <summary>An <c>SttbfFfn</c> holding one entry per (name, ff) pair given.</summary>
    private static byte[] Table(params (string Name, int Family)[] entries)
    {
        List<byte> bytes = [0, 0, 0, 0];  // count and the extra-data length, both skipped by the walk

        foreach ((string name, int family) in entries)
        {
            byte[] encoded = Encoding.Unicode.GetBytes(name);
            int payload = NameOffset + encoded.Length + 2;

            bytes.Add((byte)payload);
            bytes.Add((byte)(((family & 0x7) << 4) | 0x2));  // ff in bits 4-6, prq = variable
            bytes.AddRange(new byte[NameOffset - 1]);
            bytes.AddRange(encoded);
            bytes.AddRange([0, 0]);
        }

        return [.. bytes];
    }

    [Theory]
    [InlineData(0, DeclaredFontFamily.Unknown)]
    [InlineData(1, DeclaredFontFamily.Roman)]
    [InlineData(2, DeclaredFontFamily.Swiss)]
    [InlineData(3, DeclaredFontFamily.Modern)]
    [InlineData(4, DeclaredFontFamily.Script)]
    [InlineData(5, DeclaredFontFamily.Decorative)]
    public void TheFfFieldIsReadFromBitsFourToSix(int stated, DeclaredFontFamily expected)
    {
        // Bits 4 to 6, so `(first >> 4) & 7` — beside `prq` in bits 0 and 1 and `fTrueType` in bit 2,
        // which the low nibble above sets to 2 so that a reader taking the whole byte would get this
        // wrong rather than accidentally right.
        Ww8FontTable table = Ww8FontTable.Parse(Table(("Times", stated)));

        table.Name(0).ShouldBe("Times");
        table.Family(0).ShouldBe(expected);
    }

    [Fact]
    public void TheDeclaredClassesAreKeyedTheWayTheResolverLooksThemUp()
    {
        Ww8FontTable table = Ww8FontTable.Parse(
            Table(("Times New Roman", 1), ("Helvetica", 2), ("Courier New", 3), ("Wingdings", 0)));

        IReadOnlyDictionary<string, DeclaredFontFamily> declared = table.DeclaredFamilies();

        // Normalised names, because that is what the resolver's own table is keyed on and a document
        // spells one family several ways.
        declared["timesnewroman"].ShouldBe(DeclaredFontFamily.Roman);
        declared["helvetica"].ShouldBe(DeclaredFontFamily.Swiss);
        declared["couriernew"].ShouldBe(DeclaredFontFamily.Modern);

        // An entry stating FF_DONTCARE says nothing, so it is left out rather than carried as a class
        // meaning "unknown": an absent key and a key holding Unknown resolve the same way, and leaving
        // it out is what keeps the dictionary empty — and the whole rule inert — for the documents that
        // declare nothing, which is most of them.
        declared.ContainsKey("wingdings").ShouldBeFalse();
        declared.Count.ShouldBe(3);
    }

    [Fact]
    public void ATableThatDeclaresNothingProducesNoEntries()
    {
        Ww8FontTable.Parse(Table(("Arial", 0), ("Symbol", 0))).DeclaredFamilies().ShouldBeEmpty();
        Ww8FontTable.Empty.DeclaredFamilies().ShouldBeEmpty();
        Ww8FontTable.Empty.Family(0).ShouldBe(DeclaredFontFamily.Unknown);
    }

    [Fact]
    public void AFamilyIndexOutsideTheTableIsUnknownRatherThanAnError()
    {
        // The same tolerance Name(int) has, and for the same reason: a sprmCRgFtc0 naming an index the
        // table does not hold is a malformed document, not a reason to fail the layout.
        Ww8FontTable table = Ww8FontTable.Parse(Table(("Times", 1)));

        table.Family(-1).ShouldBe(DeclaredFontFamily.Unknown);
        table.Family(9).ShouldBe(DeclaredFontFamily.Unknown);
    }
}
