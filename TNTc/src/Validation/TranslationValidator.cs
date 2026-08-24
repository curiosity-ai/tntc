using System.Text.RegularExpressions;

namespace TNTc;

public enum ValidationSeverity
{
    Warning,
    Error
}

public sealed record ValidationIssue(ValidationSeverity Severity, string Rule, string Message);

/// <summary>Checks that a translation kept everything a translation is not allowed to change - placeholders, markup, links and the surrounding whitespace the caller concatenates against.</summary>
public static partial class TranslationValidator
{
    [GeneratedRegex(@"\{\d+(?::[^{}]*)?\}|\{\{[^{}]*\}\}|%[sd]", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"</?([A-Za-z][A-Za-z0-9]*)(?:\s[^<>]*)?/?>", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    public static List<ValidationIssue> Validate(string originalString, string? translatedString)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(translatedString))
        {
            if (!string.IsNullOrWhiteSpace(originalString)) issues.Add(new ValidationIssue(ValidationSeverity.Error, "empty", "the translation is empty"));

            return issues;
        }

        CompareMultiSet(issues, ValidationSeverity.Error, "placeholder", "placeholder", Matches(PlaceholderRegex(), originalString), Matches(PlaceholderRegex(), translatedString));
        CompareMultiSet(issues, ValidationSeverity.Error, "url", "URL", Matches(UrlRegex(), originalString), Matches(UrlRegex(), translatedString));

        var originalTags   = Matches(TagRegex(), originalString);
        var translatedTags = Matches(TagRegex(), translatedString);
        var beforeTags     = issues.Count;

        CompareMultiSet(issues, ValidationSeverity.Error, "html", "HTML/XML tag", Names(originalTags), Names(translatedTags));

        // Only worth reporting once the tags themselves line up - otherwise it just restates the error above.
        if (issues.Count == beforeTags) CompareMultiSet(issues, ValidationSeverity.Warning, "html-attributes", "HTML/XML tag", originalTags, translatedTags);

        var originalNewlines   = originalString.Count(c => c == '\n');
        var translatedNewlines = translatedString.Count(c => c == '\n');

        if (originalNewlines != translatedNewlines) issues.Add(new ValidationIssue(ValidationSeverity.Error, "newlines", $"the original has {originalNewlines} line break(s), the translation has {translatedNewlines}"));

        var originalLeading   = LeadingWhitespace(originalString);
        var translatedLeading = LeadingWhitespace(translatedString);

        if (originalLeading != translatedLeading) issues.Add(new ValidationIssue(ValidationSeverity.Warning, "leading-whitespace", $"the original starts with {Describe(originalLeading)}, the translation with {Describe(translatedLeading)}"));

        var originalTrailing   = TrailingWhitespace(originalString);
        var translatedTrailing = TrailingWhitespace(translatedString);

        if (originalTrailing != translatedTrailing) issues.Add(new ValidationIssue(ValidationSeverity.Warning, "trailing-whitespace", $"the original ends with {Describe(originalTrailing)}, the translation with {Describe(translatedTrailing)}"));

        return issues;
    }

    /// <summary>Reports what the two multi-sets do not have in common.</summary>
    private static void CompareMultiSet(List<ValidationIssue> issues, ValidationSeverity severity, string rule, string what, List<string> original, List<string> translated)
    {
        var missing = Missing(original, translated);
        var added   = Missing(translated, original);

        foreach (var value in missing)
        {
            issues.Add(new ValidationIssue(severity, rule, $"{what} {Quote(value)} is in the original but not in the translation"));
        }

        foreach (var value in added)
        {
            issues.Add(new ValidationIssue(severity, rule, $"{what} {Quote(value)} is in the translation but not in the original"));
        }
    }

    private static List<string> Names(List<string> tags) => tags.Select(t => TagRegex().Match(t).Groups[1].Value.ToLowerInvariant()).ToList();

    private static List<string> Missing(List<string> expected, List<string> actual)
    {
        var remaining = new List<string>(actual);
        var missing   = new List<string>();

        foreach (var value in expected)
        {
            if (!remaining.Remove(value)) missing.Add(value);
        }

        return missing;
    }

    private static List<string> Matches(Regex regex, string text) => regex.Matches(text).Select(m => m.Value).ToList();

    private static string LeadingWhitespace(string text)
    {
        var end = 0;

        while (end < text.Length && char.IsWhiteSpace(text[end])) end++;

        return text.Substring(0, end);
    }

    private static string TrailingWhitespace(string text)
    {
        var start = text.Length;

        while (start > 0 && char.IsWhiteSpace(text[start - 1])) start--;

        return text.Substring(start);
    }

    private static string Describe(string whitespace) => whitespace.Length == 0 ? "no whitespace" : Quote(whitespace);

    private static string Quote(string value) => "'" + value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "'";
}
