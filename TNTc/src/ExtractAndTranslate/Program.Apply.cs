using TNTc;

namespace TNT.CLI;

public partial class Program
{
    private const int MAX_REPORTED_REJECTIONS = 50;

    private static readonly TranslationRecordState[] _protectedStates = [TranslationRecordState.Final, TranslationRecordState.Translated];

    /// <summary>Merges a filled-in batch file back into the translation files, validating that every translation kept the original's placeholders, markup, links and whitespace.</summary>
    public static void Apply(string rootFolderPath, string batchPath, string model, string? stateName, bool allowWarnings, bool force)
    {
        if (!File.Exists(batchPath)) throw new CommandFailedException($"Batch file {batchPath} does not exist.");

        var state = TranslationRecordState.ClaudeSkillGenerated;

        if (!string.IsNullOrWhiteSpace(stateName) && !Enum.TryParse(stateName, ignoreCase: true, out state)) throw new CommandFailedException($"Unknown translation state '{stateName}'. Known states: {string.Join(", ", Enum.GetNames<TranslationRecordState>())}");

        var batch     = TranslationBatch.FromJson(File.ReadAllText(batchPath), batchPath);
        var languages = new List<Language>();

        foreach (var code in batch.Items.SelectMany(i => i.Translations.Keys).Distinct())
        {
            if (!LanguageHelper.TryMapLanguage(code, out var language)) throw new CommandFailedException($"Batch file {batchPath} carries unknown language code '{code}'. Known codes: {LanguageHelper.AllLanguageCodes}");

            if (!languages.Contains(language)) languages.Add(language);
        }

        if (languages.Count == 0) throw new CommandFailedException($"Batch file {batchPath} carries no translations.");

        var allStrings = TranslationStore.Read(rootFolderPath, languages.ToArray());

        var applied    = 0;
        var empty      = 0;
        var kept = 0;
        var rejections = new List<string>();

        foreach (var item in batch.Items)
        {
            if (item.OriginalString is null)
            {
                rejections.Add("an item has no originalString");
                continue;
            }

            if (!allStrings.TryGetValue(item.OriginalString, out var entry))
            {
                rejections.Add($"{Describe(item.OriginalString)}: not a known string - run 'tntc extract' first, and do not edit originalString");
                continue;
            }

            foreach (var (code, translation) in item.Translations)
            {
                var language = LanguageHelper.MapLanguage(code);

                if (string.IsNullOrWhiteSpace(translation))
                {
                    empty++;
                    continue;
                }

                var issues = TranslationValidator.Validate(item.OriginalString, translation);
                var fatal  = issues.Where(i => i.Severity == ValidationSeverity.Error || !allowWarnings).ToArray();

                if (fatal.Length > 0)
                {
                    rejections.Add($"{code} {Describe(item.OriginalString)}: {string.Join("; ", fatal.Select(i => $"[{i.Rule}] {i.Message}"))}");
                    continue;
                }

                entry.TranslatedStrings ??= new Dictionary<Language, TranslatedString>();

                if (!force && entry.TranslatedStrings.TryGetValue(language, out var existing) && _protectedStates.Contains(existing.State) && !existing.IsPending)
                {
                    kept++;
                    continue;
                }

                entry.TranslatedStrings[language] = new TranslatedString()
                {
                    String      = translation,
                    State       = state,
                    GeneratedBy = model
                };
                applied++;
            }
        }

        if (applied > 0) TranslationStore.Write(rootFolderPath, allStrings, languages.ToArray());

        Console.WriteLine($"Applied {applied} translation(s) as {state} by {model} across {string.Join(", ", languages.Select(LanguageHelper.MapLanguage))}.");

        if (empty > 0) Console.WriteLine($"Left {empty} slot(s) untouched - no translation was filled in.");
        if (kept > 0) Console.WriteLine($"Kept {kept} reviewed translation(s) as they are ({string.Join(" / ", _protectedStates)}); pass --force to overwrite them.");

        if (rejections.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine($"Rejected {rejections.Count} translation(s):");

        foreach (var rejection in rejections.Take(MAX_REPORTED_REJECTIONS))
        {
            Console.WriteLine($"  {rejection}");
        }

        if (rejections.Count > MAX_REPORTED_REJECTIONS) Console.WriteLine($"  ... and {rejections.Count - MAX_REPORTED_REJECTIONS} more");

        throw new CommandFailedException($"{rejections.Count} translation(s) were rejected and not written. Fix them in {batchPath} and apply again.", exitCode: 2);
    }

    private static string Describe(string originalString)
    {
        var single = originalString.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

        return single.Length <= 60 ? $"'{single}'" : $"'{single.Substring(0, 57)}...'";
    }
}
