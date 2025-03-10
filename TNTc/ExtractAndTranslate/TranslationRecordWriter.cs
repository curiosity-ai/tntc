using CodeScanner;

namespace TNTc;

public class TranslationRecords
{
    public Language            Language { get; set; }
    public TranslationRecord[] Records  { get; set; }
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
public class TranslationRecordWriter
{


}