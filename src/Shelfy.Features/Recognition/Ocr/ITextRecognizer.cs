namespace Shelfy.Features.Recognition.Ocr;

/// <summary>Reads text from a shelf photo (ADR-007). Implementations: Azure AI Vision, NotConfigured, Fake.</summary>
public interface ITextRecognizer
{
    /// <summary>Recognizes text in an image. The stream is read once and not disposed.</summary>
    /// <exception cref="RecognitionException">When recognition fails; <see cref="RecognitionException.IsRetryable"/> says whether trying again may help.</exception>
    Task<OcrResult> RecognizeAsync(Stream image, string contentType, CancellationToken ct = default);
}
