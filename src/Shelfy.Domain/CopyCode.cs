using System.Security.Cryptography;
using System.Text;

namespace Shelfy.Domain;

/// <summary>
/// The short human-friendly code of a <see cref="Copy"/>, printed on a sticker or QR label (ADR-012).
/// 8 characters of Crockford base32 (0-9, A-Z without I, L, O, U), i.e. 40 random bits. Stored without separator, shown as
/// "XXXX-XXXX". Reading is forgiving: lower case, spaces and dashes are ignored, O reads as 0 and I/L read as 1.
/// </summary>
public static class CopyCode
{
    public const int Length = 8;
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>A new random code (cryptographic RNG). Uniqueness is enforced by the database; callers retry on a clash.</summary>
    public static string New()
    {
        Span<byte> bytes = stackalloc byte[Length];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[bytes[i] & 31];
        }

        return new string(chars);
    }

    /// <summary>"7K3MQ9XA" becomes "7K3M-Q9XA".</summary>
    public static string Format(string code) => code.Length == Length ? code[..4] + "-" + code[4..] : code;

    /// <summary>Normalizes what a person typed or a scanner read. Returns null when it cannot be a code.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var sb = new StringBuilder(Length);
        foreach (var raw in input)
        {
            if (raw is '-' or ' ' or '_')
            {
                continue;
            }

            var c = char.ToUpperInvariant(raw) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other
            };
            if (Alphabet.IndexOf(c) < 0)
            {
                return null;
            }

            sb.Append(c);
        }

        return sb.Length == Length ? sb.ToString() : null;
    }
}
