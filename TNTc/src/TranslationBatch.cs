using System.Text.Json;
using System.Text.Json.Serialization;

namespace TNTc;

/// <summary>The interchange file between <c>tntc missing</c> and <c>tntc apply</c>: every string still waiting for a translation, with an empty slot per language to fill in.</summary>
public class TranslationBatch
{
    /// <summary>The languages this batch covers.</summary>
    public BatchLanguage[] Languages { get; set; } = Array.Empty<BatchLanguage>();

    /// <summary>How many strings are pending in the whole project, before <c>--limit</c> was applied.</summary>
    public int TotalPending { get; set; }

    /// <summary>How many strings this batch carries.</summary>
    public int Count { get; set; }

    public TranslationBatchItem[] Items { get; set; } = Array.Empty<TranslationBatchItem>();

    private static readonly JsonSerializerOptions _options = new JsonSerializerOptions()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder                = new PassThroughJavaScriptEncoder()
    };

    private static readonly JsonSerializerOptions _optionsRead = new JsonSerializerOptions()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public string ToJson() => JsonSerializer.Serialize(this, _options);

    public static TranslationBatch FromJson(string json, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<TranslationBatch>(json, _optionsRead) ?? throw new InvalidDataException($"{path} deserialized to nothing.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{path} is not a valid translation batch (line {e.LineNumber}, position {e.BytePositionInLine}): {e.Message}", e);
        }
    }
}

public class BatchLanguage
{
    public string Code { get; set; }
    public string Name { get; set; }
}

public class TranslationBatchItem
{
    /// <summary>The English source string, exactly as it appears in the code. This is the key <c>apply</c> merges on - never edit it.</summary>
    public string OriginalString { get; set; }

    /// <summary>Where the string is used, as <c>path/to/File.cs:line</c>. Empty when the string is no longer referenced by any source.</summary>
    public string[] SourceLocations { get; set; } = Array.Empty<string>();

    /// <summary>One entry per language that needs this string, keyed by language code. Fill in the value; leave a language out to skip it.</summary>
    public Dictionary<string, string> Translations { get; set; } = new Dictionary<string, string>();

    /// <summary>The text currently on file for a language that is being re-translated, keyed by language code. Informational - <c>apply</c> ignores it.</summary>
    public Dictionary<string, string>? CurrentTranslations { get; set; }
}
