using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Features.Shelves.DeleteShelf;
using Shelfy.Features.Shelves.GetShelf;
using Shelfy.Features.Shelves.HideShelf;
using Shelfy.Infrastructure;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Features.Shelves;

/// <summary>"Hide shelf" (soft) and "Delete everything" (hard), ADR-010.</summary>
public class ClearShelfHandlerTests
{
    private static readonly TestClock Clock = new();

    private sealed record Seed(Guid ShelfId, Guid OtherShelfId, Guid BookId, string PhotoKey, string OtherPhotoKey);

    private static async Task<Seed> SeedAsync(ShelfyDbContext db, InMemoryPhotoStorage storage)
    {
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Louis", City = "Brooklyn" };
        var dune = new Book { Id = Guid.NewGuid(), Title = "Dune", Author = "Frank Herbert", Isbn = "9780441172719" };
        var mine = new Shelf { Id = Guid.NewGuid(), UserId = user.Id };
        var other = new Shelf { Id = Guid.NewGuid(), UserId = user.Id };
        mine.AddBook(dune.Id);
        other.AddBook(dune.Id);
        db.AddRange(user, dune, mine, other);

        string Photo(Shelf s)
        {
            var key = $"shelves/{s.Id:N}/{Guid.NewGuid():N}.jpg";
            storage.Files[key] = [0xFF, 0xD8, 0xFF];
            var job = RecognitionJob.CreatePending(s.Id, key, "image/jpeg", 3, Guid.NewGuid().ToString("N"), 3, DateTime.UtcNow);
            job.Spines.Add(new DetectedSpine { Id = Guid.NewGuid(), RecognitionJobId = job.Id, RawText = "DUNE", CandidateTitle = "Dune", BookId = dune.Id });
            job.Spines.Add(new DetectedSpine { Id = Guid.NewGuid(), RecognitionJobId = job.Id, RawText = "???", CandidateTitle = "???" });
            db.RecognitionJobs.Add(job);
            return key;
        }

        var seed = new Seed(mine.Id, other.Id, dune.Id, Photo(mine), Photo(other));
        await db.SaveChangesAsync();
        return seed;
    }

    [Fact]
    public async Task Hide_Keeps_All_Data_But_The_Shelf_Is_No_Longer_Served()
    {
        await using var db = TestDb.Create();
        var storage = new InMemoryPhotoStorage();
        var seed = await SeedAsync(db, storage);

        var result = await new HideShelfHandler(db, Clock).HandleAsync(seed.ShelfId);

        Assert.True(result.IsSuccess);
        var shelf = await db.Shelves.SingleAsync(s => s.Id == seed.ShelfId);
        Assert.True(shelf.IsHidden);
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, shelf.HiddenAt);
        Assert.Equal(1, await db.ShelfBooks.CountAsync(sb => sb.ShelfId == seed.ShelfId));
        Assert.Equal(1, await db.RecognitionJobs.CountAsync(j => j.ShelfId == seed.ShelfId));
        Assert.Equal(2, await db.DetectedSpines.CountAsync(s => db.RecognitionJobs.Any(j => j.Id == s.RecognitionJobId && j.ShelfId == seed.ShelfId)));
        Assert.True(storage.Files.ContainsKey(seed.PhotoKey));

        var read = await new GetShelfHandler(db).HandleAsync(new GetShelfRequest(seed.ShelfId));
        Assert.Equal(HandlerStatus.NotFound, read.Status);
        Assert.True((await new GetShelfHandler(db).HandleAsync(new GetShelfRequest(seed.OtherShelfId))).IsSuccess);
    }

    [Fact]
    public async Task Hide_Twice_Is_Fine_And_Keeps_The_First_Time()
    {
        await using var db = TestDb.Create();
        var seed = await SeedAsync(db, new InMemoryPhotoStorage());
        var clock = new TestClock();
        var handler = new HideShelfHandler(db, clock);

        await handler.HandleAsync(seed.ShelfId);
        var first = (await db.Shelves.SingleAsync(s => s.Id == seed.ShelfId)).HiddenAt;
        clock.Advance(TimeSpan.FromHours(1));
        var again = await handler.HandleAsync(seed.ShelfId);

        Assert.True(again.IsSuccess);
        Assert.Equal(first, (await db.Shelves.SingleAsync(s => s.Id == seed.ShelfId)).HiddenAt);
    }

    [Fact]
    public async Task Hide_Unknown_Shelf_Is_NotFound()
    {
        await using var db = TestDb.Create();

        var result = await new HideShelfHandler(db, Clock).HandleAsync(Guid.NewGuid());

        Assert.Equal(HandlerStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Delete_Removes_Everything_Of_The_Shelf_But_Not_The_Catalog_Or_Other_Shelves()
    {
        await using var db = TestDb.Create();
        var storage = new InMemoryPhotoStorage();
        var seed = await SeedAsync(db, storage);
        storage.Files[$"shelves/{seed.ShelfId:N}/orphan.jpg"] = [1]; // uploaded at the moment of deletion: no job row yet

        var result = await new DeleteShelfHandler(db, storage, NullLogger<DeleteShelfHandler>.Instance).HandleAsync(seed.ShelfId);

        Assert.True(result.IsSuccess);
        Assert.False(await db.Shelves.AnyAsync(s => s.Id == seed.ShelfId));
        Assert.False(await db.ShelfBooks.AnyAsync(sb => sb.ShelfId == seed.ShelfId));
        Assert.False(await db.RecognitionJobs.AnyAsync(j => j.ShelfId == seed.ShelfId));
        Assert.Equal(2, await db.DetectedSpines.CountAsync()); // only the other shelf's two spines remain
        Assert.DoesNotContain(storage.Files.Keys, k => k.StartsWith($"shelves/{seed.ShelfId:N}/"));

        Assert.True(await db.Books.AnyAsync(b => b.Id == seed.BookId));
        Assert.True(await db.Shelves.AnyAsync(s => s.Id == seed.OtherShelfId));
        Assert.Equal(1, await db.ShelfBooks.CountAsync(sb => sb.ShelfId == seed.OtherShelfId));
        Assert.Equal(1, await db.RecognitionJobs.CountAsync(j => j.ShelfId == seed.OtherShelfId));
        Assert.True(storage.Files.ContainsKey(seed.OtherPhotoKey));
        Assert.True(await db.Users.AnyAsync());
    }

    [Fact]
    public async Task Delete_Twice_And_Delete_Unknown_Are_Fine()
    {
        await using var db = TestDb.Create();
        var storage = new InMemoryPhotoStorage();
        var seed = await SeedAsync(db, storage);
        var handler = new DeleteShelfHandler(db, storage, NullLogger<DeleteShelfHandler>.Instance);

        Assert.True((await handler.HandleAsync(seed.ShelfId)).IsSuccess);
        Assert.True((await handler.HandleAsync(seed.ShelfId)).IsSuccess);
        Assert.True((await handler.HandleAsync(Guid.NewGuid())).IsSuccess);
        Assert.True(await db.Shelves.AnyAsync(s => s.Id == seed.OtherShelfId));
    }

    [Fact]
    public async Task Delete_Also_Removes_A_Hidden_Shelf()
    {
        await using var db = TestDb.Create();
        var storage = new InMemoryPhotoStorage();
        var seed = await SeedAsync(db, storage);
        await new HideShelfHandler(db, Clock).HandleAsync(seed.ShelfId);

        await new DeleteShelfHandler(db, storage, NullLogger<DeleteShelfHandler>.Instance).HandleAsync(seed.ShelfId);

        Assert.False(await db.Shelves.AnyAsync(s => s.Id == seed.ShelfId));
        Assert.False(storage.Files.ContainsKey(seed.PhotoKey));
    }
}
