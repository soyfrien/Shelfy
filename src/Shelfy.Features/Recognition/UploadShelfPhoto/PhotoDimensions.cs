using System.Buffers.Binary;

namespace Shelfy.Features.Recognition.UploadShelfPhoto;

/// <summary>
/// Reads the pixel size of a JPEG or PNG from its headers, without decoding the image. Returns null when the size can't be
/// read (a damaged or unusual file): such photos are left to the OCR service as before, never rejected on a guess.
/// </summary>
public static class PhotoDimensions
{
    /// <summary>Azure AI Vision (Read / Image Analysis 4.0) refuses images smaller than 50 x 50 pixels.</summary>
    public const int MinSidePixels = 50;

    /// <summary>How far into the file a JPEG frame header is looked for (EXIF thumbnails can push it back).</summary>
    public const int MaxScanBytes = 1024 * 1024;

    public static (int Width, int Height)? Read(ReadOnlySpan<byte> data, PhotoFormat format) => format switch
    {
        PhotoFormat.Png => ReadPng(data),
        PhotoFormat.Jpeg => ReadJpeg(data),
        _ => null
    };

    public static async Task<(int Width, int Height)?> ReadAsync(Stream stream, PhotoFormat format, CancellationToken ct = default)
    {
        var buffer = new byte[format == PhotoFormat.Png ? 32 : MaxScanBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return Read(buffer.AsSpan(0, total), format);
    }

    // PNG: 8-byte signature, then the IHDR chunk: length (4), "IHDR" (4), width (4, big-endian), height (4).
    private static (int, int)? ReadPng(ReadOnlySpan<byte> d)
    {
        if (d.Length < 24 || d[12] != (byte)'I' || d[13] != (byte)'H' || d[14] != (byte)'D' || d[15] != (byte)'R')
        {
            return null;
        }

        var w = BinaryPrimitives.ReadInt32BigEndian(d[16..]);
        var h = BinaryPrimitives.ReadInt32BigEndian(d[20..]);
        return w > 0 && h > 0 ? (w, h) : null;
    }

    // JPEG: walk the marker segments after SOI until a start-of-frame (SOF0..SOF15 except DHT C4, JPG C8, DAC CC).
    private static (int, int)? ReadJpeg(ReadOnlySpan<byte> d)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8)
        {
            return null;
        }

        var i = 2;
        while (i + 4 <= d.Length)
        {
            if (d[i] != 0xFF)
            {
                return null; // not a marker where one must be: give up rather than guess
            }

            var marker = d[i + 1];
            if (marker == 0xFF)
            {
                i++; // fill byte
                continue;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                i += 2; // markers without a length
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                return null; // end of image or start of scan before any frame header
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(d[(i + 2)..]);
            if (length < 2)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (i + 9 > d.Length || length < 7)
                {
                    return null;
                }

                int h = BinaryPrimitives.ReadUInt16BigEndian(d[(i + 5)..]);
                int w = BinaryPrimitives.ReadUInt16BigEndian(d[(i + 7)..]);
                return w > 0 && h > 0 ? (w, h) : null;
            }

            i += 2 + length;
        }

        return null;
    }
}
