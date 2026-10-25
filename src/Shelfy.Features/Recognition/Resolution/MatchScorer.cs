using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shelfy.Features.Recognition.Resolution;

/// <summary>Pure fuzzy scoring of a lookup candidate against what was read from a spine. Scores are 0..1.</summary>
public static partial class MatchScorer
{
    // With an author on the spine the score is title * (AuthorFloor + (1 - AuthorFloor) * author): an exact title by a
    // clearly different author lands at AuthorFloor, below any sensible acceptance threshold.
    private const double AuthorFloor = 0.55;
    private const double NoAuthorFactor = 0.9;
    private const double UnknownAuthorScore = 0.5;
    private const double SurnameMatchScore = 0.85;

    private static readonly HashSet<string> LeadingArticles = new() { "the", "a", "an" };

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlnum();

    public static double Score(BookQuery query, string candidateTitle, IReadOnlyCollection<string> candidateAuthors)
    {
        var titleScore = TitleSimilarity(query.Title, candidateTitle);
        if (string.IsNullOrWhiteSpace(query.Author))
        {
            return Math.Round(titleScore * NoAuthorFactor, 4);
        }

        var authorScore = candidateAuthors.Count == 0
            ? UnknownAuthorScore
            : candidateAuthors.Max(a => AuthorSimilarity(query.Author!, a));

        return Math.Round(titleScore * (AuthorFloor + (1 - AuthorFloor) * authorScore), 4);
    }

    public static double TitleSimilarity(string queryTitle, string candidateTitle)
    {
        var q = NormalizeTitle(queryTitle);
        var c = NormalizeTitle(candidateTitle);
        if (q.Length == 0 || c.Length == 0)
        {
            return 0;
        }

        var best = Similarity(q, c);

        // "Dune: Deluxe Edition" should match a spine that only says "Dune".
        var colon = candidateTitle.IndexOfAny(new[] { ':', '(' });
        if (colon > 0)
        {
            var main = NormalizeTitle(candidateTitle[..colon]);
            if (main.Length > 0)
            {
                best = Math.Max(best, Similarity(q, main));
            }
        }

        return best;
    }

    public static double AuthorSimilarity(string queryAuthor, string candidateAuthor)
    {
        var q = NormalizeName(queryAuthor);
        var c = NormalizeName(candidateAuthor);
        if (q.Length == 0 || c.Length == 0)
        {
            return 0;
        }

        var best = Similarity(q, c);
        var qs = q.Split(' ')[^1];
        var cs = c.Split(' ')[^1];
        if (qs.Length > 2 && (qs == cs || (qs.Length >= 5 && Levenshtein(qs, cs) <= 1)))
        {
            best = Math.Max(best, SurnameMatchScore);
        }

        return best;
    }

    /// <summary>Lowercase, accents and punctuation removed, leading article dropped.</summary>
    public static string NormalizeTitle(string text)
    {
        var tokens = Tokenize(text);
        if (tokens.Count > 1 && LeadingArticles.Contains(tokens[0]))
        {
            tokens.RemoveAt(0);
        }

        return string.Join(' ', tokens);
    }

    /// <summary>Like <see cref="NormalizeTitle"/> but merges runs of initials ("J. R. R." and "J.R.R." both become "jrr") and handles "Surname, Given".</summary>
    public static string NormalizeName(string text)
    {
        var comma = text.IndexOf(',');
        if (comma > 0 && text.IndexOf(',', comma + 1) < 0)
        {
            text = text[(comma + 1)..].Trim() + " " + text[..comma].Trim();
        }

        // "J.R.R." has no spaces; spread the periods first so each initial is its own token.
        var tokens = Tokenize(text.Replace(".", ". "));
        var merged = new List<string>();
        var inInitialRun = false;
        foreach (var t in tokens)
        {
            if (t.Length == 1)
            {
                if (inInitialRun)
                {
                    merged[^1] += t;
                }
                else
                {
                    merged.Add(t);
                    inInitialRun = true;
                }
            }
            else
            {
                merged.Add(t);
                inInitialRun = false;
            }
        }

        return string.Join(' ', merged);
    }

    private static List<string> Tokenize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        var cleaned = NonAlnum().Replace(sb.ToString().ToLowerInvariant().Replace("&", " and "), " ").Trim();
        return cleaned.Length == 0 ? new List<string>() : cleaned.Split(' ').ToList();
    }

    private static double Similarity(string a, string b)
    {
        if (a == b)
        {
            return 1;
        }

        var lev = 1.0 - (double)Levenshtein(a, b) / Math.Max(a.Length, b.Length);
        return Math.Max(lev, TokenF1(a, b));
    }

    /// <summary>F1 of fuzzy token overlap: tokens match when equal or (for long tokens) one edit apart.</summary>
    private static double TokenF1(string a, string b)
    {
        var at = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bt = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (at.Length == 0 || bt.Length == 0)
        {
            return 0;
        }

        var used = new bool[bt.Length];
        var matched = 0;
        foreach (var x in at)
        {
            for (var j = 0; j < bt.Length; j++)
            {
                if (!used[j] && TokensMatch(x, bt[j]))
                {
                    used[j] = true;
                    matched++;
                    break;
                }
            }
        }

        if (matched == 0)
        {
            return 0;
        }

        var precision = (double)matched / bt.Length;
        var recall = (double)matched / at.Length;
        return 2 * precision * recall / (precision + recall);
    }

    private static bool TokensMatch(string x, string y) =>
        x == y || (Math.Min(x.Length, y.Length) >= 5 && Levenshtein(x, y) <= 1);

    public static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}
