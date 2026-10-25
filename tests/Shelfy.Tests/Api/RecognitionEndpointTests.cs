using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Features.Recognition;
using Shelfy.Features.Recognition.Ocr;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Api;

/// <summary>A fresh host (and database) per test, so queued jobs from one test never leak into another.</summary>
public class RecognitionEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public RecognitionEndpointTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<Guid> NewShelfAsync()
    {
        var userId = await _factory.CreateUserAsync();
        var response = await _client.PostAsJsonAsync("/api/shelves", new { userId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string contentType = "image/jpeg", string field = "file")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, field, fileName);
        return form;
    }

    private async Task<JsonElement> GetJobAsync(Guid jobId)
    {
        var response = await _client.GetAsync($"/api/recognition-jobs/{jobId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Upload_Returns_202_With_A_Job_Id_And_A_Status_Url()
    {
        var shelfId = await NewShelfAsync();

        var response = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Jpeg(4000), "shelf.jpg"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var jobId = body.RootElement.GetProperty("jobId").GetGuid();
        Assert.Equal("Pending", body.RootElement.GetProperty("status").GetString());
        Assert.False(body.RootElement.GetProperty("duplicate").GetBoolean());
        Assert.EndsWith($"/api/recognition-jobs/{jobId}", response.Headers.Location!.ToString());

        var job = await GetJobAsync(jobId);
        Assert.Equal("Pending", job.GetProperty("status").GetString());
        Assert.Equal(shelfId, job.GetProperty("shelfId").GetGuid());
        Assert.Equal(0, job.GetProperty("attempts").GetInt32());
        Assert.Equal(0, job.GetProperty("retryCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("error").ValueKind);
        Assert.Equal(0, job.GetProperty("spines").GetArrayLength());

        Assert.Single(Directory.GetFiles(_factory.PhotoFolder, "*.jpg", SearchOption.AllDirectories).Where(f => f.Contains(shelfId.ToString("N"))));
    }

    [Fact]
    public async Task Uploading_The_Same_Photo_Again_Returns_The_Same_Job()
    {
        var shelfId = await NewShelfAsync();
        var bytes = TestImages.Jpeg(1234);

        var first = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(bytes, "a.jpg"));
        var retry = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(bytes, "a.jpg"));

        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        using var a = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var b = JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
        Assert.Equal(a.RootElement.GetProperty("jobId").GetGuid(), b.RootElement.GetProperty("jobId").GetGuid());
        Assert.True(b.RootElement.GetProperty("duplicate").GetBoolean());
    }

    [Fact]
    public async Task Upload_Rejects_A_Text_File_Renamed_To_Jpg_With_415()
    {
        var shelfId = await NewShelfAsync();

        var response = await _client.PostAsync($"/api/shelves/{shelfId}/photos",
            Form(System.Text.Encoding.UTF8.GetBytes("<html>not an image</html>"), "shelf.jpg"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Contains("JPEG and PNG", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_Rejects_A_File_Over_20_MB_With_413()
    {
        var shelfId = await NewShelfAsync();
        var bytes = TestImages.Jpeg(20 * 1024 * 1024); // header + 20 MB > limit

        var response = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(bytes, "huge.jpg"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("20 MB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_Requires_A_File_Field()
    {
        var shelfId = await NewShelfAsync();

        var response = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Jpeg(), "a.jpg", field: "wrong"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    public async Task Upload_Without_A_Multipart_Body_Is_415_Not_A_Server_Error(string body)
    {
        var shelfId = await NewShelfAsync();
        using var content = body.Length == 0 ? null : new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/shelves/{shelfId}/photos", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        if (body.Length == 0)
        {
            // With a wrong Content-Type routing already answers 415 (the endpoint only consumes multipart/form-data);
            // with none, the handler explains what to send.
            Assert.Contains("multipart/form-data", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Upload_To_An_Unknown_Shelf_Is_404()
    {
        var response = await _client.PostAsync($"/api/shelves/{Guid.NewGuid()}/photos", Form(TestImages.Jpeg(), "a.jpg"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_Job_Is_404()
    {
        var response = await _client.GetAsync($"/api/recognition-jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_Photo_Goes_From_Upload_To_A_Recognized_Shelf()
    {
        var shelfId = await NewShelfAsync();
        var upload = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Png(9000), "shelf.png", "image/png"));
        using var accepted = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var jobId = accepted.RootElement.GetProperty("jobId").GetGuid();

        Assert.True(await _factory.RunWorkerOnceAsync());

        var job = await GetJobAsync(jobId);
        Assert.Equal("Completed", job.GetProperty("status").GetString());
        Assert.Equal(1, job.GetProperty("attempts").GetInt32());
        Assert.Equal(2, job.GetProperty("booksAdded").GetInt32());

        var spines = job.GetProperty("spines").EnumerateArray().ToList();
        Assert.Equal(3, spines.Count);
        Assert.Equal("The Hobbit", spines[0].GetProperty("candidateTitle").GetString());
        Assert.Equal("Resolved", spines[0].GetProperty("status").GetString());
        Assert.Equal("9780547928227", spines[0].GetProperty("book").GetProperty("isbn").GetString());
        Assert.Equal("Foundation", spines[2].GetProperty("candidateTitle").GetString());
        Assert.Equal("Unresolved", spines[2].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, spines[2].GetProperty("book").ValueKind);
        Assert.Equal(new[] { "Dune", "The Hobbit" },
            job.GetProperty("books").EnumerateArray().Select(b => b.GetProperty("title").GetString()!).OrderBy(t => t));

        var shelf = JsonDocument.Parse(await _client.GetStringAsync($"/api/shelves/{shelfId}")).RootElement;
        var titles = shelf.GetProperty("books").EnumerateArray().Select(b => b.GetProperty("title").GetString()).OrderBy(t => t).ToList();
        Assert.Equal(new[] { "Dune", "The Hobbit" }, titles);
    }

    [Fact]
    public async Task Failures_Are_Visible_On_The_Job()
    {
        var shelfId = await NewShelfAsync();
        var upload = await _client.PostAsync($"/api/shelves/{shelfId}/photos", Form(TestImages.Jpeg(777), "shelf.jpg"));
        using var accepted = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var jobId = accepted.RootElement.GetProperty("jobId").GetGuid();
        _factory.Resolver.ThrowOnResolve = new RecognitionException("OpenLibrary returned 503.", isRetryable: true);
        try
        {
            await _factory.RunWorkerOnceAsync();
        }
        finally
        {
            _factory.Resolver.ThrowOnResolve = null;
        }

        var job = await GetJobAsync(jobId);
        Assert.Equal("Pending", job.GetProperty("status").GetString());
        Assert.Equal(1, job.GetProperty("attempts").GetInt32());
        Assert.Contains("503", job.GetProperty("error").GetString());
        Assert.NotEqual(JsonValueKind.Null, job.GetProperty("nextAttemptAt").ValueKind);
    }
}

public class RecognitionConfigurationTests
{
    private static async Task<ITextRecognizer> RecognizerAsync(Dictionary<string, string?> config)
    {
        await using var factory = new ApiFactory(config);
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITextRecognizer>();
    }

    [Fact]
    public async Task Without_Azure_Credentials_The_Recognizer_Is_NotConfigured()
    {
        var recognizer = await RecognizerAsync(new() { ["Recognition:OcrProvider"] = "Auto" });

        Assert.IsType<NotConfiguredTextRecognizer>(recognizer);
    }

    [Fact]
    public async Task With_Azure_Credentials_The_Recognizer_Is_Azure_Vision()
    {
        var recognizer = await RecognizerAsync(new()
        {
            ["Recognition:OcrProvider"] = "Auto",
            ["AzureVision:Endpoint"] = "https://example-vision.cognitiveservices.azure.com",
            ["AzureVision:Key"] = "not-a-real-key"
        });

        Assert.IsType<AzureVisionTextRecognizer>(recognizer);
    }

    [Fact]
    public async Task Fake_Provider_Is_Opt_In()
    {
        Assert.IsType<FakeTextRecognizer>(await RecognizerAsync(new() { ["Recognition:OcrProvider"] = "Fake" }));
    }

    [Fact]
    public async Task The_Worker_Is_Only_Registered_When_Enabled()
    {
        await using var disabled = new ApiFactory();
        Assert.DoesNotContain(disabled.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            s => s is Shelfy.Features.Recognition.Worker.RecognitionWorker);

        await using var enabled = new ApiFactory(new() { ["Recognition:WorkerEnabled"] = "true" });
        Assert.Contains(enabled.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            s => s is Shelfy.Features.Recognition.Worker.RecognitionWorker);
    }
}
