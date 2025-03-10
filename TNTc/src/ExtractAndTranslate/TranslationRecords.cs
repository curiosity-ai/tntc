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
    public string OriginalString { get; set; }
    public string TranslatedString { get; set; }
    public SourceLocation[] SourceLocations { get; set; }
}

public enum TranslationRecordState
{
    New,
    NeedsReview,
    GPT4oMiniGenerated,
    NeedsReviewTranslation,
    Translated,
    Final,
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
    public string String { get; set; }
    public TranslationRecordState State { get; set; }
}

public class TranslatedLanguageStrings
{
    public string OriginalString { get; set; }
    public Dictionary<Language, TranslatedString> TranslatedStrings { get; set; }
    public List<SourceLocation> SourceLocations { get; set; }
}
