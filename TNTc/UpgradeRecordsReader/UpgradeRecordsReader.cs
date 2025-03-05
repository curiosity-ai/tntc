using System.Text.Json;
using CodeScanner;

namespace TNTc;

public static class UpgradeRecordsReader
{
    public static IEnumerable<TranslationRecords> ReadRecords(string folderName)
    {
        var sources = File.ReadAllText(Path.Combine(folderName, ".tnt", "sources.json"));

        var oldSources = JsonSerializer.Deserialize<OldSources>(sources, OldSourcesConverter.Settings);

        foreach (var translationFile in Directory.EnumerateFiles(Path.Combine(folderName, ".tnt"), "translation-*.json"))
        {
            var translationFileText = File.ReadAllText(Path.Combine(folderName, translationFile));

            var oldRecords = JsonSerializer.Deserialize<OldRecords>(translationFileText, OldRecordsConverter.Settings);

            var translationRecordsList = new List<TranslationRecord>();

            var translationRecords = new TranslationRecords()
            {
                Language = LanguageHelper.MapLanguage(oldRecords.Language),
            };

            foreach (var oldRecord in oldRecords.Records)
            {
                var stateString      = oldRecord[0].String;
                var origString       = oldRecord[1].String;
                var translatedString = oldRecord[2].String;
                var sourceLocations  = oldRecord[3].StringArray;

                var state = stateString switch
                {
                    "translated"               => OldGoogleTranslateTranslationRecordState.Translated,
                    "new"                      => OldGoogleTranslateTranslationRecordState.New,
                    "final"                    => OldGoogleTranslateTranslationRecordState.Final,
                    "needs-review"             => OldGoogleTranslateTranslationRecordState.NeedsReview,
                    "needs-teview-translation" => OldGoogleTranslateTranslationRecordState.NeedsReviewTranslation,
                    _                          => throw new ArgumentOutOfRangeException(stateString)
                };

                translationRecordsList.Add(new TranslationRecord()
                {
                    OriginalString   = origString,
                    TranslatedString = translatedString,
                    SourceLocations = sourceLocations.Select(s => new SourceLocation()
                    {
                        SourceFilePath = s
                    }).ToArray(),
                    State = state
                });
            }

            translationRecords.Records = translationRecordsList.ToArray();

            yield return translationRecords;
        }
    }
  
}