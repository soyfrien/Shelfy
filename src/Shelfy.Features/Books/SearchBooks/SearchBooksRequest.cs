namespace Shelfy.Features.Books.SearchBooks;

public record SearchBooksRequest(string Query, int Take = 20);
