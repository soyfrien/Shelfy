namespace Shelfy.Features.Recognition.Parsing;

/// <summary>A spine as parsed from OCR output: the text to search for plus the raw text for display and debugging.</summary>
/// <param name="Position">0-based order in the photo (left to right).</param>
/// <param name="RawText">All OCR lines of the spine joined with " | ", bounded in length.</param>
/// <param name="Title">Normalized title candidate; may be empty when only an ISBN was read.</param>
/// <param name="Author">Normalized author candidate, if one could be told apart.</param>
/// <param name="Isbn">A valid ISBN-13 read from the spine text, if any.</param>
/// <param name="Confidence">Mean OCR word confidence 0..1, null when unavailable.</param>
public record SpineCandidate(int Position, string RawText, string Title, string? Author, string? Isbn, double? Confidence);
