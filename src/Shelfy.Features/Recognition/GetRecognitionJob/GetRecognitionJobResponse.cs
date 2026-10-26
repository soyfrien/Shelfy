namespace Shelfy.Features.Recognition.GetRecognitionJob;

public record ResolvedBookDto(Guid BookId, string Title, string Author, string? Isbn, string? CoverUrl);

public record DetectedSpineDto(
    int Position,
    string RawText,
    double? Confidence,
    string CandidateTitle,
    string? CandidateAuthor,
    string? CandidateIsbn,
    string Status,
    string? ResolvedIsbn,
    double? MatchScore,
    string? MatchSource,
    ResolvedBookDto? Book,
    Guid? Id = null,
    string? NoiseReason = null);

/// <param name="Status">Pending, Processing, Completed or Failed.</param>
/// <param name="Attempts">How many times processing was started.</param>
/// <param name="RetryCount">Attempts beyond the first.</param>
/// <param name="Error">Last error, set while retrying and after the final failure.</param>
/// <param name="NextAttemptAt">When a Pending job will next be tried (UTC).</param>
/// <param name="Spines">Every spine found, including unresolved ones (shown so the user can correct them).</param>
/// <param name="Books">Distinct books the spines resolved to.</param>
public record RecognitionJobResponse(
    Guid Id,
    Guid ShelfId,
    string Status,
    int Attempts,
    int MaxAttempts,
    int RetryCount,
    string? Error,
    int BooksAdded,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? NextAttemptAt,
    IReadOnlyList<DetectedSpineDto> Spines,
    IReadOnlyList<ResolvedBookDto> Books);
