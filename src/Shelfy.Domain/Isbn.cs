using System.Text.RegularExpressions;

namespace Shelfy.Domain;

/// <summary>ISBN helpers. Everything is normalized to ISBN-13 digits so a Book is unique per ISBN (BookConfiguration).</summary>
public static partial class Isbn
{
    [GeneratedRegex(@"(?<![\d-])(?:97[89][\d\- ]{10,16}\d|\d[\d\- ]{8,12}[\dXx])(?![\d-])")]
    private static partial Regex Candidate();

    /// <summary>Returns the ISBN-13 for a valid ISBN-10 or ISBN-13 (hyphens and spaces allowed); null otherwise.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = Digits(value);
        return digits.Length switch
        {
            13 when digits.All(char.IsAsciiDigit) && IsValid13(digits) => digits,
            10 when IsValid10(digits) => Convert10To13(digits),
            _ => null
        };
    }

    /// <summary>
    /// Picks one ISBN-13 from a work's edition ISBNs: the first valid one in an English-language registration group
    /// (978-0 or 978-1), otherwise the first valid one. Null when none is valid.
    /// </summary>
    public static string? PickPreferred(IEnumerable<string?> isbns)
    {
        string? fallback = null;
        foreach (var raw in isbns)
        {
            var isbn = Normalize(raw);
            if (isbn is null)
            {
                continue;
            }

            if (isbn.StartsWith("9780", StringComparison.Ordinal) || isbn.StartsWith("9781", StringComparison.Ordinal))
            {
                return isbn;
            }

            fallback ??= isbn;
        }

        return fallback;
    }

    /// <summary>Digits and a trailing X from an ISBN-like string, hyphens and spaces dropped. Empty when none.</summary>
    public static string Digits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return new string(value.Where(c => char.IsAsciiDigit(c) || c is 'X' or 'x').ToArray()).ToUpperInvariant();
    }

    /// <summary>
    /// The ISBN-10 for a 978-prefixed ISBN-13 (or a valid ISBN-10). Null for 979-prefixed ISBN-13s and invalid input.
    /// </summary>
    public static string? ToIsbn10(string? value)
    {
        var isbn13 = Normalize(value);
        if (isbn13 is null || !isbn13.StartsWith("978", StringComparison.Ordinal))
        {
            return null;
        }

        var body = isbn13[3..12];
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (body[i] - '0') * (10 - i);
        }

        var check = (11 - sum % 11) % 11;
        return body + (check == 10 ? "X" : check.ToString());
    }

    /// <summary>Finds the first valid ISBN in free text (for example "ISBN 978-0-547-92822-7") and returns it as ISBN-13.</summary>
    public static string? FindIn(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        foreach (Match m in Candidate().Matches(text))
        {
            var isbn = Normalize(m.Value);
            if (isbn is not null)
            {
                return isbn;
            }
        }

        return null;
    }

    private static bool IsValid13(string d)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (d[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return (10 - sum % 10) % 10 == d[12] - '0';
    }

    private static bool IsValid10(string d)
    {
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            int v;
            if (d[i] == 'X')
            {
                if (i != 9)
                {
                    return false;
                }

                v = 10;
            }
            else if (char.IsAsciiDigit(d[i]))
            {
                v = d[i] - '0';
            }
            else
            {
                return false;
            }

            sum += v * (10 - i);
        }

        return sum % 11 == 0;
    }

    private static string Convert10To13(string d10)
    {
        var body = "978" + d10[..9];
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (body[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return body + ((10 - sum % 10) % 10);
    }
}
