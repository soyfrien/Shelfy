namespace Shelfy.Features.Books.SearchShelves;

public record SearchCopyDto(string Code, string Status);

public record SearchShelfDto(Guid Id, string OwnerName, bool Available, IReadOnlyList<SearchCopyDto> Copies);

public record SearchBookDto(
    Guid Id,
    string Title,
    string Author,
    string? Isbn,
    string? CoverUrl,
    bool Available,
    IReadOnlyList<SearchShelfDto> Shelves);

public record SearchShelvesResponse(
    string Query,
    int Total,
    int Limit,
    int Offset,
    IReadOnlyList<SearchBookDto> Books);
