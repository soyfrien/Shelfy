using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shelfy.Features.Recognition.Worker;

/// <summary>
/// Polls the queue and processes one job at a time (the VM is small). Failures are recorded on the queue, which
/// schedules retries with backoff and eventually dead-letters the job. A job interrupted by shutdown is picked up
/// again once its lease expires.
/// </summary>
public class RecognitionWorker(
    IServiceScopeFactory scopes,
    IJobQueue queue,
    IOptions<RecognitionOptions> options,
    ILogger<RecognitionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
        logger.LogInformation("Recognition worker started (poll {Poll}s, max attempts {Max})", poll.TotalSeconds, options.Value.MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await RunOnceAsync(stoppingToken))
                {
                    await Task.Delay(poll, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Queue/database trouble: log and back off; never let the loop die.
                logger.LogError(ex, "Recognition worker loop error");
                try
                {
                    await Task.Delay(poll, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Claims and processes at most one job. Returns false when the queue was empty.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        var lease = await queue.ClaimNextAsync(ct);
        if (lease is null)
        {
            return false;
        }

        logger.LogInformation("Job {JobId} claimed (attempt {Attempt}/{Max})", lease.JobId, lease.Attempt, lease.MaxAttempts);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(Math.Max(1, options.Value.ProcessingTimeoutMinutes)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            using var scope = scopes.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<RecognitionJobProcessor>();
            var summary = await processor.ProcessAsync(lease.JobId, linked.Token);
            await queue.CompleteAsync(lease, CancellationToken.None);
            logger.LogInformation("Job {JobId} completed: {Spines} spines, {Resolved} resolved, {Added} books added",
                lease.JobId, summary.SpinesDetected, summary.SpinesResolved, summary.BooksAdded);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogWarning("Job {JobId} interrupted by shutdown; it will be retried after its lease expires", lease.JobId);
            throw;
        }
        catch (OperationCanceledException)
        {
            await FailAsync(lease, "Processing timed out.", retryable: true);
        }
        catch (RecognitionException ex)
        {
            await FailAsync(lease, ex.Message, ex.IsRetryable);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} failed unexpectedly", lease.JobId);
            await FailAsync(lease, $"Unexpected error: {ex.GetType().Name}: {ex.Message}", retryable: true);
        }

        return true;
    }

    private async Task FailAsync(JobLease lease, string error, bool retryable)
    {
        logger.LogWarning("Job {JobId} attempt {Attempt}/{Max} failed (retryable: {Retryable}): {Error}",
            lease.JobId, lease.Attempt, lease.MaxAttempts, retryable, error);
        await queue.FailAsync(lease, error, retryable, CancellationToken.None);
    }
}
