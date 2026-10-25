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
