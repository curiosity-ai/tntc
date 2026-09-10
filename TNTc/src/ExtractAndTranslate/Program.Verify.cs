using TNTc;

namespace TNT.CLI;

public partial class Program
{
    private const int MAX_REPORTED_ISSUES_PER_LANGUAGE = 20;

    /// <summary>Checks that every translation file parses and that every translation on file kept the original's placeholders, markup, links and whitespace. Exits non-zero when an error is found, so it can guard a build.</summary>
    public static void Verify(string rootFolderPath, string? languageCodes, bool strict)
    {
        var languages = LanguageHelper.ParseLanguages(languageCodes);
        var errors    = 0;
        var warnings  = 0;

        Console.WriteLine($"{"lang",-6}{"total",8}{"pending",9}{"by pkg",8}{"unused",8}{"errors",8}{"warnings",10}");

        foreach (var language in languages)
        {
            var path = TranslationStore.TranslationFilePath(rootFolderPath, language);

            if (!File.Exists(path))
            {
                Console.WriteLine($"{LanguageHelper.MapLanguage(language),-6}{"- no translation file -",51}");
                continue;
            }

            List<TranslatedRecord> records;

            try
            {
                records = TranslationStore.ReadLanguage(rootFolderPath, language);
            }
            catch (InvalidDataException e)
            {
                Console.WriteLine($"{LanguageHelper.MapLanguage(language),-6}   ERROR {e.Message}");
                errors++;
                continue;
            }

            var pending          = 0;
            var byPackage        = 0;
            var unused           = 0;
            var languageErrors   = new List<string>();
            var languageWarnings = new List<string>();

            foreach (var record in records)
            {
                if (record.SourceLocations is null || record.SourceLocations.Length == 0) unused++;

                if (string.IsNullOrWhiteSpace(record.TranslatedString))
                {
                    // A record left to a referenced package carries no text of ours on purpose - it
                    // is answered, not outstanding.
                    if (record.State == TranslationRecordState.PackageProvided) byPackage++; else pending++;

                    continue;
                }

                foreach (var issue in TranslationValidator.Validate(record.OriginalString, record.TranslatedString))
                {
                    var message = $"{Describe(record.OriginalString)}: [{issue.Rule}] {issue.Message}";

                    if (issue.Severity == ValidationSeverity.Error || strict)
                    {
                        languageErrors.Add(message);
                    }
                    else
                    {
                        languageWarnings.Add(message);
                    }
                }
            }

            errors   += languageErrors.Count;
            warnings += languageWarnings.Count;

            Console.WriteLine($"{LanguageHelper.MapLanguage(language),-6}{records.Count,8}{pending,9}{byPackage,8}{unused,8}{languageErrors.Count,8}{languageWarnings.Count,10}");

            foreach (var error in languageErrors.Take(MAX_REPORTED_ISSUES_PER_LANGUAGE))
            {
                Console.WriteLine($"      ERROR {error}");
            }

            if (languageErrors.Count > MAX_REPORTED_ISSUES_PER_LANGUAGE) Console.WriteLine($"      ... and {languageErrors.Count - MAX_REPORTED_ISSUES_PER_LANGUAGE} more error(s)");
        }

        VerifyPackages(rootFolderPath, languages, TranslationStore.Read(rootFolderPath, languages));

        Console.WriteLine();
        Console.WriteLine($"{errors} error(s), {warnings} warning(s).");

        if (!strict && warnings > 0) Console.WriteLine("Run with --strict to fail on warnings too.");

        if (errors > 0) throw new CommandFailedException($"Verification failed with {errors} error(s).", exitCode: 3);
    }
}
