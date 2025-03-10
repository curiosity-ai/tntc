using System.Diagnostics;
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
    private static JsonSerializerOptions _optionsWrite = new JsonSerializerOptions()
    {
        WriteIndented = true,
        Converters =
        {
            new SourceLocationConverter(),
            new TranslationRecordStateConverter()
        },
        Encoder = new PassThroughJavaScriptEncoder()
    };

    private static JsonSerializerOptions _optionsRead = new JsonSerializerOptions()
    {
        Converters =
        {
            new SourceLocationConverter(),
            new TranslationRecordStateConverter()
        },
    };

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

            foreach (var translatableString in AnalyzeStrings(sourceCode, csFile.Substring(rootFolderPathPrefix.Length)))
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
//        var languages = Enum.GetValues<Language>();
        Language[] languages = [Language.Chinese, Language.German, Language.French, Language.Spansih, Language.Italian, Language.Portuguese];

        var allStrings = ReadExisting(rootFolderPath, languages);

        var sourceFolders = EnumerateDirectoriesToSearchForStrings(rootFolderPath).ToArray();

        var rootFolderPathPrefix = LongestCommonPrefix(sourceFolders);

        foreach (var tntFolder in sourceFolders)
        {
            foreach (var translatableString in ExtractStrings(tntFolder, rootFolderPathPrefix))
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
        }
        await TranslateStringsAndWriteStringsToDiskAsync(rootFolderPath, allStrings, languages);

        Console.WriteLine("Done.");
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

    private static Dictionary<string, TranslatedLanguageStrings> ReadExisting(string tntFolder, Language[] languages)
    {
        var strings = new Dictionary<string, TranslatedLanguageStrings>();

        foreach (var language in languages)
        {
            var translationJson = File.ReadAllText(Path.Combine(tntFolder, ".tnt", $"translation-{LanguageHelper.MapLanguage(language)}.json"), Encoding.UTF8);

            foreach (var translatedRecord in JsonSerializer.Deserialize<List<TranslatedRecord>>(translationJson, _optionsRead))
            {
                if (strings.TryGetValue(translatedRecord.OriginalString, out TranslatedLanguageStrings value))
                {
                    value.SourceLocations = new List<SourceLocation>();

                    value.TranslatedStrings[language] = new TranslatedString()
                    {
                        State  = translatedRecord.State,
                        String = translatedRecord.TranslatedString
                    };

                    strings[translatedRecord.OriginalString] = value;
                }
                else
                {
                    strings[translatedRecord.OriginalString] = new TranslatedLanguageStrings()
                    {
                        OriginalString = translatedRecord.OriginalString,
                        TranslatedStrings = new Dictionary<Language, TranslatedString>
                        {
                            {
                                language, new TranslatedString() { State = translatedRecord.State, String = translatedRecord.TranslatedString }
                            }
                        },
                        SourceLocations = new List<SourceLocation>()
                    };
                }
            }
        }

        return strings;
    }

    public class TranslatedRecord
    {
        public TranslationRecordState State            { get; set; }
        public string                 OriginalString   { get; set; }
        public string                 TranslatedString { get; set; }
        public SourceLocation[]       SourceLocations  { get; set; }
    }

    private static void WriteStringsToDisk(string rootFolder, Dictionary<string, TranslatedLanguageStrings> allStrings)
    {
        if (!Directory.Exists(Path.Combine(rootFolder, ".tnt")))
        {
            Directory.CreateDirectory(Path.Combine(rootFolder, ".tnt"));
        }

        if (!Directory.Exists(Path.Combine(rootFolder, ".tnt-content")))
        {
            Directory.CreateDirectory(Path.Combine(rootFolder, ".tnt-content"));
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
                        SourceLocations  = translatedLanguageStrings.SourceLocations?.ToArray()
                    });
                    perLanguage[language]        = records;
                    perLanguageTNTFile[language] = perLanguageTNTFile.GetValueOrDefault(language, new List<string[]>());
                    perLanguageTNTFile[language].Add([origString, translatedString.String]);

                }
            }
        }

        foreach (var (language, translationPairs) in perLanguageTNTFile)
        {
            File.WriteAllText(Path.Combine(rootFolder, ".tnt-content", $"{LanguageHelper.MapLanguage(language)}.tnt"), JsonSerializer.Serialize(translationPairs.OrderBy(e => e.First(), StringComparer.Ordinal).ToArray(), _optionsWrite), Encoding.UTF8);
        }

        foreach (var (language, translationStates) in perLanguage)
        {
            File.WriteAllText(Path.Combine(rootFolder, ".tnt", $"translation-{LanguageHelper.MapLanguage(language)}.json"), JsonSerializer.Serialize(translationStates.OrderBy(e => e.OriginalString, StringComparer.Ordinal).ToArray(), _optionsWrite), Encoding.UTF8);
        }
    }


    private static IEnumerable<KeyValuePair<string, TranslatedLanguageStrings>> FilterStrings(IEnumerable<KeyValuePair<string, TranslatedLanguageStrings>> allStrings, Language[] languages)
    {

        foreach (var keyValuePair in allStrings)
        {
            if (keyValuePair.Value.SourceLocations is object
             && keyValuePair.Value.SourceLocations.Any())
            {
                if ((keyValuePair.Value.TranslatedStrings is null
                 || languages.Any(l => !keyValuePair.Value.TranslatedStrings.ContainsKey(l))
                 || keyValuePair.Value.TranslatedStrings.Any(s => s.Value.State switch
                    {
                        TranslationRecordState.New => true,
                        _                          => false
                    })))
                {
                    yield return new KeyValuePair<string, TranslatedLanguageStrings>(keyValuePair.Key, keyValuePair.Value);
                }
            }
        }
    }


    private static async Task TranslateStringsAndWriteStringsToDiskAsync(string rootFolderPath, Dictionary<string, TranslatedLanguageStrings> allStrings, Language[] languages)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("OPENAI_API_KEY missing as environment variable");

        ChatClient client = new(model: "gpt-4o-mini", apiKey: apiKey);

        var allStringsFiltered = FilterStrings(allStrings.AsEnumerable(), languages).ToArray();

        var total     = allStringsFiltered.Length;
        var current   = 0;
        var chunkSize = 15;

        ChatCompletionOptions options = new()
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "translated_strings",
                jsonSchema: BinaryData.FromBytes("""
                                                 {
                                                     "type" : "object",
                                                     "properties": {},
                                                     "additionalProperties" : {
                                                         "type" : "object",
                                                         "properties": {},
                                                         "additionalProperties" : {
                                                             "type" : "string"
                                                        }
                                                     }
                                                 }
                                                 """u8.ToArray()),
                jsonSchemaIsStrict: true)
        };

        var stopWatch = Stopwatch.StartNew();

        foreach (var allStringsChunks in allStringsFiltered.Chunk(chunkSize))
        {
            var languageStrings = string.Join("\n", languages.Select(l => $"{l} ({LanguageHelper.MapLanguage(l)})"));

            var prompt = $$""""""
                           You are translating strings extracted from the source code of an application into multiple languages. 
                           They could either be from ui elements like buttons or text labels or information text for the user. 

                           Translate the strings into these languages. Use the language code in brackets instead of the full language name.
                           {{languageStrings}}

                           Words that should not be translated and be kept as the original string:
                           Curiosity - the name of the app
                           Space - a custom collection of items by the user
                           Workspace - the name of a Curiosity server instance
                           Sidebar - the sidebar in the app

                           Urls or urls in html should be kept literal and not be translated. Query parameters in urls should also be kept literal and not be translated.

                           Only answer with a json document with this schema and nothing else:
                           {
                             "original string" : {
                                  "language code" : "translated string",
                                  "language code2" : "translated string2",
                                  ...
                             },
                             "original string2" : {
                                  "language code" : "translated string",
                                  "language code2" : "translated string2",
                                  ...
                             }
                           }

                           Translate these strings :
                           {{JsonSerializer.Serialize(allStringsChunks.Select(kv => kv.Key).ToArray(), _optionsWrite)}}

                           """""";

            ChatCompletion completion = await client.CompleteChatAsync(
            [
                new UserChatMessage(prompt),
            ], options);

            var result = completion.Content[0].Text;

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
                TranslatedLanguageStrings translatedStrings;

                if (allStrings.TryGetValue(origString, out translatedStrings))
                {
                    foreach (var (langCode, translatedString) in translatedPairs)
                    {
                        var language = LanguageHelper.MapLanguage(langCode);

                        translatedStrings.TranslatedStrings ??= new Dictionary<Language, TranslatedString>();

                        if (!translatedStrings.TranslatedStrings.ContainsKey(language) || translatedStrings.TranslatedStrings[language].State == TranslationRecordState.New)
                        {
                            translatedStrings.TranslatedStrings[language] = new TranslatedString()
                            {
                                String = translatedString,
                                State  = TranslationRecordState.GPT4oMiniGenerated,

                            };
                        }
                    }
                }
                else
                {
                    translatedStrings = new TranslatedLanguageStrings()
                    {
                        OriginalString = origString,
                    };

                    foreach (var (langCode, translatedString) in translatedPairs)
                    {
                        var language = LanguageHelper.MapLanguage(langCode);

                        translatedStrings.TranslatedStrings ??= new Dictionary<Language, TranslatedString>();

                        if (!translatedStrings.TranslatedStrings.ContainsKey(language) || translatedStrings.TranslatedStrings[language].State == TranslationRecordState.New)
                        {
                            translatedStrings.TranslatedStrings[language] = new TranslatedString()
                            {
                                String = translatedString,
                                State  = TranslationRecordState.GPT4oMiniGenerated,

                            };
                        }
                    }
                    allStrings[origString] = translatedStrings;
                }
            }
            current += chunkSize;
            var elapsed = stopWatch.Elapsed;

            TimeSpan totalTime = elapsed * ((double)total / (double)current);

            Console.WriteLine($"Done {current}/{total} remiaining: {totalTime - elapsed:g}");

            WriteStringsToDisk(rootFolderPath, allStrings);

        }

    }
}