namespace Shelfy.Domain;

/// <summary>
/// A remembered fix (ADR-011): when a user says "this spine text is that book", the normalized text is stored here and the
/// recognition pipeline consults it before the external catalogs, so the same text matches automatically next time.
/// Global, not per shelf or user (there are no accounts yet).
/// </summary>
public class UserCorrection
{
    public Guid Id { get; set; }

    /// <summary>The spine text, normalized (lowercase, no accents or punctuation, words separated by one space). Unique.</summary>
    public string TextKey { get; set; } = "";

    public Guid BookId { get; set; }
    public Book? Book { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>How many times the pipeline matched a spine through this correction.</summary>
    public int UseCount { get; set; }
}
