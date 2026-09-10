using System.Text;
using System.Text.Json;
using CodeScanner;

namespace TNTc;

/// <summary>Reads and writes the two folders that hold a project's translations: <c>.tnt</c> (the per-language records with their state and source locations) and <c>.tnt-content</c> (the flat pairs the application ships).</summary>
public static class TranslationStore
{
    public const string TNT_FOLDER         = ".tnt";
    public const string TNT_CONTENT_FOLDER = ".tnt-content";

    public static readonly JsonSerializerOptions OptionsWrite = new JsonSerializerOptions()
    {
        WriteIndented = true,
        Converters =
        {
            new SourceLocationConverter(),
            new TranslationRecordStateConverter()
        },
        Encoder = new PassThroughJavaScriptEncoder()
    };

    public static readonly JsonSerializerOptions OptionsRead = new JsonSerializerOptions()
    {
        Converters =
        {
            new SourceLocationConverter(),
            new TranslationRecordStateConverter()
        },
    };

    public static string TranslationFilePath(string rootFolder, Language language)
        => Path.Combine(rootFolder, TNT_FOLDER, $"translation-{LanguageHelper.MapLanguage(language)}.json");

    public static string ContentFilePath(string rootFolder, Language language)
        => Path.Combine(rootFolder, TNT_CONTENT_FOLDER, $"{LanguageHelper.MapLanguage(language)}.tnt");

    public static List<TranslatedRecord> ReadLanguage(string rootFolder, Language language)
    {
        var path = TranslationFilePath(rootFolder, language);

        if (!File.Exists(path)) return new List<TranslatedRecord>();

        var json = File.ReadAllText(path, Encoding.UTF8);

        try
        {
            return JsonSerializer.Deserialize<List<TranslatedRecord>>(json, OptionsRead) ?? new List<TranslatedRecord>();
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{path} is not valid JSON (line {e.LineNumber}, position {e.BytePositionInLine}): {e.Message}", e);
        }
    }

    /// <summary>Reads every requested language into one table keyed by the original string. The source locations recorded across the language files are unioned onto each entry; <c>extract</c> discards them and re-derives them from the sources.</summary>
    public static Dictionary<string, TranslatedLanguageStrings> Read(string rootFolder, Language[] languages)
    {
        var strings = new Dictionary<string, TranslatedLanguageStrings>();

        foreach (var language in languages)
        {
            foreach (var record in ReadLanguage(rootFolder, language))
            {
                if (!strings.TryGetValue(record.OriginalString, out var entry))
                {
                    entry = new TranslatedLanguageStrings()
                    {
                        OriginalString    = record.OriginalString,
                        TranslatedStrings = new Dictionary<Language, TranslatedString>(),
                        SourceLocations   = new List<SourceLocation>()
                    };
                    strings[record.OriginalString] = entry;
                }

                entry.TranslatedStrings[language] = new TranslatedString()
                {
                    State       = record.State,
                    String      = record.TranslatedString,
                    GeneratedBy = record.GeneratedBy
                };

                if (record.SourceLocations is null) continue;

                foreach (var sourceLocation in record.SourceLocations)
                {
                    if (!entry.SourceLocations.Contains(sourceLocation)) entry.SourceLocations.Add(sourceLocation);
                }
            }
        }

        return strings;
    }

    /// <summary>Writes both folders for every requested language. A record with no translated text yet is kept in <c>.tnt</c> (it is the work queue) but left out of <c>.tnt-content</c>, so the application falls back to the original string instead of rendering an empty one. What the referenced packages contribute (<c>.tnt/packages/</c>) is folded into <c>.tnt-content</c> underneath this project's own entries - read from disk rather than from a restored package graph, so every command reproduces the same tables.</summary>
    public static void Write(string rootFolder, Dictionary<string, TranslatedLanguageStrings> allStrings, Language[] languages)
        => Write(rootFolder, allStrings, languages, PackageTranslationStore.Read(rootFolder));

    /// <inheritdoc cref="Write(string, Dictionary{string, TranslatedLanguageStrings}, Language[])"/>
    public static void Write(string rootFolder, Dictionary<string, TranslatedLanguageStrings> allStrings, Language[] languages, PackageContributions packageContributions)
    {
        Directory.CreateDirectory(Path.Combine(rootFolder, TNT_FOLDER));
        Directory.CreateDirectory(Path.Combine(rootFolder, TNT_CONTENT_FOLDER));

        var perLanguage        = languages.ToDictionary(l => l, _ => new List<TranslatedRecord>());
        var perLanguageTNTFile = languages.ToDictionary(l => l, _ => new List<string[]>());

        foreach (var (originalString, translatedLanguageStrings) in allStrings)
        {
            if (translatedLanguageStrings.TranslatedStrings is null) continue;

            foreach (var (language, translatedString) in translatedLanguageStrings.TranslatedStrings)
            {
                if (!perLanguage.TryGetValue(language, out var records)) continue;

                records.Add(new TranslatedRecord()
                {
                    State            = translatedString.State,
                    GeneratedBy      = translatedString.GeneratedBy,
                    OriginalString   = translatedLanguageStrings.OriginalString,
                    TranslatedString = translatedString.String,
                    SourceLocations  = translatedLanguageStrings.SourceLocations?.ToArray()
                });

                if (!translatedString.IsPending) perLanguageTNTFile[language].Add([originalString, translatedString.String]);
            }
        }

        foreach (var (language, translationPairs) in perLanguageTNTFile)
        {
            // A package's entry is only kept where this project has nothing of its own for that key:
            // a workspace that words something differently keeps its wording, and a string it never
            // had arrives translated.
            var ours = new HashSet<string>(translationPairs.Select(p => p[0]), StringComparer.Ordinal);

            if (packageContributions.Translations.TryGetValue(language, out var fromPackages))
            {
                foreach (var (originalString, translation) in fromPackages)
                {
                    if (!ours.Add(originalString)) continue;

                    translationPairs.Add([originalString, translation.TranslatedString]);
                }
            }

            File.WriteAllText(ContentFilePath(rootFolder, language), JsonSerializer.Serialize(translationPairs.OrderBy(e => e[0], StringComparer.Ordinal).ToArray(), OptionsWrite), Encoding.UTF8);
        }

        foreach (var (language, records) in perLanguage)
        {
            File.WriteAllText(TranslationFilePath(rootFolder, language), JsonSerializer.Serialize(records.OrderBy(e => e.OriginalString, StringComparer.Ordinal).ToArray(), OptionsWrite), Encoding.UTF8);
        }
    }
}
