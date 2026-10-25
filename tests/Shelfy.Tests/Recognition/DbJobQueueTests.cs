using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Recognition.Worker;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Recognition;

/// <summary>Runs the real claim/complete/fail SQL (ExecuteUpdate) against in-memory SQLite. PostgreSQL itself is not exercised.</summary>
public class DbJobQueueTests
{
    private static async Task<(PipelineHarness H, Guid JobId)> SetupAsync()
    {
        var h = new PipelineHarness().Build();
        var (_, jobId) = await h.SeedJobAsync();
        return (h, jobId);
    }

    [Fact]
    public async Task Claims_A_Pending_Job_And_Marks_It_Processing()
    {
        var (h, jobId) = await SetupAsync();

        var lease = await h.Queue.ClaimNextAsync();

        Assert.Equal(new JobLease(jobId, 1, 3), lease);
        var job = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Processing, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(h.Clock.GetUtcNow().UtcDateTime.AddMinutes(h.Options.LeaseMinutes), job.LockedUntil);
        Assert.NotNull(job.StartedAt);
    }

    [Fact]
    public async Task Returns_Null_When_There_Is_Nothing_To_Do()
    {
        using var h = new PipelineHarness().Build();

        Assert.Null(await h.Queue.ClaimNextAsync());
    }

    [Fact]
    public async Task A_Claimed_Job_Cannot_Be_Claimed_Twice()
    {
        var (h, _) = await SetupAsync();

        Assert.NotNull(await h.Queue.ClaimNextAsync());
        Assert.Null(await h.Queue.ClaimNextAsync());
    }

    [Fact]
    public async Task Concurrent_Claims_Of_One_Job_Yield_Exactly_One_Winner()
    {
        var (h, _) = await SetupAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => h.Queue.ClaimNextAsync())));

        Assert.Single(results, r => r is not null);
    }

    [Fact]
    public async Task Claims_The_Oldest_Due_Job_First()
    {
        var h = new PipelineHarness().Build();
        var (shelfId, first) = await h.SeedJobAsync();
        h.Clock.Advance(TimeSpan.FromSeconds(10));
        var (_, second) = await h.SeedJobAsync(shelfId);

        var a = await h.Queue.ClaimNextAsync();
        var b = await h.Queue.ClaimNextAsync();

        Assert.Equal(first, a!.JobId);
        Assert.Equal(second, b!.JobId);
    }

    [Fact]
    public async Task Does_Not_Claim_A_Job_Before_Its_Retry_Time()
    {
        var (h, _) = await SetupAsync();
        var lease = (await h.Queue.ClaimNextAsync())!;
        await h.Queue.FailAsync(lease, "temporary", retryable: true);

        Assert.Null(await h.Queue.ClaimNextAsync());

        h.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Null(await h.Queue.ClaimNextAsync());

        h.Clock.Advance(TimeSpan.FromSeconds(2));
        var again = await h.Queue.ClaimNextAsync();
        Assert.Equal(2, again!.Attempt);
    }

    [Fact]
    public async Task Retryable_Failure_Goes_Back_To_Pending_With_Exponential_Backoff()
    {
        var (h, jobId) = await SetupAsync();

        var lease1 = (await h.Queue.ClaimNextAsync())!;
        await h.Queue.FailAsync(lease1, "first", retryable: true);
        var afterFirst = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Pending, afterFirst.Status);
        Assert.Equal("first", afterFirst.Error);
        Assert.Equal(h.Clock.GetUtcNow().UtcDateTime.AddSeconds(30), afterFirst.NextAttemptAt);
        Assert.Null(afterFirst.LockedUntil);

        h.Clock.Advance(TimeSpan.FromSeconds(30));
        var lease2 = (await h.Queue.ClaimNextAsync())!;
        await h.Queue.FailAsync(lease2, "second", retryable: true);
        var afterSecond = await h.GetJobAsync(jobId);
        Assert.Equal(h.Clock.GetUtcNow().UtcDateTime.AddSeconds(60), afterSecond.NextAttemptAt);
    }

    [Fact]
    public async Task Backoff_Is_Capped()
    {
        var (h, _) = await SetupAsync();
        var queue = (DbJobQueue)h.Queue;

        Assert.Equal(TimeSpan.FromSeconds(30), queue.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(120), queue.Backoff(3));
        Assert.Equal(TimeSpan.FromSeconds(600), queue.Backoff(10));
    }

    [Fact]
    public async Task Fails_For_Good_After_The_Last_Attempt_And_Keeps_The_Photo()
    {
        var (h, jobId) = await SetupAsync();
        var photoKey = (await h.GetJobAsync(jobId)).PhotoKey;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            h.Clock.Advance(TimeSpan.FromHours(1));
            var lease = (await h.Queue.ClaimNextAsync())!;
            Assert.Equal(attempt, lease.Attempt);
            await h.Queue.FailAsync(lease, $"boom {attempt}", retryable: true);
        }

        var job = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Failed, job.Status);
        Assert.Equal("boom 3", job.Error);
        Assert.Equal(3, job.Attempts);
        Assert.NotNull(job.CompletedAt);
        Assert.True(h.Storage.Files.ContainsKey(photoKey));
        h.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(await h.Queue.ClaimNextAsync());
    }

    [Fact]
    public async Task Permanent_Failure_Fails_Immediately()
    {
        var (h, jobId) = await SetupAsync();
        var lease = (await h.Queue.ClaimNextAsync())!;

        await h.Queue.FailAsync(lease, "bad config", retryable: false);

        var job = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Failed, job.Status);
        Assert.Equal(1, job.Attempts);
    }

    [Fact]
    public async Task Complete_Marks_The_Job_Completed_And_Clears_The_Error()
    {
        var (h, jobId) = await SetupAsync();
        var lease1 = (await h.Queue.ClaimNextAsync())!;
        await h.Queue.FailAsync(lease1, "temporary", retryable: true);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var lease2 = (await h.Queue.ClaimNextAsync())!;

        await h.Queue.CompleteAsync(lease2);

        var job = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Completed, job.Status);
        Assert.Null(job.Error);
        Assert.NotNull(job.CompletedAt);
        Assert.Null(job.LockedUntil);
    }

    [Fact]
    public async Task A_Job_Abandoned_By_A_Crashed_Worker_Is_Reclaimed_After_The_Lease_Expires()
    {
        var (h, jobId) = await SetupAsync();
        await h.Queue.ClaimNextAsync(); // worker "crashes" here

        h.Clock.Advance(TimeSpan.FromMinutes(h.Options.LeaseMinutes - 1));
        Assert.Null(await h.Queue.ClaimNextAsync());

        h.Clock.Advance(TimeSpan.FromMinutes(2));
        var lease = await h.Queue.ClaimNextAsync();
        Assert.Equal(jobId, lease!.JobId);
        Assert.Equal(2, lease.Attempt);
    }

    [Fact]
    public async Task A_Job_Abandoned_During_Its_Final_Attempt_Is_Dead_Lettered()
    {
        var (h, jobId) = await SetupAsync();
        for (var i = 0; i < 3; i++)
        {
            await h.Queue.ClaimNextAsync();
            h.Clock.Advance(TimeSpan.FromMinutes(h.Options.LeaseMinutes + 1));
        }

        Assert.Null(await h.Queue.ClaimNextAsync());

        var job = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Failed, job.Status);
        Assert.Contains("interrupted", job.Error);
    }

    [Fact]
    public async Task A_Stale_Worker_Cannot_Overwrite_The_Result_Of_The_Worker_That_Took_Over()
    {
        var (h, jobId) = await SetupAsync();
        var stale = (await h.Queue.ClaimNextAsync())!;
        h.Clock.Advance(TimeSpan.FromMinutes(h.Options.LeaseMinutes + 1));
        var current = (await h.Queue.ClaimNextAsync())!;

        await h.Queue.FailAsync(stale, "late failure from the old worker", retryable: false);
        await h.Queue.CompleteAsync(stale);

        var job = await h.GetJobAsync(jobId);
        Assert.Equal(RecognitionJobStatus.Processing, job.Status);
        Assert.Null(job.Error);

        await h.Queue.CompleteAsync(current);
        Assert.Equal(RecognitionJobStatus.Completed, (await h.GetJobAsync(jobId)).Status);
    }

    [Fact]
    public async Task Truncates_Very_Long_Errors()
    {
        var (h, jobId) = await SetupAsync();
        var lease = (await h.Queue.ClaimNextAsync())!;

        await h.Queue.FailAsync(lease, new string('e', 5000), retryable: false);

        Assert.Equal(2000, (await h.GetJobAsync(jobId)).Error!.Length);
    }
}
