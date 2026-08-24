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
        yield return rootFolderPath;
        Console.WriteLine($"Searching for strings in {Path.GetFullPath(rootFolderPath)}");

        if (File.Exists(Path.Combine(rootFolderPath, ".tnt", "extra-sources.json")))
        {
            foreach (var extraPath in JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(rootFolderPath, ".tnt", "extra-sources.json"), Encoding.UTF8)))
            {
                var fullPath = Path.GetFullPath(Path.Combine(rootFolderPath, extraPath));

                yield return fullPath;
                Console.WriteLine($"Searching for strings in {fullPath}");
            }
        }
    }

    /// <summary>Scans the sources for translatable strings, refreshes every source location, and queues anything not translated yet as <see cref="TranslationRecordState.New"/> with no text. Does not translate - that is what <c>missing</c> and <c>apply</c> are for.</summary>
    public static void Extract(string rootFolderPath, string? languageCodes)
    {
        var languages  = LanguageHelper.ParseLanguages(languageCodes);
        var allStrings = TranslationStore.Read(rootFolderPath, languages);

        foreach (var entry in allStrings.Values)
        {
            entry.SourceLocations.Clear();
        }

        var sourceFolders        = EnumerateDirectoriesToSearchForStrings(rootFolderPath).ToArray();
        var rootFolderPathPrefix = LongestCommonPrefix(sourceFolders);
        var found                = 0;

        foreach (var sourceFolder in sourceFolders)
        {
            foreach (var translatableString in ExtractStrings(sourceFolder, rootFolderPathPrefix))
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
        }

        var queued  = new Dictionary<Language, int>();
        var unused  = 0;

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
                if (entry.TranslatedStrings.ContainsKey(language)) continue;

                entry.TranslatedStrings[language] = new TranslatedString() { State = TranslationRecordState.New, String = "" };
                queued[language]                  = queued.GetValueOrDefault(language) + 1;
            }
        }

        TranslationStore.Write(rootFolderPath, allStrings, languages);

        Console.WriteLine();
        Console.WriteLine($"Found {found} translatable string usage(s), {allStrings.Count - unused} distinct string(s) in use, {unused} no longer referenced.");

        foreach (var language in languages)
        {
            var pending = allStrings.Values.Count(e => e.SourceLocations.Count > 0 && e.TranslatedStrings.TryGetValue(language, out var t) && t.IsPending);

            Console.WriteLine($"  {LanguageHelper.MapLanguage(language)} ({language}): {queued.GetValueOrDefault(language)} newly queued, {pending} pending in total");
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
