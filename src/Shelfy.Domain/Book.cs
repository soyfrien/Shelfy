namespace Shelfy.Domain;

/// <summary>A canonical book record shared across users. A Book exists once.</summary>
public class Book
{
    public Guid Id { get; set; }
    public string? Isbn { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string? CoverUrl { get; set; }
}
