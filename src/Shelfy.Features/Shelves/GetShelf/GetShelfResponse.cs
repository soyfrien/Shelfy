using Shelfy.Domain;

namespace Shelfy.Features.Shelves.GetShelf;

public record ShelfBookDto(Guid BookId, string Title, string Author, string? Isbn, string? CoverUrl, AvailabilityStatus Status);

public record GetShelfResponse(Guid Id, Guid UserId, bool IsPublic, IReadOnlyList<ShelfBookDto> Books, string OwnerName = "Someone");
