using CodeScanner;
using TNTc;

namespace TNT.CLI;

public partial class Program
{
    private const int MAX_REPORTED_FINDINGS = 20;

    /// <summary>
    /// Rebuilds <c>.tnt/packages/</c> from the packages this project restored to: the tables they
    /// ship, and - when asked - the strings their assemblies were compiled to display. The half that
    /// was not asked for is carried over from what is already on file, so refreshing one does not
    /// silently discard the other.
    /// </summary>
    public static PackageContributions ImportPackages(string rootFolderPath, Language[] languages, bool includeTables, bool scanAssemblies, PackageContributions existing)
    {
        var projectFolders = EnumerateDirectoriesToSearchForStrings(rootFolderPath).ToArray();
        var packages       = PackageGraph.Resolve(projectFolders, out var foldersWithoutAssets);

        // Nothing restored means nothing resolved, which would otherwise read as "no package
        // contributes anything" and quietly take away what the last run imported.
        if (foldersWithoutAssets.Count == projectFolders.Length) throw new CommandFailedException($"None of the {projectFolders.Length} scanned folder(s) has an obj/{PackageGraph.ASSETS_FILE} - run 'dotnet restore' first, or drop --include-packages / --scan-assemblies.");

        foreach (var folder in foldersWithoutAssets)
        {
            Console.Error.WriteLine($"warning: {folder} has no obj/{PackageGraph.ASSETS_FILE} - restore it, or its packages contribute nothing.");
        }

        var contributions = new PackageContributions()
        {
            Translations = includeTables ? new Dictionary<Language, Dictionary<string, PackageTranslation>>() : existing.Translations,
            Scanned      = scanAssemblies ? new List<ScannedString>() : existing.Scanned
        };

        var previous     = existing.Packages.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var scannedByKey = new Dictionary<string, List<SourceLocation>>(StringComparer.Ordinal);
        var findings     = new List<string>();

        foreach (var package in packages)
        {
            previous.TryGetValue(package.Id, out var before);

            var entry = new PackageManifestEntry()
            {
                Id             = package.Id,
                Version        = package.Version,
                Tables         = includeTables ? new Dictionary<string, int>() : before?.Tables ?? new Dictionary<string, int>(),
                ScannedStrings = scanAssemblies ? 0 : before?.ScannedStrings
            };

            if (includeTables)
            {
                foreach (var (language, table) in PackageTranslationStore.ReadPackageTables(package, languages))
                {
                    if (!contributions.Translations.TryGetValue(language, out var byString))
                    {
                        byString                          = new Dictionary<string, PackageTranslation>(StringComparer.Ordinal);
                        contributions.Translations[language] = byString;
                    }

                    foreach (var (originalString, translated) in table)
                    {
                        byString[originalString] = new PackageTranslation() { TranslatedString = translated, Coordinates = package.Coordinates };
                    }

                    entry.Tables[LanguageHelper.MapLanguage(language)] = table.Count;
                }
            }

            if (scanAssemblies)
            {
                var found = new HashSet<string>(StringComparer.Ordinal);

                foreach (var assembly in package.Assemblies)
                {
                    var result = AssemblyStringScanner.Scan(assembly);

                    foreach (var literal in result.Literals)
                    {
                        found.Add(literal.Value);

                        if (!scannedByKey.TryGetValue(literal.Value, out var locations))
                        {
                            locations                     = new List<SourceLocation>();
                            scannedByKey[literal.Value] = locations;
                        }

                        var location = new SourceLocation() { SourceFilePath = $"{package.Id}!{literal.Member}", SourceFileLine = 0 };

                        if (!locations.Contains(location)) locations.Add(location);
                    }

                    foreach (var finding in result.Findings)
                    {
                        findings.Add($"{package.Id}!{finding.Member}: {finding.Message}");
                    }
                }

                entry.ScannedStrings = found.Count;
            }

            contributions.Packages.Add(entry);
        }

        if (scanAssemblies)
        {
            contributions.Scanned = scannedByKey
               .OrderBy(e => e.Key, StringComparer.Ordinal)
               .Select(e => new ScannedString() { OriginalString = e.Key, SourceLocations = e.Value.ToArray() })
               .ToList();
        }

        ReportImport(contributions, packages.Count, includeTables, scanAssemblies, findings);

        return contributions;
    }

    private static void ReportImport(PackageContributions contributions, int packageCount, bool includeTables, bool scanAssemblies, List<string> findings)
    {
        var withTables = contributions.Packages.Count(p => p.Tables.Count > 0);

        Console.WriteLine();
        Console.WriteLine($"Resolved {packageCount} referenced package(s).");

        if (includeTables) Console.WriteLine($"  {withTables} ship a translation table; {contributions.Translations.Sum(t => t.Value.Count)} translation(s) imported across {contributions.Translations.Count} language(s).");
        if (scanAssemblies) Console.WriteLine($"  {contributions.Scanned.Count} distinct string(s) found in the referenced assemblies.");

        foreach (var package in contributions.Packages)
        {
            if (package.Tables.Count == 0 && (package.ScannedStrings ?? 0) == 0) continue;

            var tables = package.Tables.Count > 0 ? $"{package.Tables.Count} table(s)" : "no tables";
            var strings = package.ScannedStrings is { } scanned ? $", {scanned} string(s) in its assemblies" : "";

            Console.WriteLine($"    {package.Coordinates}: {tables}{strings}");
        }

        if (findings.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine($"{findings.Count} call site(s) in the referenced assemblies build the string before looking it up, so nothing can translate them:");

        foreach (var finding in findings.Take(MAX_REPORTED_FINDINGS))
        {
            Console.WriteLine($"  {finding}");
        }

        if (findings.Count > MAX_REPORTED_FINDINGS) Console.WriteLine($"  ... and {findings.Count - MAX_REPORTED_FINDINGS} more");
    }

    /// <summary>The strings scanned out of the referenced assemblies, as usages <c>extract</c> can treat like any other.</summary>
    private static IEnumerable<TranslatableString> FromScannedAssemblies(PackageContributions packages)
    {
        foreach (var scanned in packages.Scanned)
        {
            foreach (var location in scanned.SourceLocations)
            {
                yield return new TranslatableString() { SourceString = scanned.OriginalString, SourceLocation = location };
            }
        }
    }

    /// <summary>Re-imports what the referenced packages contribute and rewrites <c>.tnt-content</c> from what is on file. Does not read this project's own sources - queuing new work is what <c>extract</c> is for.</summary>
    public static void Sync(string rootFolderPath, string? languageCodes, bool scanAssemblies)
    {
        var languages     = LanguageHelper.ParseLanguages(languageCodes);
        var existing      = PackageTranslationStore.Read(rootFolderPath);
        var contributions = ImportPackages(rootFolderPath, languages, includeTables: true, scanAssemblies: scanAssemblies, existing);

        PackageTranslationStore.Write(rootFolderPath, contributions);
        TranslationStore.Write(rootFolderPath, TranslationStore.Read(rootFolderPath, languages), languages, contributions);

        Console.WriteLine();
        Console.WriteLine($"Rewrote {TranslationStore.TNT_CONTENT_FOLDER} for {string.Join(", ", languages.Select(LanguageHelper.MapLanguage))}.");

        if (scanAssemblies) Console.WriteLine("Run 'tntc extract' to queue what the scan found, then 'tntc missing'.");
    }
}
