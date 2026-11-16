namespace Shelfy.Features.Books.SearchShelves;

public record SearchShelvesRequest(string? Query, int Limit = SearchShelvesHandler.DefaultLimit, int Offset = 0);
