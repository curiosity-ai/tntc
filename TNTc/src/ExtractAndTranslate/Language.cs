namespace TNTc;

public enum Language
{
    Czech, //cs
    German, // de
    French, // fr
    Spanish, // es
    Greek, //el
    Hebrew, //he
    Hindi, // hi
    Italian, //it
    Japanese, //ja
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
    public static readonly Language[] AllLanguages = Enum.GetValues<Language>();

    public static Language MapLanguage(string oldRecordsLanguage)
    {
        return oldRecordsLanguage switch
        {
            "cs" => Language.Czech,
            "de" => Language.German,
            "fr" => Language.French,
            "es" => Language.Spanish,
            "el" => Language.Greek,
            "he" => Language.Hebrew,
            "hi" => Language.Hindi,
            "it" => Language.Italian,
            "ja" => Language.Japanese,
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
            Language.Spanish    => "es",
            Language.Greek      => "el",
            Language.Hebrew     => "he",
            Language.Hindi      => "hi",
            Language.Italian    => "it",
            Language.Japanese   => "ja",
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

    public static bool TryMapLanguage(string languageCode, out Language language)
    {
        foreach (var candidate in AllLanguages)
        {
            if (string.Equals(MapLanguage(candidate), languageCode, StringComparison.OrdinalIgnoreCase))
            {
                language = candidate;
                return true;
            }
        }

        language = default;
        return false;
    }

    public static string AllLanguageCodes => string.Join(", ", AllLanguages.Select(MapLanguage));

    /// <summary>Parses a comma-separated list of language codes, or returns every known language when the list is null or empty.</summary>
    public static Language[] ParseLanguages(string? languageCodes)
    {
        if (string.IsNullOrWhiteSpace(languageCodes)) return AllLanguages;

        var languages = new List<Language>();

        foreach (var code in languageCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryMapLanguage(code, out var language)) throw new ArgumentException($"Unknown language code '{code}'. Known codes: {AllLanguageCodes}");

            if (!languages.Contains(language)) languages.Add(language);
        }

        return languages.ToArray();
    }
}
