using System.Text;
using System.Text.Json;
using CodeScanner;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TNTc;

namespace TNT.CLI;

public partial class Program
{
    private static List<TranslatableString> AnalyzeStrings(string sourceCode, string filePath)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceCode);

        var compilation = CSharpCompilation.Create("temp")
           .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
           .AddSyntaxTrees(tree);

        var semanticModel = compilation.GetSemanticModel(tree);
        var collector     = new StringsCollector(semanticModel, filePath);
        collector.Visit(tree.GetRoot());

        return collector._stringsWithTMethod;
    }

    public static IEnumerable<TranslatableString> ExtractStrings(string folderPath, string rootFolderPathPrefix)
    {
        var allCSFiles = Directory.EnumerateFiles(folderPath, "*.cs", SearchOption.AllDirectories);

        foreach (var csFile in allCSFiles)
        {
            var sourceCode = File.ReadAllText(csFile);

            foreach (var translatableString in AnalyzeStrings(sourceCode, csFile.Substring(rootFolderPathPrefix.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            {
                yield return translatableString;
            }

        }
    }

    public static IEnumerable<string> EnumerateDirectoriesToSearchForStrings(string rootFolderPath)
    {
        // Full path, like the extra sources below: source locations are made relative to the longest
        // common prefix of all scanned folders, so a relative project folder next to absolute extra
        // sources would collapse that prefix to nothing and record machine-specific paths.
        yield return Path.GetFullPath(rootFolderPath);

        if (File.Exists(Path.Combine(rootFolderPath, ".tnt", "extra-sources.json")))
        {
            foreach (var extraPath in JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(rootFolderPath, ".tnt", "extra-sources.json"), Encoding.UTF8)))
            {
                var fullPath = Path.GetFullPath(Path.Combine(rootFolderPath, extraPath));

                yield return fullPath;
            }
        }
    }

    /// <summary>Scans the sources for translatable strings, refreshes every source location, and queues anything not translated yet as <see cref="TranslationRecordState.New"/> with no text. Does not translate - that is what <c>missing</c> and <c>apply</c> are for.</summary>
    public static void Extract(string rootFolderPath, string? languageCodes, bool includePackages, bool scanAssemblies)
    {
        var languages  = LanguageHelper.ParseLanguages(languageCodes);
        var allStrings = TranslationStore.Read(rootFolderPath, languages);
        var packages   = PackageTranslationStore.Read(rootFolderPath);

        if (includePackages || scanAssemblies)
        {
            packages = ImportPackages(rootFolderPath, languages, includePackages, scanAssemblies, packages);
            PackageTranslationStore.Write(rootFolderPath, packages);
        }

        foreach (var entry in allStrings.Values)
        {
            entry.SourceLocations.Clear();
        }

        var sourceFolders        = EnumerateDirectoriesToSearchForStrings(rootFolderPath).ToArray();
        var rootFolderPathPrefix = LongestCommonPrefix(sourceFolders);
        var found                = 0;

        foreach (var sourceFolder in sourceFolders)
        {
            Console.WriteLine($"Searching for strings in {sourceFolder}");
        }

        // The strings a referenced assembly was scanned for arrive here as ordinary usages, so a
        // package's string is queued, reported and translated exactly like one of our own - it only
        // names a member rather than a file and a line.
        foreach (var translatableString in sourceFolders.SelectMany(f => ExtractStrings(f, rootFolderPathPrefix)).Concat(FromScannedAssemblies(packages)))
        {
            found++;

            if (!allStrings.TryGetValue(translatableString.SourceString, out var entry))
            {
                entry = new TranslatedLanguageStrings()
                {
                    OriginalString    = translatableString.SourceString,
                    TranslatedStrings = new Dictionary<Language, TranslatedString>(),
                    SourceLocations   = new List<SourceLocation>()
                };
                allStrings[translatableString.SourceString] = entry;
            }

            if (!entry.SourceLocations.Contains(translatableString.SourceLocation)) entry.SourceLocations.Add(translatableString.SourceLocation);
        }

        var queued   = new Dictionary<Language, int>();
        var covered  = new Dictionary<Language, int>();
        var unused   = 0;

        foreach (var entry in allStrings.Values)
        {
            if (entry.SourceLocations.Count == 0)
            {
                unused++;
                continue; // a string no source references any more never gets queued for translation
            }

            entry.TranslatedStrings ??= new Dictionary<Language, TranslatedString>();

            foreach (var language in languages)
            {
                // A package that translated the string already answers for it, so there is nothing to
                // hand a translator - and a queued record of ours would only ask for the work twice.
                // It is marked rather than dropped, so the file still lists every string in use and
                // says who answers this one.
                if (packages.TryGetTranslation(language, entry.OriginalString, out var fromPackage))
                {
                    covered[language] = covered.GetValueOrDefault(language) + 1;

                    if (!entry.TranslatedStrings.TryGetValue(language, out var ours) || ours.IsPending)
                    {
                        entry.TranslatedStrings[language] = new TranslatedString() { State = TranslationRecordState.PackageProvided, String = "", GeneratedBy = fromPackage.Coordinates };
                    }

                    continue;
                }

                if (entry.TranslatedStrings.TryGetValue(language, out var existing))
                {
                    // A package that used to answer for it no longer does, so it is work again.
                    if (existing.State != TranslationRecordState.PackageProvided || !existing.IsPending) continue;

                    entry.TranslatedStrings.Remove(language);
                }

                entry.TranslatedStrings[language] = new TranslatedString() { State = TranslationRecordState.New, String = "" };
                queued[language]                  = queued.GetValueOrDefault(language) + 1;
            }
        }

        TranslationStore.Write(rootFolderPath, allStrings, languages, packages);

        Console.WriteLine();
        Console.WriteLine($"Found {found} translatable string usage(s), {allStrings.Count - unused} distinct string(s) in use, {unused} no longer referenced.");

        foreach (var language in languages)
        {
            var pending    = allStrings.Values.Count(e => e.SourceLocations.Count > 0 && e.TranslatedStrings.TryGetValue(language, out var t) && t.IsPending && t.State != TranslationRecordState.PackageProvided);
            var byPackages = covered.GetValueOrDefault(language) > 0 ? $", {covered[language]} answered by a package" : "";

            Console.WriteLine($"  {LanguageHelper.MapLanguage(language)} ({language}): {queued.GetValueOrDefault(language)} newly queued, {pending} pending in total{byPackages}");
        }

        Console.WriteLine();
        Console.WriteLine("Done. Run 'tntc missing' to get the pending strings, then 'tntc apply' to write the translations back.");
    }

    static string LongestCommonPrefix(string[] strs)
    {
        if (strs == null || strs.Length == 0)
            return "";

        string prefix = strs[0];

        foreach (string str in strs)
        {
            while (!str.StartsWith(prefix))
            {
                if (prefix.Length == 0)
                    return "";

                prefix = prefix.Substring(0, prefix.Length - 1);
            }
        }

        return prefix;
    }
}
