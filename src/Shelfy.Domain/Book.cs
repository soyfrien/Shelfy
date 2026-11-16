namespace Shelfy.Domain;

/// <summary>A canonical book record shared across users. A Book exists once.</summary>
public class Book
{
    public Guid Id { get; set; }

    /// <summary>The ISBN as originally stored (often already an ISBN-13). Prefer <see cref="Isbn13"/> for lookup.</summary>
    public string? Isbn { get; set; }

    /// <summary>Normalized ISBN-13 digits (no hyphens). Filled on save from <see cref="Isbn"/>. Null when unknown or invalid.</summary>
    public string? Isbn13 { get; set; }

    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string? CoverUrl { get; set; }
}
