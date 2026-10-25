using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Features.Recognition;
using Shelfy.Features.Recognition.Storage;
using Shelfy.Features.Recognition.UploadShelfPhoto;
using Shelfy.Features.Recognition.Worker;
using Shelfy.Infrastructure;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Recognition;

public class UploadShelfPhotoTests
{
    private sealed class Fixture
    {
        public ShelfyDbContext Db { get; } = TestDb.Create();
        public InMemoryPhotoStorage Storage { get; } = new();
        public TestClock Clock { get; } = new();
        public Guid ShelfId { get; private set; }

        public async Task<Fixture> InitAsync()
        {
            var user = new User { Id = Guid.NewGuid(), DisplayName = "Louis", City = "Brooklyn" };
            var shelf = new Shelf { Id = Guid.NewGuid(), UserId = user.Id };
            Db.Users.Add(user);
            Db.Shelves.Add(shelf);
            await Db.SaveChangesAsync();
            ShelfId = shelf.Id;
            return this;
        }

        public UploadShelfPhotoHandler Handler(IPhotoStorage? storage = null) =>
            new(Db, storage ?? Storage, new DbJobQueue(null!, Options.Create(new RecognitionOptions()), Clock),
                Options.Create(new RecognitionOptions { MaxAttempts = 5 }), Clock);

        public Task<HandlerResult<UploadShelfPhotoResponse>> Upload(byte[] bytes, string? fileName = "shelf.jpg", long? declared = null, Guid? shelfId = null) =>
            Handler().HandleAsync(new UploadShelfPhotoRequest(shelfId ?? ShelfId, fileName, declared ?? bytes.Length, new MemoryStream(bytes)));
    }

    [Fact]
    public async Task Accepts_A_Jpeg_Stores_It_And_Creates_A_Pending_Job()
    {
        var f = await new Fixture().InitAsync();
        var bytes = TestImages.Jpeg(5000);

        var result = await f.Upload(bytes);

        Assert.True(result.IsSuccess);
        Assert.Equal("Pending", result.Value!.Status);
        Assert.False(result.Value.Duplicate);
        Assert.Equal($"/api/recognition-jobs/{result.Value.JobId}", result.Value.StatusUrl);

        var job = await f.Db.RecognitionJobs.SingleAsync();
        Assert.Equal(result.Value.JobId, job.Id);
        Assert.Equal(f.ShelfId, job.ShelfId);
        Assert.Equal(RecognitionJobStatus.Pending, job.Status);
        Assert.Equal("image/jpeg", job.ContentType);
        Assert.Equal(bytes.Length, job.SizeBytes);
        Assert.Equal(5, job.MaxAttempts);
        Assert.Equal(f.Clock.GetUtcNow().UtcDateTime, job.NextAttemptAt);
        Assert.EndsWith(".jpg", job.PhotoKey);
        Assert.Equal(bytes, f.Storage.Files[job.PhotoKey]); // stored byte-for-byte, header included
    }

    [Fact]
    public async Task Accepts_A_Png()
    {
        var f = await new Fixture().InitAsync();

        var result = await f.Upload(TestImages.Png(), "shelf.png");

        Assert.True(result.IsSuccess);
        var job = await f.Db.RecognitionJobs.SingleAsync();
        Assert.Equal("image/png", job.ContentType);
        Assert.EndsWith(".png", job.PhotoKey);
    }

    [Fact]
    public async Task Accepts_A_File_Without_An_Extension_Because_The_Content_Decides()
    {
        var f = await new Fixture().InitAsync();

        Assert.True((await f.Upload(TestImages.Jpeg(), fileName: "camera-capture")).IsSuccess);
    }

    [Theory]
    [InlineData("hello world, not an image")]
    [InlineData("%PDF-1.7 fake pdf")]
    [InlineData("GIF89a....")]
    public async Task Rejects_Non_Image_Content_Even_With_A_Jpg_Name(string text)
    {
        var f = await new Fixture().InitAsync();

        var result = await f.Upload(System.Text.Encoding.ASCII.GetBytes(text), "shelf.jpg");

        Assert.Equal(HandlerStatus.UnsupportedMediaType, result.Status);
        Assert.Contains("JPEG and PNG", result.Error);
        Assert.Empty(f.Storage.Files);
        Assert.Empty(f.Db.RecognitionJobs);
    }

    [Theory]
    [InlineData("shelf.png")]   // JPEG content named .png
    [InlineData("shelf.heic")]
    [InlineData("shelf.jpg.exe")]
    public async Task Rejects_A_Name_That_Contradicts_The_Content(string name)
    {
        var f = await new Fixture().InitAsync();

        var result = await f.Upload(TestImages.Jpeg(), name);

        Assert.Equal(HandlerStatus.UnsupportedMediaType, result.Status);
        Assert.Empty(f.Storage.Files);
    }

    [Fact]
    public async Task Rejects_An_Empty_File()
    {
        var f = await new Fixture().InitAsync();

        var result = await f.Upload(Array.Empty<byte>());

        Assert.Equal(HandlerStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Rejects_A_Declared_Size_Over_20_MB_Without_Reading_The_Body()
    {
        var f = await new Fixture().InitAsync();
        var stream = new ThrowOnReadStream();

        var result = await f.Handler().HandleAsync(new UploadShelfPhotoRequest(f.ShelfId, "a.jpg", 20L * 1024 * 1024 + 1, stream));

        Assert.Equal(HandlerStatus.TooLarge, result.Status);
        Assert.Contains("20 MB", result.Error);
    }

    [Fact]
    public async Task Rejects_A_Body_Over_20_MB_That_Under_Declares_Its_Size_And_Keeps_Nothing()
    {
        var f = await new Fixture().InitAsync();
        var bytes = TestImages.Jpeg((int)UploadShelfPhotoHandler.MaxPhotoBytes + 1024);

        var result = await f.Upload(bytes, declared: 1000);

        Assert.Equal(HandlerStatus.TooLarge, result.Status);
        Assert.Empty(f.Storage.Files);
        Assert.Empty(f.Db.RecognitionJobs);
    }

    [Fact]
    public async Task Accepts_A_File_Of_Exactly_20_MB()
    {
        var f = await new Fixture().InitAsync();
        var bytes = TestImages.Jpeg((int)UploadShelfPhotoHandler.MaxPhotoBytes - 10);
        Assert.Equal(UploadShelfPhotoHandler.MaxPhotoBytes, bytes.Length);

        Assert.True((await f.Upload(bytes)).IsSuccess);
    }

    [Fact]
    public async Task Returns_NotFound_For_An_Unknown_Shelf()
    {
        var f = await new Fixture().InitAsync();

        var result = await f.Upload(TestImages.Jpeg(), shelfId: Guid.NewGuid());

        Assert.Equal(HandlerStatus.NotFound, result.Status);
        Assert.Empty(f.Storage.Files);
    }

    [Fact]
    public async Task Returns_Invalid_For_An_Empty_Shelf_Id()
    {
        var f = await new Fixture().InitAsync();

        Assert.Equal(HandlerStatus.Invalid, (await f.Upload(TestImages.Jpeg(), shelfId: Guid.Empty)).Status);
    }

    [Fact]
    public async Task Retrying_The_Same_Photo_Returns_The_Existing_Job_And_Stores_Nothing_Twice()
    {
        var f = await new Fixture().InitAsync();
        var bytes = TestImages.Jpeg(2048);

        var first = await f.Upload(bytes);
        var retry = await f.Upload(bytes);

        Assert.True(retry.IsSuccess);
        Assert.True(retry.Value!.Duplicate);
        Assert.Equal(first.Value!.JobId, retry.Value.JobId);
        Assert.Single(f.Db.RecognitionJobs);
        Assert.Single(f.Storage.Files);
    }

    [Fact]
    public async Task The_Same_Photo_On_Another_Shelf_Is_Not_A_Duplicate()
    {
        var f = await new Fixture().InitAsync();
        var otherShelf = new Shelf { Id = Guid.NewGuid(), UserId = f.Db.Users.Single().Id };
        f.Db.Shelves.Add(otherShelf);
        await f.Db.SaveChangesAsync();
        var bytes = TestImages.Jpeg(2048);

        var a = await f.Upload(bytes);
        var b = await f.Upload(bytes, shelfId: otherShelf.Id);

        Assert.NotEqual(a.Value!.JobId, b.Value!.JobId);
        Assert.False(b.Value.Duplicate);
    }

    [Fact]
    public async Task A_Failed_Job_Does_Not_Block_Uploading_The_Photo_Again()
    {
        var f = await new Fixture().InitAsync();
        var bytes = TestImages.Jpeg(2048);
        var first = await f.Upload(bytes);
        var job = await f.Db.RecognitionJobs.SingleAsync();
        job.Status = RecognitionJobStatus.Failed;
        await f.Db.SaveChangesAsync();

        var again = await f.Upload(bytes);

        Assert.False(again.Value!.Duplicate);
        Assert.NotEqual(first.Value!.JobId, again.Value.JobId);
        Assert.Equal(2, f.Db.RecognitionJobs.Count());
    }

    [Fact]
    public async Task Removes_The_Stored_File_When_Saving_The_Job_Fails()
    {
        var f = await new Fixture().InitAsync();
        // A job with a missing shelf violates nothing in the in-memory provider, so force a failure through the queue instead.
        var handler = new UploadShelfPhotoHandler(f.Db, f.Storage, new ThrowingQueue(), Options.Create(new RecognitionOptions()), f.Clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new UploadShelfPhotoRequest(f.ShelfId, "a.jpg", 100, new MemoryStream(TestImages.Jpeg()))));

        Assert.Empty(f.Storage.Files);
    }

    [Fact]
    public async Task Hashes_And_Counts_While_Streaming()
    {
        var bytes = TestImages.Jpeg(100_000);
        var header = bytes[..12];
        using var metered = new MeteredHashingStream(new MemoryStream(bytes[12..]), header, 1_000_000);

        await metered.CopyToAsync(Stream.Null);

        Assert.Equal(bytes.Length, metered.BytesRead);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), metered.Sha256Hex);
    }

    private sealed class ThrowOnReadStream : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("body must not be read");
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowingQueue : IJobQueue
    {
        public Task EnqueueAsync(Guid jobId, CancellationToken ct = default) => throw new InvalidOperationException("queue down");
        public Task<JobLease?> ClaimNextAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task CompleteAsync(JobLease lease, CancellationToken ct = default) => throw new NotSupportedException();
        public Task FailAsync(JobLease lease, string error, bool retryable, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

public class LocalFileSystemPhotoStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shelfy-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Saves_Reads_And_Deletes_A_Photo()
    {
        var storage = new LocalFileSystemPhotoStorage(_root);
        var bytes = TestImages.Jpeg(300);

        await storage.SaveAsync("shelves/abc/1.jpg", new MemoryStream(bytes), "image/jpeg");

        await using (var read = await storage.OpenReadAsync("shelves/abc/1.jpg"))
        {
            using var ms = new MemoryStream();
            await read.CopyToAsync(ms);
            Assert.Equal(bytes, ms.ToArray());
        }

        await storage.DeleteAsync("shelves/abc/1.jpg");
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync("shelves/abc/1.jpg"));
        await storage.DeleteAsync("shelves/abc/1.jpg"); // deleting twice is fine
    }

    [Theory]
    [InlineData("../escape.jpg")]
    [InlineData("a/../../escape.jpg")]
    [InlineData("/etc/passwd")]
    [InlineData("a\\b.jpg")]
    [InlineData("")]
    public async Task Rejects_Keys_That_Could_Escape_The_Root(string key)
    {
        var storage = new LocalFileSystemPhotoStorage(_root);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(key, new MemoryStream(new byte[1]), "image/jpeg"));
    }

    [Fact]
    public async Task A_Failed_Write_Leaves_No_Partial_File()
    {
        var storage = new LocalFileSystemPhotoStorage(_root);

        await Assert.ThrowsAsync<PhotoTooLargeException>(async () =>
        {
            using var tooBig = new MeteredHashingStream(new MemoryStream(new byte[5000]), Array.Empty<byte>(), 1000);
            await storage.SaveAsync("shelves/x/1.jpg", tooBig, "image/jpeg");
        });

        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task The_Azure_Blob_Stub_Says_It_Is_Not_Implemented()
    {
        var stub = new AzureBlobPhotoStorage();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => stub.SaveAsync("k", Stream.Null, "image/jpeg"));

        Assert.Contains("not implemented", ex.Message);
    }
}
