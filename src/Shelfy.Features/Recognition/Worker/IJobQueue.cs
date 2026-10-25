namespace Shelfy.Features.Recognition.Worker;

/// <summary>A claim on one job. <see cref="Attempt"/> is 1 for the first processing attempt.</summary>
public record JobLease(Guid JobId, int Attempt, int MaxAttempts);

/// <summary>
/// Work queue for recognition jobs. v1 implementation is <see cref="DbJobQueue"/> (the RecognitionJobs table).
/// An Azure Service Bus implementation can replace it without touching the processor or the worker (ADR-008).
/// </summary>
public interface IJobQueue
{
    /// <summary>Called after a job row has been created. The database queue needs nothing here; a message queue publishes a message.</summary>
    Task EnqueueAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Atomically claims the next runnable job (Pending and due, or Processing with an expired lease), or returns null.</summary>
    Task<JobLease?> ClaimNextAsync(CancellationToken ct = default);

    /// <summary>Marks a claimed job Completed.</summary>
    Task CompleteAsync(JobLease lease, CancellationToken ct = default);

    /// <summary>
    /// Records a failure. A retryable failure with attempts left returns the job to Pending with exponential backoff;
    /// otherwise the job becomes Failed (dead-lettered, photo kept).
    /// </summary>
    Task FailAsync(JobLease lease, string error, bool retryable, CancellationToken ct = default);
}
