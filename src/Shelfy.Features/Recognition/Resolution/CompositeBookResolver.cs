using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shelfy.Features.Recognition.Resolution;

/// <summary>
/// Tries each resolver in order (OpenLibrary first, then Google Books) and returns the first match at or above the
/// acceptance threshold; otherwise the best match above the threshold, otherwise null.
/// If every resolver failed with provider trouble, throws a retryable exception so the job is retried.
/// </summary>
public class CompositeBookResolver(IEnumerable<IBookResolver> resolvers, IOptions<RecognitionOptions> options,
    ILogger<CompositeBookResolver>? logger = null) : IBookResolver
{
    private readonly IReadOnlyList<IBookResolver> _resolvers = resolvers.ToList();

    public string Name => "Composite";

    public async Task<BookMatch?> ResolveAsync(BookQuery query, CancellationToken ct = default)
    {
        var threshold = options.Value.MinMatchScore;
        BookMatch? best = null;
        var failures = new List<RecognitionException>();

        foreach (var resolver in _resolvers)
        {
            BookMatch? match;
            try
            {
                match = await resolver.ResolveAsync(query, ct);
            }
            catch (RecognitionException ex) when (ex.IsRetryable)
            {
                logger?.LogWarning("Resolver {Resolver} failed: {Message}", resolver.Name, ex.Message);
                failures.Add(ex);
                continue;
            }

            if (match is null)
            {
                continue;
            }

            if (match.Score >= threshold)
            {
                return match;
            }

            if (best is null || match.Score > best.Score)
            {
                best = match;
            }
        }

        if (_resolvers.Count > 0 && failures.Count == _resolvers.Count)
        {
            throw new RecognitionException("All book resolvers failed: " + string.Join(" ", failures.Select(f => f.Message)), isRetryable: true);
        }

        return best is not null && best.Score >= threshold ? best : null;
    }
}
