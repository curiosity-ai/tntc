namespace TNTc;

public enum Language
{
    Czech, //cs
    German, // de
    French, // fr
    Spansih, // es
    Greek, //el
    Hebrew, //he
    Hindi, // hi
    Italian, //it
    Japanses, //ja
    Korean, //ko
    Malay, //ms
    Nepali, //ne
    Dutch, //nl
    Polish, //pl
    Portuguese, //pt
    Russian, //russian
    Serbian, //sr
    Swedish, //sv
    Ukrainian, //uk
    Chinese // zh
}
public static class LanguageHelper
{

    public static Language MapLanguage(string oldRecordsLanguage)
    {
        return oldRecordsLanguage switch
        {
            "cs" => Language.Czech,
            "de" => Language.German,
            "fr" => Language.French,
            "es" => Language.Spansih,
            "el" => Language.Greek,
            "he" => Language.Hebrew,
            "hi" => Language.Hindi,
            "it" => Language.Italian,
            "ja" => Language.Japanses,
            "ko" => Language.Korean,
            "ms" => Language.Malay,
            "ne" => Language.Nepali,
            "nl" => Language.Dutch,
            "pl" => Language.Polish,
            "pt" => Language.Portuguese,
            "ru" => Language.Russian,
            "sr" => Language.Serbian,
            "sv" => Language.Swedish,
            "uk" => Language.Ukrainian,
            "zh" => Language.Chinese,
            _    => throw new ArgumentOutOfRangeException(nameof(oldRecordsLanguage), oldRecordsLanguage, null)
        };
    }

    public static string MapLanguage(Language language)
    {
        return language switch
        {
            Language.Czech      => "cs",
            Language.German     => "de",
            Language.French     => "fr",
            Language.Spansih    => "es",
            Language.Greek      => "el",
            Language.Hebrew     => "he",
            Language.Hindi      => "hi",
            Language.Italian    => "it",
            Language.Japanses   => "ja",
            Language.Korean     => "ko",
            Language.Malay      => "ms",
            Language.Nepali     => "ne",
            Language.Dutch      => "nl",
            Language.Polish     => "pl",
            Language.Portuguese => "pt",
            Language.Russian    => "ru",
            Language.Serbian    => "sr",
            Language.Swedish    => "sv",
            Language.Ukrainian  => "uk",
            Language.Chinese    => "zh",
            _                   => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
    }
}