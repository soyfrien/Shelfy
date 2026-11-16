using Microsoft.EntityFrameworkCore;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Shelves.GetShelf;

public class GetShelfHandler(ShelfyDbContext db)
{
    // TODO(ADR-006): only the owner may read a non-public shelf once authentication exists.
    public async Task<HandlerResult<GetShelfResponse>> HandleAsync(GetShelfRequest request, CancellationToken ct = default)
    {
        var shelf = await db.Shelves
            .AsNoTracking()
            .Include(s => s.Books).ThenInclude(sb => sb.Book)
            .FirstOrDefaultAsync(s => s.Id == request.ShelfId, ct);

        if (shelf is null || shelf.IsHidden)
        {
            return HandlerResult<GetShelfResponse>.NotFound("Shelf not found.");
        }

        var books = shelf.Books
            .Where(sb => sb.Book is not null)
            .Select(sb => new ShelfBookDto(sb.BookId, sb.Book!.Title, sb.Book.Author, sb.Book.Isbn, sb.Book.CoverUrl, sb.Status))
            .OrderBy(b => b.Title)
            .ToList();

        var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == shelf.UserId, ct);
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == shelf.UserId, ct);
        return HandlerResult<GetShelfResponse>.Ok(new GetShelfResponse(shelf.Id, shelf.UserId, shelf.IsPublic, books, PublicUserName.Of(owner, account)));
    }
}
