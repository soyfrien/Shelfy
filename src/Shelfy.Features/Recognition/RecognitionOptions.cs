namespace Shelfy.Features.Recognition;

/// <summary>Configuration section "Recognition". Placeholders live in appsettings.json; override with environment variables.</summary>
public class RecognitionOptions
{
    public const string SectionName = "Recognition";

    /// <summary>Runs the background worker inside the API process. Set false to run the API without processing.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>Total processing attempts per job before it is marked Failed.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Retry delay after attempt n is BackoffBaseSeconds * 2^(n-1), capped at BackoffMaxSeconds.</summary>
    public int BackoffBaseSeconds { get; set; } = 30;

    public int BackoffMaxSeconds { get; set; } = 900;

    /// <summary>A claimed job is reclaimable by another worker after this long (crash recovery).</summary>
    public int LeaseMinutes { get; set; } = 10;

    /// <summary>Hard cap on one processing attempt. Must be shorter than the lease.</summary>
    public int ProcessingTimeoutMinutes { get; set; } = 8;

    /// <summary>Idle polling interval of the worker.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Minimum title+author match score (0..1) for a resolver match to be accepted.</summary>
    public double MinMatchScore { get; set; } = 0.72;

    /// <summary>Upper bound on spines kept per photo (memory and API-call budget).</summary>
    public int MaxSpinesPerPhoto { get; set; } = 150;

    /// <summary>"Auto" (Azure Vision when configured, otherwise NotConfigured) or "Fake" (canned data for dev).</summary>
    public string OcrProvider { get; set; } = "Auto";
}

/// <summary>Configuration section "Storage".</summary>
public class PhotoStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>"Local" (default) or "AzureBlob" (stub, not implemented yet).</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>Root directory for the local provider. Relative paths are relative to the working directory.</summary>
    public string LocalPath { get; set; } = "data/photos";
}

/// <summary>Configuration section "AzureVision". Endpoint and Key come from the environment / Key Vault, never the repo.</summary>
public class AzureVisionOptions
{
    public const string SectionName = "AzureVision";

    public string? Endpoint { get; set; }
    public string? Key { get; set; }
    public string ApiVersion { get; set; } = "2023-10-01";
    public int TimeoutSeconds { get; set; } = 60;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Key) &&
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

/// <summary>Configuration section "Resolution".</summary>
public class ResolutionOptions
{
    public const string SectionName = "Resolution";

    public string UserAgent { get; set; } = "Shelfy/1.0 (+https://github.com/soyfrien/Shelfy)";

    /// <summary>Optional Google Books API key. Works without one at a lower quota.</summary>
    public string? GoogleBooksApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Retries after the first attempt for 429/5xx/network errors.</summary>
    public int MaxRetries { get; set; } = 2;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    public bool OpenLibraryEnabled { get; set; } = true;
    public bool GoogleBooksEnabled { get; set; } = true;
}
