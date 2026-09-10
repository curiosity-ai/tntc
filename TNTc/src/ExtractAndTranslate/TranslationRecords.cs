using System.Text.Json.Serialization;
using CodeScanner;

namespace TNTc;

public class TranslationRecords
{
    public Language            Language { get; set; }
    public TranslationRecord[] Records  { get; set; }
}

public class TranslatedRecord
{
    public TranslationRecordState State { get; set; }

    /// <summary>Identifies what produced the translation - the model id for <see cref="TranslationRecordState.ClaudeSkillGenerated"/>, whoever ran <c>apply</c> otherwise. Omitted when unknown, as it is on every record written before this was recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GeneratedBy { get; set; }

    public string           OriginalString   { get; set; }
    public string           TranslatedString { get; set; }
    public SourceLocation[] SourceLocations  { get; set; }
}

public enum TranslationRecordState
{
    New,
    NeedsReview,
    [Obsolete("use LLMGenerated instead")] GPT4oMiniGenerated,
    NeedsReviewTranslation,
    Translated,
    Final,
    LLMGenerated,
    ClaudeSkillGenerated,

    /// <summary>Translated by the package that ships the string, not by this project. Written into <c>.tnt/packages/</c> by <c>extract --include-packages</c>, never queued for this project's translator, and shadowed by a translation of our own.</summary>
    PackageProvided
}

public enum OldGoogleTranslateTranslationRecordState
{   

    New,
    NeedsReview,
    NeedsReviewTranslation,
    Translated,
    Final,
}

public class TranslationRecord
{
    public OldGoogleTranslateTranslationRecordState State            { get; set; }
    public string                                   OriginalString   { get; set; }
    public string                                   TranslatedString { get; set; }
    public SourceLocation[]                         SourceLocations  { get; set; }
}

public class TranslatedString
{
    public string                 String      { get; set; }
    public TranslationRecordState State       { get; set; }
    public string?                GeneratedBy { get; set; }

    /// <summary>A translation is pending while no text has been produced for it yet - <see cref="TranslationRecordState.New"/> alone does not mean pending, since the pre-LLM records carry text in that state.</summary>
    public bool IsPending => string.IsNullOrWhiteSpace(String);
}

public class TranslatedLanguageStrings
{
    public string OriginalString { get; set; }
    public Dictionary<Language, TranslatedString> TranslatedStrings { get; set; }
    public List<SourceLocation> SourceLocations { get; set; }
}
