using Paperless.Core.Diagnostics;
using Paperless.Core.Graphics;

namespace Paperless.Text.Fonts;

/// <summary>One face found on the machine: where it is, and what it says about itself.</summary>
/// <param name="Path">The file the face lives in.</param>
/// <param name="FaceIndex">Which face of that file, for a collection.</param>
/// <param name="FamilyName">The family name the face reports.</param>
/// <param name="Weight">The weight on the OpenType 1-1000 scale.</param>
/// <param name="IsItalic">Whether the face is italic or oblique.</param>
/// <param name="IsFixedPitch">Whether every glyph has the same advance.</param>
public readonly record struct InstalledFace(
    string Path,
    int FaceIndex,
    string FamilyName,
    int Weight,
    bool IsItalic,
    bool IsFixedPitch)
{
    /// <summary>A stable key for the face, for caching and for deduplicating embedded fonts.</summary>
    public string FaceKey => FaceIndex == 0 ? Path : $"{Path}#{FaceIndex}";

    /// <summary>The family name in the form the substitution table is keyed on.</summary>
    public string NormalisedFamily => FontSubstitutions.Normalise(FamilyName);
}

/// <summary>
/// Finds the faces installed on this machine, by reading their <c>name</c> tables.
/// </summary>
/// <remarks>
/// <para>
/// The font files are read rather than fontconfig being asked. That is a deliberate trade: it costs a
/// scan of the font directories at start-up and gives up any substitution rules an administrator
/// configured, in exchange for the same answer on every machine and no native dependency. Since the
/// substitution chain comes from LibreOffice's own table rather than from the platform, going through
/// fontconfig would add a second source of truth rather than the missing one.
/// </para>
/// <para>
/// One decision is the exception, and it is marked out rather than quietly folded in: which of
/// several installed faces draws a character <em>nothing on LibreOffice's fallback list</em> covers.
/// No table answers that, and the answer is measurably a property of the machine's fontconfig
/// configuration rather than of the fonts — see <see cref="FontconfigPreferences"/>. That is read,
/// and only for that. Everything above it still comes from the table.
/// </para>
/// <para>
/// Only the family name, weight and slant are read at index time, which is a few kilobytes per file
/// rather than the whole face. A machine with several hundred fonts is indexed in well under a
/// second, and nothing is parsed twice: the face itself is loaded only when something asks to measure
/// with it.
/// </para>
/// </remarks>
public sealed class SystemFontIndex
{
    /// <summary>The directories searched, in order, when none is given.</summary>
    /// <remarks>
    /// The user's own fonts come first, because a font a user installed for themselves is the one
    /// they expect a document to render in.
    /// </remarks>
    public static IReadOnlyList<string> DefaultDirectories { get; } =
    [
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "fonts"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts"),
        "/usr/local/share/fonts",
        "/usr/share/fonts",
        "/Library/Fonts",
        "/System/Library/Fonts",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"),
    ];

    /// <summary>The extensions a font file may have.</summary>
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc", ".otc"];

    private readonly Dictionary<string, List<InstalledFace>> _families =
        new(StringComparer.Ordinal);

    private readonly List<Diagnostic> _diagnostics = [];

    /// <summary>Every face found, in no particular order.</summary>
    public IEnumerable<InstalledFace> Faces => _families.Values.SelectMany(f => f);

    /// <summary>How many families were found.</summary>
    public int FamilyCount => _families.Count;

    /// <summary>Files that looked like fonts and could not be read.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>Scans the given directories, or the platform's defaults.</summary>
    public static SystemFontIndex Build(IEnumerable<string>? directories = null)
    {
        SystemFontIndex index = new();

        foreach (string directory in directories ?? DefaultDirectories)
        {
            if (!Directory.Exists(directory)) continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A directory the process cannot read is not a reason to have no fonts at all.
                continue;
            }

            foreach (string file in files)
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    continue;

                index.Add(file);
            }
        }

        return index;
    }

    /// <summary>The faces of a family, or empty when none is installed under that name.</summary>
    public IReadOnlyList<InstalledFace> Family(string? familyName)
        => _families.TryGetValue(FontSubstitutions.Normalise(familyName), out List<InstalledFace>? faces)
            ? faces
            : [];

    /// <summary>True when a family is installed.</summary>
    public bool Has(string? familyName) => Family(familyName).Count > 0;

    /// <summary>
    /// The face of a family closest to a requested weight and slant.
    /// </summary>
    /// <remarks>
    /// Slant first, then weight distance. Getting an upright face where an italic was asked for is
    /// visibly wrong in a way that a hundred points of weight is not, so slant is never traded for a
    /// closer weight — which is what sorting by a combined score would do.
    /// </remarks>
    public InstalledFace? Best(string? familyName, int weight, bool italic)
    {
        IReadOnlyList<InstalledFace> faces = Family(familyName);
        if (faces.Count == 0) return null;

        InstalledFace? best = null;
        int bestScore = int.MaxValue;

        foreach (InstalledFace face in faces)
        {
            int score = (face.IsItalic == italic ? 0 : 10_000) + Math.Abs(face.Weight - weight);
            if (score >= bestScore) continue;

            bestScore = score;
            best = face;
        }

        return best;
    }

    private void Add(string path)
    {
        OpenTypeFace? first;
        try
        {
            first = OpenTypeFace.ReadFile(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (first is null)
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Information, "PL5000",
                "A file with a font extension is not a font this reader understands, so it has been "
                + "ignored.",
                new DiagnosticLocation(path)));
            return;
        }

        int faces = first.File.FaceCount;
        for (int index = 0; index < faces; index++)
        {
            OpenTypeFace? face = index == 0 ? first : SafeRead(path, index);
            if (face?.FamilyName is not { Length: > 0 } family) continue;

            InstalledFace installed = new(
                path, index, family, face.Weight, face.IsItalic, face.IsFixedPitch);

            if (!_families.TryGetValue(installed.NormalisedFamily, out List<InstalledFace>? list))
            {
                list = [];
                _families[installed.NormalisedFamily] = list;
            }

            // The same family may be installed twice — a system copy and a user copy — and the first
            // directory searched wins, since that is the one a user expects.
            if (!list.Any(f => f.Weight == installed.Weight && f.IsItalic == installed.IsItalic))
                list.Add(installed);
        }
    }

    private static OpenTypeFace? SafeRead(string path, int faceIndex)
    {
        try
        {
            return OpenTypeFace.ReadFile(path, faceIndex);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Resolves a document's font request against the installed faces, the way LibreOffice would.
/// </summary>
/// <remarks>
/// <para>
/// The order is LibreOffice's: the requested family if it is installed, then its substitution chain
/// from LibreOffice's own table, then a face of the right general shape, then a last resort. Never
/// null — a document that names a font nobody has still has to render, and refusing to choose would
/// turn a cosmetic difference into a failure.
/// </para>
/// <para>
/// Every substitution is reported, and whether it was metric-compatible is reported with it. That
/// distinction is the difference between "this page looks slightly different" and "every page after
/// this one is wrong", because a substitute with different advance widths reflows the text and moves
/// every break after the first one.
/// </para>
/// </remarks>
public sealed class SystemFontResolver : IFontResolver, IGlyphFallbackResolver
{
    private readonly SystemFontIndex _index;
    private readonly Dictionary<string, OpenTypeFace> _loaded = new(StringComparer.Ordinal);
    private readonly Dictionary<OpenTypeFace, string> _keys = new(ReferenceEqualityComparer.Instance);
    private readonly List<FontSubstitution> _substitutions = [];
    private readonly Dictionary<(int CodePoint, int Weight, bool Italic), OpenTypeFace?> _fallbacks = [];
    private readonly List<GlyphFallback> _glyphFallbacks = [];
    private readonly FontconfigPreferences _preferences;

    /// <summary>Creates a resolver over an index of installed faces.</summary>
    /// <param name="index">The faces found on the machine.</param>
    /// <param name="preferences">
    /// The machine's fontconfig preferences, used only to order the last-resort glyph fallback;
    /// null takes this machine's own, which is read once per process.
    /// </param>
    public SystemFontResolver(SystemFontIndex index, FontconfigPreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        _index = index;
        _preferences = preferences ?? FontconfigPreferences.Machine;
    }

    /// <summary>Creates a resolver over the platform's font directories.</summary>
    public static SystemFontResolver Build() => new(SystemFontIndex.Build());

    /// <summary>The index this resolver searches.</summary>
    public SystemFontIndex Index => _index;

    /// <summary>
    /// Every substitution made so far, in the order they were made.
    /// </summary>
    /// <remarks>
    /// Recorded rather than logged: a silent substitution explains most otherwise-baffling reflow
    /// differences, so a caller comparing output against a reference wants the list, not a message
    /// that went to a log nobody read.
    /// </remarks>
    public IReadOnlyList<FontSubstitution> Substitutions => _substitutions;

    /// <summary>
    /// Every mid-run glyph fallback made so far, one per contiguous stretch, in the order they were made.
    /// </summary>
    /// <remarks>
    /// The same argument as <see cref="Substitutions"/>, and a sharper one: a fallback face is
    /// chosen for its coverage rather than for its metrics, so it is almost never metric-compatible
    /// with the face it stands in for. A run that quietly used two faces measures differently from
    /// one that used one, and without this list there is nothing to distinguish that from a layout
    /// bug. Characters that nothing installed could draw are recorded too, with a null family — a
    /// missing-glyph box is worth knowing about before it is seen on a page.
    /// </remarks>
    public IReadOnlyList<GlyphFallback> GlyphFallbacks => _glyphFallbacks;

    /// <inheritdoc/>
    /// <remarks>
    /// LibreOffice's own list first (<c>ImplInitGenericGlyphFallback</c> in
    /// <c>vcl/source/font/PhysicalFontCollection.cxx</c>), then anything installed that covers the
    /// character. The order matters for more than tidiness: the face that draws a character decides
    /// its advance width, so two renderers that pick different faces break the line differently.
    /// </remarks>
    public OpenTypeFace? FallbackFor(int codePoint, int weight = 400, bool isItalic = false)
    {
        // Cached because a run of unsupported text asks the same question for every character, and
        // answering it means opening font files until one covers the character.
        if (_fallbacks.TryGetValue((codePoint, weight, isItalic), out OpenTypeFace? cached))
        {
            return cached;
        }

        OpenTypeFace? found = null;

        foreach (string family in GlyphFallbackFamilies.InOrder)
        {
            if (_index.Best(family, weight, isItalic) is not { } candidate) continue;
            if (Covers(candidate, codePoint) is not { } face) continue;

            found = face;
            break;
        }

        // Nothing on LibreOffice's list covers it. Anything installed that does is still better than
        // a box, and the order is the machine's fontconfig preference for a generic family — which
        // is what LibreOffice itself lands on here, since it asks fontconfig before it ever reads
        // the list above. Ordinal family name remains the last resort, so a machine with no
        // fontconfig behaves exactly as this did before.
        //
        // The rank comes ahead of slant and weight because fontconfig scores family above both
        // (`PRI_FAMILY_WEAK` precedes `PRI_SLANT` and `PRI_WEIGHT` in `fcmatch.c`), and because at
        // this point the primary face has already failed: a character drawn in the preferred family
        // upright is closer to the reference than the same character drawn in an unrelated family
        // that happens to be italic.
        found ??= _index.Faces
            .OrderBy(face => _preferences.RankOf(face.FamilyName))
            .ThenBy(face => face.IsItalic == isItalic ? 0 : 1)
            .ThenBy(face => Math.Abs(face.Weight - weight))
            .ThenBy(face => face.FamilyName, StringComparer.Ordinal)
            .Select(face => Covers(face, codePoint))
            .FirstOrDefault(face => face is not null);

        _fallbacks[(codePoint, weight, isItalic)] = found;
        return found;
    }

    /// <summary>Records a fallback, resolved or not, for the caller comparing against a reference.</summary>
    public void RecordGlyphFallback(int codePoint, string? fromFamily, string? toFamily)
        => _glyphFallbacks.Add(new GlyphFallback(codePoint, fromFamily, toFamily));

    /// <summary>The face behind an installed entry when it covers a character, else null.</summary>
    private OpenTypeFace? Covers(InstalledFace candidate, int codePoint)
    {
        OpenTypeFace? face = LoadCached(candidate.FaceKey);
        return face is not null && face.HasGlyphFor(codePoint) ? face : null;
    }

    /// <summary>Loads a face by key through the resolver's own cache, or null when it cannot be read.</summary>
    private OpenTypeFace? LoadCached(string faceKey)
    {
        if (_loaded.TryGetValue(faceKey, out OpenTypeFace? existing))
        {
            // Also on the hit, because the face may have been loaded through `LoadOpenType` — which
            // fills `_loaded` and not `_keys` — and a fallback that finds it there would otherwise
            // have no key to be embedded by.
            _keys[existing] = faceKey;
            return existing;
        }

        (string path, int index) = SplitKey(faceKey);

        OpenTypeFace? face;
        try
        {
            face = path.Length > 0 ? OpenTypeFace.ReadFile(path, index) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A font that cannot be read is not a reason to abandon the search for one that can.
            face = null;
        }

        if (face is not null)
        {
            _loaded[faceKey] = face;
            _keys[face] = faceKey;
        }

        return face;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A reverse lookup over the faces this resolver has loaded rather than a fresh resolution: the
    /// face is the answer already given, and re-resolving by family name could land on a different
    /// file — a family with several faces installed, or a collection whose members share one name.
    /// Reference equality is the right key for exactly that reason.
    /// </remarks>
    public FontReference? ReferenceFor(OpenTypeFace face)
        => face is not null && _keys.TryGetValue(face, out string? faceKey)
            ? new FontReference
            {
                FamilyName = face.FamilyName ?? string.Empty,
                RequestedFamily = face.FamilyName ?? string.Empty,
                Weight = face.Weight,
                IsItalic = face.IsItalic,
                FaceKey = faceKey,
            }
            : null;

    /// <summary>The families a resolver falls back to when nothing in the chain is installed.</summary>
    /// <remarks>
    /// <para>
    /// DejaVu first, and that ordering is measured rather than chosen. A chain that names none of
    /// the faces on the machine is where LibreOffice stops consulting its own table and asks
    /// fontconfig, and fontconfig's generic families resolve to DejaVu on a stock Linux
    /// configuration — <c>60-latin.conf</c> heads every one of its three preference lists with it.
    /// Verified against LibreOffice 24.2.7.2 on this machine over fifty-five families: every single
    /// one that reached the generic path landed on DejaVu Sans, DejaVu Serif or DejaVu Sans Mono,
    /// and none landed on Liberation.
    /// </para>
    /// <para>
    /// Preferring Liberation here looked right and is not: Liberation is the metric-compatible
    /// stand-in for Arial, Times New Roman and Courier New specifically, and those three reach it
    /// through their <em>chains</em>, which name it outright. By the time control arrives here the
    /// request is for something Liberation was never built to imitate, so its metrics carry no
    /// authority — and choosing it puts a face on the page that LibreOffice would not have used.
    /// </para>
    /// </remarks>
    private static readonly string[] SerifFallbacks =
        ["dejavuserif", "liberationserif", "timesnewroman", "freeserif", "notoserif"];

    private static readonly string[] SansFallbacks =
        ["dejavusans", "liberationsans", "arial", "freesans", "notosans"];

    private static readonly string[] MonoFallbacks =
        ["dejavusansmono", "liberationmono", "couriernew", "freemono", "notosansmono"];

    /// <summary>The faces to try for a request that names no family at all.</summary>
    /// <remarks>
    /// A blank family name is not a family nobody has installed — it is a document expressing no
    /// preference, and the answer to that is the application's default rather than the answer given
    /// to an unrecognised name. So it comes from the configuration's <c>DefaultFonts</c> node rather
    /// than its <c>FontSubstitutions</c> node, and it is read from there rather than transcribed:
    /// the list is data in the tree, and a hand-copied prefix of it would silently diverge on a
    /// machine whose installed faces differ from this one's. Measured: a fixture declaring no font
    /// renders in Liberation Serif under LibreOffice 24.2.7.2 here, which is what this list heads
    /// with. Routing the blank case through the generic unknown-family rule instead sets every such
    /// document in DejaVu Sans and reflows all of them.
    /// </remarks>
    private static IReadOnlyList<string> DefaultFallbacks => FontSubstitutions.DefaultLatinTextChain;

    /// <inheritdoc/>
    public FontReference Resolve(FontRequest request)
    {
        // The document's own embedded font wins over anything installed: it is what the author saw,
        // and it is the only face guaranteed to have the metrics the document was laid out against.
        if (request.EmbeddedFaceKey is { Length: > 0 } embedded)
        {
            return new FontReference
            {
                FamilyName = request.FamilyName,
                RequestedFamily = request.FamilyName,
                Weight = request.Weight,
                IsItalic = request.IsItalic,
                FaceKey = embedded,
            };
        }

        // The requested family, if it is here.
        if (_index.Best(request.FamilyName, request.Weight, request.IsItalic) is { } exact)
            return Reference(request, exact, requested: request.FamilyName);

        // What the *document* declared about the family, when it declared something acted on.
        //
        // This runs **before** the substitution chain, and that ordering is the whole point rather than
        // a detail. `FontConfigManager::Substitute` (`vcl/unx/generic/font/fontconfig.cxx`) is the
        // *pre-match* substitution: it runs before LibreOffice consults `VCL.xcu` at all, and it asks
        // fontconfig for the requested name plus a generic family. A name fontconfig aliases only
        // weakly therefore loses to the generic and never reaches the chain. Measured against 26.2.4.2
        // with authored one-paragraph documents on the four names that can tell the two orderings
        // apart, because each has a chain entry that *is* installed: `Times`, `Helvetica`, `Albany`
        // and `Thorndale` all answer DejaVu once a class is declared, where the chain answers
        // Liberation.
        string[]? generic = DeclaredGenericFor(request);

        if (generic is not null)
        {
            // Except a *strong* metric alias, which fontconfig's own `30-metric-aliases.conf` binds hard
            // enough to beat a generic family. The test is whether an installed face declares itself the
            // equivalent of the very name asked for — Liberation Sans of Arial, Carlito of Calibri — and
            // not whether the two are transitively compatible: Liberation Sans is metrically Helvetica's
            // equal through Arial, and `Helvetica` declared swiss still renders in DejaVu Sans.
            foreach (string candidate in FontSubstitutions.ChainFor(request.FamilyName))
            {
                if (!ClaimsEquivalenceWith(candidate, request.FamilyName)) continue;
                if (_index.Best(candidate, request.Weight, request.IsItalic) is not { } aliased) continue;

                Record(request, aliased);
                return Reference(request, aliased, requested: request.FamilyName);
            }

            foreach (string candidate in generic)
            {
                if (_index.Best(candidate, request.Weight, request.IsItalic) is not { } preferred)
                    continue;

                Record(request, preferred);
                return Reference(request, preferred, requested: request.FamilyName);
            }
        }

        // LibreOffice's substitution chain, in its own order.
        foreach (string candidate in FontSubstitutions.ChainFor(request.FamilyName))
        {
            if (_index.Best(candidate, request.Weight, request.IsItalic) is not { } substitute)
                continue;

            Record(request, substitute);
            return Reference(request, substitute, requested: request.FamilyName);
        }

        // Nothing named matched, so fall back by shape. A monospaced request must not land on a
        // proportional face: the document is relying on the columns lining up.
        IReadOnlyList<string> fallbacks = GenericFallbacks(request);

        foreach (string candidate in fallbacks)
        {
            if (_index.Best(candidate, request.Weight, request.IsItalic) is not { } fallback) continue;

            Record(request, fallback);
            return Reference(request, fallback, requested: request.FamilyName);
        }

        // Last resort: whatever is installed. A document still has to render, and a caller comparing
        // against a reference has the substitution list to explain why it does not match.
        InstalledFace? any = _index.Faces
            .OrderBy(f => f.IsItalic == request.IsItalic ? 0 : 1)
            .ThenBy(f => Math.Abs(f.Weight - request.Weight))
            .ThenBy(f => f.FamilyName, StringComparer.Ordinal)
            .Cast<InstalledFace?>()
            .FirstOrDefault();

        if (any is not { } last)
        {
            // No fonts at all. Report the request unchanged rather than inventing a name: a caller
            // that cannot load the face will find out, and a made-up family would hide why.
            return new FontReference
            {
                FamilyName = request.FamilyName,
                RequestedFamily = request.FamilyName,
                Weight = request.Weight,
                IsItalic = request.IsItalic,
                FaceKey = string.Empty,
            };
        }

        Record(request, last);
        return Reference(request, last, requested: request.FamilyName);
    }

    /// <inheritdoc/>
    public IFontFace LoadFace(FontReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!_loaded.TryGetValue(reference.FaceKey, out OpenTypeFace? face))
        {
            (string path, int index) = SplitKey(reference.FaceKey);
            face = path.Length > 0 ? OpenTypeFace.ReadFile(path, index) : null;

            if (face is null)
            {
                throw new Core.MalformedDocumentException(
                    $"The face '{reference.FaceKey}' could not be read as a font.");
            }

            _loaded[reference.FaceKey] = face;
        }

        return new ResolvedFontFace(reference, face);
    }

    /// <summary>
    /// Loads a reference as the concrete OpenType face, which is what layout needs.
    /// </summary>
    /// <remarks>
    /// <see cref="LoadFace"/> answers the <see cref="IFontFace"/> a caller supplying its own embedded
    /// faces can implement, and that interface deliberately exposes only coverage and vertical metrics.
    /// Measuring and shaping need the tables themselves, so layout asks for this instead — and gets the
    /// same cached instance, since the bytes belong to the resolver either way.
    /// </remarks>
    public OpenTypeFace LoadOpenType(FontReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return ((ResolvedFontFace)LoadFace(reference)).OpenType;
    }

    private static (string Path, int Index) SplitKey(string faceKey)
    {
        int hash = faceKey.LastIndexOf('#');
        return hash < 0 || !int.TryParse(faceKey[(hash + 1)..], out int index)
            ? (faceKey, 0)
            : (faceKey[..hash], index);
    }

    private static FontReference Reference(FontRequest request, InstalledFace face, string requested)
        => new()
        {
            FamilyName = face.FamilyName,
            RequestedFamily = requested,
            Weight = face.Weight,
            IsItalic = face.IsItalic,
            FaceKey = face.FaceKey,
        };

    private void Record(FontRequest request, InstalledFace chosen)
        => _substitutions.Add(new FontSubstitution(
            request.FamilyName,
            chosen.FamilyName,
            FontSubstitutions.AreMetricCompatible(request.FamilyName, chosen.FamilyName)));

    /// <summary>
    /// The shape to fall back to once nothing the chain named turned out to be installed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LibreOffice's own <c>FontType</c> decides it, which is the point: the configuration that says
    /// what to substitute also says what shape the family is, and reading the second half means the
    /// answer is data rather than a guess. A document's declared pitch still wins, because a request
    /// marked fixed is relying on its columns whatever the family is called.
    /// </para>
    /// <para>
    /// Where the table has never heard of the family the answer is sans-serif, and that is not a
    /// coin toss. This path is what LibreOffice reaches by asking fontconfig, and fontconfig's reply
    /// for a name it does not recognise is its default family — DejaVu Sans. Measured against
    /// LibreOffice 24.2.7.2 here, every unrecognised family probed resolved to DejaVu Sans: Aptos,
    /// Segoe UI, Roboto, Lato, Montserrat, Myriad Pro, Futura, Optima, Univers and the rest. The
    /// previous rule guessed serif for all of them, on the reasoning that a name carrying no hint is
    /// probably a roman — which is the wrong default and, worse, wrong for the modern UI faces
    /// documents actually name.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> GenericFallbacks(FontRequest request)
    {
        if (request.Pitch == FontPitch.Fixed) return MonoFallbacks;

        // "No font named" and "a font nobody has" are different questions with different answers,
        // and only the second one is fontconfig's to answer.
        if (string.IsNullOrWhiteSpace(request.FamilyName)) return DefaultFallbacks;

        // What the *document* says the family is, which beats what the table says the *name* is.
        // The same helper the pre-match step above uses, so the two cannot drift apart — reaching
        // here at all means the declared generic named nothing installed either.
        if (DeclaredGenericFor(request) is { } declared) return declared;

        return FontSubstitutions.ClassOf(request.FamilyName) switch
        {
            FontFamilyClass.Fixed => MonoFallbacks,
            FontFamilyClass.Serif => SerifFallbacks,

            // A symbol face has no shape-compatible stand-in among the text faces, so there is
            // nothing better to do than treat it as text and let glyph fallback place what it can.
            _ => SansFallbacks,
        };
    }

    /// <summary>
    /// The faces the generic family a document's own declaration implies, or null when it implies none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two classes and only two, because that is what LibreOffice sends: <c>FontConfigManager::Substitute</c>
    /// (<c>vcl/unx/generic/font/fontconfig.cxx</c>) appends <c>"serif"</c> as a second <c>FC_FAMILY</c>
    /// for <c>FAMILY_ROMAN</c> and <c>"sans"</c> for <c>FAMILY_SWISS</c>, and nothing at all for any
    /// other family type. A monospaced class looks as though it ought to add <c>"monospace"</c> and
    /// does not — measured, <c>Times</c> declared <c>modern</c> still comes out Liberation Serif, which
    /// is the plain <c>fc-match Times</c> answer. The readers collapse those codes to
    /// <see cref="FontFamilyClass.Unknown"/> for the same reason.
    /// </para>
    /// <para>
    /// <strong>A declared fixed pitch wins over a declared family</strong>, because a document relying
    /// on its columns lining up is making the stronger statement: measured, <c>Garamond</c> declared
    /// roman <em>and</em> fixed answers DejaVu Sans Mono rather than DejaVu Serif.
    /// </para>
    /// <para>
    /// <strong>A pi face is exempt</strong>, whatever class the document put beside it. Every Word
    /// document that uses <c>Symbol</c> declares it roman and there is no roman equivalent of a font of
    /// arrows and Greek letters; fontconfig knows it too and binds the name hard enough to survive a
    /// generic — <c>fc-match "Symbol,serif"</c> and <c>fc-match Symbol</c> both answer OpenSymbol, and
    /// so does 26.2.4.2 on a document declaring Symbol as a roman. Without this the declaration sent
    /// every Symbol run to DejaVu Serif, which draws the characters rather than the symbols they stand
    /// for — measured on <c>ABCD-FE-01-00 Flight Envelope.docx</c> and its sibling.
    /// </para>
    /// <para>
    /// The lists themselves are the existing shape fallbacks, already documented as the answer
    /// fontconfig's generic families give on this configuration and measured face by face.
    /// </para>
    /// </remarks>
    private static string[]? DeclaredGenericFor(FontRequest request)
    {
        if (request.Pitch == FontPitch.Fixed) return MonoFallbacks;

        if (FontSubstitutions.ClassOf(request.FamilyName) == FontFamilyClass.Symbol) return null;

        return request.DeclaredClass switch
        {
            FontFamilyClass.Serif => SerifFallbacks,
            FontFamilyClass.SansSerif => SansFallbacks,
            _ => null,
        };
    }

    /// <summary>
    /// True when an installed family declares itself the metric equivalent of the name asked for.
    /// </summary>
    /// <remarks>
    /// Deliberately one-directional and deliberately not <see cref="FontSubstitutions.AreMetricCompatible"/>,
    /// which is transitive: Liberation Sans and Helvetica are compatible through Arial, and a document
    /// naming Helvetica as a grotesque still renders in DejaVu Sans. What survives a generic family in
    /// fontconfig is the alias bound to the requested name itself, so that is what is tested.
    /// </remarks>
    private static bool ClaimsEquivalenceWith(string candidate, string requested)
        => FontSubstitutions.MicrosoftEquivalentOf(candidate) is { } equivalent
           && FontSubstitutions.Normalise(equivalent) == FontSubstitutions.Normalise(requested);

    /// <summary>A face loaded through a resolver.</summary>
    private sealed class ResolvedFontFace(FontReference reference, OpenTypeFace face) : IFontFace
    {
        /// <inheritdoc/>
        public FontReference Reference { get; } = reference;

        /// <inheritdoc/>
        public int UnitsPerEm => face.UnitsPerEm;

        /// <inheritdoc/>
        public FontVerticalMetrics VerticalMetrics =>
            LineSpacing.ResolveDecorations(face, LineSpacing.Resolve(face));

        /// <summary>The underlying face, for callers needing advances or the line metrics.</summary>
        public OpenTypeFace OpenType => face;

        /// <inheritdoc/>
        public bool HasGlyphFor(int codePoint) => face.HasGlyphFor(codePoint);

        /// <inheritdoc/>
        /// <remarks>
        /// Nothing to release: the face's bytes are cached by the resolver that loaded it, so
        /// disposing one view of it must not invalidate the others.
        /// </remarks>
        public void Dispose() { }
    }
}

/// <summary>One substitution a resolver made.</summary>
/// <param name="Requested">The family the document asked for.</param>
/// <param name="Chosen">The family that was used instead.</param>
/// <param name="IsMetricCompatible">
/// True when the substitute has the original's advance widths, so every line breaks where it did.
/// This is the difference between a page that looks slightly different and a document whose every
/// later page is wrong.
/// </param>
public readonly record struct FontSubstitution(
    string Requested,
    string Chosen,
    bool IsMetricCompatible);
