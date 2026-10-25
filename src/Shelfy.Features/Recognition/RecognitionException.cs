namespace Shelfy.Features.Recognition;

/// <summary>A pipeline failure with an explicit retry decision. Retryable failures are retried with backoff; others fail the job at once.</summary>
public class RecognitionException : Exception
{
    public RecognitionException(string message, bool isRetryable, Exception? inner = null) : base(message, inner)
    {
        IsRetryable = isRetryable;
    }

    public bool IsRetryable { get; }
}
