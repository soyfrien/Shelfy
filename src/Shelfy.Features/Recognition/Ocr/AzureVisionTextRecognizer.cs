using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Shelfy.Features.Recognition.Resolution;

namespace Shelfy.Features.Recognition.Ocr;

/// <summary>
/// Azure AI Vision Image Analysis 4.0 "read" feature over HttpClient:
/// POST {endpoint}/computervision/imageanalysis:analyze?features=read&amp;api-version=... with the image bytes.
/// The image is streamed from storage (no full copy in memory). Retries are left to the job queue because the body cannot be replayed.
/// </summary>
public class AzureVisionTextRecognizer(HttpClient http, string apiVersion) : ITextRecognizer
{
    public const string KeyHeader = "Ocp-Apim-Subscription-Key";

    public async Task<OcrResult> RecognizeAsync(Stream image, string contentType, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"computervision/imageanalysis:analyze?features=read&api-version={Uri.EscapeDataString(apiVersion)}");
        request.Content = new StreamContent(image);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        HttpResponseMessage response;
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
}
