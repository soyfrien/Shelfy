using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shelfy.Domain;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Recognition.Worker;

/// <summary>
/// Queue backed by the RecognitionJobs table. Claiming is one conditional UPDATE ("claim this row only if it is still
/// claimable"), which is atomic on every relational provider, so several workers never run the same job. It is
/// deliberately not provider-specific SQL (no FOR UPDATE SKIP LOCKED): with one worker per API instance contention is
/// negligible, and a lost race just costs one more query. Lease expiry recovers jobs from crashed workers.
/// </summary>
public class DbJobQueue(IServiceScopeFactory scopes, IOptions<RecognitionOptions> options, TimeProvider clock) : IJobQueue
{
    private const int MaxClaimRaces = 5;
    private const int MaxErrorLength = 2000;

    public Task EnqueueAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask; // the Pending row is the queue entry

    public async Task<JobLease?> ClaimNextAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var lockedUntil = now.AddMinutes(opts.LeaseMinutes);

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();

        // Dead-letter jobs whose worker vanished during the final attempt.
        await db.RecognitionJobs
            .Where(j => j.Status == RecognitionJobStatus.Processing && j.LockedUntil < now && j.Attempts >= j.MaxAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, RecognitionJobStatus.Failed)
                .SetProperty(j => j.Error, "Processing was interrupted during the final attempt.")
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.CompletedAt, (DateTime?)now)
                .SetProperty(j => j.UpdatedAt, now), ct);

        var claimable = Claimable(now);
        for (var race = 0; race < MaxClaimRaces; race++)
        {
            var candidate = await db.RecognitionJobs.AsNoTracking()
                .Where(claimable)
                .OrderBy(j => j.NextAttemptAt)
                .Select(j => new { j.Id, j.Attempts, j.MaxAttempts })
                .FirstOrDefaultAsync(ct);
            if (candidate is null)
            {
                return null;
            }

            var id = candidate.Id;
            var claimed = await db.RecognitionJobs
                .Where(j => j.Id == id)
                .Where(claimable)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, RecognitionJobStatus.Processing)
                    .SetProperty(j => j.Attempts, j => j.Attempts + 1)
                    .SetProperty(j => j.LockedUntil, (DateTime?)lockedUntil)
                    .SetProperty(j => j.StartedAt, (DateTime?)now)
                    .SetProperty(j => j.UpdatedAt, now), ct);

            if (claimed == 1)
            {
                return new JobLease(id, candidate.Attempts + 1, candidate.MaxAttempts);
            }
        }

        return null;
    }

    public async Task CompleteAsync(JobLease lease, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        var id = lease.JobId;
        var attempt = lease.Attempt;
        await db.RecognitionJobs
            .Where(j => j.Id == id && j.Status == RecognitionJobStatus.Processing && j.Attempts == attempt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, RecognitionJobStatus.Completed)
                .SetProperty(j => j.Error, (string?)null)
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.CompletedAt, (DateTime?)now)
                .SetProperty(j => j.UpdatedAt, now), ct);
    }

    public async Task FailAsync(JobLease lease, string error, bool retryable, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var message = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        var id = lease.JobId;
        var attempt = lease.Attempt;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        var mine = db.RecognitionJobs.Where(j => j.Id == id && j.Status == RecognitionJobStatus.Processing && j.Attempts == attempt);

        if (retryable && attempt < lease.MaxAttempts)
        {
            var next = now + Backoff(attempt);
            await mine.ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, RecognitionJobStatus.Pending)
                .SetProperty(j => j.Error, message)
                .SetProperty(j => j.NextAttemptAt, next)
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.UpdatedAt, now), ct);
        }
        else
        {
            await mine.ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, RecognitionJobStatus.Failed)
                .SetProperty(j => j.Error, message)
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.CompletedAt, (DateTime?)now)
                .SetProperty(j => j.UpdatedAt, now), ct);
        }
    }

    /// <summary>Delay before the retry that follows the given (1-based) failed attempt.</summary>
    public TimeSpan Backoff(int failedAttempt)
    {
        var opts = options.Value;
        var seconds = opts.BackoffBaseSeconds * Math.Pow(2, Math.Max(0, failedAttempt - 1));
        return TimeSpan.FromSeconds(Math.Min(seconds, opts.BackoffMaxSeconds));
    }

    private static Expression<Func<RecognitionJob, bool>> Claimable(DateTime now) => j =>
        j.Attempts < j.MaxAttempts &&
        ((j.Status == RecognitionJobStatus.Pending && j.NextAttemptAt <= now) ||
         (j.Status == RecognitionJobStatus.Processing && j.LockedUntil < now));
}
