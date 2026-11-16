using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Shelfy.Domain;
using Shelfy.Features.Lending.Labels;

namespace Shelfy.Features.Users.LibraryCard;

/// <summary>
/// A member's Shelfy Library Card as plain SVG, generated on the fly and never stored. Everything that varies (the two
/// colors, the vanishing point, the aisle depth, the number of shelves, the rhythm, heights and colors of the book spines,
/// the floor rows and ceiling lights) comes from a SHA-256 hash of the user id, so the same member always gets the same card
/// and no two members are likely to get the same one. Flat, bold shapes (Fluent-like), a one-point perspective view down a
/// library aisle, and a clean white band at the bottom with the name, member number, "member since" and a QR code.
/// </summary>
public static class LibraryCardSvg
{
    public const int Width = 856;   // 85.6 x 54 mm, the ID-1 card ratio
    public const int Height = 540;
    public const int ArtHeight = 372;
    private const int MaxNameLength = 26;

    /// <summary>Short member number from the id hash: 8 Crockford base32 characters shown as "XXXX-XXXX" (cosmetic, not a key).</summary>
    public static string MemberNumber(Guid userId)
    {
        var hash = Hash(userId);
        var chars = new char[CopyCode.Length];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = CopyCode.Alphabet[hash[24 + i] & 31];
        }

        return CopyCode.Format(new string(chars));
    }

    /// <summary>The varying parameters of a card, exposed for tests and the sample renderer.</summary>
    public sealed record Design(
        double Hue, double AccentHue, string Primary, string Accent, double VanishX, double VanishY, double FarScale,
        int Rows, double[] Rhythm, double[] Heights, int[] ColorPattern, int FloorRows, int Lights);

    public static Design DesignFor(Guid userId)
    {
        var rng = new SplitMix(BitConverter.ToUInt64(Hash(userId), 0));
        var hue = rng.Next() * 360;
        var accentHue = (hue + 140 + rng.Next() * 80) % 360;
        var primary = Hsl(hue, 0.55 + rng.Next() * 0.2, 0.26 + rng.Next() * 0.06);
        var accent = Hsl(accentHue, 0.72 + rng.Next() * 0.18, 0.56 + rng.Next() * 0.06);
        var vx = Width * (0.30 + rng.Next() * 0.40);
        var vy = ArtHeight * (0.38 + rng.Next() * 0.22);
        var far = 0.16 + rng.Next() * 0.14;
        var rows = 3 + (int)(rng.Next() * 4); // 3..6
        var rhythm = Enumerable.Range(0, 3 + (int)(rng.Next() * 4)).Select(_ => 0.6 + rng.Next() * 0.9).ToArray();
        var heights = Enumerable.Range(0, 2 + (int)(rng.Next() * 4)).Select(_ => 0.58 + rng.Next() * 0.36).ToArray();
        var colors = Enumerable.Range(0, 3 + (int)(rng.Next() * 5)).Select(_ => (int)(rng.Next() * 5)).ToArray();
        var floorRows = 4 + (int)(rng.Next() * 6);
        var lights = 2 + (int)(rng.Next() * 3);
        return new Design(hue, accentHue, primary, accent, vx, vy, far, rows, rhythm, heights, colors, floorRows, lights);
    }

    /// <param name="userId">Seeds the whole design.</param>
    /// <param name="displayName">Printed on the card (escaped, shortened when very long).</param>
    /// <param name="memberSince">Account creation (UTC); printed as month and year.</param>
    /// <param name="qrUrl">Where the QR code points: the member's public shelf, or the site.</param>
    public static string Render(Guid userId, string displayName, DateTime memberSince, string qrUrl)
    {
        var d = DesignFor(userId);
        var rng = new SplitMix(BitConverter.ToUInt64(Hash(userId), 8)); // second stream: per-book jitter
        var number = MemberNumber(userId);
        var name = displayName.Trim();
        if (name.Length > MaxNameLength)
        {
            name = name[..(MaxNameLength - 1)].TrimEnd() + "…";
        }

        var since = memberSince.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var p = d.Primary;
        var a = d.Accent;
        var ink = Mix(p, "#000000", 0.45);           // text on white: very dark, the member's hue
        var inkSoft = Mix(p, "#000000", 0.15);
        var ceiling = Mix(p, "#000000", 0.22);
        var floor = Mix(p, "#000000", 0.38);
        var leftWall = p;
        var rightWall = Mix(p, "#ffffff", 0.07);
        var board = Mix(p, "#000000", 0.5);
        var back = a;
        var backBoard = Mix(a, p, 0.55);
        var bookColors = new[] { a, Mix(a, "#ffffff", 0.45), Mix(p, "#ffffff", 0.55), Mix(p, "#ffffff", 0.88), Mix(a, p, 0.35) };

        double vx = d.VanishX, vy = d.VanishY, f = d.FarScale, depth = 1 / f - 1;
        double S(double z) => 1 / (1 + z);
        // A point of the near frame (x, y) pushed back to depth z, projected toward the vanishing point.
        (double X, double Y) P(double x, double y, double z) => (vx + S(z) * (x - vx), vy + S(z) * (y - vy));
        var nearL = 0.0; var nearR = (double)Width; var nearT = 0.0; var nearB = (double)ArtHeight;
        var (fl, ft) = P(nearL, nearT, depth);
        var (fr, fb) = P(nearR, nearB, depth);

        var sb = new StringBuilder(48 * 1024);
        var clip = "c" + number.Replace("-", "");
        var title = $"Shelfy Library Card for {name}";
        var desc = $"Member No. {number}, member since {since}. A flat, two-color view down a library aisle with {d.Rows} rows of shelves on each side.";
        sb.Append(CultureInfo.InvariantCulture,
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {Width} {Height}" width="{Width}" height="{Height}" role="img" aria-labelledby="{clip}t {clip}d">""");
        sb.Append($"<title id=\"{clip}t\">{X(title)}</title><desc id=\"{clip}d\">{X(desc)}</desc>");
        sb.Append($"<defs><clipPath id=\"{clip}\"><rect width=\"{Width}\" height=\"{Height}\" rx=\"30\"/></clipPath></defs>");
        sb.Append($"<g clip-path=\"url(#{clip})\">");

        // Ceiling, floor, walls, far wall.
        Poly(sb, ceiling, (nearL, nearT), (nearR, nearT), (fr, ft), (fl, ft));
        Poly(sb, floor, (nearL, nearB), (nearR, nearB), (fr, fb), (fl, fb));
        Poly(sb, leftWall, (nearL, nearT), (fl, ft), (fl, fb), (nearL, nearB));
        Poly(sb, rightWall, (nearR, nearT), (fr, ft), (fr, fb), (nearR, nearB));
        sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{N(fl)}\" y=\"{N(ft)}\" width=\"{N(fr - fl)}\" height=\"{N(fb - ft)}\" fill=\"{back}\"/>");

        // Floor rows: lines running to the vanishing point and cross lines at even depth steps.
        var floorLine = Mix(floor, "#ffffff", 0.12);
        for (var i = 1; i < d.FloorRows; i++)
        {
            var x = Width * (double)i / d.FloorRows;
            var (x2, y2) = P(x, nearB, depth);
            sb.Append(CultureInfo.InvariantCulture, $"<line x1=\"{N(x)}\" y1=\"{N(nearB)}\" x2=\"{N(x2)}\" y2=\"{N(y2)}\" stroke=\"{floorLine}\" stroke-width=\"2\"/>");
        }

        for (var z = 0.5; z < depth; z += 0.5 + z * 0.35)
        {
            var (x1, y1) = P(nearL, nearB, z);
            var (x2, _) = P(nearR, nearB, z);
            sb.Append(CultureInfo.InvariantCulture, $"<line x1=\"{N(x1)}\" y1=\"{N(y1)}\" x2=\"{N(x2)}\" y2=\"{N(y1)}\" stroke=\"{floorLine}\" stroke-width=\"{N(2 * S(z) + 0.5)}\"/>");
        }

        // Ceiling lights: flat panels in the middle of the ceiling, evenly spaced in depth.
        var light = Mix(a, "#ffffff", 0.62);
        for (var i = 0; i < d.Lights; i++)
        {
            var z0 = depth * (i + 0.35) / (d.Lights + 0.4);
            var z1 = z0 + depth * 0.22 / d.Lights;
            double lx0 = vx - Width * 0.09, lx1 = vx + Width * 0.09;
            Poly(sb, light, P(lx0, nearT, z0), P(lx1, nearT, z0), P(lx1, nearT, z1), P(lx0, nearT, z1));
        }

        // Shelves on both walls and across the far wall.
        double top = ArtHeight * 0.05, bottom = ArtHeight * 0.97, gap = (bottom - top) / d.Rows, thick = Math.Max(5, gap * 0.08);
        for (var side = 0; side < 2; side++)
        {
            var x = side == 0 ? nearL : nearR;
            for (var row = 0; row < d.Rows; row++)
            {
                var shelfY = top + (row + 1) * gap;    // the board the books stand on
                var z = 0.0;
                var k = side * 7 + row * 3;
                while (z < depth)
                {
                    var w = 0.11 * d.Rhythm[k % d.Rhythm.Length] * (0.85 + rng.Next() * 0.3);
                    if (rng.Next() < 0.06)
                    {
                        z += w * 0.8; // a gap on the shelf
                        k++;
                        continue;
                    }

                    var z1 = Math.Min(depth, z + w * 0.92);
                    var h = (gap - thick) * d.Heights[(k + row) % d.Heights.Length] * (0.94 + rng.Next() * 0.08);
                    var color = bookColors[d.ColorPattern[k % d.ColorPattern.Length]];
                    Poly(sb, color, P(x, shelfY, z), P(x, shelfY - h, z), P(x, shelfY - h, z1), P(x, shelfY, z1));
                    z += w;
                    k++;
                }

                Poly(sb, board, P(x, shelfY, 0), P(x, shelfY + thick, 0), P(x, shelfY + thick, depth), P(x, shelfY, depth));
            }
        }

        for (var row = 0; row < d.Rows; row++)
        {
            var (_, y) = P(nearL, top + (row + 1) * gap, depth);
            sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{N(fl)}\" y=\"{N(y)}\" width=\"{N(fr - fl)}\" height=\"{N(Math.Max(1.5, thick * f))}\" fill=\"{backBoard}\"/>");
        }

        // Wordmark pill.
        sb.Append($"<rect x=\"28\" y=\"26\" width=\"128\" height=\"42\" rx=\"21\" fill=\"#ffffff\"/>");
        sb.Append($"<text x=\"92\" y=\"55\" text-anchor=\"middle\" font-family=\"'Segoe UI Variable','Segoe UI',Inter,Helvetica,Arial,sans-serif\" font-size=\"23\" font-weight=\"800\" fill=\"{ink}\">Shelfy</text>");

        // The band: accent stripe, then white with the text and the QR code.
        sb.Append(CultureInfo.InvariantCulture, $"<rect y=\"{ArtHeight}\" width=\"{Width}\" height=\"{Height - ArtHeight}\" fill=\"#ffffff\"/>");
        sb.Append(CultureInfo.InvariantCulture, $"<rect y=\"{ArtHeight}\" width=\"{Width}\" height=\"8\" fill=\"{a}\"/>");
        const string font = "font-family=\"'Segoe UI Variable','Segoe UI',Inter,Helvetica,Arial,sans-serif\"";
        sb.Append($"<text x=\"40\" y=\"{ArtHeight + 44}\" {font} font-size=\"15\" font-weight=\"700\" letter-spacing=\"2.6\" fill=\"{inkSoft}\">SHELFY LIBRARY CARD</text>");
        sb.Append($"<text x=\"40\" y=\"{ArtHeight + 92}\" {font} font-size=\"40\" font-weight=\"700\" fill=\"{ink}\">{X(name)}</text>");
        sb.Append($"<text x=\"40\" y=\"{ArtHeight + 134}\" {font} font-size=\"18\" fill=\"{inkSoft}\">No. <tspan font-family=\"ui-monospace,'Cascadia Mono',Consolas,Menlo,monospace\" font-weight=\"700\" fill=\"{ink}\">{number}</tspan>  ·  Member since {X(since)}</text>");

        var qr = QrCode.Encode(qrUrl);
        const int qrSize = 128;
        var qrX = Width - 40 - qrSize;
        var qrY = ArtHeight + 8 + (Height - ArtHeight - 8 - qrSize) / 2;
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg x=\"{qrX}\" y=\"{qrY}\" width=\"{qrSize}\" height=\"{qrSize}\" viewBox=\"0 0 {qr.Size} {qr.Size}\" shape-rendering=\"crispEdges\"><path fill=\"{ink}\" d=\"{qr.ToPath(0)}\"/></svg>");

        sb.Append("</g></svg>");
        return sb.ToString();
    }

    private static byte[] Hash(Guid id) => SHA256.HashData(id.ToByteArray());

    private static string X(string s) => WebUtility.HtmlEncode(s);

    private static string N(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static void Poly(StringBuilder sb, string fill, params (double X, double Y)[] pts)
    {
        sb.Append("<polygon fill=\"").Append(fill).Append("\" points=\"");
        for (var i = 0; i < pts.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(N(pts[i].X)).Append(',').Append(N(pts[i].Y));
        }

        sb.Append("\"/>");
    }

    internal static string Hsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var hp = h / 60;
        var x = c * (1 - Math.Abs(hp % 2 - 1));
        var (r, g, b) = hp switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        var m = l - c / 2;
        return Hex(r + m, g + m, b + m);
    }

    internal static string Mix(string a, string b, double t)
    {
        var (ar, ag, ab) = Rgb(a);
        var (br, bg, bb) = Rgb(b);
        return Hex(ar + (br - ar) * t, ag + (bg - ag) * t, ab + (bb - ab) * t);
    }

    /// <summary>WCAG relative luminance contrast ratio between two "#rrggbb" colors.</summary>
    public static double Contrast(string a, string b)
    {
        static double L(string c)
        {
            var (r, g, bl) = Rgb(c);
            static double Lin(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(bl);
        }

        var (l1, l2) = (L(a), L(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    /// <summary>The dark text color used on the white band (exposed for the contrast test).</summary>
    public static string InkFor(Guid userId) => Mix(DesignFor(userId).Primary, "#000000", 0.45);

    public static string InkSoftFor(Guid userId) => Mix(DesignFor(userId).Primary, "#000000", 0.15);

    private static (double, double, double) Rgb(string hex) =>
        (Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0, Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0, Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0);

    private static string Hex(double r, double g, double b)
    {
        static int C(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return $"#{C(r):x2}{C(g):x2}{C(b):x2}";
    }

    /// <summary>SplitMix64: tiny, fast, and the same on every platform and .NET version (unlike System.Random seeding).</summary>
    private sealed class SplitMix(ulong seed)
    {
        private ulong _state = seed;

        public double Next()
        {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / (1UL << 53));
        }
    }
}
