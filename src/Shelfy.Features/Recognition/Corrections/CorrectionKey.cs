using Shelfy.Features.Recognition.Parsing;

namespace Shelfy.Features.Recognition.Corrections;

/// <summary>
/// The key under which a user fix is remembered (ADR-011): the spine text with case, accents and punctuation removed and
/// words separated by one space, so "THE HOBBIT | Tolkien" and "the hobbit tolkien" are the same key.
/// </summary>
public static class CorrectionKey
{
    public const int MaxLength = 500;

    /// <summary>A text this short is too ambiguous to remember: it would match too many unrelated spines.</summary>
    public const int MinLetters = 4;

    public static string? From(string? text)
    {
        var tokens = SpineDeduper.Tokens(text);
        var key = string.Join(' ', tokens);
        if (key.Count(char.IsLetter) < MinLetters)
        {
            return null;
        }

        return key.Length <= MaxLength ? key : key[..MaxLength].TrimEnd();
    }

    /// <summary>
    /// The keys to look up for one spine: its whole raw text and, when different, its parsed title (a spine whose title line
    /// is read the same way next time but with different surrounding noise). The first key is the exact one.
    /// </summary>
    public static IReadOnlyList<string> For(string? rawText, string? candidateTitle)
    {
        var keys = new List<string>(2);
        if (From(rawText) is { } raw)
        {
            keys.Add(raw);
        }

        // A title alone must be a real title to be trusted (two words, or one long word).
        var title = From(candidateTitle);
        if (title is not null && !keys.Contains(title) && (title.Contains(' ') || title.Length >= 8))
        {
            keys.Add(title);
        }

        return keys;
    }
}
