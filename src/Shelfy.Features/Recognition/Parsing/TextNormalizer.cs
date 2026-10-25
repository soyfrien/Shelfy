using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shelfy.Features.Recognition.Parsing;

/// <summary>Pure text clean-up helpers for OCR output.</summary>
public static partial class TextNormalizer
{
    private static readonly HashSet<string> SmallWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "as", "at", "but", "by", "for", "in", "nor", "of", "on", "or", "the", "to", "up", "vs", "from"
    };

    private static readonly HashSet<string> NamePartlessParticles = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "del", "della", "di", "da", "van", "von", "der", "den", "la", "le", "bin", "ibn", "al", "el", "du", "dos", "ten", "ter", "st."
    };

    private static readonly HashSet<string> NameSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "jr", "jr.", "sr", "sr.", "ii", "iii", "iv", "phd", "ph.d."
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Removes control characters, collapses whitespace, fixes common OCR punctuation and trims decoration.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c) && !char.IsWhiteSpace(c))
            {
                continue;
            }

            sb.Append(c switch
            {
                '\u2018' or '\u2019' or '\u02BC' => '\'',
                '\u201C' or '\u201D' => '"',
                '\u00A0' => ' ',
                _ => c
            });
        }

        var cleaned = Whitespace().Replace(sb.ToString(), " ").Trim();
        return TrimDecoration(cleaned);
    }

    /// <summary>Trims leading and trailing decoration such as bars, dashes, colons and quotes, keeping ?, ! and initials' periods.</summary>
    public static string TrimDecoration(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && !char.IsLetterOrDigit(text[start]) && text[start] != '#')
        {
            start++;
        }

        while (end > start && IsTrailingDecoration(text[end - 1]))
        {
            end--;
        }

        return text[start..end].Trim();
    }

    private static bool IsTrailingDecoration(char c) =>
        c is ' ' or '-' or '\u2013' or '\u2014' or '_' or '|' or ':' or ';' or ',' or '/' or '\\' or '"' or '\'' or '*' or '~' or '=';

    public static int LetterCount(string text) => text.Count(char.IsLetter);

    public static bool IsAllUpper(string text)
    {
        var letters = text.Where(char.IsLetter).ToList();
        return letters.Count > 1 && letters.All(char.IsUpper);
    }

    /// <summary>Title-cases ALL-CAPS text (spines are usually printed in capitals). Mixed-case text is returned unchanged.</summary>
    public static string SmartCase(string text)
    {
        if (!IsAllUpper(text))
        {
            return text;
        }

        var words = text.Split(' ');
        var result = new StringBuilder();
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (i > 0)
            {
                result.Append(' ');
            }

            if (w.Length == 0)
            {
                continue;
            }

            // Initials such as J.R.R. or J. stay upper case.
            if (InitialsPattern().IsMatch(w))
            {
                result.Append(w);
                continue;
            }

            var lower = w.ToLowerInvariant();
            if (i > 0 && (SmallWords.Contains(lower) || NamePartlessParticles.Contains(lower)))
            {
                result.Append(lower);
                continue;
            }

            result.Append(CapitalizeWord(lower));
        }

        return result.ToString();
    }

    private static string CapitalizeWord(string lower)
    {
        // Capitalize the first letter and any letter after a hyphen or apostrophe-prefix such as O'Brien / Jean-Paul.
        var chars = lower.ToCharArray();
        var cap = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (cap && char.IsLetter(chars[i]))
            {
                chars[i] = char.ToUpper(chars[i], CultureInfo.InvariantCulture);
                cap = false;
            }

            if (chars[i] == '-' || (chars[i] == '\'' && i == 1))
            {
                cap = true;
            }
        }

        return new string(chars);
    }

    [GeneratedRegex(@"^(?:\p{L}\.)+$|^\p{L}\.?$")]
    private static partial Regex InitialsPattern();

    /// <summary>True when the text reads like a person's name: 2-4 capitalized tokens, initials and particles allowed, no digits or title words.</summary>
    public static bool LooksLikeAuthorName(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("by ", StringComparison.OrdinalIgnoreCase))
        {
            t = t[3..].Trim();
        }

        if (t.Length == 0 || t.Any(char.IsDigit))
        {
            return false;
        }

        var tokens = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 2 or > 5)
        {
            return false;
        }

        var nameTokens = 0;
        foreach (var token in tokens)
        {
            var bare = token.TrimEnd(',');
            if (InitialsPattern().IsMatch(bare))
            {
                nameTokens++;
                continue;
            }

            if (NamePartlessParticles.Contains(bare) || NameSuffixes.Contains(bare))
            {
                continue;
            }

            if (SmallWords.Contains(bare) || !char.IsUpper(bare[0]) || !bare.All(c => char.IsLetter(c) || c is '\'' or '-' or '.'))
            {
                return false;
            }

            nameTokens++;
        }

        return nameTokens >= 2;
    }
}
