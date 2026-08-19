using System.Xml.Linq;

namespace Paperless.Text.Fonts;

/// <summary>
/// What a machine's fontconfig configuration says about families: which ones it prefers for a
/// generic family, and which generic family each named family belongs to.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two questions, one reader, because they are answered by the same files.</strong> The
/// first is asked at the very end of glyph fallback and is what this class was written for; the
/// second — <see cref="GenericClassOf"/> — is asked at the *start* of substitution, and reading it
/// from anywhere else is a measured defect. Both are properties of the configuration rather than of
/// the fonts, so both belong to whoever has already parsed it.
/// </para>
/// <para>
/// This exists for one question, asked at the very end of glyph fallback: <em>nothing on
/// LibreOffice's own fallback list is installed and several faces on this machine cover the
/// character — which one does LibreOffice draw it in?</em> The answer is not a property of the
/// fonts. It is a property of the machine's fontconfig configuration, and that was established by
/// measurement rather than assumed: with the machine's <c>/etc/fonts</c> in force,
/// <c>fc-match "宋体:charset=624b"</c> answers <em>WenQuanYi Zen Hei</em>; with a minimal
/// configuration naming only the font directory it answers <em>IPAGothic</em>, because nothing but
/// the scan order then separates the candidates. The difference is
/// <c>/etc/fonts/conf.d/64-wqy-zenhei.conf</c>, which lists WenQuanYi Zen Hei under
/// <c>&lt;alias&gt;&lt;family&gt;sans-serif&lt;/family&gt;&lt;prefer&gt;</c>.
/// </para>
/// <para>
/// So no rule derived from the font files — code-page bits, Unicode ranges, coverage size, name —
/// can reproduce the reference renderer's choice, and the resolver's previous tie-break
/// (alphabetical by family, which its own comment described as having no basis) put IPAGothic and
/// Unifont ahead of WenQuanYi Zen Hei on every Han character. Reading the configuration is
/// therefore not a second source of truth competing with LibreOffice's substitution table; it is
/// the source of truth for the one decision the table does not make. LibreOffice asks fontconfig
/// through <c>FcFontSetMatch</c> in <c>vcl/unx/generic/font/fontconfig.cxx</c> before it ever
/// consults its own list.
/// </para>
/// <para>
/// Deliberately narrow. For the preference order, only <c>&lt;alias&gt;</c> elements whose subject
/// is a *generic* family (<c>serif</c>, <c>sans-serif</c>, <c>monospace</c> and friends) and whose
/// body is a <c>&lt;prefer&gt;</c> list are read. The metric-alias files — <c>Helvetica</c>
/// preferring <c>Nimbus Sans</c> — are aliases of a concrete family and say nothing about which
/// face should draw a character nobody's font covers, so they are skipped there. Conditional
/// <c>&lt;match target="pattern"&gt;</c> rules are skipped too: they turn on a language or a size
/// this resolver does not carry, and guessing at their conditions would be worse than not
/// reproducing them.
/// </para>
/// <para>
/// The classification reads the *other* half of the same elements, and reads them for a concrete
/// subject rather than a generic one. <c>&lt;default&gt;</c> is what files a family under a generic
/// — fontconfig's own semantics, "append this family if the pattern has not already named one" —
/// and <c>&lt;accept&gt;</c> and <c>&lt;prefer&gt;</c> add families to the pattern that can carry a
/// generic of their own. Both are needed and both are measured: <c>Century Schoolbook</c> defaults
/// to the concrete <c>New Century Schoolbook</c>, which is filed under no generic at all, so it
/// takes the sans-serif default; <c>Palatino</c> is filed under nothing either but *accepts*
/// <c>Palatino Linotype</c>, which <c>45-latin.conf</c> files under <c>serif</c> — which is why
/// <c>fc-match Palatino</c> answers DejaVu Serif and <c>fc-match "Century Schoolbook"</c> does not.
/// </para>
/// <para>
/// The order is fontconfig's own. A <c>&lt;prefer&gt;</c> list is an
/// <c>&lt;edit name="family" mode="prepend"&gt;</c> applied at the position of the matched family,
/// so each file's entries land *behind* those of the files already processed and configuration
/// files rank in ascending name order. That is checkable on this machine:
/// <c>fc-match sans-serif</c> answers DejaVu Sans (from <c>57-dejavu-sans.conf</c>) and WenQuanYi
/// Zen Hei second (from <c>64-wqy-zenhei.conf</c>), which is the order the file names give.
/// </para>
/// </remarks>
public sealed class FontconfigPreferences
{
    /// <summary>The generic families whose preference lists are read.</summary>
    /// <remarks>
    /// CSS's generic families plus fontconfig's own additions. A preference expressed for any of
    /// them is a statement about what should draw text nothing else claims, which is exactly the
    /// question glyph fallback asks.
    /// </remarks>
    private static readonly HashSet<string> Generics = new(StringComparer.Ordinal)
    {
        "serif", "sans-serif", "sans serif", "sans", "monospace", "mono",
        "cursive", "fantasy", "system-ui", "emoji", "math",
    };

    /// <summary>How deep an <c>&lt;include&gt;</c> chain is followed.</summary>
    /// <remarks>
    /// A configuration that includes itself is malformed rather than impossible, and a stack
    /// overflow inside a font resolver is a poor way to find out.
    /// </remarks>
    private const int MaxIncludeDepth = 8;

    /// <summary>
    /// The generic families a <c>&lt;default&gt;</c> may name, and the shape each one means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three generics have face lists in this resolver and the rest do not. <c>system-ui</c>,
    /// <c>cursive</c>, <c>fantasy</c> and <c>emoji</c> are therefore grouped with the grotesques,
    /// which is not a claim about their shape but about where a pattern filed under them actually
    /// lands: the configuration's overall default, which is the head of the sans-serif list.
    /// Measured on 26.2.4.2 here — <c>Segoe UI</c> is filed <c>system-ui</c> by
    /// <c>45-generic.conf</c> and LibreOffice draws it in DejaVu Sans.
    /// </para>
    /// <para>
    /// <c>math</c> is the one that reads wrong and is measured: a maths face is a roman, but there
    /// is no maths font on a stock configuration, so <c>60-generic.conf</c>'s preference list is
    /// empty of installed faces and the pattern falls all the way through to the configuration's
    /// overall default. <c>Cambria Math</c> is filed <c>math</c> by <c>45-generic.conf</c> and
    /// 26.2.4.2 draws it in DejaVu <em>Sans</em>. Filing it as a roman is the sort of guess this
    /// whole class exists to stop making.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, FontFamilyClass> GenericShapes =
        new(StringComparer.Ordinal)
        {
            ["serif"] = FontFamilyClass.Serif,
            ["sansserif"] = FontFamilyClass.SansSerif,
            ["sans"] = FontFamilyClass.SansSerif,
            ["monospace"] = FontFamilyClass.Fixed,
            ["mono"] = FontFamilyClass.Fixed,
            ["systemui"] = FontFamilyClass.SansSerif,
            ["cursive"] = FontFamilyClass.SansSerif,
            ["fantasy"] = FontFamilyClass.SansSerif,
            ["emoji"] = FontFamilyClass.SansSerif,
            ["math"] = FontFamilyClass.SansSerif,
        };

    /// <summary>How far an <c>&lt;alias&gt;&lt;default&gt;</c> chain is walked.</summary>
    /// <remarks>
    /// <c>Century Schoolbook → New Century Schoolbook</c> is one hop and the longest chain in a
    /// stock configuration is three. A cycle is malformed rather than impossible and the walk is
    /// cycle-safe anyway; this only bounds the pathological case.
    /// </remarks>
    private const int MaxDefaultDepth = 16;

    private readonly Dictionary<string, int> _ranks;
    private readonly Dictionary<string, List<string>> _aliasEdges;
    private readonly Dictionary<string, FontFamilyClass> _classes = new(StringComparer.Ordinal);

    private FontconfigPreferences(
        Dictionary<string, int> ranks, Dictionary<string, List<string>> aliasEdges, bool configured)
    {
        _ranks = ranks;
        _aliasEdges = aliasEdges;
        IsConfigured = configured;
    }

    /// <summary>A preference set naming nothing, for a machine with no fontconfig at all.</summary>
    public static FontconfigPreferences None { get; } =
        new(
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, List<string>>(StringComparer.Ordinal),
            configured: false);

    /// <summary>
    /// Whether a fontconfig configuration was found at all.
    /// </summary>
    /// <remarks>
    /// The distinction the resolver needs is not "does fontconfig know this family" but "is there a
    /// fontconfig on this machine": where there is one, a family it has no rule for still gets an
    /// answer — its default generic — and where there is none, nothing overrides LibreOffice's own
    /// table. Windows and most macOS installations are the second case and must keep behaving as
    /// they did before this was read at all.
    /// </remarks>
    public bool IsConfigured { get; }

    /// <summary>The machine's own preferences, read once.</summary>
    /// <remarks>
    /// Lazily, because a process that renders nothing should not pay for a directory scan, and
    /// cached, because the answer cannot change while the process runs. A machine with no
    /// fontconfig — every Windows and most macOS installations — gets <see cref="None"/> and the
    /// resolver behaves exactly as it did before this existed.
    /// </remarks>
    public static FontconfigPreferences Machine => _machine.Value;

    private static readonly Lazy<FontconfigPreferences> _machine = new(Load);

    /// <summary>The preferred families, best first, as normalised names.</summary>
    public IReadOnlyList<string> InOrder =>
        _ranks.OrderBy(entry => entry.Value).Select(entry => entry.Key).ToList();

    /// <summary>
    /// Where a family sits in the preference order, or <see cref="int.MaxValue"/> when it is not
    /// named at all.
    /// </summary>
    /// <param name="familyName">A family name; normalised here, so either form is accepted.</param>
    public int RankOf(string? familyName)
        => _ranks.TryGetValue(FontSubstitutions.Normalise(familyName), out int rank)
            ? rank
            : int.MaxValue;

    /// <summary>
    /// True when the configuration has an <c>&lt;alias&gt;</c> rule whose subject is this family,
    /// whether or not the chain from it reaches a generic.
    /// </summary>
    /// <remarks>
    /// The question the substitution order turns on. A family fontconfig names is one its own
    /// aliases speak for; a family it names nowhere gets nothing but the default generic, and
    /// LibreOffice's <c>SubstFonts</c> chain is then unreachable because the pre-match hook has
    /// already answered. <c>CG Times</c>, <c>Times-Roman</c>, <c>Helv</c> and <c>SansSerif</c> are
    /// the corpus's four instances and all four are measured: the chain sends each to a Liberation
    /// face and 26.2.4.2 draws all four in DejaVu Sans.
    /// </remarks>
    public bool Names(string? familyName)
        => _aliasEdges.ContainsKey(FontSubstitutions.Normalise(familyName));

    /// <summary>
    /// The generic family fontconfig files a family under, as a shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FontFamilyClass.Unknown"/> only when there is no configuration to ask. Where there
    /// is one, a family it has no rule for is <see cref="FontFamilyClass.SansSerif"/> rather than
    /// unknown, because that is what <c>49-sansserif.conf</c> does: it appends <c>sans-serif</c> to
    /// any pattern that has not already named a generic, so fontconfig always has an answer. This is
    /// the whole difference from <see cref="FontSubstitutions.ClassOf"/>, which reads
    /// <c>VCL.xcu</c>'s <c>FontType</c> — the two disagree on ten of the 296 families the sample
    /// corpus names, <c>Century Schoolbook</c> among them, and the running binary follows this one.
    /// </para>
    /// <para>
    /// <c>&lt;default&gt;</c> chains are walked, because most of them do not name a generic
    /// directly. <c>Century Schoolbook</c> defaults to the concrete <c>New Century Schoolbook</c>,
    /// which is filed under nothing — so the walk ends without a generic and the answer is the
    /// sans-serif default, which is exactly what <c>fc-match "Century Schoolbook"</c> answers.
    /// <c>Palatino Linotype</c> defaults to <c>Palatino</c>, which <c>45-latin.conf</c> files under
    /// <c>serif</c>, so the walk finds one.
    /// </para>
    /// <para>
    /// Breadth-first, and the first generic reached wins. fontconfig appends every matching
    /// <c>&lt;default&gt;</c> in file order and then matches against the resulting family list in
    /// order, so the nearest generic is the one that decides.
    /// </para>
    /// </remarks>
    public FontFamilyClass GenericClassOf(string? familyName)
    {
        if (!IsConfigured) return FontFamilyClass.Unknown;

        string key = FontSubstitutions.Normalise(familyName);
        if (key.Length == 0) return FontFamilyClass.Unknown;

        lock (_classes)
        {
            if (_classes.TryGetValue(key, out FontFamilyClass cached)) return cached;

            FontFamilyClass found = Walk(key);
            _classes[key] = found;
            return found;
        }
    }

    private FontFamilyClass Walk(string key)
    {
        // A family that *is* a generic classifies as itself, which is how `sans-serif` and `serif`
        // answer when a document names one outright.
        if (GenericShapes.TryGetValue(key, out FontFamilyClass direct)) return direct;

        HashSet<string> seen = new(StringComparer.Ordinal) { key };
        Queue<(string Name, int Depth)> queue = new();
        queue.Enqueue((key, 0));

        while (queue.Count > 0)
        {
            (string name, int depth) = queue.Dequeue();
            if (depth >= MaxDefaultDepth || !_aliasEdges.TryGetValue(name, out List<string>? targets))
                continue;

            foreach (string target in targets)
            {
                if (GenericShapes.TryGetValue(target, out FontFamilyClass generic)) return generic;
                if (seen.Add(target)) queue.Enqueue((target, depth + 1));
            }
        }

        // fontconfig's own answer for a name it has no generic for: `49-sansserif.conf` appends
        // sans-serif to every pattern that named none.
        return FontFamilyClass.SansSerif;
    }

    /// <summary>Reads the machine's configuration from its usual place.</summary>
    /// <remarks>
    /// <c>FONTCONFIG_FILE</c> wins, then <c>$FONTCONFIG_PATH/fonts.conf</c>, then
    /// <c>/etc/fonts/fonts.conf</c> — the same order fontconfig itself uses, which matters because
    /// every comparison against a reference rendering has to be made against the configuration
    /// that produced it.
    /// </remarks>
    public static FontconfigPreferences Load()
    {
        foreach (string candidate in RootCandidates())
        {
            if (!File.Exists(candidate)) continue;

            Dictionary<string, int> ranks = new(StringComparer.Ordinal);
            Dictionary<string, List<string>> aliasEdges = new(StringComparer.Ordinal);
            ReadFile(candidate, ranks, aliasEdges, 0);
            return new FontconfigPreferences(ranks, aliasEdges, configured: true);
        }

        return None;
    }

    /// <summary>Reads a set of configuration files in the order given, for tests.</summary>
    public static FontconfigPreferences Read(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        Dictionary<string, int> ranks = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> aliasEdges = new(StringComparer.Ordinal);
        foreach (string file in files) ReadFile(file, ranks, aliasEdges, 0);
        return new FontconfigPreferences(ranks, aliasEdges, configured: true);
    }

    private static IEnumerable<string> RootCandidates()
    {
        if (Environment.GetEnvironmentVariable("FONTCONFIG_FILE") is { Length: > 0 } named)
        {
            yield return Path.IsPathRooted(named) ? named : Path.Combine("/etc/fonts", named);
        }

        if (Environment.GetEnvironmentVariable("FONTCONFIG_PATH") is { Length: > 0 } directory)
        {
            yield return Path.Combine(directory, "fonts.conf");
        }

        yield return "/etc/fonts/fonts.conf";
    }

    private static void ReadFile(
        string path,
        Dictionary<string, int> ranks,
        Dictionary<string, List<string>> aliasEdges,
        int depth)
    {
        if (depth > MaxIncludeDepth) return;

        XDocument document;
        try
        {
            // No DTD resolution: every fontconfig file declares one and none of them needs it.
            using FileStream stream = File.OpenRead(path);
            document = XDocument.Load(stream, LoadOptions.None);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException
                      or System.Xml.XmlException)
        {
            // A configuration file that cannot be read leaves the machine with fewer preferences,
            // which is the state every non-Linux machine is in anyway.
            return;
        }

        if (document.Root is null) return;

        foreach (XElement element in document.Root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "alias":
                    ReadAlias(element, ranks, aliasEdges);
                    break;
                case "include":
                    foreach (string included in Included(element, path))
                    {
                        ReadFile(included, ranks, aliasEdges, depth + 1);
                    }

                    break;
            }
        }
    }

    private static void ReadAlias(
        XElement alias, Dictionary<string, int> ranks, Dictionary<string, List<string>> aliasEdges)
    {
        // The subject is the alias's own `family` child; the preferences are inside `prefer`.
        string? subject = alias.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "family")?.Value.Trim();

        if (subject is null) return;

        bool generic = Generics.Contains(subject);

        // The classification half. `<default>` is what files a family under a generic; `<accept>`
        // and `<prefer>` name families fontconfig adds to the pattern, and those can carry a
        // generic of their own — `Palatino` is filed under nothing, accepts `Palatino Linotype`,
        // and `45-latin.conf` files *that* under `serif`, which is why `fc-match Palatino` answers
        // DejaVu Serif. Only for a concrete subject: a generic's `<prefer>` list is its preference
        // order, and following it would make every family on the machine reachable from every
        // other.
        string[] bodies = generic ? [] : ["default", "accept", "prefer"];

        foreach (XElement body in alias.Elements().Where(e => bodies.Contains(e.Name.LocalName)))
        {
            string key = FontSubstitutions.Normalise(subject);
            if (key.Length == 0) continue;

            foreach (XElement family in body.Elements().Where(e => e.Name.LocalName == "family"))
            {
                string target = FontSubstitutions.Normalise(family.Value.Trim());
                if (target.Length == 0 || target == key) continue;

                if (!aliasEdges.TryGetValue(key, out List<string>? targets))
                {
                    targets = [];
                    aliasEdges[key] = targets;
                }

                // File order is fontconfig's own append order, and duplicates would only lengthen
                // the walk.
                if (!targets.Contains(target, StringComparer.Ordinal)) targets.Add(target);
            }
        }

        if (!generic) return;

        foreach (XElement prefer in alias.Elements().Where(e => e.Name.LocalName == "prefer"))
        {
            foreach (XElement family in prefer.Elements().Where(e => e.Name.LocalName == "family"))
            {
                string normalised = FontSubstitutions.Normalise(family.Value.Trim());
                if (normalised.Length == 0) continue;

                // First mention wins: a family named by two files takes the better rank, which is
                // what prepending at the matched family's position produces.
                if (!ranks.ContainsKey(normalised)) ranks[normalised] = ranks.Count;
            }
        }
    }

    private static IEnumerable<string> Included(XElement include, string from)
    {
        string target = include.Value.Trim();
        if (target.Length == 0) yield break;

        if (target.StartsWith('~'))
        {
            target = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                target.TrimStart('~', '/'));
        }
        else if (!Path.IsPathRooted(target))
        {
            target = Path.Combine(Path.GetDirectoryName(from) ?? ".", target);
        }

        if (Directory.Exists(target))
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(target, "*.conf");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                yield break;
            }

            // By name, ascending: fontconfig reads a configuration directory in exactly that order
            // and the numeric prefixes on the files exist to control it.
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files) yield return file;
        }
        else if (File.Exists(target))
        {
            yield return target;
        }
    }
}
