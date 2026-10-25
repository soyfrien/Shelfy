using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Domain;
using Shelfy.Infrastructure;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Api;

/// <summary>Batch fields on the upload endpoint and GET /api/shelves/{id}/batches/{batchId} (progress of a tiled photo).</summary>
public class UploadBatchEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public UploadBatchEndpointTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<Guid> NewShelfAsync()
    {
        var userId = await _factory.CreateUserAsync();
        var response = await _client.PostAsJsonAsync("/api/shelves", new { userId });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent Form(byte[] bytes, Guid? batch = null, string? part = null, string? parts = null, string? rawBatch = null)
    {
        var form = new MultipartFormDataContent();
        if (batch is not null || rawBatch is not null)
        {
            form.Add(new StringContent(rawBatch ?? batch!.Value.ToString()), "batchId");
        }

        if (part is not null)
        {
            form.Add(new StringContent(part), "part");
        }

        if (parts is not null)
        {
            form.Add(new StringContent(parts), "parts");
        }

        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "tile.jpg");
        return form;
    }

    private async Task<JsonElement> UploadAsync(Guid shelfId, byte[] bytes, Guid batch, int part, int parts)
    {
        var response = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(bytes, batch, part.ToString(), parts.ToString()));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<JsonElement> BatchAsync(Guid shelfId, Guid batch)
    {
        var response = await _client.GetAsync($"/api/shelves/{shelfId}/batches/{batch}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Theory]
    [InlineData(null, null, "not-a-guid")]               // batchId is not a GUID
    [InlineData(null, null, "00000000-0000-0000-0000-000000000000")]
    [InlineData(null, "3", "abc")]
    public async Task Invalid_Batch_Fields_Are_400_Before_Anything_Is_Stored(string? part, string? parts, string rawBatch)
    {
        var shelfId = await NewShelfAsync();

        var response = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Jpeg(100), null, part, parts, rawBatch));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Directory.Exists(_factory.PhotoFolder) ? Directory.GetFiles(_factory.PhotoFolder, "*", SearchOption.AllDirectories) : Array.Empty<string>());
    }

    [Theory]
    [InlineData(null, "2")]    // part missing
    [InlineData("1", null)]    // parts missing
    [InlineData("0", "2")]
    [InlineData("3", "2")]
    [InlineData("1", "25")]
    [InlineData("x", "2")]
    public async Task A_Batch_Needs_A_Valid_Part_And_Parts(string? part, string? parts)
    {
        var shelfId = await NewShelfAsync();

        var response = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Jpeg(100), Guid.NewGuid(), part, parts));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Uploads_Without_Batch_Fields_Still_Work_As_Before()
    {
        var shelfId = await NewShelfAsync();

        var response = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Jpeg(100)));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Batch_Status_Reports_Progress_Per_Part_And_Merges_The_Overlap()
    {
        var shelfId = await NewShelfAsync();
        var batch = Guid.NewGuid();
        var a = await UploadAsync(shelfId, TestImages.Jpeg(1001), batch, 1, 2);

        var early = await BatchAsync(shelfId, batch);
        Assert.Equal(2, early.GetProperty("parts").GetInt32());
        Assert.Equal(1, early.GetProperty("received").GetInt32());
        Assert.Equal(1, early.GetProperty("pending").GetInt32());
        Assert.False(early.GetProperty("done").GetBoolean());

        var b = await UploadAsync(shelfId, TestImages.Jpeg(1002), batch, 2, 2);
        Assert.NotEqual(a.GetProperty("jobId").GetGuid(), b.GetProperty("jobId").GetGuid());
        var queued = await BatchAsync(shelfId, batch);
        Assert.Equal(2, queued.GetProperty("received").GetInt32());
        Assert.Equal(2, queued.GetProperty("pending").GetInt32());
        Assert.False(queued.GetProperty("done").GetBoolean());

        Assert.True(await _factory.RunWorkerOnceAsync());
        var half = await BatchAsync(shelfId, batch);
        Assert.Equal(1, half.GetProperty("completed").GetInt32());
        Assert.Equal(1, half.GetProperty("pending").GetInt32());
        Assert.False(half.GetProperty("done").GetBoolean());
        Assert.Equal(new[] { 1, 2 }, half.GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("part").GetInt32()));
        Assert.Equal("Completed", half.GetProperty("jobs")[0].GetProperty("status").GetString());

        Assert.True(await _factory.RunWorkerOnceAsync());
        var done = await BatchAsync(shelfId, batch);
        Assert.True(done.GetProperty("done").GetBoolean());
        Assert.Equal(2, done.GetProperty("completed").GetInt32());
        Assert.Equal(2, done.GetProperty("booksAdded").GetInt32());   // the second tile saw the same canned shelf: nothing new

        // Both tiles read Hobbit, Dune and Foundation. Merged: two books once each, Foundation (unresolved) once.
        Assert.Equal(new[] { "Dune", "The Hobbit" },
            done.GetProperty("books").EnumerateArray().Select(x => x.GetProperty("title").GetString()!).OrderBy(t => t));
        var spines = done.GetProperty("spines").EnumerateArray().ToList();
        Assert.Equal(3, spines.Count);
        Assert.Single(spines, s => s.GetProperty("status").GetString() == "Unresolved");

        var shelf = JsonDocument.Parse(await _client.GetStringAsync($"/api/shelves/{shelfId}")).RootElement;
        Assert.Equal(2, shelf.GetProperty("books").GetArrayLength());
    }

    [Fact]
    public async Task Unknown_Batch_Or_Shelf_Is_404_And_A_Batch_Is_Scoped_To_Its_Shelf()
    {
        var shelfId = await NewShelfAsync();
        var otherShelf = await NewShelfAsync();
        var batch = Guid.NewGuid();
        await UploadAsync(shelfId, TestImages.Jpeg(300), batch, 1, 1);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/shelves/{shelfId}/batches/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/shelves/{otherShelf}/batches/{batch}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/shelves/{Guid.NewGuid()}/batches/{batch}")).StatusCode);
    }

    [Fact]
    public async Task A_Failed_Part_Uploaded_Again_Is_A_New_Job_And_Replaces_The_Old_One_In_The_Status()
    {
        var shelfId = await NewShelfAsync();
        var batch = Guid.NewGuid();
        var bytes = TestImages.Jpeg(500);
        var first = await UploadAsync(shelfId, bytes, batch, 1, 1);
        var failedId = first.GetProperty("jobId").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
            await db.RecognitionJobs.Where(j => j.Id == failedId).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, RecognitionJobStatus.Failed).SetProperty(j => j.Error, "boom"));
        }

        var failed = await BatchAsync(shelfId, batch);
        Assert.Equal(1, failed.GetProperty("failed").GetInt32());
        Assert.True(failed.GetProperty("done").GetBoolean());

        var retry = await UploadAsync(shelfId, bytes, batch, 1, 1);

        Assert.False(retry.GetProperty("duplicate").GetBoolean());
        Assert.NotEqual(failedId, retry.GetProperty("jobId").GetGuid());
        var after = await BatchAsync(shelfId, batch);
        Assert.Equal(1, after.GetProperty("received").GetInt32());
        Assert.Equal(0, after.GetProperty("failed").GetInt32());
        Assert.Equal(1, after.GetProperty("pending").GetInt32());
        Assert.False(after.GetProperty("done").GetBoolean());
        Assert.Equal(retry.GetProperty("jobId").GetGuid(), after.GetProperty("jobs")[0].GetProperty("jobId").GetGuid());
    }

    [Fact]
    public async Task Repeating_An_Upload_Inside_The_Batch_Returns_The_Same_Job_But_Another_Batch_Gets_Its_Own()
    {
        var shelfId = await NewShelfAsync();
        var batch = Guid.NewGuid();
        var bytes = TestImages.Jpeg(700);
        var first = await UploadAsync(shelfId, bytes, batch, 1, 2);

        var again = await UploadAsync(shelfId, bytes, batch, 1, 2);
        var elsewhere = await UploadAsync(shelfId, bytes, Guid.NewGuid(), 1, 1);

        Assert.True(again.GetProperty("duplicate").GetBoolean());
        Assert.Equal(first.GetProperty("jobId").GetGuid(), again.GetProperty("jobId").GetGuid());
        Assert.False(elsewhere.GetProperty("duplicate").GetBoolean());
        Assert.NotEqual(first.GetProperty("jobId").GetGuid(), elsewhere.GetProperty("jobId").GetGuid());
    }
}
