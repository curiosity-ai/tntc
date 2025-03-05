using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text.Unicode;
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

    public static IEnumerable<TranslatableString> HandleTranslationProjectFolder(string folderPath, string rootFolderPath)
    {
        var allCSFiles = Directory.EnumerateFiles(folderPath, "*.cs", SearchOption.AllDirectories);

        foreach (var csFile in allCSFiles)
        {
            var sourceCode = File.ReadAllText(csFile);

            foreach (var translatableString in AnalyzeStrings(sourceCode, csFile.Substring(rootFolderPath.Length)))
            {
                yield return translatableString;
            }

        }
    }

    public static IEnumerable<string> EnumerateTranslationProjectFolders(string rootFolderPath)
    {
        foreach (var enumerateDirectory in Directory.EnumerateDirectories(rootFolderPath, ".tnt", SearchOption.AllDirectories))
        {
            yield return enumerateDirectory.Substring(0, enumerateDirectory.Length - ".tnt".Length);
        }
    }


    public class TranslatedString
    {
        public string                 String { get; set; }
        public TranslationRecordState State  { get; set; }
    }

    public class TranslatedLanguageStrings
    {
        public string                                 OriginalString    { get; set; }
        public Dictionary<Language, TranslatedString> TranslatedStrings { get; set; }
        public List<SourceLocation>                   SourceLocations   { get; set; }
    }


    public static async Task Extract(string rootFolderPath)
    {

        foreach (var tntFolder in EnumerateTranslationProjectFolders(rootFolderPath))
        {
            var allStrings = new Dictionary<string, TranslatedLanguageStrings>();

            var existing = UpgradeRecordsReader.ReadRecords(tntFolder).ToArray();

            foreach (var exisitngRecords in existing)
            {
                foreach (var exisitngRecord in exisitngRecords.Records)
                {
                    switch (exisitngRecord.State)
                    {
                        case OldGoogleTranslateTranslationRecordState.New:
                        case OldGoogleTranslateTranslationRecordState.NeedsReview:
                        case OldGoogleTranslateTranslationRecordState.NeedsReviewTranslation:
                        {
                            continue; // ignore, needs better translation than google translate
                        }
                    }

                    if (allStrings.TryGetValue(exisitngRecord.OriginalString, out TranslatedLanguageStrings value))
                    {
//                        value.SourceLocations.AddRange(exisitngRecord.SourceLocations);
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

            foreach (var translatableString in HandleTranslationProjectFolder(tntFolder, rootFolderPath))
            {
                if (allStrings.TryGetValue(translatableString.SourceString, out TranslatedLanguageStrings value))
                {
                    value.OriginalString = translatableString.SourceString;

                    value.SourceLocations.Add(translatableString.SourceLocation);
                }
                else
                {
                    allStrings[translatableString.SourceString] = new TranslatedLanguageStrings()
                    {
                        OriginalString  = translatableString.SourceString,
                        SourceLocations = new List<SourceLocation>() { translatableString.SourceLocation }
                    };
                }
            }


            allStrings = await TranslateStringsAsync(allStrings, new[] { Language.German, Language.French, Language.Italian, Language.Chinese, Language.Malay });

            WriteStringsToDisk(tntFolder, allStrings);

        }

        Console.WriteLine("Done.");
    }

    public class TranslatedRecord
    {
        public TranslationRecordState State            { get; set; }
        public string                 OriginalString   { get; set; }
        public string                 TranslatedString { get; set; }
        public SourceLocation[]       SourceLocations  { get; set; }
    }

    private static void WriteStringsToDisk(string tntFolder, Dictionary<string, TranslatedLanguageStrings> allStrings)
    {
//TODO write .tnt/sources.json
//TODO write .tnt/translation-*.json.json

        if (!Directory.Exists(Path.Combine(tntFolder, ".tnt")))
        {
            Directory.CreateDirectory(Path.Combine(tntFolder, ".tnt"));
        }

        if (!Directory.Exists(Path.Combine(tntFolder, ".tnt-content")))
        {
            Directory.CreateDirectory(Path.Combine(tntFolder, ".tnt-content"));
        }

        var perLanguage        = new Dictionary<Language, List<TranslatedRecord>>();
        var perLanguageTNTFile = new Dictionary<Language, List<string[]>>();


        foreach (var (origString, translatedLanguageStrings) in allStrings)
        {
            if (translatedLanguageStrings.TranslatedStrings is object)
            {
                foreach (var (language, translatedString) in translatedLanguageStrings.TranslatedStrings)
                {
                    var records = perLanguage.GetValueOrDefault(language, new List<TranslatedRecord>());

                    records.Add(new TranslatedRecord()
                    {
                        State            = translatedString.State,
                        OriginalString   = translatedLanguageStrings.OriginalString,
                        TranslatedString = translatedString.String,
                        SourceLocations  = translatedLanguageStrings.SourceLocations.ToArray()
                    });
                    perLanguage[language]        = records;
                    perLanguageTNTFile[language] = perLanguageTNTFile.GetValueOrDefault(language, new List<string[]>());
                    perLanguageTNTFile[language].Add([origString, translatedString.String]);

                }
            }
        }

        var encoderSettings = new TextEncoderSettings();
        encoderSettings.AllowRange(UnicodeRanges.All);
        encoderSettings.AllowCharacters('\u0022', '\u0027', '\u00A0');
//        encoderSettings.AllowCharacters('\u0022');
//        encoderSettings.AllowRange(UnicodeRanges.Cyrillic);

        var optionsWrite = new JsonSerializerOptions()
        {
            WriteIndented = true,
            Converters =
            {
                new SourceLocationConverter(),
                new TranslationRecordStateConverter()
            },
//            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            Encoder = new PassThroughJavaScriptEncoder()
//            Encoder =JavaScriptEncoder.Create(encoderSettings), 

        };

        var optionsRead = new JsonSerializerOptions()
        {
            Converters =
            {
                new SourceLocationConverter(),
                new TranslationRecordStateConverter()
            },
        };

        foreach (var (language, translationPairs) in perLanguageTNTFile)
        {
            File.WriteAllText(Path.Combine(tntFolder, ".tnt-content", $"{LanguageHelper.MapLanguage(language)}.tnt"), JsonSerializer.Serialize(translationPairs.OrderBy(e => e.First(), StringComparer.Ordinal).ToArray(), optionsWrite), Encoding.UTF8);
        }

        foreach (var (language, translationStates) in perLanguage)
        {
            File.WriteAllText(Path.Combine(tntFolder, ".tnt", $"translation-{LanguageHelper.MapLanguage(language)}.json"), JsonSerializer.Serialize(translationStates.OrderBy(e => e.OriginalString, StringComparer.Ordinal).ToArray(), optionsWrite), Encoding.UTF8);
        }
    }



    private static async Task<Dictionary<string, TranslatedLanguageStrings>> TranslateStringsAsync(Dictionary<string, TranslatedLanguageStrings> allStrings, Language[] languages)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("OPENAI_API_KEY missing as environment variable");

        ChatClient client = new(model: "gpt-4o-mini", apiKey: apiKey);

        var total     = allStrings.Count;
        var current   = 0;
        var chunkSize = 25;

        foreach (var allStringsChunks in allStrings.Chunk(chunkSize))
        {

            //TODO filter out already translated


            var languageStrings = string.Join("\n", languages.Select(l => $"{l} ({LanguageHelper.MapLanguage(l)})"));


            var prompt = $$""""""
                           You are translating strings extracted from the source code of an application into multiple languages. 
                           They could either be from ui elements like buttons or text labels or information text for the user. 

                           Translate the strings into these languages. Use the language code in brackets instead of the full language name.
                           {{languageStrings}}

                           Only answer with a json document with this schema and nothing else:
                           {
                             "original string" :   {
                                  "language code" : "translated string",
                                  "language code2" : "translated string2",
                                  ...
                           },
                             "original string2" :   {
                                  "language code" : "translated string",
                                  "language code2" : "translated string2",
                                  ...
                           }
                           }

                           Translate these strings :
                           {{JsonSerializer.Serialize(allStringsChunks, new JsonSerializerOptions() { WriteIndented = true })}}

                           """""";


            ChatCompletion completion = await client.CompleteChatAsync(prompt);

            var result = completion.Content[0].Text;
            Console.WriteLine(result);

            if (result.StartsWith("```json"))
            {
                result = result.Substring("```json".Length);
            }

            if (result.EndsWith("```"))
            {
                result = result.Substring(0, result.Length - "```".Length);
            }

            var resultParsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(result);

            foreach (var (origString, translatedPairs) in resultParsed)
            {
                if (allStrings.TryGetValue(origString, out TranslatedLanguageStrings translatedStrings))
                {
                    foreach (var (langCode, translatedString) in translatedPairs)
                    {
                        var language = LanguageHelper.MapLanguage(langCode);

                        translatedStrings.TranslatedStrings ??= new Dictionary<Language, TranslatedString>();

                        if (!translatedStrings.TranslatedStrings.ContainsKey(language))
                        {
                            translatedStrings.TranslatedStrings[language] = new TranslatedString()
                            {
                                String = translatedString,
                                State  = TranslationRecordState.GPT4oMiniGenerated,

                            };
                        }
                    }
                }
            }
            current += chunkSize;
            Console.WriteLine($"Done {current}/{total}");
            break;
        }

        return allStrings;
    }
}