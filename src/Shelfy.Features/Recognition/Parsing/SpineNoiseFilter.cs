using Shelfy.Features.Recognition.Resolution;

namespace Shelfy.Features.Recognition.Parsing;

/// <summary>
/// Decides which unmatched reads are too poor to list as "Spines I couldn't match" (docs/recognition.md, "Noise filter").
/// Pure and deterministic. A filtered read stays in the database and can still be shown on request; it is only not listed by default.
/// The thresholds come from the 230 spines of a real 56-book shelf (173 unmatched), keeping every read that looks like words.
/// When in doubt a read is shown.
/// </summary>
public static class SpineNoiseFilter
{
    public const string TooShort = "short";
    public const string Punctuation = "punctuation";
    public const string Filler = "filler";
    public const string Unsure = "unsure";
    public const string Duplicate = "duplicate";

    /// <summary>Fewer letters than this in the whole read ("TH", "I").</summary>
    public const int MinLetters = 3;

    /// <summary>A lone word of fewer letters than this is a cut-off fragment ("HEN", "OPE", "FAN").</summary>
    public const int MinSingleWordLetters = 4;

    /// <summary>Share of the non-space characters that must be letters.</summary>
    public const double MinLetterShare = 0.5;

    /// <summary>A short read this unsure is OCR noise ("OGET'S TI" at 0.44). Longer reads are kept: they usually hold real words.</summary>
    public const double UnsureBelow = 0.5;
    public const int UnsureShortLetters = 12;

    /// <summary>Whatever the length, a one- or two-word read below this confidence is noise ("putnam" 0.22).</summary>
    public const double VeryUnsureBelow = 0.3;

    /// <summary>A lone word below this confidence is most likely a misread fragment ("TALE" 0.57, "AMERTON" 0.54).</summary>
    public const double SingleWordUnsureBelow = 0.6;

    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "of", "and", "with", "who", "to", "in", "on", "for", "by", "or", "at", "is", "it", "my", "i", "de", "la", "el"
    };

    /// <summary>The reason this read should not be listed, or null when it should be.</summary>
    public static string? Reason(string? rawText, double? confidence)
    {
        var text = (rawText ?? "").Trim();
        var letters = text.Count(char.IsLetter);
        if (letters < MinLetters)
        {
            return letters == 0 && text.Length >= 3 ? Punctuation : TooShort;
        }

        var visible = text.Count(c => !char.IsWhiteSpace(c) && c != '|');
        if (visible > 0 && (double)letters / visible < MinLetterShare)
        {
            return Punctuation;
        }

        var tokens = SpineDeduper.Tokens(text);
        var longest = tokens.Count == 0 ? 0 : tokens.Max(t => t.Count(char.IsLetter));
        if (longest < MinLetters)
        {
            return TooShort;                       // "a b c", "i i"
        }

        if (tokens.All(t => Fillers.Contains(t)))
        {
            return Filler;                         // "THE", "WITH", "WHO"
        }

        if (tokens.Count == 1 && longest < MinSingleWordLetters)
        {
            return TooShort;
        }

        if (confidence is { } c)
        {
            if (c < VeryUnsureBelow && tokens.Count <= 2)
            {
                return Unsure;
            }

            if (c < SingleWordUnsureBelow && tokens.Count == 1)
            {
                return Unsure;
            }

            if (c < UnsureBelow && letters < UnsureShortLetters)
            {
                return Unsure;
            }
        }

        return null;
    }

    public static bool IsNoise(string? rawText, double? confidence) => Reason(rawText, confidence) is not null;
}
