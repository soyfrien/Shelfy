using System.Net;

namespace Shelfy.Features.Recognition.Resolution;

/// <summary>
/// Retries idempotent, body-less requests on network errors, timeouts, 429 and 5xx with exponential backoff.
/// Honors Retry-After (capped). The delay function is injectable so tests do not sleep.
/// </summary>
public class RetryHandler : DelegatingHandler
{
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(10);

    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public RetryHandler(int maxRetries, TimeSpan baseDelay, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _maxRetries = Math.Max(0, maxRetries);
        _baseDelay = baseDelay;
        _delay = delay ?? Task.Delay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt >= _maxRetries)
                {
                    return response;
                }
            }
            catch (HttpRequestException) when (attempt < _maxRetries)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < _maxRetries)
            {
                // HttpClient timeout, not caller cancellation.
            }

            var wait = response?.Headers.RetryAfter is { } ra ? RetryAfterDelay(ra) : null;
            response?.Dispose();
            await _delay(wait ?? TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt)), cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode code) =>
        code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    private static TimeSpan? RetryAfterDelay(System.Net.Http.Headers.RetryConditionHeaderValue ra)
    {
        TimeSpan? d = ra.Delta ?? (ra.Date.HasValue ? ra.Date.Value - DateTimeOffset.UtcNow : null);
        if (d is null)
        {
            return null;
        }

        return d.Value < TimeSpan.Zero ? TimeSpan.Zero : d.Value > MaxRetryAfter ? MaxRetryAfter : d.Value;
    }
}
