namespace Shelfy.Features.Recognition.Ocr;

/// <summary>
/// Spaces out calls to the OCR service across the whole process so the Azure AI Vision free tier (about 20 calls per
/// minute) is not exceeded when many tiles are queued. One instance is shared by all jobs (register as singleton).
/// A 429 answer can push the next call further back with <see cref="Delay"/>.
/// </summary>
public class OcrThrottle(TimeSpan minInterval, TimeProvider? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
    private readonly object _lock = new();
    private DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;

    public TimeSpan MinInterval { get; } = minInterval < TimeSpan.Zero ? TimeSpan.Zero : minInterval;

    /// <summary>Waits until a call may start, then reserves the slot.</summary>
    public async Task WaitAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            while (true)
            {
                TimeSpan wait;
                lock (_lock)
                {
                    wait = _nextAllowed - _clock.GetUtcNow();
                    if (wait <= TimeSpan.Zero)
                    {
                        _nextAllowed = _clock.GetUtcNow() + MinInterval;
                        return;
                    }
                }

                await _delay(wait, ct); // a 429 may have pushed the slot back meanwhile: check again
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Makes the next call wait at least this long from now (used after a 429).</summary>
    public void Delay(TimeSpan by)
    {
        var until = _clock.GetUtcNow() + by;
        lock (_lock)
        {
            if (until > _nextAllowed)
            {
                _nextAllowed = until;
            }
        }
    }
}
