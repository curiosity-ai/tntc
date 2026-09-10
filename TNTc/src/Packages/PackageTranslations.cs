using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeScanner;

namespace TNTc;

/// <summary>What one referenced package contributes: the languages it ships a table for, and how many strings its assemblies were found to display.</summary>
public sealed class PackageManifestEntry
{
    public string                  Id             { get; set; } = "";
    public string                  Version        { get; set; } = "";
    public Dictionary<string, int> Tables         { get; set; } = new Dictionary<string, int>();

    /// <summary>How many distinct strings the assembly scan found, or null when this package was never scanned.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ScannedStrings { get; set; }

    [JsonIgnore]
    public string Coordinates => $"{Id}/{Version}";
}

/// <summary>A string a package translated for us, and which package it came from.</summary>
public sealed class PackageTranslation
{
    public string TranslatedString { get; set; } = "";
    public string Coordinates      { get; set; } = "";
}

/// <summary>A string an assembly was found to display, and the members it is displayed from.</summary>
public sealed class ScannedString
{
    public string           OriginalString  { get; set; } = "";
    public SourceLocation[] SourceLocations { get; set; } = Array.Empty<SourceLocation>();
}

/// <summary>
/// Everything the referenced packages contribute, as it is kept on disk under <c>.tnt/packages/</c>:
/// the tables they ship, the strings their assemblies were scanned for, and the manifest saying
/// which package each came from.
///
/// It is written from a restored package graph by <c>extract --include-packages</c> /
/// <c>--scan-assemblies</c> and read by every other command, so <c>apply</c> and <c>sync</c>
/// reproduce the same <c>.tnt-content</c> without needing a restore - and so what a package
/// contributes arrives as a reviewable diff rather than at run time.
/// </summary>
public sealed class PackageContributions
{
    public List<PackageManifestEntry>                                   Packages     { get; set; } = new List<PackageManifestEntry>();
    public Dictionary<Language, Dictionary<string, PackageTranslation>> Translations { get; set; } = new Dictionary<Language, Dictionary<string, PackageTranslation>>();
    public List<ScannedString>                                          Scanned      { get; set; } = new List<ScannedString>();

    public bool IsEmpty => Packages.Count == 0 && Translations.Count == 0 && Scanned.Count == 0;

    /// <summary>The text a package translated this string to, if any did.</summary>
    public bool TryGetTranslation(Language language, string originalString, out PackageTranslation translation)
    {
        translation = null!;

        return Translations.TryGetValue(language, out var byString) && byString.TryGetValue(originalString, out translation!);
    }
}

/// <summary>Reads and writes <c>.tnt/packages/</c> - the generated record of what the referenced packages contribute.</summary>
public static class PackageTranslationStore
{
    public const string PACKAGES_FOLDER = "packages";
    public const string MANIFEST_FILE   = "packages.json";
    public const string SCANNED_FILE    = "strings.json";

    private static readonly JsonSerializerOptions _write = new JsonSerializerOptions()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters             = { new SourceLocationConverter(), new TranslationRecordStateConverter() },
        Encoder                = new PassThroughJavaScriptEncoder()
    };

    private static readonly JsonSerializerOptions _read = new JsonSerializerOptions()
    {
        PropertyNameCaseInsensitive = true,
        Converters                  = { new SourceLocationConverter(), new TranslationRecordStateConverter() }
    };

    public static string Folder(string rootFolder) => Path.Combine(rootFolder, TranslationStore.TNT_FOLDER, PACKAGES_FOLDER);

    private static string ManifestPath(string rootFolder)              => Path.Combine(Folder(rootFolder), MANIFEST_FILE);
    private static string ScannedPath(string rootFolder)               => Path.Combine(Folder(rootFolder), SCANNED_FILE);
    private static string TablePath(string rootFolder, Language l)     => Path.Combine(Folder(rootFolder), $"translations-{LanguageHelper.MapLanguage(l)}.json");

    /// <summary>What is on disk. A project that never imported a package's tables reads back empty, which is what makes every command work unchanged without the flags.</summary>
    public static PackageContributions Read(string rootFolder)
    {
        var contributions = new PackageContributions();
        var folder        = Folder(rootFolder);

        if (!Directory.Exists(folder)) return contributions;

        contributions.Packages = ReadJson<List<PackageManifestEntry>>(ManifestPath(rootFolder)) ?? new List<PackageManifestEntry>();
        contributions.Scanned  = ReadJson<List<ScannedString>>(ScannedPath(rootFolder))         ?? new List<ScannedString>();

        foreach (var language in LanguageHelper.AllLanguages)
        {
            var records = ReadJson<List<TranslatedRecord>>(TablePath(rootFolder, language));

            if (records is null || records.Count == 0) continue;

            var byString = new Dictionary<string, PackageTranslation>();

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.TranslatedString)) continue;

                byString[record.OriginalString] = new PackageTranslation() { TranslatedString = record.TranslatedString, Coordinates = record.GeneratedBy ?? "" };
            }

            contributions.Translations[language] = byString;
        }

        return contributions;
    }

    /// <summary>Rewrites the folder wholesale, so a package that is no longer referenced stops contributing - which is the only way a removed reference can take its strings with it.</summary>
    public static void Write(string rootFolder, PackageContributions contributions)
    {
        var folder = Folder(rootFolder);

        if (contributions.IsEmpty)
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            return;
        }

        Directory.CreateDirectory(folder);

        foreach (var stale in Directory.EnumerateFiles(folder, "translations-*.json"))
        {
            File.Delete(stale);
        }

        WriteJson(ManifestPath(rootFolder), contributions.Packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToArray());
        WriteJson(ScannedPath(rootFolder),  contributions.Scanned.OrderBy(s => s.OriginalString, StringComparer.Ordinal).ToArray());

        foreach (var (language, byString) in contributions.Translations)
        {
            if (byString.Count == 0) continue;

            var records = byString
               .OrderBy(e => e.Key, StringComparer.Ordinal)
               .Select(e => new TranslatedRecord()
                {
                    State            = TranslationRecordState.PackageProvided,
                    GeneratedBy      = e.Value.Coordinates,
                    OriginalString   = e.Key,
                    TranslatedString = e.Value.TranslatedString,
                    SourceLocations  = Array.Empty<SourceLocation>()
                })
               .ToArray();

            WriteJson(TablePath(rootFolder, language), records);
        }
    }

    /// <summary>Loads the <c>l10n/&lt;code&gt;.tnt</c> tables a package ships, in the layout <c>tntc</c> itself writes to <c>.tnt-content</c>.</summary>
    public static Dictionary<Language, Dictionary<string, string>> ReadPackageTables(ReferencedPackage package, Language[] languages)
    {
        var tables = new Dictionary<Language, Dictionary<string, string>>();
        var folder = Path.Combine(package.Folder, "l10n");

        if (!Directory.Exists(folder)) return tables;

        foreach (var language in languages)
        {
            var path = Path.Combine(folder, $"{LanguageHelper.MapLanguage(language)}.tnt");

            if (!File.Exists(path)) continue;

            string[][]? pairs;

            try
            {
                pairs = JsonSerializer.Deserialize<string[][]>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"{package.Coordinates} ships {path}, which is not a valid translation table: {e.Message}", e);
            }

            if (pairs is null) continue;

            var byString = new Dictionary<string, string>();

            foreach (var pair in pairs)
            {
                if (pair.Length < 2 || string.IsNullOrWhiteSpace(pair[1])) continue;

                byString[pair[0]] = pair[1];
            }

            if (byString.Count > 0) tables[language] = byString;
        }

        return tables;
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), _read);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{path} is not valid JSON (line {e.LineNumber}, position {e.BytePositionInLine}): {e.Message}", e);
        }
    }

    private static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, _write), Encoding.UTF8);
}
