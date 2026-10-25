namespace Shelfy.Domain;

/// <summary>One book spine found in a photo: the raw OCR text, the parsed candidate and how it resolved.</summary>
public class DetectedSpine
{
    public Guid Id { get; set; }
    public Guid RecognitionJobId { get; set; }

    /// <summary>Order in the photo (left to right, then top to bottom).</summary>
    public int Position { get; set; }

    public string RawText { get; set; } = "";

    /// <summary>OCR confidence 0..1, null when the OCR engine gave none.</summary>
    public double? Confidence { get; set; }

    public string CandidateTitle { get; set; } = "";
    public string? CandidateAuthor { get; set; }
    public string? CandidateIsbn { get; set; }

    public SpineResolutionStatus Status { get; set; } = SpineResolutionStatus.Unresolved;

    public Guid? BookId { get; set; }
    public Book? Book { get; set; }
    public string? ResolvedIsbn { get; set; }

    /// <summary>Title+author match score 0..1 of the accepted match.</summary>
    public double? MatchScore { get; set; }

    /// <summary>Which resolver matched: OpenLibrary, GoogleBooks.</summary>
    public string? MatchSource { get; set; }

    public void MarkResolved(Book book, double score, string source)
    {
        Status = SpineResolutionStatus.Resolved;
        BookId = book.Id;
        ResolvedIsbn = book.Isbn;
        MatchScore = score;
        MatchSource = source;
    }
}
