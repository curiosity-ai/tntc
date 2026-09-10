using TNTc;

namespace TNT.CLI;

public partial class Program
{
    private const int MAX_REPORTED_PACKAGE_ISSUES = 10;

    /// <summary>
    /// Reports what the referenced packages contribute and, more usefully, what they do not: the
    /// strings a package draws that neither its own tables nor this project's translate. That is the
    /// number a host cares about - a string in this list renders in English however many languages
    /// the application ships.
    /// </summary>
    /// <remarks>Nothing here can fail the command: an issue in a table a package ships is reported, but it is the package's to fix, and a host must not be blocked by it.</remarks>
    public static void VerifyPackages(string rootFolderPath, Language[] languages, Dictionary<string, TranslatedLanguageStrings> allStrings)
    {
        var packages = PackageTranslationStore.Read(rootFolderPath);

        if (packages.IsEmpty) return;

        Console.WriteLine();
        Console.WriteLine("Referenced packages");

        foreach (var package in packages.Packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            var tables  = package.Tables.Count == 0 ? "no tables" : $"{package.Tables.Count} table(s): {string.Join(" ", package.Tables.Keys.OrderBy(c => c, StringComparer.Ordinal))}";
            var scanned = package.ScannedStrings is { } count ? $"{count} string(s) scanned" : "not scanned";

            Console.WriteLine($"  {package.Coordinates,-40} {tables}, {scanned}");
        }

        ReportPackageTableIssues(packages);

        if (packages.Scanned.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine($"Coverage of the {packages.Scanned.Count} string(s) scanned out of those assemblies:");

        foreach (var language in languages)
        {
            var byPackage = 0;
            var byUs      = 0;
            var nobody    = new List<string>();

            foreach (var scanned in packages.Scanned)
            {
                if (packages.TryGetTranslation(language, scanned.OriginalString, out _))
                {
                    byPackage++;
                    continue;
                }

                if (allStrings.TryGetValue(scanned.OriginalString, out var entry)
                 && entry.TranslatedStrings is not null
                 && entry.TranslatedStrings.TryGetValue(language, out var ours)
                 && !ours.IsPending)
                {
                    byUs++;
                    continue;
                }

                nobody.Add(scanned.OriginalString);
            }

            Console.WriteLine($"  {LanguageHelper.MapLanguage(language),-4} {byPackage,5} by a package, {byUs,5} by us, {nobody.Count,5} by nothing");
        }
    }

    private static void ReportPackageTableIssues(PackageContributions packages)
    {
        var issues = new List<string>();

        foreach (var (language, byString) in packages.Translations)
        {
            foreach (var (originalString, translation) in byString)
            {
                foreach (var issue in TranslationValidator.Validate(originalString, translation.TranslatedString))
                {
                    issues.Add($"{translation.Coordinates} {LanguageHelper.MapLanguage(language)} {Describe(originalString)}: [{issue.Rule}] {issue.Message}");
                }
            }
        }

        if (issues.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine($"  {issues.Count} issue(s) in the tables those packages ship - report them to the package, they cannot be fixed here:");

        foreach (var issue in issues.OrderBy(i => i, StringComparer.Ordinal).Take(MAX_REPORTED_PACKAGE_ISSUES))
        {
            Console.WriteLine($"    {issue}");
        }

        if (issues.Count > MAX_REPORTED_PACKAGE_ISSUES) Console.WriteLine($"    ... and {issues.Count - MAX_REPORTED_PACKAGE_ISSUES} more");
    }
}
