using CodeScanner;
using TNTc;

namespace TNT.CLI;

public partial class Program
{
    /// <summary>Writes every string still waiting for a translation as a batch file, for a translator (the Claude Code skill, or a human) to fill in and hand to <c>apply</c>.</summary>
    public static void Missing(string rootFolderPath, string? languageCodes, int limit, string? outputPath, bool includeUnused, string? retranslateStates)
    {
        var languages  = LanguageHelper.ParseLanguages(languageCodes);
        var retranslate = ParseStates(retranslateStates);
        var allStrings = TranslationStore.Read(rootFolderPath, languages);

        var pending = new List<TranslationBatchItem>();

        foreach (var entry in allStrings.Values.OrderBy(e => e.OriginalString, StringComparer.Ordinal))
        {
            if (!includeUnused && (entry.SourceLocations is null || entry.SourceLocations.Count == 0)) continue;

            var item = new TranslationBatchItem()
            {
                OriginalString  = entry.OriginalString,
                SourceLocations = entry.SourceLocations?.Select(l => $"{l.SourceFilePath}:{l.SourceFileLine}").ToArray() ?? Array.Empty<string>()
            };

            foreach (var language in languages)
            {
                var code = LanguageHelper.MapLanguage(language);

                if (entry.TranslatedStrings is null || !entry.TranslatedStrings.TryGetValue(language, out var translated))
                {
                    item.Translations[code] = "";
                    continue;
                }

                if (translated.IsPending)
                {
                    item.Translations[code] = "";
                    continue;
                }

                if (retranslate.Contains(translated.State))
                {
                    item.Translations[code]         = "";
                    item.CurrentTranslations      ??= new Dictionary<string, string>();
                    item.CurrentTranslations[code]  = translated.String;
                }
            }

            if (item.Translations.Count > 0) pending.Add(item);
        }

        var batch = new TranslationBatch()
        {
            Languages    = languages.Select(l => new BatchLanguage() { Code = LanguageHelper.MapLanguage(l), Name = l.ToString() }).ToArray(),
            TotalPending = pending.Count,
            Count        = limit > 0 ? Math.Min(limit, pending.Count) : pending.Count,
            Items        = (limit > 0 ? pending.Take(limit) : pending).ToArray()
        };

        var json = batch.ToJson();

        if (string.IsNullOrEmpty(outputPath))
        {
            Console.Out.Write(json);
            Console.Out.Write('\n');
            return;
        }

        File.WriteAllText(outputPath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"{batch.Count} of {batch.TotalPending} pending string(s) written to {outputPath} for {string.Join(", ", batch.Languages.Select(l => l.Code))}.");

        if (batch.TotalPending > batch.Count) Console.WriteLine($"{batch.TotalPending - batch.Count} string(s) remain - run 'tntc missing' again after applying this batch.");
    }

    private static HashSet<TranslationRecordState> ParseStates(string? states)
    {
        var parsed = new HashSet<TranslationRecordState>();

        if (string.IsNullOrWhiteSpace(states)) return parsed;

        foreach (var state in states.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<TranslationRecordState>(state, ignoreCase: true, out var parsedState)) throw new CommandFailedException($"Unknown translation state '{state}'. Known states: {string.Join(", ", Enum.GetNames<TranslationRecordState>())}");

            parsed.Add(parsedState);
        }

        return parsed;
    }
}
