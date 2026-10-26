using Microsoft.Extensions.Logging;

namespace Shelfy.Features.Recognition.Resolution;

/// <summary>
/// Asks the providers in order (OpenLibrary, then Google Books) until enough books were found, and merges the answers by ISBN.
/// A provider that is down is skipped; when every provider failed it throws a retryable exception so the caller can say
/// "the book catalogs aren't answering".
/// </summary>
public class CompositeBookSearch(IEnumerable<IBookSearch> providers, ILogger<CompositeBookSearch>? logger = null) : IBookSearch
{
    private readonly IReadOnlyList<IBookSearch> _providers = providers.ToList();

    public string Name => "Composite";

    public Task<IReadOnlyList<BookMatch>> SearchAsync(BookQuery query, int limit, CancellationToken ct = default) =>
        RunAsync(p => p.SearchAsync(query, limit, ct), limit);

    public Task<IReadOnlyList<BookMatch>> SearchTextAsync(string text, int limit, CancellationToken ct = default) =>
        RunAsync(p => p.SearchTextAsync(text, limit, ct), limit);

    private async Task<IReadOnlyList<BookMatch>> RunAsync(Func<IBookSearch, Task<IReadOnlyList<BookMatch>>> call, int limit)
    {
        var merged = new Dictionary<string, BookMatch>();
        var failures = new List<RecognitionException>();
        foreach (var provider in _providers)
        {
            try
            {
                foreach (var m in await call(provider))
                {
                    if (!merged.TryGetValue(m.Isbn, out var existing) || m.Score > existing.Score)
                    {
                        merged[m.Isbn] = m;
                    }
                }
            }
            catch (RecognitionException ex) when (ex.IsRetryable)
            {
                logger?.LogWarning("Book search via {Provider} failed: {Message}", provider.Name, ex.Message);
                failures.Add(ex);
                continue;
            }

            if (merged.Count >= limit)
            {
                break;
            }
        }

        if (_providers.Count > 0 && failures.Count == _providers.Count)
        {
            throw new RecognitionException("All book catalogs failed: " + string.Join(" ", failures.Select(f => f.Message)), isRetryable: true);
        }

        return merged.Values.ToList();
    }
}
