using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Shelfy.Features.Recognition.Resolution;

namespace Shelfy.Features.Recognition.Ocr;

/// <summary>
/// Azure AI Vision Image Analysis 4.0 "read" feature over HttpClient:
/// POST {endpoint}/computervision/imageanalysis:analyze?features=read&amp;api-version=... with the image bytes.
/// The image is streamed from storage (no full copy in memory).
/// Calls are spaced by a shared <see cref="OcrThrottle"/> (free tier: about 20 per minute). A 429 is retried in place, up to
/// <c>maxInlineRetries</c> times, after the server's Retry-After (or 5 s, 10 s, ...; at most 30 s) when the image stream can
/// be rewound; other failures, and 429s that persist, are left to the job queue's retry with backoff.
/// </summary>
public class AzureVisionTextRecognizer(
    HttpClient http,
    string apiVersion,
    OcrThrottle? throttle = null,
    int maxInlineRetries = 0,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : ITextRecognizer
{
    public static readonly TimeSpan MaxInlineWait = TimeSpan.FromSeconds(30);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((t, ct) => Task.Delay(t, ct));

    public const string KeyHeader = "Ocp-Apim-Subscription-Key";

    public async Task<OcrResult> RecognizeAsync(Stream image, string contentType, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        for (var attempt = 0; ; attempt++)
        {
            if (throttle is not null)
            {
                await throttle.WaitAsync(ct);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"computervision/imageanalysis:analyze?features=read&api-version={Uri.EscapeDataString(apiVersion)}");
            request.Content = new BorrowedStreamContent(image); // the caller owns the stream; it may be sent again after a 429
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new RecognitionException($"Azure AI Vision request failed: {ex.Message}", isRetryable: true, ex);
            }

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= maxInlineRetries || !image.CanSeek)
            {
                break;
            }

            var wait = RetryDelay(response, attempt);
            response.Dispose();
            throttle?.Delay(wait);
            await _delay(wait, ct);
            image.Position = 0;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || code >= 500;
                var detail = await ReadErrorAsync(response, ct);
                var hint = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? " Check AzureVision__Key and AzureVision__Endpoint."
                    : "";
                throw new RecognitionException($"Azure AI Vision returned {code}.{hint}{detail}", retryable);
            }

            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
                return Parse(doc.RootElement);
            }
            catch (JsonException ex)
            {
                throw new RecognitionException("Azure AI Vision returned an unreadable response.", isRetryable: true, ex);
            }
        }
    }

    /// <summary>Retry-After (seconds or a date) when sent, otherwise 5 s doubling per attempt; always between 1 s and 30 s.</summary>
    internal static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var after = response.Headers.RetryAfter;
        TimeSpan wait = after?.Delta ?? (after?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(5 * Math.Pow(2, attempt)));
        if (wait < TimeSpan.FromSeconds(1))
        {
            wait = TimeSpan.FromSeconds(1);
        }

        return wait > MaxInlineWait ? MaxInlineWait : wait;
    }

    /// <summary>Maps the Image Analysis 4.0 JSON (readResult.blocks[].lines[]) to <see cref="OcrResult"/>. Public for tests.</summary>
    public static OcrResult Parse(JsonElement root)
    {
        int? width = null, height = null;
        if (root.TryGetProperty("metadata", out var meta))
        {
            width = meta.TryGetProperty("width", out var w) && w.TryGetInt32(out var wi) ? wi : null;
            height = meta.TryGetProperty("height", out var h) && h.TryGetInt32(out var hi) ? hi : null;
        }

        var lines = new List<OcrLine>();
        if (root.TryGetProperty("readResult", out var read))
        {
            foreach (var block in read.GetArray("blocks"))
            {
                foreach (var line in block.GetArray("lines"))
                {
                    var text = line.GetString("text");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var words = line.GetArray("words")
                        .Select(w => new OcrWord(
                            w.GetString("text") ?? "",
                            w.TryGetProperty("confidence", out var c) && c.TryGetDouble(out var cd) ? cd : null,
                            ParsePolygon(w)))
                        .ToList();
                    lines.Add(new OcrLine(text, ParsePolygon(line), words));
                }
            }
        }

        return new OcrResult(width, height, lines);
    }

    private static List<OcrPoint> ParsePolygon(JsonElement e) =>
        e.GetArray("boundingPolygon")
            .Where(p => p.TryGetProperty("x", out _) && p.TryGetProperty("y", out _))
            .Select(p => new OcrPoint(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble()))
            .ToList();

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var message = err.GetString("message");
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return " " + (message.Length > 300 ? message[..300] : message);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            // best-effort detail only
        }

        return "";
    }

    /// <summary>StreamContent that leaves the stream open when the request is disposed, so it can be rewound and resent.</summary>
    private sealed class BorrowedStreamContent(Stream stream) : StreamContent(stream)
    {
        protected override void Dispose(bool disposing)
        {
        }
    }
}
