namespace Shelfy.Features.Recognition.UploadShelfPhoto;

public enum PhotoFormat
{
    Unknown,
    Jpeg,
    Png
}

/// <summary>Identifies an image by its first bytes (magic numbers), never by file name or declared content type.</summary>
public static class PhotoSignature
{
    public const int HeaderLength = 12;

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static PhotoFormat Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return PhotoFormat.Jpeg;
        }

        if (header.Length >= Png.Length && header[..Png.Length].SequenceEqual(Png))
        {
            return PhotoFormat.Png;
        }

        return PhotoFormat.Unknown;
    }

    public static string ContentType(PhotoFormat format) => format switch
    {
        PhotoFormat.Jpeg => "image/jpeg",
        PhotoFormat.Png => "image/png",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string Extension(PhotoFormat format) => format == PhotoFormat.Jpeg ? "jpg" : "png";

    /// <summary>True when the file name has no extension or an extension that agrees with the detected format.</summary>
    public static bool ExtensionMatches(string? fileName, PhotoFormat format)
    {
        var ext = Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
        return ext.Length == 0 || format switch
        {
            PhotoFormat.Jpeg => ext is "jpg" or "jpeg",
            PhotoFormat.Png => ext == "png",
            _ => false
        };
    }
}
