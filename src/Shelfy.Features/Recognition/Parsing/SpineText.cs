namespace Shelfy.Features.Recognition.Parsing;

/// <summary>Identity of a spine's text for "the same spine seen twice": case, accents and punctuation do not matter.</summary>
public static class SpineText
{
    public static string Key(string? text)
    {
        var key = string.Join(' ', SpineDeduper.Tokens(text));
        return key.Length > 0 ? key : (text ?? "").Trim().ToLowerInvariant();   // punctuation only: compare as written
    }
}
