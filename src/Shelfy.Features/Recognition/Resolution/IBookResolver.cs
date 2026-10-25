namespace Shelfy.Features.Recognition.Resolution;

/// <summary>What to look up. At least one of Title or Isbn is set.</summary>
public record BookQuery(string Title, string? Author = null, string? Isbn = null);

/// <summary>A resolved book. <see cref="Isbn"/> is always a valid ISBN-13. Score is 0..1.</summary>
public record BookMatch(string Isbn, string Title, string Author, string? CoverUrl, double Score, string Source);

/// <summary>Resolves a parsed spine to a real book (ADR-007).</summary>
public interface IBookResolver
{
    string Name { get; }

    /// <summary>Returns the best match found, or null when there is none. Callers apply their own acceptance threshold to <see cref="BookMatch.Score"/>.</summary>
    /// <exception cref="RecognitionException">Retryable when the provider is unavailable (429, 5xx, network).</exception>
    Task<BookMatch?> ResolveAsync(BookQuery query, CancellationToken ct = default);
}
