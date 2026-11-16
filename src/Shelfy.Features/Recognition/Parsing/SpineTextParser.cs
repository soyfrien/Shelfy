using System.Text.RegularExpressions;
using Shelfy.Features.Recognition.Ocr;
using Shelfy.Domain;

namespace Shelfy.Features.Recognition.Parsing;

/// <summary>
/// Turns OCR lines of a shelf photo into one candidate (title, author) per book spine. Pure and deterministic.
/// <para>
/// Spines are mostly vertical text. Each line gets an orientation and a text "thickness" (font height) from its polygon.
/// Lines of the same orientation are grouped into one spine when they overlap along the text axis and sit next to each
/// other across it (a title and an author printed side by side on one spine), or stack in one column.
/// Within a spine the thickest line is the title; an author-looking line is the author. Lines without geometry become
/// one candidate each. This is a heuristic: mixed-up groupings are expected on busy shelves, and the resolver is
/// tolerant because it scores title and author fuzzily (see docs/pipeline.md).
/// </para>
/// </summary>
public static partial class SpineTextParser
{
    public const int DefaultMaxSpines = 150;
    public const int MaxRawTextLength = 1000;

    private const double MinAlongOverlap = 0.4;
    private const double MaxEdgeGapOfThickness = 0.6;
    private const double SameColumnOfThickness = 0.5;
    private const double MaxStackGapOfThickness = 3.0;
    private const double TitleContinuationRatio = 0.8;
    private const double AuthorFallbackRatio = 0.4;

    private static readonly HashSet<string> NoisePhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "a novel", "a memoir", "the new york times bestseller", "new york times bestseller", "bestseller",
        "international bestseller", "national bestseller", "#1 bestseller", "penguin classics", "penguin books",
        "vintage", "vintage books", "harpercollins", "random house", "simon & schuster", "oxford world's classics",
        "bantam", "ballantine", "tor", "tor books", "orbit", "del rey", "faber", "puffin", "scholastic", "doubleday",
        "knopf", "pocket books", "macmillan", "norton", "w. w. norton"
    };

    [GeneratedRegex(@"\b(bestsell(er|ing)|new york times|award|winner|pulitzer|booker|national book)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseWords();

    [GeneratedRegex(@"^(?<title>.+?)\s+by\s+(?<author>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ByPattern();

    [GeneratedRegex(@"^(?<left>.+?)\s+[-\u2013\u2014]\s+(?<right>.+)$")]
    private static partial Regex DashPattern();

    public static IReadOnlyList<SpineCandidate> Parse(OcrResult ocr, int maxSpines = DefaultMaxSpines)
    {
        ArgumentNullException.ThrowIfNull(ocr);

        var geometric = new List<LineInfo>();
        var plain = new List<LineInfo>();
        foreach (var line in ocr.Lines)
        {
            var info = LineInfo.From(line);
            if (info is null)
            {
                continue;
            }

            (info.HasGeometry ? geometric : plain).Add(info);
        }

        var results = new List<SpineCandidate>();
        foreach (var cluster in Cluster(geometric).OrderBy(c => c.Average(l => l.Cx)).ThenBy(c => c.Average(l => l.Cy)))
        {
            var candidate = BuildCandidate(cluster, results.Count);
            if (candidate is not null)
            {
                results.Add(candidate);
            }
        }

        foreach (var line in plain)
        {
            var candidate = BuildCandidate(new List<LineInfo> { line }, results.Count);
            if (candidate is not null)
            {
                results.Add(candidate);
            }
        }

        return results.Take(Math.Max(0, maxSpines)).ToList();
    }

    private static bool IsNoise(string cleaned)
    {
        if (cleaned.Length == 0 || TextNormalizer.LetterCount(cleaned) < 2)
        {
            return true;
        }

        var lowered = cleaned.ToLowerInvariant();
        return NoisePhrases.Contains(lowered) || NoiseWords().IsMatch(lowered);
    }

    private static List<List<LineInfo>> Cluster(List<LineInfo> lines)
    {
        var parent = Enumerable.Range(0, lines.Count).ToArray();
        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (SameSpine(lines[i], lines[j]))
                {
                    parent[Find(i)] = Find(j);
                }
            }
        }

        return lines.Select((l, i) => (l, root: Find(i))).GroupBy(x => x.root).Select(g => g.Select(x => x.l).ToList()).ToList();
    }

    private static bool SameSpine(LineInfo a, LineInfo b)
    {
        if (a.Orientation != b.Orientation)
        {
            return false;
        }

        var minThickness = Math.Min(a.Thickness, b.Thickness);
        var maxThickness = Math.Max(a.Thickness, b.Thickness);
        var acrossDistance = Math.Abs(a.AcrossCenter - b.AcrossCenter);

        var overlap = Math.Min(a.AlongEnd, b.AlongEnd) - Math.Max(a.AlongStart, b.AlongStart);
        var shorter = Math.Min(a.AlongEnd - a.AlongStart, b.AlongEnd - b.AlongStart);
        if (overlap > 0 && shorter > 0 && overlap / shorter >= MinAlongOverlap)
        {
            var edgeGap = acrossDistance - (a.Thickness + b.Thickness) / 2;
            if (edgeGap <= MaxEdgeGapOfThickness * minThickness)
            {
                return true;
            }
        }

        // Same column, disjoint along the text axis: for example a title and a publisher line at the other end of the spine.
        if (acrossDistance <= SameColumnOfThickness * maxThickness)
        {
            var alongGap = Math.Max(a.AlongStart, b.AlongStart) - Math.Min(a.AlongEnd, b.AlongEnd);
            return alongGap <= MaxStackGapOfThickness * maxThickness;
        }

        return false;
    }

    private static SpineCandidate? BuildCandidate(List<LineInfo> cluster, int position)
    {
        var ordered = cluster.OrderBy(l => l.ReadingOrderKey).ThenBy(l => l.AlongStart).ToList();
        var rawText = string.Join(" | ", ordered.Select(l => l.Raw));
        if (rawText.Length > MaxRawTextLength)
        {
            rawText = rawText[..MaxRawTextLength];
        }

        string? isbn = null;
        var textLines = new List<LineInfo>();
        foreach (var line in ordered)
        {
            var found = Isbn.FindIn(line.Cleaned);
            if (found is not null)
            {
                isbn ??= found;
                continue;
            }

            if (!IsNoise(line.Cleaned))
            {
                textLines.Add(line);
            }
        }

        if (textLines.Count == 0 && isbn is null)
        {
            return null;
        }

        string title = "";
        string? author = null;
        List<LineInfo> used = new();

        if (textLines.Count > 0)
        {
            var primary = textLines.OrderByDescending(l => l.Thickness).ThenByDescending(l => l.Length).First();
            var others = textLines.Where(l => !ReferenceEquals(l, primary)).OrderByDescending(l => l.Thickness).ToList();
            var titleLines = new List<LineInfo> { primary };
            LineInfo? authorLine = null;

            // A big author name over a smaller non-name title: swap.
            if (TextNormalizer.LooksLikeAuthorName(primary.Cleaned))
            {
                var nonName = others.FirstOrDefault(l => !TextNormalizer.LooksLikeAuthorName(l.Cleaned));
                if (nonName is not null && !others.Any(l => TextNormalizer.LooksLikeAuthorName(l.Cleaned)))
                {
                    titleLines = new List<LineInfo> { nonName };
                    authorLine = primary;
                    others = others.Where(l => !ReferenceEquals(l, nonName)).ToList();
                }
            }

            foreach (var other in others)
            {
                if (authorLine is null && TextNormalizer.LooksLikeAuthorName(other.Cleaned))
                {
                    authorLine = other;
                }
            }

            foreach (var other in others)
            {
                if (ReferenceEquals(other, authorLine))
                {
                    continue;
                }

                if (other.Thickness >= TitleContinuationRatio * titleLines[0].Thickness
                    && !TextNormalizer.LooksLikeAuthorName(other.Cleaned))
                {
                    titleLines.Add(other);
                }
            }

            if (authorLine is null)
            {
                authorLine = others
                    .Where(l => !titleLines.Contains(l))
                    .Where(l => l.Thickness >= AuthorFallbackRatio * titleLines[0].Thickness)
                    .FirstOrDefault(l => l.Cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4
                                         && !l.Cleaned.Any(char.IsDigit));
            }

            title = string.Join(" ", titleLines.OrderBy(l => l.ReadingOrderKey).Select(l => l.Cleaned));
            author = authorLine?.Cleaned;
            used.AddRange(titleLines);
            if (authorLine is not null)
            {
                used.Add(authorLine);
            }

            // "Title by Author" / "Title - Author" printed on one line.
            if (author is null)
            {
                (title, author) = SplitSingleLine(title);
            }
        }

        title = TextNormalizer.SmartCase(TextNormalizer.Clean(title));
        author = string.IsNullOrWhiteSpace(author) ? null : TextNormalizer.SmartCase(TextNormalizer.Clean(StripBy(author)));
        if (title.Length == 0 && isbn is null)
        {
            return null;
        }

        var confidences = (used.Count > 0 ? used : ordered).SelectMany(l => l.WordConfidences).ToList();
        double? confidence = confidences.Count > 0 ? Math.Round(confidences.Average(), 4) : null;

        return new SpineCandidate(position, rawText, Truncate(title, 500), author is null ? null : Truncate(author, 300), isbn, confidence);
    }

    private static (string Title, string? Author) SplitSingleLine(string text)
    {
        var by = ByPattern().Match(text);
        if (by.Success)
        {
            return (by.Groups["title"].Value, by.Groups["author"].Value);
        }

        var dash = DashPattern().Match(text);
        if (dash.Success)
        {
            var left = dash.Groups["left"].Value;
            var right = dash.Groups["right"].Value;
            var leftName = TextNormalizer.LooksLikeAuthorName(left);
            var rightName = TextNormalizer.LooksLikeAuthorName(right);
            if (rightName && !leftName)
            {
                return (left, right);
            }

            if (leftName && !rightName)
            {
                return (right, left);
            }
        }

        return (text, null);
    }

    private static string StripBy(string author) =>
        author.StartsWith("by ", StringComparison.OrdinalIgnoreCase) ? author[3..] : author;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd();

    private enum Orientation
    {
        Horizontal,
        VerticalDown,
        VerticalUp
    }

    private sealed class LineInfo
    {
        public required string Raw { get; init; }
        public required string Cleaned { get; init; }
        public required bool HasGeometry { get; init; }
        public Orientation Orientation { get; init; }
        public double Cx { get; init; }
        public double Cy { get; init; }
        public double AlongStart { get; init; }
        public double AlongEnd { get; init; }
        public double AcrossCenter { get; init; }
        public double Thickness { get; init; }
        public double Length => AlongEnd - AlongStart;
        public IReadOnlyList<double> WordConfidences { get; init; } = Array.Empty<double>();

        /// <summary>Order in which a human reads multiple lines of one spine.</summary>
        public double ReadingOrderKey => Orientation switch
        {
            Orientation.VerticalDown => -AcrossCenter, // text rotated clockwise: next line is to the left
            _ => AcrossCenter                          // horizontal and counter-clockwise: next line is below / to the right
        };

        public static LineInfo? From(OcrLine line)
        {
            var cleaned = TextNormalizer.Clean(line.Text);
            if (cleaned.Length == 0)
            {
                return null;
            }

            var confidences = line.Words.Where(w => w.Confidence.HasValue).Select(w => w.Confidence!.Value).ToList();
            var poly = line.Polygon;
            if (poly.Count < 3)
            {
                return new LineInfo { Raw = line.Text.Trim(), Cleaned = cleaned, HasGeometry = false, WordConfidences = confidences };
            }

            double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X), minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
            double dx, dy, thickness;
            if (poly.Count == 4)
            {
                dx = ((poly[1].X - poly[0].X) + (poly[2].X - poly[3].X)) / 2;
                dy = ((poly[1].Y - poly[0].Y) + (poly[2].Y - poly[3].Y)) / 2;
                var t1 = Math.Sqrt(Math.Pow(poly[3].X - poly[0].X, 2) + Math.Pow(poly[3].Y - poly[0].Y, 2));
                var t2 = Math.Sqrt(Math.Pow(poly[2].X - poly[1].X, 2) + Math.Pow(poly[2].Y - poly[1].Y, 2));
                thickness = (t1 + t2) / 2;
            }
            else
            {
                // Unknown corner order: assume text runs along the longer side of the bounding box.
                var w = maxX - minX;
                var h = maxY - minY;
                dx = w >= h ? w : 0;
                dy = w >= h ? 0 : h;
                thickness = Math.Min(w, h);
            }

            var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            var abs = Math.Abs(angle);
            var orientation = abs is > 45 and < 135
                ? (angle > 0 ? Orientation.VerticalDown : Orientation.VerticalUp)
                : Orientation.Horizontal;

            var cx = poly.Average(p => p.X);
            var cy = poly.Average(p => p.Y);
            var vertical = orientation != Orientation.Horizontal;
            return new LineInfo
            {
                Raw = line.Text.Trim(),
                Cleaned = cleaned,
                HasGeometry = true,
                Orientation = orientation,
                Cx = cx,
                Cy = cy,
                AlongStart = vertical ? minY : minX,
                AlongEnd = vertical ? maxY : maxX,
                AcrossCenter = vertical ? cx : cy,
                Thickness = Math.Max(thickness, 1),
                WordConfidences = confidences
            };
        }
    }
}
