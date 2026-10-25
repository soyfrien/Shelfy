namespace Shelfy.Features.Books.SearchBooks;

public record BookSummary(Guid Id, string Title, string Author, string? Isbn, string? CoverUrl);

public record SearchBooksResponse(IReadOnlyList<BookSummary> Books);
