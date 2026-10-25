using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Infrastructure;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Api;

/// <summary>Hide / delete a shelf through the real host (ADR-010): HTTP behaviour, database rows and photo files on disk.</summary>
public class ClearShelfEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public ClearShelfEndpointTests() => _client = _factory.CreateClient();

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

    private static MultipartFormDataContent Form(byte[] bytes, Guid? batch = null)
    {
        var form = new MultipartFormDataContent();
        if (batch is not null)
        {
            form.Add(new StringContent(batch.Value.ToString()), "batchId");
            form.Add(new StringContent("1"), "part");
            form.Add(new StringContent("1"), "parts");
        }

        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "shelf.jpg");
        return form;
    }

    /// <summary>Uploads a photo and lets the worker read it, so the shelf has books, spines, a job and a file.</summary>
    private async Task<(Guid JobId, Guid BatchId)> UploadAndProcessAsync(Guid shelfId, int size = 3000)
    {
        var batch = Guid.NewGuid();
        var upload = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Jpeg(size), batch));
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        using var json = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        Assert.True(await _factory.RunWorkerOnceAsync());
        return (json.RootElement.GetProperty("jobId").GetGuid(), batch);
    }

    private string[] PhotoFiles(Guid shelfId)
    {
        var folder = Path.Combine(_factory.PhotoFolder, "shelves", shelfId.ToString("N"));
        return Directory.Exists(folder) ? Directory.GetFiles(folder) : [];
    }

    private async Task<T> DbAsync<T>(Func<ShelfyDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ShelfyDbContext>());
    }

    [Fact]
    public async Task Hide_Makes_Every_Public_Endpoint_And_The_Shelf_Page_Answer_404_But_Keeps_The_Data()
    {
        var shelfId = await NewShelfAsync();
        var (jobId, batchId) = await UploadAndProcessAsync(shelfId);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/shelves/{shelfId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/s/{shelfId}")).StatusCode);

        var hide = await _client.PostAsync($"/api/shelves/{shelfId}/hide", null);

        Assert.Equal(HttpStatusCode.NoContent, hide.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/shelves/{shelfId}")).StatusCode);
        var page = await _client.GetAsync($"/s/{shelfId}");
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.DoesNotContain("Dune", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/shelves/{shelfId}/batches/{batchId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/recognition-jobs/{jobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Jpeg(5000)))).StatusCode);

        // Nothing was erased, and the rejected upload left no file behind.
        Assert.Single(PhotoFiles(shelfId));
        Assert.Equal(2, await DbAsync(db => db.ShelfBooks.CountAsync(sb => sb.ShelfId == shelfId)));
        Assert.Equal(1, await DbAsync(db => db.RecognitionJobs.CountAsync(j => j.ShelfId == shelfId)));
        Assert.True(await DbAsync(db => db.Shelves.AnyAsync(s => s.Id == shelfId && s.IsHidden && s.HiddenAt != null)));
    }

    [Fact]
    public async Task Hide_Is_Idempotent_And_404_For_An_Unknown_Shelf()
    {
        var shelfId = await NewShelfAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"/api/shelves/{shelfId}/hide", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"/api/shelves/{shelfId}/hide", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"/api/shelves/{Guid.NewGuid()}/hide", null)).StatusCode);
    }

    [Fact]
    public async Task Delete_Removes_Rows_And_Photo_Files_But_Keeps_The_Books_Catalog()
    {
        var keep = await NewShelfAsync();
        await UploadAndProcessAsync(keep, 2000);
        var shelfId = await NewShelfAsync();
        var (jobId, batchId) = await UploadAndProcessAsync(shelfId, 4000);
        Assert.Single(PhotoFiles(shelfId));
        var booksBefore = await DbAsync(db => db.Books.CountAsync());
        Assert.Equal(2, booksBefore);

        var delete = await _client.DeleteAsync($"/api/shelves/{shelfId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/shelves/{shelfId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/s/{shelfId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/recognition-jobs/{jobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/shelves/{shelfId}/batches/{batchId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Jpeg(5000)))).StatusCode);

        Assert.Empty(PhotoFiles(shelfId));
        Assert.False(Directory.Exists(Path.Combine(_factory.PhotoFolder, "shelves", shelfId.ToString("N"))));
        Assert.False(await DbAsync(db => db.Shelves.AnyAsync(s => s.Id == shelfId)));
        Assert.False(await DbAsync(db => db.ShelfBooks.AnyAsync(sb => sb.ShelfId == shelfId)));
        Assert.False(await DbAsync(db => db.RecognitionJobs.AnyAsync(j => j.ShelfId == shelfId)));
        Assert.Equal(booksBefore, await DbAsync(db => db.Books.CountAsync()));

        // The other shelf is untouched.
        Assert.Single(PhotoFiles(keep));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/shelves/{keep}")).StatusCode);
        Assert.Equal(2, await DbAsync(db => db.ShelfBooks.CountAsync(sb => sb.ShelfId == keep)));
    }

    [Fact]
    public async Task Delete_Is_Idempotent_Even_For_An_Unknown_Shelf()
    {
        var shelfId = await NewShelfAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/shelves/{shelfId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/shelves/{shelfId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/shelves/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task A_Photo_Waiting_On_A_Hidden_Shelf_Is_Skipped_Without_Reading_It_Or_Retrying()
    {
        var shelfId = await NewShelfAsync();
        var upload = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Jpeg(3500)));
        using var json = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var jobId = json.RootElement.GetProperty("jobId").GetGuid();
        await _client.PostAsync($"/api/shelves/{shelfId}/hide", null);

        Assert.True(await _factory.RunWorkerOnceAsync());
        Assert.False(await _factory.RunWorkerOnceAsync()); // not retried

        var job = await DbAsync(db => db.RecognitionJobs.AsNoTracking().SingleAsync(j => j.Id == jobId));
        Assert.Equal(Shelfy.Domain.RecognitionJobStatus.Failed, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Contains("cleared", job.Error);
        Assert.Equal(0, await DbAsync(db => db.ShelfBooks.CountAsync(sb => sb.ShelfId == shelfId)));
        Assert.Equal(0, await DbAsync(db => db.DetectedSpines.CountAsync()));
    }

    [Fact]
    public async Task A_Photo_Waiting_On_A_Deleted_Shelf_Is_Gone_With_The_Shelf()
    {
        var shelfId = await NewShelfAsync();
        await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Jpeg(3600)));
        await _client.DeleteAsync($"/api/shelves/{shelfId}");

        Assert.False(await _factory.RunWorkerOnceAsync()); // nothing to claim
        Assert.Empty(PhotoFiles(shelfId));
    }
}
