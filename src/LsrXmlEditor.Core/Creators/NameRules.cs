using System.Globalization;
using System.Text.RegularExpressions;

namespace LsrXmlEditor.Core.Creators;

public static partial class NameRules
{
    public const string GangPrefix = "AMBIENT_GANG_";
    private const int ShortNameLimit = 12;
    private const int PlateLength = 3;
    private static readonly HashSet<string> FillerWords = new(["the", "of", "and"], StringComparer.OrdinalIgnoreCase);

    public static string Token(string text)
    {
        var value = text.Trim();
        if (value.StartsWith(GangPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[GangPrefix.Length..];
        }

        return string.Concat(Words(value).Select(p => char.ToUpperInvariant(p[0]) + (p.All(c => !char.IsLower(c)) ? p[1..].ToLowerInvariant() : p[1..])));
    }

    public static string GangId(string name)
    {
        var words = Words(StripArticle(name)).Select(w => w.ToUpperInvariant()).ToList();
        return words.Count == 0 ? string.Empty : GangPrefix + string.Join('_', words);
    }

    public static string Initials(string name)
    {
        var words = Words(name).ToList();
        var significant = words.Where(w => !FillerWords.Contains(w)).ToList();
        if (significant.Count == 0)
        {
            significant = words;
        }

        if (significant.Count == 1)
        {
            var word = significant[0].ToUpperInvariant();
            return word.Length <= 5 ? word : word[..4];
        }

        return string.Concat(significant.Select(w => char.ToUpperInvariant(w[0])));
    }

    public static string ShortName(string name)
    {
        var stripped = StripArticle(name).Trim();
        return stripped.Length <= ShortNameLimit ? stripped.ToUpperInvariant() : Initials(name);
    }

    public static string PlatePrefix(string name)
    {
        var words = Words(StripArticle(name)).ToList();
        if (words.Count == 0)
        {
            return string.Empty;
        }

        var letters = words.Count > 1 ? string.Concat(words.Select(w => w[0])) : words[0];
        letters = letters.ToUpperInvariant();
        return letters.Length > PlateLength ? letters[..PlateLength] : letters;
    }

    public static string Replace(string text, string from, string to) =>
        from.Length > 0 && text.Contains(from, StringComparison.Ordinal) ? text.Replace(from, to, StringComparison.Ordinal) : text;

    public static string LastWord(string text) => Words(text).LastOrDefault() ?? string.Empty;

    public static bool IsValidId(string id) => id.Length > 0 && !id.Any(c => char.IsWhiteSpace(c) || c is '<' or '>' or '&' or '"');

    public static IEnumerable<double> Numbers(string text)
    {
        var decimalComma = !text.Contains('.') && CommaDecimal().IsMatch(text);
        var pattern = decimalComma ? CommaNumber() : DotNumber();
        foreach (Match match in pattern.Matches(text))
        {
            if (double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                yield return value;
            }
        }
    }

    private static string StripArticle(string name)
    {
        var value = name.Trim();
        return value.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? value[4..] : value;
    }

    private static IEnumerable<string> Words(string text) => WordSplit().Split(text).Where(p => p.Length > 0);

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex WordSplit();

    [GeneratedRegex(@"-?\d+,\d+")]
    private static partial Regex CommaDecimal();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])-?\d+(?:,\d+)?")]
    private static partial Regex CommaNumber();

    [GeneratedRegex(@"(?<![A-Za-z0-9_.])-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?")]
    private static partial Regex DotNumber();
}
