namespace Shelfy.Features.Recognition.Ocr;

/// <summary>Registered when no OCR provider is configured. Fails the job (without retries) with an actionable message.</summary>
public class NotConfiguredTextRecognizer : ITextRecognizer
{
    public const string Message =
        "No OCR provider is configured. Set AzureVision__Endpoint and AzureVision__Key (Azure AI Vision) " +
        "or, for development only, Recognition__OcrProvider=Fake. The photo is kept; re-upload it after configuring OCR.";

    public Task<OcrResult> RecognizeAsync(Stream image, string contentType, CancellationToken ct = default) =>
        throw new RecognitionException(Message, isRetryable: false);
}
