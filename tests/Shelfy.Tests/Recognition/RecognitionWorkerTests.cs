using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Domain;
using Shelfy.Features.Recognition;
using Shelfy.Features.Recognition.Ocr;
using Shelfy.Features.Recognition.Resolution;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Recognition;

/// <summary>The whole pipeline with fakes: stored photo -> OCR -> parsing -> resolution -> shelf, driven through the worker.</summary>
public class RecognitionWorkerTests
{
    private static PipelineHarness Harness()
    {
        var h = new PipelineHarness();
        h.Resolver.Matches["The Hobbit"] = FakeBookResolver.Match("9780547928227", "The Hobbit", "J.R.R. Tolkien");
        h.Resolver.Matches["Dune"] = FakeBookResolver.Match("9780441172719", "Dune", "Frank Herbert");
        // "Foundation" is deliberately unknown to the resolver.
        return h.Build();
    }

    private static async Task<List<(string Title, string Isbn, AvailabilityStatus Status)>> ShelfContentsAsync(PipelineHarness h, Guid shelfId)
    {
        await using var db = h.Database.CreateContext();
        var rows = await db.ShelfBooks.Where(sb => sb.ShelfId == shelfId).Include(sb => sb.Book).ToListAsync();
        return rows.Select(r => (r.Book!.Title, r.Book.Isbn!, r.Status)).OrderBy(r => r.Title).ToList();
    }

    [Fact]
    public async Task Happy_Path_Adds_Resolved_Books_As_Private_And_Keeps_Unresolved_Spines()
    {
        using var h = Harness();
        var (shelfId, jobId) = await h.SeedJobAsync();

        var worked = await h.Worker.RunOnceAsync(CancellationToken.None);

        Assert.True(worked);
        var job = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Completed, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(2, job.BooksAdded);
        Assert.Null(job.Error);

        Assert.Equal(new[]
        {
            ("Dune", "9780441172719", AvailabilityStatus.Private),
            ("The Hobbit", "9780547928227", AvailabilityStatus.Private)
        }, await ShelfContentsAsync(h, shelfId));

        await using var db = h.Database.CreateContext();
        var spines = await db.DetectedSpines.Where(s => s.RecognitionJobId == jobId).OrderBy(s => s.Position).ToListAsync();
        Assert.Equal(3, spines.Count);
        Assert.Equal(new[] { SpineResolutionStatus.Resolved, SpineResolutionStatus.Resolved, SpineResolutionStatus.Unresolved },
            spines.Select(s => s.Status));
        var hobbit = spines[0];
        Assert.Equal("The Hobbit", hobbit.CandidateTitle);
        Assert.Equal("J.R.R. Tolkien", hobbit.CandidateAuthor);
        Assert.Contains("THE HOBBIT", hobbit.RawText);
        Assert.Equal("9780547928227", hobbit.ResolvedIsbn);
        Assert.Equal("Fake", hobbit.MatchSource);
        Assert.NotNull(hobbit.BookId);
        var unmatched = spines[2];
        Assert.Equal("Foundation", unmatched.CandidateTitle);
        Assert.Null(unmatched.BookId);
        Assert.Null(unmatched.ResolvedIsbn);
        Assert.Equal(0.95, unmatched.Confidence);
    }

    [Fact]
    public async Task Tries_The_Title_Alone_When_Title_And_Author_Find_Nothing()
    {
        using var h = Harness();
        await h.SeedJobAsync();

        await h.Worker.RunOnceAsync(CancellationToken.None);

        // Foundation had an author, found nothing, and was retried without it.
        var foundation = h.Resolver.Queries.Where(q => q.Title == "Foundation").ToList();
        Assert.Equal(2, foundation.Count);
        Assert.Equal("Isaac Asimov", foundation[0].Author);
        Assert.Null(foundation[1].Author);
    }

    [Fact]
    public async Task Does_Not_Add_The_Same_Book_Twice_From_One_Photo()
    {
        using var h = Harness();
        h.Recognizer = new FakeTextRecognizer(new OcrResult(null, null, new[]
        {
            FakeTextRecognizer.VerticalLine("DUNE", 100, 50, 400, 40),
            FakeTextRecognizer.VerticalLine("DUNE", 300, 50, 400, 40), // a second copy of the same book
            FakeTextRecognizer.VerticalLine("THE HOBBIT", 500, 50, 400, 40)
        }));
        var (shelfId, jobId) = await h.SeedJobAsync();

        await h.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, (await ShelfContentsAsync(h, shelfId)).Count);
        await using var db = h.Database.CreateContext();
        Assert.Equal(2, await db.Books.CountAsync());
        Assert.Equal(3, await db.DetectedSpines.CountAsync(s => s.RecognitionJobId == jobId)); // both spines are recorded
        Assert.Equal(2, (await h.GetJobAsync(jobId)).BooksAdded);
    }

    [Fact]
    public async Task Reuses_An_Existing_Book_By_Isbn_And_Skips_Books_Already_On_The_Shelf()
    {
        using var h = Harness();
        var (shelfId, firstJob) = await h.SeedJobAsync();
        await h.Worker.RunOnceAsync(CancellationToken.None);

        // A second photo of the same shelf with the same books.
        var (_, secondJob) = await h.SeedJobAsync(shelfId);
        await h.Worker.RunOnceAsync(CancellationToken.None);

        await using var db = h.Database.CreateContext();
        Assert.Equal(2, await db.Books.CountAsync());
        Assert.Equal(2, await db.ShelfBooks.CountAsync(sb => sb.ShelfId == shelfId));
        Assert.Equal(0, (await h.GetJobAsync(secondJob)).BooksAdded);
        Assert.Equal(2, (await h.GetJobAsync(firstJob)).BooksAdded);
        var secondSpines = await db.DetectedSpines.Where(s => s.RecognitionJobId == secondJob && s.Status == SpineResolutionStatus.Resolved).ToListAsync();
        Assert.Equal(2, secondSpines.Count); // still linked to the books for display
    }

    [Fact]
    public async Task Does_Not_Change_The_Status_Of_Books_Already_On_The_Shelf()
    {
        using var h = Harness();
        var (shelfId, _) = await h.SeedJobAsync();
        await h.Worker.RunOnceAsync(CancellationToken.None);
        await using (var db = h.Database.CreateContext())
        {
            var dune = await db.ShelfBooks.Include(sb => sb.Book).SingleAsync(sb => sb.Book!.Title == "Dune");
            dune.OfferForSale(9.5m);
            await db.SaveChangesAsync();
        }

        await h.SeedJobAsync(shelfId);
        await h.Worker.RunOnceAsync(CancellationToken.None);

        await using var check = h.Database.CreateContext();
        var after = await check.ShelfBooks.Include(sb => sb.Book).SingleAsync(sb => sb.Book!.Title == "Dune");
        Assert.Equal(AvailabilityStatus.Sale, after.Status);
        Assert.Equal(9.5m, after.SalePrice);
    }

    [Fact]
    public async Task Transient_Failure_Is_Retried_With_Backoff_And_Then_Succeeds()
    {
        using var h = Harness();
        var (shelfId, jobId) = await h.SeedJobAsync();
        h.Resolver.ThrowOnResolve = new RecognitionException("OpenLibrary returned 503.", isRetryable: true);

        await h.Worker.RunOnceAsync(CancellationToken.None);

        var afterFail = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Pending, afterFail.Status);
        Assert.Equal(1, afterFail.Attempts);
        Assert.Contains("503", afterFail.Error);
        Assert.Equal(h.Clock.GetUtcNow().UtcDateTime.AddSeconds(30), afterFail.NextAttemptAt);
        Assert.False(await h.Worker.RunOnceAsync(CancellationToken.None)); // not due yet
        Assert.Empty(await ShelfContentsAsync(h, shelfId));

        h.Resolver.ThrowOnResolve = null;
        h.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(await h.Worker.RunOnceAsync(CancellationToken.None));

        var done = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Completed, done.Status);
        Assert.Equal(2, done.Attempts);
        Assert.Null(done.Error);
        Assert.Equal(2, (await ShelfContentsAsync(h, shelfId)).Count);
    }

    [Fact]
    public async Task A_Retry_Replaces_Spines_From_The_Interrupted_Attempt()
    {
        using var h = Harness();
        var (_, jobId) = await h.SeedJobAsync();
        await using (var db = h.Database.CreateContext())
        {
            db.DetectedSpines.Add(new DetectedSpine { Id = Guid.NewGuid(), RecognitionJobId = jobId, Position = 99, RawText = "stale", CandidateTitle = "Stale" });
            await db.SaveChangesAsync();
        }

        await h.Worker.RunOnceAsync(CancellationToken.None);

        await using var check = h.Database.CreateContext();
        var spines = await check.DetectedSpines.Where(s => s.RecognitionJobId == jobId).ToListAsync();
        Assert.Equal(3, spines.Count);
        Assert.DoesNotContain(spines, s => s.CandidateTitle == "Stale");
    }

    [Fact]
    public async Task Gives_Up_After_The_Maximum_Number_Of_Attempts()
    {
        using var h = Harness();
        var (shelfId, jobId) = await h.SeedJobAsync();
        h.Resolver.ThrowOnResolve = new RecognitionException("providers down", isRetryable: true);

        for (var i = 0; i < 3; i++)
        {
            h.Clock.Advance(TimeSpan.FromHours(1));
            Assert.True(await h.Worker.RunOnceAsync(CancellationToken.None));
        }

        var job = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Failed, job.Status);
        Assert.Equal(3, job.Attempts);
        Assert.Equal("providers down", job.Error);
        Assert.Equal(job.PhotoKey, h.Storage.Files.Keys.Single(k => k == job.PhotoKey)); // photo kept for a later retry
        h.Clock.Advance(TimeSpan.FromHours(1));
        Assert.False(await h.Worker.RunOnceAsync(CancellationToken.None));
        Assert.Empty(await ShelfContentsAsync(h, shelfId));
    }

    [Fact]
    public async Task Missing_Ocr_Configuration_Fails_The_Job_At_Once_With_A_Helpful_Error()
    {
        using var h = Harness();
        h.Recognizer = new NotConfiguredTextRecognizer();
        var (_, jobId) = await h.SeedJobAsync();

        await h.Worker.RunOnceAsync(CancellationToken.None);

        var job = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Failed, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Contains("AzureVision__Key", job.Error);
    }

    [Fact]
    public async Task A_Missing_Photo_Fails_Permanently()
    {
        using var h = Harness();
        var (_, jobId) = await h.SeedJobAsync();
        h.Storage.Files.Clear();

        await h.Worker.RunOnceAsync(CancellationToken.None);

        var job = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Failed, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Contains("photo is missing", job.Error);
    }

    [Fact]
    public async Task An_Unexpected_Exception_Is_Retried()
    {
        using var h = Harness();
        h.Recognizer = new ThrowingRecognizer(new InvalidOperationException("kaboom"));
        var (_, jobId) = await h.SeedJobAsync();

        await h.Worker.RunOnceAsync(CancellationToken.None);

        var job = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Pending, job.Status);
        Assert.Contains("kaboom", job.Error);
    }

    [Fact]
    public async Task A_Job_That_No_Longer_Exists_Fails_Permanently()
    {
        using var h = Harness();

        using var scope = h.Services.CreateScope();
        var ex = await Assert.ThrowsAsync<RecognitionException>(() =>
            scope.ServiceProvider.GetRequiredService<Shelfy.Features.Recognition.Worker.RecognitionJobProcessor>().ProcessAsync(Guid.NewGuid()));

        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public async Task Photos_With_No_Recognizable_Text_Complete_With_No_Books()
    {
        using var h = Harness();
        h.Recognizer = new FakeTextRecognizer(new OcrResult(100, 100, Array.Empty<OcrLine>()));
        var (shelfId, jobId) = await h.SeedJobAsync();

        await h.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(RecognitionJobStatus.Completed, (await h.GetJobAsync(jobId)).Status);
        Assert.Empty(await ShelfContentsAsync(h, shelfId));
    }

    [Fact]
    public async Task Matches_Below_The_Threshold_Are_Left_Unresolved()
    {
        using var h = Harness();
        h.Resolver.Matches["Dune"] = FakeBookResolver.Match("9780441172719", "Dune Buggy Repair", "Someone Else", score: 0.4);
        var (_, jobId) = await h.SeedJobAsync();

        await h.Worker.RunOnceAsync(CancellationToken.None);

        await using var db = h.Database.CreateContext();
        var dune = await db.DetectedSpines.SingleAsync(s => s.RecognitionJobId == jobId && s.CandidateTitle == "Dune");
        Assert.Equal(SpineResolutionStatus.Unresolved, dune.Status);
        Assert.False(await db.Books.AnyAsync(b => b.Title.StartsWith("Dune")));
    }

    [Fact]
    public async Task Uses_An_Isbn_Read_From_The_Spine_Without_Needing_A_Title()
    {
        using var h = Harness();
        h.Recognizer = new FakeTextRecognizer(new OcrResult(null, null, new[] { OcrLine.TextOnly("9780441172719") }));
        h.Resolver.Matches[""] = FakeBookResolver.Match("9780441172719", "Dune", "Frank Herbert");
        var (shelfId, _) = await h.SeedJobAsync();

        await h.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal("9780441172719", Assert.Single(h.Resolver.Queries).Isbn);
        Assert.Single(await ShelfContentsAsync(h, shelfId));
    }

    [Fact]
    public async Task Respects_The_Spine_Limit()
    {
        using var h = Harness();
        h.Options.MaxSpinesPerPhoto = 2;
        h.Recognizer = new FakeTextRecognizer(new OcrResult(null, null,
            Enumerable.Range(0, 10).Select(i => OcrLine.TextOnly($"Some Book {i}")).ToArray()));
        var (_, jobId) = await h.SeedJobAsync();

        await h.Worker.RunOnceAsync(CancellationToken.None);

        await using var db = h.Database.CreateContext();
        Assert.Equal(2, await db.DetectedSpines.CountAsync(s => s.RecognitionJobId == jobId));
    }

    [Fact]
    public async Task Processes_One_Job_At_A_Time_In_Order()
    {
        using var h = Harness();
        var (shelfId, a) = await h.SeedJobAsync();
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        var (_, b) = await h.SeedJobAsync(shelfId);

        await h.Worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(RecognitionJobStatus.Completed, (await h.GetJobAsync(a)).Status);
        Assert.Equal(RecognitionJobStatus.Pending, (await h.GetJobAsync(b)).Status);
    }

    [Fact]
    public async Task Returns_False_When_The_Queue_Is_Empty()
    {
        using var h = Harness();

        Assert.False(await h.Worker.RunOnceAsync(CancellationToken.None));
    }

    /// <summary>Deletes the shelf of the job while "OCR" runs, like a user pressing "Delete everything" mid-read.</summary>
    private sealed class DeletesShelfDuringOcr(PipelineHarness h, Guid shelfId) : ITextRecognizer
    {
        public async Task<OcrResult> RecognizeAsync(Stream image, string contentType, CancellationToken ct = default)
        {
            await using var db = h.Database.CreateContext();
            var spines = db.DetectedSpines.Where(s => db.RecognitionJobs.Any(j => j.Id == s.RecognitionJobId && j.ShelfId == shelfId));
            db.DetectedSpines.RemoveRange(spines);
            db.RecognitionJobs.RemoveRange(db.RecognitionJobs.Where(j => j.ShelfId == shelfId));
            db.Shelves.RemoveRange(db.Shelves.Where(s => s.Id == shelfId));
            await db.SaveChangesAsync(ct);
            return ScriptedRecognizer.Lines("THE HOBBIT", "J.R.R. TOLKIEN");
        }
    }

    [Fact]
    public async Task A_Shelf_Deleted_During_Processing_Does_Not_Resurrect_Anything_And_The_Worker_Carries_On()
    {
        using var h = Harness();
        var (shelfId, jobId) = await h.SeedJobAsync();
        h.Recognizer = new DeletesShelfDuringOcr(h, shelfId);

        var worked = await h.Worker.RunOnceAsync(CancellationToken.None);

        Assert.True(worked);
        await using var db = h.Database.CreateContext();
        Assert.False(await db.RecognitionJobs.AnyAsync(j => j.Id == jobId));
        Assert.False(await db.Shelves.AnyAsync(s => s.Id == shelfId));
        Assert.False(await db.ShelfBooks.AnyAsync(sb => sb.ShelfId == shelfId));
        Assert.False(await db.DetectedSpines.AnyAsync());
        Assert.False(await h.Worker.RunOnceAsync(CancellationToken.None)); // queue is empty, no retry loop
    }
}
