using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Shelfy.Features.Recognition.Resolution;

namespace Shelfy.Features.Recognition.Parsing;

/// <summary>
/// Pure helpers that decide whether two reads are the same physical spine. Overlapping tiles of one wide shelf photo see
/// the spines in the overlap zone twice, and a spine cut by a tile edge may be read as a fragment (only its author line,
/// "FOUNDA", ...).
///
/// Texts are normalized (case, accents, punctuation) and split into tokens. Two tokens match when they are equal, one
/// edit apart (long tokens), or one is a prefix of the other (a word cut by the tile edge, at least 4 letters).
/// Two texts are the same spine when
///   * their Dice similarity over matched tokens is at least <see cref="DiceThreshold"/> (two complete reads with some
///     OCR noise), or
///   * the shorter text is a FRAGMENT of the longer one: all its tokens match a run of tokens that starts or ends the
///     longer text (a cut loses a whole line or the end of the text), and that is at least two tokens, or a single token
///     that matched as a cut word. So "Dune" and "Dune Messiah", or "Foundation" and "Foundation and Empire" (neighbouring
///     books), are NOT merged.
/// This is only used to drop UNRESOLVED duplicates and fragments; resolved books are de-duplicated by ISBN, never by text.
/// </summary>
public static partial class SpineDeduper
{
    public const double DiceThreshold = 0.8;
    public const int MinPrefixLetters = 4;

    /// <summary>Unresolved reads from tiles with a mean confidence below this are treated as junk.</summary>
    public const double MinFragmentConfidence = 0.35;

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlnum();

    public static IReadOnlyList<string> Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        // RawText joins the OCR lines of a spine with " | ".
        var cleaned = NonAlnum().Replace(sb.ToString().ToLowerInvariant().Replace("&", " and "), " ").Trim();
        return cleaned.Length == 0 ? Array.Empty<string>() : cleaned.Split(' ');
    }

    /// <summary>True when the two texts probably show the same spine.</summary>
    public static bool IsSameSpine(string? a, string? b) => IsSameSpine(Tokens(a), Tokens(b));

    public static bool IsSameSpine(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return false;
        }

        var (small, large) = a.Count <= b.Count ? (a, b) : (b, a);
        var used = new bool[large.Count];
        var matchedAt = new List<int>();
        var partial = 0;
        foreach (var t in small)
        {
            for (var j = 0; j < large.Count; j++)
            {
                if (used[j])
                {
                    continue;
                }

                var kind = Match(t, large[j]);
                if (kind == TokenMatch.None)
                {
                    continue;
                }

                used[j] = true;
                matchedAt.Add(j);
                if (kind == TokenMatch.Prefix)
                {
                    partial++;
                }

                break;
            }
        }

        var matched = matchedAt.Count;
        if (matched == 0)
        {
            return false;
        }

        // Two complete reads with some noise. A lone matching word is only enough when both reads are just that word.
        var dice = 2.0 * matched / (a.Count + b.Count);
        if (dice >= DiceThreshold && (matched >= 2 || a.Count == b.Count || partial > 0))
        {
            return true;
        }

        // A fragment: every token of the shorter read sits in one run that starts or ends the longer read.
        if (matched == small.Count)
        {
            var first = matchedAt.Min();
            var last = matchedAt.Max();
            var contiguous = last - first + 1 == matched && (first == 0 || last == large.Count - 1);
            return contiguous && (matched >= 2 || partial == 1);
        }

        return false;
    }

    /// <summary>
    /// How good a read is: more letters and higher OCR confidence win. A fragment cut by a tile edge has fewer letters
    /// than the whole spine read in a neighbouring tile.
    /// </summary>
    public static double Quality(string? text, double? confidence) =>
        Tokens(text).Sum(t => t.Count(char.IsLetter)) * (confidence ?? 0.7);

    /// <summary>
    /// A read that is too short or too unsure to list as an unmatched spine when it came from a tile: edge fragments and
    /// OCR noise. Only applied to unresolved reads of tiled photos.
    /// </summary>
    public static bool IsJunkFragment(string? text, double? confidence)
    {
        var tokens = Tokens(text);
        var letters = tokens.Sum(t => t.Count(char.IsLetter));
        if (letters < 4 || !tokens.Any(t => t.Count(char.IsLetter) >= 3))
        {
            return true;
        }

        return confidence is < MinFragmentConfidence;
    }

    private enum TokenMatch { None, Exact, Fuzzy, Prefix }

    private static TokenMatch Match(string x, string y)
    {
        if (x == y)
        {
            return TokenMatch.Exact;
        }

        var min = Math.Min(x.Length, y.Length);
        if (min >= 5 && MatchScorer.Levenshtein(x, y) <= 1)
        {
            return TokenMatch.Fuzzy;
        }

        // A word cut by the tile edge: "foundat" / "foundation" (letters only; digits are not cut words).
        var (s, l) = x.Length <= y.Length ? (x, y) : (y, x);
        if (s.Length >= MinPrefixLetters && s.All(char.IsLetter) && l.StartsWith(s, StringComparison.Ordinal))
        {
            return TokenMatch.Prefix;
        }

        return TokenMatch.None;
    }
}
