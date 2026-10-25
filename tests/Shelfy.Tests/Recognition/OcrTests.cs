using System.Net;
using Shelfy.Features.Recognition;
using Shelfy.Features.Recognition.Ocr;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Recognition;

public class OcrTests
{
    private static AzureVisionTextRecognizer Recognizer(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example-vision.cognitiveservices.azure.com/") }, "2023-10-01");

    [Fact]
    public async Task Azure_Vision_Posts_The_Image_To_The_Read_Endpoint_And_Parses_Lines_With_Polygons()
    {
        var stub = StubHandler.Always(Fixtures.Read("AzureVision/read-vertical-spines.json"));
        using var image = new MemoryStream(TestImages.Jpeg(1000));

        var result = await Recognizer(stub).RecognizeAsync(image, "image/jpeg");

        var request = stub.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/computervision/imageanalysis:analyze", request.RequestUri!.AbsolutePath);
        Assert.Contains("features=read", request.RequestUri.Query);
        Assert.Contains("api-version=2023-10-01", request.RequestUri.Query);
        Assert.Equal("1010 bytes", stub.Bodies.Single());

        Assert.Equal(900, result.ImageWidth);
        Assert.Equal(5, result.Lines.Count);
        var hobbit = result.Lines[0];
        Assert.Equal("THE HOBBIT", hobbit.Text);
        Assert.Equal(4, hobbit.Polygon.Count);
        Assert.Equal(2, hobbit.Words.Count);
        Assert.Equal(0.98, hobbit.Words[0].Confidence);
    }

    [Fact]
    public async Task Azure_Vision_Unauthorized_Is_A_Permanent_Failure_With_A_Config_Hint()
    {
        var stub = StubHandler.Always("{\"error\":{\"code\":\"401\",\"message\":\"Access denied due to invalid subscription key.\"}}", HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<RecognitionException>(() => Recognizer(stub).RecognizeAsync(new MemoryStream(TestImages.Jpeg()), "image/jpeg"));

        Assert.False(ex.IsRetryable);
        Assert.Contains("AzureVision__Key", ex.Message);
        Assert.Contains("invalid subscription key", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Azure_Vision_Throttling_And_Server_Errors_Are_Retryable(HttpStatusCode code)
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(code));

        var ex = await Assert.ThrowsAsync<RecognitionException>(() => Recognizer(stub).RecognizeAsync(new MemoryStream(TestImages.Jpeg()), "image/jpeg"));

        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public async Task Azure_Vision_Bad_Request_Is_Permanent()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<RecognitionException>(() => Recognizer(stub).RecognizeAsync(new MemoryStream(TestImages.Jpeg()), "image/jpeg"));

        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public async Task Azure_Vision_Network_Failure_Is_Retryable()
    {
        var stub = new StubHandler(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<RecognitionException>(() => Recognizer(stub).RecognizeAsync(new MemoryStream(TestImages.Jpeg()), "image/jpeg"));

        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public async Task Azure_Vision_Response_Without_Text_Gives_No_Lines()
    {
        var result = await Recognizer(StubHandler.Always("{\"modelVersion\":\"x\",\"metadata\":{\"width\":10,\"height\":10},\"readResult\":{\"blocks\":[]}}"))
            .RecognizeAsync(new MemoryStream(TestImages.Jpeg()), "image/jpeg");

        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task NotConfigured_Recognizer_Fails_Permanently_With_A_Helpful_Message()
    {
        var ex = await Assert.ThrowsAsync<RecognitionException>(() =>
            new NotConfiguredTextRecognizer().RecognizeAsync(Stream.Null, "image/jpeg"));

        Assert.False(ex.IsRetryable);
        Assert.Contains("AzureVision__Endpoint", ex.Message);
        Assert.Contains("AzureVision__Key", ex.Message);
    }

    [Fact]
    public async Task Fake_Recognizer_Returns_Its_Canned_Result_And_Counts_Calls()
    {
        var fake = new FakeTextRecognizer();

        var result = await fake.RecognizeAsync(Stream.Null, "image/png");

        Assert.Equal(7, result.Lines.Count);
        Assert.Equal(1, fake.Calls);
    }

    [Theory]
    [InlineData(null, "key", false)]
    [InlineData("", "key", false)]
    [InlineData("https://x.cognitiveservices.azure.com", null, false)]
    [InlineData("http://insecure.example.com", "key", false)]
    [InlineData("not a url", "key", false)]
    [InlineData("https://x.cognitiveservices.azure.com", "key", true)]
    public void Azure_Vision_Options_Require_An_Https_Endpoint_And_A_Key(string? endpoint, string? key, bool expected) =>
        Assert.Equal(expected, new AzureVisionOptions { Endpoint = endpoint, Key = key }.IsConfigured);
}

public class OcrThrottlingTests
{
    private const string OkJson = "{\"metadata\":{\"width\":10,\"height\":10},\"readResult\":{\"blocks\":[]}}";

    private static HttpResponseMessage TooMany(int? retryAfterSeconds = null)
    {
        var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (retryAfterSeconds is { } s)
        {
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(s));
        }

        return r;
    }

    private static AzureVisionTextRecognizer Recognizer(StubHandler handler, int inlineRetries, List<TimeSpan> waits, OcrThrottle? throttle = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example-vision.cognitiveservices.azure.com/") }, "2023-10-01",
            throttle, inlineRetries, (t, _) => { waits.Add(t); return Task.CompletedTask; });

    [Fact]
    public async Task A_429_Is_Retried_In_Place_After_Retry_After_With_The_Whole_Image_Resent()
    {
        var stub = StubHandler.Sequence(() => TooMany(7), () => TooMany(), () => StubHandler.Json(OkJson));
        var waits = new List<TimeSpan>();
        using var image = new MemoryStream(TestImages.Jpeg(1000));

        var result = await Recognizer(stub, 2, waits).RecognizeAsync(image, "image/jpeg");

        Assert.Empty(result.Lines);
        Assert.Equal(3, stub.Requests.Count);
        Assert.Equal(new[] { "1010 bytes", "1010 bytes", "1010 bytes" }, stub.Bodies);   // same bytes every time
        Assert.Equal(new[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(10) }, waits);  // header, then 5 s * 2^1 without one
    }

    [Fact]
    public async Task A_Long_Retry_After_Is_Capped()
    {
        var stub = StubHandler.Sequence(() => TooMany(600), () => StubHandler.Json(OkJson));
        var waits = new List<TimeSpan>();

        await Recognizer(stub, 1, waits).RecognizeAsync(new MemoryStream(TestImages.Jpeg()), "image/jpeg");

        Assert.Equal(new[] { TimeSpan.FromSeconds(30) }, waits);
    }

    [Fact]
    public async Task A_Persistent_429_Ends_As_A_Retryable_Failure_For_The_Queue_After_The_Inline_Retries()
    {
        var stub = new StubHandler(_ => TooMany(1));
        var waits = new List<TimeSpan>();

        var ex = await Assert.ThrowsAsync<RecognitionException>(() =>
            Recognizer(stub, 2, waits).RecognizeAsync(new MemoryStream(TestImages.Jpeg()), "image/jpeg"));

        Assert.True(ex.IsRetryable);
        Assert.Equal(3, stub.Requests.Count);
        Assert.Equal(2, waits.Count);
    }

    [Fact]
    public async Task A_Stream_That_Cannot_Be_Rewound_Is_Left_To_The_Queue()
    {
        var stub = new StubHandler(_ => TooMany(1));
        var waits = new List<TimeSpan>();
        using var inner = new MemoryStream(TestImages.Jpeg());
        using var forward = new ForwardOnlyStream(inner);

        var ex = await Assert.ThrowsAsync<RecognitionException>(() => Recognizer(stub, 2, waits).RecognizeAsync(forward, "image/jpeg"));

        Assert.True(ex.IsRetryable);
        Assert.Single(stub.Requests);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task The_Throttle_Spaces_Calls_And_A_429_Pushes_The_Next_Call_Back()
    {
        var clock = new TestClock();
        var waits = new List<TimeSpan>();
        var throttle = new OcrThrottle(TimeSpan.FromMilliseconds(3200), clock, (t, _) => { waits.Add(t); clock.Advance(t); return Task.CompletedTask; });

        await throttle.WaitAsync();                       // first call: immediate
        await throttle.WaitAsync();                       // second: waits the full interval
        clock.Advance(TimeSpan.FromSeconds(1));
        await throttle.WaitAsync();                       // third: only the rest of the interval
        throttle.Delay(TimeSpan.FromSeconds(10));
        await throttle.WaitAsync();                       // after a 429: waits for the pushed-back slot

        Assert.Equal(new[]
        {
            TimeSpan.FromMilliseconds(3200), TimeSpan.FromMilliseconds(2200), TimeSpan.FromSeconds(10)
        }, waits);
    }

    [Fact]
    public async Task The_Recognizer_Waits_For_The_Throttle_Before_Every_Call()
    {
        var clock = new TestClock();
        var waits = new List<TimeSpan>();
        var throttle = new OcrThrottle(TimeSpan.FromSeconds(3), clock, (t, _) => { waits.Add(t); clock.Advance(t); return Task.CompletedTask; });
        var stub = StubHandler.Always(OkJson);
        var recognizer = Recognizer(stub, 0, new List<TimeSpan>(), throttle);

        for (var i = 0; i < 3; i++)
        {
            await recognizer.RecognizeAsync(new MemoryStream(TestImages.Jpeg()), "image/jpeg");
        }

        Assert.Equal(3, stub.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3) }, waits);
    }

    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
