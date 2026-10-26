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

    /// <summary>Which resolver matched: OpenLibrary, GoogleBooks; "Memory" for a remembered correction, "User" when the user picked the book.</summary>
    public string? MatchSource { get; set; }

    /// <summary>
    /// The user said "this is not a book" (tap to fix, ADR-011): the spine is no longer listed as unmatched. The read is kept.
    /// </summary>
    public bool Dismissed { get; set; }

    /// <summary>The ways a spine ends up resolved other than by a catalog lookup.</summary>
    public const string SourceUser = "User";
    public const string SourceMemory = "Memory";

    public bool IsResolvedByUser => Status == SpineResolutionStatus.Resolved && MatchSource == SourceUser;

    public void MarkResolved(Book book, double score, string source)
    {
        Status = SpineResolutionStatus.Resolved;
        BookId = book.Id;
        ResolvedIsbn = book.Isbn;
        MatchScore = score;
        MatchSource = source;
        Dismissed = false;
    }
}
