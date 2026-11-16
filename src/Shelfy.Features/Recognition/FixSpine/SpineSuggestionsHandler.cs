using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shelfy.Features.Common;
using Shelfy.Features.Recognition.Parsing;
using Shelfy.Features.Recognition.Resolution;
using Shelfy.Infrastructure;
using Shelfy.Domain;

namespace Shelfy.Features.Recognition.FixSpine;

/// <summary>
/// Books to offer for an unmatched spine (tap to fix, ADR-011), or, with <c>q</c>, the result of the sheet's search box.
/// Suggestions: the spine text is tried several ways, cheapest and most likely first, until five good candidates are found:
/// the whole text, the parsed title and author, the longest line alone, title and author swapped, then single lines.
/// Every candidate is scored against the text that found it; the best few are returned. Nothing is stored.
/// </summary>
public class SpineSuggestionsHandler(ShelfyDbContext db, IBookSearch search, ILogger<SpineSuggestionsHandler>? logger = null)
{
    public const int DefaultTake = 5;
    public const int SearchTake = 8;
    public const int MaxLookups = 6;

    /// <summary>A candidate must resemble the text at least this much to be offered. Low on purpose: the person decides.</summary>
    public const double MinSuggestionScore = 0.3;

    /// <summary>A candidate this good counts as "found" and ends the search early.</summary>
    public const double GoodScore = 0.72;

    private const int MaxQueryWords = 8;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(25);

    public async Task<HandlerResult<SpineSuggestionsResponse>> HandleAsync(Guid shelfId, Guid spineId, string? q, int take = DefaultTake, CancellationToken ct = default)
    {
        var spine = await SpineFixSupport.FindSpineAsync(db, shelfId, spineId, track: false, ct);
        if (spine is null)
        {
            return HandlerResult<SpineSuggestionsResponse>.NotFound("Spine not found.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Budget);
        var text = (spine.RawText ?? "").Trim();
        try
        {
            if (!string.IsNullOrWhiteSpace(q))
            {
                return await SearchBoxAsync(spineId, text, q.Trim(), Math.Clamp(take, 1, 20), timeout.Token);
            }

            return await SuggestAsync(spineId, spine.RawText ?? "", spine.CandidateTitle, spine.CandidateAuthor, Math.Clamp(take, 1, 10), timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return HandlerResult<SpineSuggestionsResponse>.Ok(new SpineSuggestionsResponse(spineId, text, Array.Empty<BookSuggestionDto>(), true));
        }
    }

    private async Task<HandlerResult<SpineSuggestionsResponse>> SearchBoxAsync(Guid spineId, string text, string q, int take, CancellationToken ct)
    {
        var results = new List<BookSuggestionDto>();
        var seen = new HashSet<string>();

        // Books Shelfy already knows first (they are quick and were confirmed before), then the catalogs.
        foreach (var b in await LocalBooksAsync(q, take, ct))
        {
            if (b.Isbn is not null && seen.Add(b.Isbn))
            {
                results.Add(new BookSuggestionDto(b.Isbn, b.Title, b.Author, b.CoverUrl, 0, "Shelfy", "search", b.Id));
            }
        }

        var unavailable = false;
        try
        {
            foreach (var m in await search.SearchTextAsync(Limit(q), take, ct))
            {
                if (seen.Add(m.Isbn))
                {
                    results.Add(new BookSuggestionDto(m.Isbn, m.Title, m.Author, m.CoverUrl, 0, m.Source, "search"));
                }
            }
        }
        catch (RecognitionException ex) when (ex.IsRetryable)
        {
            logger?.LogWarning("Catalog search for the fix sheet failed: {Message}", ex.Message);
            unavailable = results.Count == 0;
        }

        return HandlerResult<SpineSuggestionsResponse>.Ok(new SpineSuggestionsResponse(spineId, text, results.Take(take).ToList(), unavailable));
    }

    private async Task<List<(string Isbn, string Title, string Author, string? CoverUrl, Guid Id)>> LocalBooksAsync(string q, int take, CancellationToken ct)
    {
        var query = db.Books.AsNoTracking().Where(b => b.Isbn != null);
        if (Isbn.Normalize(q) is { } isbn)
        {
            query = query.Where(b => b.Isbn == isbn);
        }
        else
        {
            var words = SpineDeduper.Tokens(q).Where(w => w.Length >= 2).Take(5).ToList();
            if (words.Count == 0)
            {
                return new();
            }

            foreach (var w in words)
            {
                var word = w;
                query = query.Where(b => (b.Title + " " + b.Author).ToLower().Contains(word));
            }
        }

        var rows = await query.OrderBy(b => b.Title).Take(take).Select(b => new { b.Isbn, b.Title, b.Author, b.CoverUrl, b.Id }).ToListAsync(ct);
        return rows.Select(b => (b.Isbn!, b.Title, b.Author, b.CoverUrl, b.Id)).ToList();
    }

    private async Task<HandlerResult<SpineSuggestionsResponse>> SuggestAsync(Guid spineId, string raw, string? candidateTitle, string? candidateAuthor, int take, CancellationToken ct)
    {
        var strategies = Strategies(raw, candidateTitle, candidateAuthor);
        var best = new Dictionary<string, BookSuggestionDto>();
        var lookups = 0;
        var failures = 0;
        foreach (var s in strategies)
        {
            if (lookups >= MaxLookups || best.Values.Count(b => b.Score >= GoodScore) >= take)
            {
                break;
            }

            lookups++;
            IReadOnlyList<BookMatch> found;
            try
            {
                found = s.Structured is { } query
                    ? await search.SearchAsync(query, 8, ct)
                    : await search.SearchTextAsync(s.Probe, 8, ct);
            }
            catch (RecognitionException ex) when (ex.IsRetryable)
            {
                logger?.LogWarning("Suggestions strategy {Strategy} failed: {Message}", s.Name, ex.Message);
                failures++;
                continue;
            }

            foreach (var m in found)
            {
                // A book found through one line ("William Gibson") is also compared with the whole text ("Nuromanzir William Gibson"),
                // so the one that looks like the whole spine ranks first, not just any book by that author.
                var score = strategies.Max(x => SuggestionScorer.Score(x.Probe, m));
                if (!best.TryGetValue(m.Isbn, out var old) || score > old.Score)
                {
                    best[m.Isbn] = new BookSuggestionDto(m.Isbn, m.Title, m.Author, m.CoverUrl, Math.Round(score, 3), m.Source, old?.Strategy ?? s.Name);
                }
            }
        }

        var list = best.Values.Where(b => b.Score >= MinSuggestionScore).OrderByDescending(b => b.Score).ThenBy(b => b.Title).Take(take).ToList();
        var unavailable = list.Count == 0 && lookups > 0 && failures == lookups;
        return HandlerResult<SpineSuggestionsResponse>.Ok(new SpineSuggestionsResponse(spineId, raw.Trim(), list, unavailable));
    }

    public record Strategy(string Name, string Probe, BookQuery? Structured = null);

    /// <summary>The ways to look a spine up, in the order they are tried. Duplicate probes are dropped.</summary>
    public static IReadOnlyList<Strategy> Strategies(string raw, string? candidateTitle, string? candidateAuthor)
    {
        var lines = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Count(char.IsLetter) >= 3).ToList();
        var full = Limit(string.Join(' ', lines));
        var result = new List<Strategy>();
        var seen = new HashSet<string>();

        void Add(Strategy s)
        {
            if (s.Probe.Count(char.IsLetter) >= 3 && seen.Add(SpineText.Key(s.Probe) + (s.Structured is null ? "" : "|s")))
            {
                result.Add(s);
            }
        }

        Add(new Strategy("full", full));

        var title = candidateTitle?.Trim();
        var author = candidateAuthor?.Trim();
        if (!string.IsNullOrEmpty(title))
        {
            // The parsed guess keeps the title and author lines apart, which the catalogs can use.
            if (!string.IsNullOrEmpty(author))
            {
                Add(new Strategy("parsed", $"{title} {author}", new BookQuery(title, author)));
            }
            else if (SpineText.Key(title) != SpineText.Key(full))
            {
                Add(new Strategy("parsed", title, new BookQuery(title)));
            }
        }

        if (lines.Count > 1)
        {
            Add(new Strategy("longest", Limit(lines.OrderByDescending(l => l.Count(char.IsLetter)).First())));
        }

        if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(author))
        {
            // The title and the author were taken the wrong way round.
            Add(new Strategy("swapped", $"{author} {title}", new BookQuery(author, title)));
        }

        foreach (var line in lines.Where(l => l.Count(char.IsLetter) >= 5).OrderByDescending(l => l.Count(char.IsLetter)).Take(3))
        {
            Add(new Strategy("line", Limit(line)));
        }

        return result;
    }

    private static string Limit(string text) => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(MaxQueryWords));
}

/// <summary>Scores how well a catalog book fits the text that found it. Pure.</summary>
public static class SuggestionScorer
{
    public static double Score(string probe, BookMatch candidate)
    {
        if (string.IsNullOrWhiteSpace(probe))
        {
            return 0;
        }

        return new[]
        {
            MatchScorer.TitleSimilarity(probe, candidate.Title),
            MatchScorer.TitleSimilarity(probe, $"{candidate.Title} {candidate.Author}"),
            MatchScorer.TitleSimilarity(probe, $"{candidate.Author} {candidate.Title}")
        }.Max();
    }
}
