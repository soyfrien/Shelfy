using System.Net;
using System.Text.Json;

namespace Shelfy.Features.Recognition.Resolution;

internal static class HttpResolverSupport
{
    /// <summary>GETs JSON. Returns null for "no result" statuses (4xx other than 403/429); throws a retryable exception for provider trouble.</summary>
    public static async Task<JsonDocument?> GetJsonAsync(HttpClient client, string relativeUrl, string provider, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new RecognitionException($"{provider} request failed: {ex.Message}", isRetryable: true, ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden || (int)response.StatusCode >= 500)
            {
                throw new RecognitionException($"{provider} returned {(int)response.StatusCode}.", isRetryable: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (JsonException ex)
            {
                throw new RecognitionException($"{provider} returned invalid JSON.", isRetryable: true, ex);
            }
        }
    }

    public static string? GetString(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static IEnumerable<JsonElement> GetArray(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : Enumerable.Empty<JsonElement>();
}
