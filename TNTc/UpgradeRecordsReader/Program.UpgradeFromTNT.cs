using System.Text.Json;
using System.Text.Json.Serialization;
using CodeScanner;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TNTc;
using OpenAI.Chat;
using J = System.Text.Json.Serialization.JsonPropertyNameAttribute;
using N = System.Text.Json.Serialization.JsonIgnoreCondition;

namespace TNT.CLI;

public partial class Program
{

    private static void UpgradePrivateFiles(string rootFolderPath)
    {
        
    }
    public static void UpgradeFromTNT(string rootFolderPath)
    {
        foreach (var tntFolder in EnumerateTranslationProjectFolders(rootFolderPath))
        {
            var allStrings = new Dictionary<string, TranslatedLanguageStrings>();

            var existing = UpgradeRecordsReader.ReadRecords(tntFolder).ToArray();

            foreach (var exisitngRecords in existing)
            {
                foreach (var exisitngRecord in exisitngRecords.Records)
                {
                    if (allStrings.TryGetValue(exisitngRecord.OriginalString, out TranslatedLanguageStrings value))
                    {
                        if (value.TranslatedStrings.TryGetValue(exisitngRecords.Language, out var translatedStringForLanguage))
                        {
                            throw new InvalidOperationException();
                        }
                        else
                        {
                            value.TranslatedStrings[exisitngRecords.Language] = new TranslatedString()
                            {
                                String = exisitngRecord.TranslatedString,
                                State = exisitngRecord.State switch
                                {
                                    OldGoogleTranslateTranslationRecordState.New                    => TranslationRecordState.New,
                                    OldGoogleTranslateTranslationRecordState.NeedsReview            => TranslationRecordState.NeedsReview,
                                    OldGoogleTranslateTranslationRecordState.NeedsReviewTranslation => TranslationRecordState.NeedsReviewTranslation,
                                    OldGoogleTranslateTranslationRecordState.Translated             => TranslationRecordState.Translated,
                                    OldGoogleTranslateTranslationRecordState.Final                  => TranslationRecordState.Final,
                                    _                                                               => throw new ArgumentOutOfRangeException()
                                }
                            };
                        }
                    }
                    else
                    {
                        allStrings[exisitngRecord.OriginalString] = new TranslatedLanguageStrings()
                        {
                            OriginalString = exisitngRecord.OriginalString,
                            TranslatedStrings = new Dictionary<Language, TranslatedString>()
                            {
                                {
                                    exisitngRecords.Language, new TranslatedString()
                                    {
                                        String = exisitngRecord.TranslatedString,
                                        State = exisitngRecord.State switch
                                        {
                                            OldGoogleTranslateTranslationRecordState.New                    => TranslationRecordState.New,
                                            OldGoogleTranslateTranslationRecordState.NeedsReview            => TranslationRecordState.NeedsReview,
                                            OldGoogleTranslateTranslationRecordState.NeedsReviewTranslation => TranslationRecordState.NeedsReviewTranslation,
                                            OldGoogleTranslateTranslationRecordState.Translated             => TranslationRecordState.Translated,
                                            OldGoogleTranslateTranslationRecordState.Final                  => TranslationRecordState.Final,
                                            _                                                               => throw new ArgumentOutOfRangeException()
                                        }
                                    }
                                }
                            },
                            SourceLocations = new List<SourceLocation>()
                        };
                    }
                }
            }


            WriteStringsToDisk(tntFolder, allStrings);

        }

        Console.WriteLine("Done.");
    }

}