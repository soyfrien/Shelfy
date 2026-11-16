using System.Text.Json;
using Shelfy.Features.Recognition.Parsing;
using Shelfy.Domain;

namespace Shelfy.Features.Recognition.Resolution;

/// <summary>Resolves via the OpenLibrary search API (https://openlibrary.org/search.json). No API key needed; identify politely with a User-Agent.</summary>
public class OpenLibraryBookResolver(HttpClient http) : IBookResolver, IBookSearch
{
    public const string SourceName = "OpenLibrary";
    private const string Fields = "key,title,author_name,isbn,cover_i";
    private const int Limit = 8;

    public string Name => SourceName;

    public async Task<BookMatch?> ResolveAsync(BookQuery query, CancellationToken ct = default)
    {
        if (query.Isbn is not null)
        {
            var byIsbn = await SearchAsync($"search.json?q=isbn:{Uri.EscapeDataString(query.Isbn)}&limit=1&fields={Fields}", ct);
            var first = byIsbn.FirstOrDefault();
            if (first is not null)
            {
                return new BookMatch(query.Isbn, first.Title, first.Author, first.CoverUrl, 1.0, SourceName);
            }
        }

        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return null;
        }

        var url = $"search.json?title={Uri.EscapeDataString(query.Title)}&limit={Limit}&fields={Fields}";
        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            url += $"&author={Uri.EscapeDataString(query.Author)}";
        }

        var docs = await SearchAsync(url, ct);
        Doc? best = null;
        var bestScore = -1.0;
        foreach (var doc in docs.Where(d => d.Isbn is not null))
        {
            var score = MatchScorer.Score(query, doc.Title, doc.Authors);
            if (score > bestScore)
            {
                best = doc;
                bestScore = score;
            }
        }

        return best is null ? null : new BookMatch(best.Isbn!, best.Title, best.Author, best.CoverUrl, bestScore, SourceName);
    }

    public async Task<IReadOnlyList<BookMatch>> SearchAsync(BookQuery query, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return Array.Empty<BookMatch>();
        }

        var url = $"search.json?title={Uri.EscapeDataString(query.Title)}&limit={Math.Clamp(limit, 1, 20)}&fields={Fields}";
        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            url += $"&author={Uri.EscapeDataString(query.Author)}";
        }

        return (await SearchAsync(url, ct)).Where(d => d.Isbn is not null)
            .Select(d => new BookMatch(d.Isbn!, d.Title, d.Author, d.CoverUrl, MatchScorer.Score(query, d.Title, d.Authors), SourceName))
            .ToList();
    }

    public async Task<IReadOnlyList<BookMatch>> SearchTextAsync(string text, int limit, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return Array.Empty<BookMatch>();
        }

        var q = Isbn.Normalize(text) is { } isbn ? $"isbn:{isbn}" : text;
        var url = $"search.json?q={Uri.EscapeDataString(q)}&limit={Math.Clamp(limit, 1, 20)}&fields={Fields}";
        return (await SearchAsync(url, ct)).Where(d => d.Isbn is not null)
            .Select(d => new BookMatch(Isbn.Normalize(text) ?? d.Isbn!, d.Title, d.Author, d.CoverUrl, 0, SourceName))
            .ToList();
    }

    private async Task<List<Doc>> SearchAsync(string relativeUrl, CancellationToken ct)
    {
        using var json = await HttpResolverSupport.GetJsonAsync(http, relativeUrl, SourceName, ct);
        var result = new List<Doc>();
        if (json is null)
        {
            return result;
        }

        foreach (var d in json.RootElement.GetArray("docs"))
        {
            var title = d.GetString("title");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var authors = d.GetArray("author_name").Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!).ToList();
            // A work lists ISBNs of every edition and language. Prefer English-language registration groups (978-0, 978-1).
            var isbn = Isbn.PickPreferred(d.GetArray("isbn")
                .Where(i => i.ValueKind == JsonValueKind.String)
                .Select(i => i.GetString()));

            string? cover = null;
            if (d.TryGetProperty("cover_i", out var ci) && ci.ValueKind == JsonValueKind.Number && ci.TryGetInt64(out var coverId))
            {
                cover = $"https://covers.openlibrary.org/b/id/{coverId}-M.jpg";
            }

            result.Add(new Doc(title, authors, isbn, cover));
        }

        return result;
    }

    private sealed record Doc(string Title, List<string> Authors, string? Isbn, string? CoverUrl)
    {
        public string Author => Authors.Count == 0 ? "" : string.Join(", ", Authors.Take(3));
    }
}
