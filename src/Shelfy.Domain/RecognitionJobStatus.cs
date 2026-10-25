namespace Shelfy.Domain;

/// <summary>Lifecycle of a <see cref="RecognitionJob"/>. See docs/pipeline.md.</summary>
public enum RecognitionJobStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
