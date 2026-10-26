using System.Text.Json;
using Shelfy.Features.Recognition.Parsing;

namespace Shelfy.Features.Recognition.Resolution;

/// <summary>Resolves via the Google Books volumes API. The API key is optional (lower quota without it).</summary>
public class GoogleBooksResolver(HttpClient http, string? apiKey = null) : IBookResolver, IBookSearch
{
    public const string SourceName = "GoogleBooks";
    private const int MaxResults = 8;

    public string Name => SourceName;

    public async Task<BookMatch?> ResolveAsync(BookQuery query, CancellationToken ct = default)
    {
        if (query.Isbn is not null)
        {
            var byIsbn = await SearchAsync($"isbn:{query.Isbn}", 1, ct);
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

        var q = $"intitle:{Sanitize(query.Title)}";
        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            q += $" inauthor:{Sanitize(query.Author)}";
        }

        var volumes = await SearchAsync(q, MaxResults, ct);
        Volume? best = null;
        var bestScore = -1.0;
        foreach (var v in volumes.Where(v => v.Isbn is not null))
        {
            var score = MatchScorer.Score(query, v.Title, v.Authors);
            if (score > bestScore)
            {
                best = v;
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

        var q = $"intitle:{Sanitize(query.Title)}";
        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            q += $" inauthor:{Sanitize(query.Author)}";
        }

        return (await SearchAsync(q, Math.Clamp(limit, 1, 20), ct)).Where(v => v.Isbn is not null)
            .Select(v => new BookMatch(v.Isbn!, v.Title, v.Author, v.CoverUrl, MatchScorer.Score(query, v.Title, v.Authors), SourceName))
            .ToList();
    }

    public async Task<IReadOnlyList<BookMatch>> SearchTextAsync(string text, int limit, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return Array.Empty<BookMatch>();
        }

        var isbn = Isbn.Normalize(text);
        var q = isbn is not null ? $"isbn:{isbn}" : text.Replace("\"", " ");
        return (await SearchAsync(q, Math.Clamp(limit, 1, 20), ct)).Where(v => v.Isbn is not null)
            .Select(v => new BookMatch(isbn ?? v.Isbn!, v.Title, v.Author, v.CoverUrl, 0, SourceName))
            .ToList();
    }

    private static string Sanitize(string s) => s.Replace("\"", " ").Replace(":", " ");

    private async Task<List<Volume>> SearchAsync(string q, int max, CancellationToken ct)
    {
        var url = $"books/v1/volumes?q={Uri.EscapeDataString(q)}&maxResults={max}&printType=books";
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            url += $"&key={Uri.EscapeDataString(apiKey)}";
        }

        using var json = await HttpResolverSupport.GetJsonAsync(http, url, SourceName, ct);
        var result = new List<Volume>();
        if (json is null)
        {
            return result;
        }

        foreach (var item in json.RootElement.GetArray("items"))
        {
            if (!item.TryGetProperty("volumeInfo", out var info))
            {
                continue;
            }

            var title = info.GetString("title");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var authors = info.GetArray("authors").Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!).ToList();
            string? isbn13 = null, isbn10 = null;
            foreach (var id in info.GetArray("industryIdentifiers"))
            {
                var type = id.GetString("type");
                var normalized = Isbn.Normalize(id.GetString("identifier"));
                if (normalized is null)
                {
                    continue;
                }

                if (type == "ISBN_13")
                {
                    isbn13 ??= normalized;
                }
                else if (type == "ISBN_10")
                {
                    isbn10 ??= normalized;
                }
            }

            string? cover = null;
            if (info.TryGetProperty("imageLinks", out var links))
            {
                cover = links.GetString("thumbnail") ?? links.GetString("smallThumbnail");
                cover = cover?.Replace("http://", "https://").Replace("&edge=curl", "");
            }

            result.Add(new Volume(title, authors, isbn13 ?? isbn10, cover));
        }

        return result;
    }

    private sealed record Volume(string Title, List<string> Authors, string? Isbn, string? CoverUrl)
    {
        public string Author => Authors.Count == 0 ? "" : string.Join(", ", Authors.Take(3));
    }
}
