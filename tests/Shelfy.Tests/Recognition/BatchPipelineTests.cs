using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Recognition.Ocr;
using Shelfy.Features.Recognition.Resolution;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Recognition;

/// <summary>Overlapping tiles of one wide photo: the same spine is read in two tiles and must be listed once.</summary>
public class BatchPipelineTests
{
    private static PipelineHarness Harness()
    {
        var h = new PipelineHarness();
        h.Resolver.Matches["The Hobbit"] = FakeBookResolver.Match("9780547928227", "The Hobbit", "J.R.R. Tolkien");
        h.Resolver.Matches["Dune"] = FakeBookResolver.Match("9780441172719", "Dune", "Frank Herbert");
        h.Resolver.Matches["Neuromancer"] = FakeBookResolver.Match("9780441569595", "Neuromancer", "William Gibson");
        return h.Build();
    }

    private static async Task<List<Guid>> SeedTilesAsync(PipelineHarness h, int parts)
    {
        var batch = Guid.NewGuid();
        var (shelfId, first) = await h.SeedJobAsync(batchId: batch, part: 1, parts: parts);
        var ids = new List<Guid> { first };
        for (var i = 2; i <= parts; i++)
        {
            ids.Add((await h.SeedJobAsync(shelfId, batch, i, parts)).JobId);
        }

        return ids;
    }

    private static async Task<(List<string> Shelf, List<DetectedSpine> Spines)> ReadAsync(PipelineHarness h, List<Guid> jobs)
    {
        await using var db = h.Database.CreateContext();
        var shelfId = (await db.RecognitionJobs.AsNoTracking().FirstAsync(j => j.Id == jobs[0])).ShelfId;
        var shelf = await db.ShelfBooks.Where(sb => sb.ShelfId == shelfId).Include(sb => sb.Book).Select(sb => sb.Book!.Title).ToListAsync();
        var spines = await db.DetectedSpines.AsNoTracking().Where(s => jobs.Contains(s.RecognitionJobId)).ToListAsync();
        return (shelf.OrderBy(t => t).ToList(), spines);
    }

    [Fact]
    public async Task A_Book_Seen_In_Two_Tiles_Is_On_The_Shelf_Once()
    {
        using var h = Harness();
        h.Recognizer = new ScriptedRecognizer(
            ScriptedRecognizer.Lines("The Hobbit by J.R.R. Tolkien", "Dune by Frank Herbert"),
            ScriptedRecognizer.Lines("Dune by Frank Herbert", "Neuromancer by William Gibson"));
        var jobs = await SeedTilesAsync(h, 2);

        await h.Worker.RunOnceAsync(CancellationToken.None);
        await h.Worker.RunOnceAsync(CancellationToken.None);

        var (shelf, _) = await ReadAsync(h, jobs);
        Assert.Equal(new[] { "Dune", "Neuromancer", "The Hobbit" }, shelf);
        var second = await h.GetJobAsync(jobs[1]);
        Assert.Equal(RecognitionJobStatus.Completed, second.Status);
        Assert.Equal(1, second.BooksAdded); // only Neuromancer was new
    }

    [Fact]
    public async Task An_Unresolved_Spine_In_The_Overlap_Is_Listed_Once()
    {
        using var h = Harness();
        h.Recognizer = new ScriptedRecognizer(
            ScriptedRecognizer.Lines("The Hobbit by J.R.R. Tolkien", "Second Foundation by Isaac Asimov"),
            ScriptedRecognizer.Lines("Second Foundation by Isaac Asimov", "Neuromancer by William Gibson"),
            ScriptedRecognizer.Lines("Neuromancer by William Gibson", "Count Zero by William Gibson"));
        var jobs = await SeedTilesAsync(h, 3);

        for (var i = 0; i < 3; i++)
        {
            await h.Worker.RunOnceAsync(CancellationToken.None);
        }

        var (shelf, spines) = await ReadAsync(h, jobs);
        Assert.Equal(new[] { "Neuromancer", "The Hobbit" }, shelf);
        var unresolved = spines.Where(s => s.Status == SpineResolutionStatus.Unresolved).Select(s => s.CandidateTitle).OrderBy(t => t).ToList();
        Assert.Equal(new[] { "Count Zero", "Second Foundation" }, unresolved);
    }

    [Fact]
    public async Task A_Fragment_From_One_Tile_Is_Replaced_By_The_Better_Read_In_The_Next()
    {
        using var h = Harness();
        h.Recognizer = new ScriptedRecognizer(
            ScriptedRecognizer.Lines("Second Foundation by Isaac"),
            ScriptedRecognizer.Lines("Second Foundation by Isaac Asimov"));
        var jobs = await SeedTilesAsync(h, 2);

        await h.Worker.RunOnceAsync(CancellationToken.None);
        await h.Worker.RunOnceAsync(CancellationToken.None);

        var (_, spines) = await ReadAsync(h, jobs);
        var only = Assert.Single(spines);
        Assert.Equal(jobs[1], only.RecognitionJobId);
        Assert.Contains("Asimov", only.RawText);
    }

    [Fact]
    public async Task A_Better_Read_Arriving_First_Keeps_The_Fragment_Out()
    {
        using var h = Harness();
        h.Recognizer = new ScriptedRecognizer(
            ScriptedRecognizer.Lines("Second Foundation by Isaac Asimov"),
            ScriptedRecognizer.Lines("Second Foundation by Isaac"));
        var jobs = await SeedTilesAsync(h, 2);

        await h.Worker.RunOnceAsync(CancellationToken.None);
        await h.Worker.RunOnceAsync(CancellationToken.None);

        var (_, spines) = await ReadAsync(h, jobs);
        var only = Assert.Single(spines);
        Assert.Equal(jobs[0], only.RecognitionJobId);
    }

    [Fact]
    public async Task An_Unresolved_Fragment_Is_Dropped_When_Another_Tile_Resolves_The_Book()
    {
        using var h = Harness();
        h.Recognizer = new ScriptedRecognizer(
            ScriptedRecognizer.Lines("Neuromancer by William"),            // first read happens to miss: say the resolver did not know it ...
            ScriptedRecognizer.Lines("Neuromancer by William Gibson"));
        h.Resolver.Matches.Remove("Neuromancer");
        var jobs = await SeedTilesAsync(h, 2);

        await h.Worker.RunOnceAsync(CancellationToken.None);
        h.Resolver.Matches["Neuromancer"] = FakeBookResolver.Match("9780441569595", "Neuromancer", "William Gibson"); // ... but it does now
        await h.Worker.RunOnceAsync(CancellationToken.None);

        var (shelf, spines) = await ReadAsync(h, jobs);
        Assert.Equal(new[] { "Neuromancer" }, shelf);
        var only = Assert.Single(spines);
        Assert.Equal(SpineResolutionStatus.Resolved, only.Status);
    }

    [Fact]
    public async Task Noise_Reads_In_Tiles_Are_Not_Listed_But_A_Single_Photo_Keeps_Them()
    {
        // Tiled: "xq" and "7" are dropped as junk. Not tiled (parts = 1): behaviour is unchanged, everything is kept.
        using var tiled = Harness();
        tiled.Recognizer = new ScriptedRecognizer(ScriptedRecognizer.Lines("Count Zero by William Gibson", "xq", "Zed"));
        var tiledJobs = await SeedTilesAsync(tiled, 2);
        await tiled.Worker.RunOnceAsync(CancellationToken.None);
        var (_, tiledSpines) = await ReadAsync(tiled, tiledJobs);
        Assert.Equal(new[] { "Count Zero" }, tiledSpines.Select(s => s.CandidateTitle).ToArray());

        using var single = Harness();
        single.Recognizer = new ScriptedRecognizer(ScriptedRecognizer.Lines("Count Zero by William Gibson", "xq"));
        var singleJobs = await SeedTilesAsync(single, 1);
        await single.Worker.RunOnceAsync(CancellationToken.None);
        var (_, singleSpines) = await ReadAsync(single, singleJobs);
        Assert.Equal(2, singleSpines.Count);
    }

    [Fact]
    public async Task Photos_Without_A_Batch_Are_Not_Deduplicated_Against_Each_Other()
    {
        using var h = Harness();
        h.Recognizer = new ScriptedRecognizer(ScriptedRecognizer.Lines("Count Zero by William Gibson"));
        var (shelfId, a) = await h.SeedJobAsync();
        var (_, b) = await h.SeedJobAsync(shelfId);

        await h.Worker.RunOnceAsync(CancellationToken.None);
        await h.Worker.RunOnceAsync(CancellationToken.None);

        var (_, spines) = await ReadAsync(h, new List<Guid> { a, b });
        Assert.Equal(2, spines.Count);
    }

    [Fact]
    public async Task An_Author_Only_Read_In_A_Tile_Is_Not_Looked_Up_As_A_Title()
    {
        // A tile edge cut the title off, leaving only the author line. The whole spine is read in the next tile.
        using var h = Harness();
        h.Resolver.Matches["Philip K. Dick"] = FakeBookResolver.Match("9780000000002", "Philip K. Dick", "Someone Else");
        h.Resolver.Matches["Do Androids Dream of Electric Sheep?"] =
            FakeBookResolver.Match("9780345404473", "Do Androids Dream of Electric Sheep?", "Philip K. Dick");
        h.Recognizer = new ScriptedRecognizer(
            ScriptedRecognizer.Lines("Philip K. Dick"),
            ScriptedRecognizer.Lines("Do Androids Dream of Electric Sheep? by Philip K. Dick"));
        var jobs = await SeedTilesAsync(h, 2);

        await h.Worker.RunOnceAsync(CancellationToken.None);
        var (afterFirst, firstSpines) = await ReadAsync(h, jobs);
        Assert.Empty(afterFirst);                                   // nothing wrong was added to the shelf
        Assert.Equal(SpineResolutionStatus.Unresolved, Assert.Single(firstSpines).Status);

        await h.Worker.RunOnceAsync(CancellationToken.None);
        var (shelf, spines) = await ReadAsync(h, jobs);
        Assert.Equal(new[] { "Do Androids Dream of Electric Sheep?" }, shelf);
        Assert.Equal(SpineResolutionStatus.Resolved, Assert.Single(spines).Status); // the fragment is gone

        // The same read in an untiled photo is looked up as before.
        using var single = Harness();
        single.Resolver.Matches["Philip K. Dick"] = FakeBookResolver.Match("9780000000002", "Philip K. Dick", "Someone Else");
        single.Recognizer = new ScriptedRecognizer(ScriptedRecognizer.Lines("Philip K. Dick"));
        var singleJobs = await SeedTilesAsync(single, 1);
        await single.Worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(new[] { "Philip K. Dick" }, (await ReadAsync(single, singleJobs)).Shelf);
    }
}
