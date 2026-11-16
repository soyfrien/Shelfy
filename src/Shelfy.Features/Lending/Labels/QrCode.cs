using System.Text;

namespace Shelfy.Features.Lending.Labels;

/// <summary>
/// A small dependency-free QR Code encoder (ISO/IEC 18004): byte mode, error correction level M, versions 1-9 (up to 180
/// bytes, plenty for a copy URL). Follows the reference algorithm by Project Nayuki (MIT). Modules[y, x] true = dark.
/// </summary>
public sealed class QrCode
{
    private static readonly int[] EccPerBlockM = [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22];
    private static readonly int[] BlocksM = [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5];
    private const int FormatBitsM = 0;
    public const int MaxVersion = 9;

    public int Version { get; }
    public int Size { get; }
    public int Mask { get; private set; }
    private readonly bool[,] _modules;
    private readonly bool[,] _isFunction;

    public bool this[int x, int y] => _modules[y, x];

    private QrCode(int version)
    {
        Version = version;
        Size = version * 4 + 17;
        _modules = new bool[Size, Size];
        _isFunction = new bool[Size, Size];
    }

    public static QrCode Encode(string text) => Encode(Encoding.UTF8.GetBytes(text));

    public static QrCode Encode(byte[] data)
    {
        var version = 1;
        for (; ; version++)
        {
            if (version > MaxVersion)
            {
                throw new ArgumentException("Data too long for this QR encoder.", nameof(data));
            }

            if (4 + 8 + data.Length * 8 <= DataCodewords(version) * 8)
            {
                break;
            }
        }

        var capacityBits = DataCodewords(version) * 8;
        var bits = new List<bool>();
        void Append(int value, int count)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                bits.Add(((value >> i) & 1) != 0);
            }
        }

        Append(0b0100, 4);           // byte mode
        Append(data.Length, 8);      // character count (versions 1-9)
        foreach (var b in data)
        {
            Append(b, 8);
        }

        Append(0, Math.Min(4, capacityBits - bits.Count));
        Append(0, (8 - bits.Count % 8) % 8);
        for (var pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11)
        {
            Append(pad, 8);
        }

        var codewords = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            }
        }

        var qr = new QrCode(version);
        qr.DrawFunctionPatterns();
        qr.DrawCodewords(qr.AddEccAndInterleave(codewords));

        var best = -1;
        var minPenalty = long.MaxValue;
        for (var m = 0; m < 8; m++)
        {
            qr.ApplyMask(m);
            qr.DrawFormatBits(m);
            var p = qr.Penalty();
            if (p < minPenalty)
            {
                best = m;
                minPenalty = p;
            }

            qr.ApplyMask(m); // XOR again undoes it
        }

        qr.ApplyMask(best);
        qr.DrawFormatBits(best);
        qr.Mask = best;
        return qr;
    }

    /// <summary>The symbol as SVG path data ("M x y h1 v1 h-1 z" per dark module), offset by <paramref name="border"/>.</summary>
    public string ToPath(int border)
    {
        var sb = new StringBuilder();
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (_modules[y, x])
                {
                    sb.Append('M').Append(x + border).Append(',').Append(y + border).Append("h1v1h-1z");
                }
            }
        }

        return sb.ToString();
    }

    private static int RawDataModules(int ver)
    {
        var result = (16 * ver + 128) * ver + 64;
        if (ver >= 2)
        {
            var numAlign = ver / 7 + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
            if (ver >= 7)
            {
                result -= 36;
            }
        }

        return result;
    }

    private static int DataCodewords(int ver) => RawDataModules(ver) / 8 - EccPerBlockM[ver] * BlocksM[ver];

    private void Set(int x, int y, bool dark)
    {
        _modules[y, x] = dark;
        _isFunction[y, x] = true;
    }

    private void DrawFunctionPatterns()
    {
        for (var i = 0; i < Size; i++)
        {
            Set(6, i, i % 2 == 0);
            Set(i, 6, i % 2 == 0);
        }

        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        var align = AlignmentPositions();
        var n = align.Length;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0))
                {
                    continue;
                }

                for (var dy = -2; dy <= 2; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        Set(align[i] + dx, align[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                    }
                }
            }
        }

        DrawFormatBits(0);
        DrawVersion();
    }

    private void DrawFinder(int cx, int cy)
    {
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && x < Size && y >= 0 && y < Size)
                {
                    Set(x, y, dist != 2 && dist != 4);
                }
            }
        }
    }

    private int[] AlignmentPositions()
    {
        if (Version == 1)
        {
            return [];
        }

        var numAlign = Version / 7 + 2;
        var step = (Version * 8 + numAlign * 3 + 5) / (numAlign * 4 - 4) * 2;
        var result = new List<int> { 6 };
        for (int i = 0, pos = Size - 7; i < numAlign - 1; i++, pos -= step)
        {
            result.Insert(1, pos);
        }

        return result.ToArray();
    }

    private void DrawFormatBits(int mask)
    {
        var data = FormatBitsM << 3 | mask;
        var rem = data;
        for (var i = 0; i < 10; i++)
        {
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        }

        var bits = ((data << 10) | rem) ^ 0x5412;
        bool Bit(int i) => ((bits >> i) & 1) != 0;

        for (var i = 0; i <= 5; i++) Set(8, i, Bit(i));
        Set(8, 7, Bit(6));
        Set(8, 8, Bit(7));
        Set(7, 8, Bit(8));
        for (var i = 9; i < 15; i++) Set(14 - i, 8, Bit(i));

        for (var i = 0; i < 8; i++) Set(Size - 1 - i, 8, Bit(i));
        for (var i = 8; i < 15; i++) Set(8, Size - 15 + i, Bit(i));
        Set(8, Size - 8, true);
    }

    private void DrawVersion()
    {
        if (Version < 7)
        {
            return;
        }

        var rem = Version;
        for (var i = 0; i < 12; i++)
        {
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        }

        var bits = Version << 12 | rem;
        for (var i = 0; i < 18; i++)
        {
            var bit = ((bits >> i) & 1) != 0;
            int a = Size - 11 + i % 3, b = i / 3;
            Set(a, b, bit);
            Set(b, a, bit);
        }
    }

    private byte[] AddEccAndInterleave(byte[] data)
    {
        int numBlocks = BlocksM[Version], eccLen = EccPerBlockM[Version];
        var rawCodewords = RawDataModules(Version) / 8;
        var numShort = numBlocks - rawCodewords % numBlocks;
        var shortLen = rawCodewords / numBlocks;
        var divisor = RsDivisor(eccLen);
        var blocks = new List<byte[]>();
        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            var datLen = shortLen - eccLen + (i < numShort ? 0 : 1);
            var dat = data[k..(k + datLen)];
            k += datLen;
            var ecc = RsRemainder(dat, divisor);
            var block = new byte[shortLen + 1];
            dat.CopyTo(block, 0);
            ecc.CopyTo(block, block.Length - eccLen);
            blocks.Add(block);
        }

        var result = new List<byte>(rawCodewords);
        for (var i = 0; i < blocks[0].Length; i++)
        {
            for (var j = 0; j < blocks.Count; j++)
            {
                if (i != shortLen - eccLen || j >= numShort)
                {
                    result.Add(blocks[j][i]);
                }
            }
        }

        return result.ToArray();
    }

    private static byte[] RsDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < result.Length; j++)
            {
                result[j] = (byte)GfMul(result[j], root);
                if (j + 1 < result.Length)
                {
                    result[j] ^= result[j + 1];
                }
            }

            root = GfMul(root, 0x02);
        }

        return result;
    }

    private static byte[] RsRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++)
            {
                result[i] ^= (byte)GfMul(divisor[i], factor);
            }
        }

        return result;
    }

    private static int GfMul(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }

        return z & 0xFF;
    }

    private void DrawCodewords(byte[] data)
    {
        var i = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }

            for (var vert = 0; vert < Size; vert++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? Size - 1 - vert : vert;
                    if (!_isFunction[y, x] && i < data.Length * 8)
                    {
                        _modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
            }
        }
    }

    private void ApplyMask(int mask)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0
                };
                if (!_isFunction[y, x] && invert)
                {
                    _modules[y, x] = !_modules[y, x];
                }
            }
        }
    }

    /// <summary>Simplified mask penalty (runs, 2x2 blocks, dark balance). Any mask is valid; this just picks a cleaner one.</summary>
    private long Penalty()
    {
        long result = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var a = 0; a < Size; a++)
            {
                var run = 0;
                var color = false;
                for (var b = 0; b < Size; b++)
                {
                    var m = pass == 0 ? _modules[a, b] : _modules[b, a];
                    if (b > 0 && m == color)
                    {
                        run++;
                        if (run == 5) result += 3;
                        else if (run > 5) result++;
                    }
                    else
                    {
                        color = m;
                        run = 1;
                    }
                }
            }
        }

        var dark = 0;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (_modules[y, x]) dark++;
                if (x < Size - 1 && y < Size - 1 && _modules[y, x] == _modules[y, x + 1] && _modules[y, x] == _modules[y + 1, x]
                    && _modules[y, x] == _modules[y + 1, x + 1])
                {
                    result += 3;
                }
            }
        }

        var total = Size * Size;
        var k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        return result + Math.Max(0, k) * 10;
    }
}
