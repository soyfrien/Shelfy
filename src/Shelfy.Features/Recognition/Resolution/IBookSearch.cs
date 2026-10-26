namespace Shelfy.Features.Recognition.Resolution;

/// <summary>
/// Lists several candidate books for the "tap to fix" sheet (ADR-011), unlike <see cref="IBookResolver"/> which picks one.
/// Providers throw a retryable <see cref="RecognitionException"/> on provider trouble.
/// </summary>
public interface IBookSearch
{
    string Name { get; }

    /// <summary>Title (and author) search. Each match carries a <see cref="BookMatch.Score"/> against the query.</summary>
    Task<IReadOnlyList<BookMatch>> SearchAsync(BookQuery query, int limit, CancellationToken ct = default);

    /// <summary>Free-text search (what a person types: title, author, or an ISBN). Scores are not computed (0).</summary>
    Task<IReadOnlyList<BookMatch>> SearchTextAsync(string text, int limit, CancellationToken ct = default);
}
