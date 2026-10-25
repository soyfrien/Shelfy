namespace Shelfy.Features.Recognition.Ocr;

/// <summary>A point in image pixels (origin top-left, y grows downwards).</summary>
public record OcrPoint(double X, double Y);

/// <summary>A recognized word. The polygon lists corners in reading order of the text: top-left, top-right, bottom-right, bottom-left.</summary>
public record OcrWord(string Text, double? Confidence, IReadOnlyList<OcrPoint> Polygon);

/// <summary>A recognized line of text. Polygon and Words may be empty when the engine gives no geometry.</summary>
public record OcrLine(string Text, IReadOnlyList<OcrPoint> Polygon, IReadOnlyList<OcrWord> Words)
{
    public static OcrLine TextOnly(string text) => new(text, Array.Empty<OcrPoint>(), Array.Empty<OcrWord>());
}

public record OcrResult(int? ImageWidth, int? ImageHeight, IReadOnlyList<OcrLine> Lines);
