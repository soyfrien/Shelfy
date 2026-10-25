namespace Shelfy.Features.Recognition.Ocr;

/// <summary>
/// Deterministic recognizer for tests and local development. Returns the lines it was given and never reads the image.
/// With no arguments it returns a small canned shelf: three vertical spines (title + author each) and one noise line.
/// </summary>
public class FakeTextRecognizer : ITextRecognizer
{
    private readonly OcrResult _result;

    public FakeTextRecognizer() : this(CannedShelf())
    {
    }

    public FakeTextRecognizer(OcrResult result)
    {
        _result = result;
    }

    public int Calls { get; private set; }

    public Task<OcrResult> RecognizeAsync(Stream image, string contentType, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(_result);
    }

    /// <summary>A vertical, top-to-bottom spine line: text runs along y, thickness along x.</summary>
    public static OcrLine VerticalLine(string text, double centerX, double top, double bottom, double thickness, double confidence = 0.95) =>
        new(text,
            new OcrPoint[]
            {
                new(centerX + thickness / 2, top), new(centerX + thickness / 2, bottom),
                new(centerX - thickness / 2, bottom), new(centerX - thickness / 2, top)
            },
            new[]
            {
                new OcrWord(text, confidence, new OcrPoint[]
                {
                    new(centerX + thickness / 2, top), new(centerX + thickness / 2, bottom),
                    new(centerX - thickness / 2, bottom), new(centerX - thickness / 2, top)
                })
            });

    public static OcrResult CannedShelf() => new(1200, 800, new[]
    {
        VerticalLine("THE HOBBIT", 100, 100, 600, 40),
        VerticalLine("J.R.R. TOLKIEN", 140, 150, 450, 22),
        VerticalLine("Dune", 300, 80, 400, 44),
        VerticalLine("Frank Herbert", 340, 120, 380, 24),
        VerticalLine("Foundation", 500, 90, 500, 42),
        VerticalLine("Isaac Asimov", 540, 130, 400, 24),
        OcrLine.TextOnly("NEW YORK TIMES BESTSELLER")
    });
}
