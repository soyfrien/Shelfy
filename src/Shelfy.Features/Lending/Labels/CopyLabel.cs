using System.Net;
using Shelfy.Domain;

namespace Shelfy.Features.Lending.Labels;

/// <summary>
/// A printable sticker for a copy (ADR-012): a QR code pointing at {origin}/c/{CODE}, the code in large letters, and the title.
/// Plain SVG, no dependencies; prints crisply at any size.
/// </summary>
public static class CopyLabel
{
    public static string Url(string origin, string code) => $"{origin.TrimEnd('/')}/c/{code}";

    public static string Svg(string url, string code, string title)
    {
        var qr = QrCode.Encode(url);
        const int border = 4;
        var dim = qr.Size + border * 2;
        var textBand = 14;
        var height = dim + textBand;
        var shortTitle = title.Length > 34 ? title[..33] + "…" : title;
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {dim} {height}" width="{dim * 8}" height="{height * 8}" shape-rendering="crispEdges">
            <title>Shelfy copy {WebUtility.HtmlEncode(CopyCode.Format(code))}</title>
            <rect width="100%" height="100%" fill="#fff"/>
            <path fill="#000" d="{qr.ToPath(border)}"/>
            <text x="{dim / 2.0}" y="{dim + 4.5}" font-family="ui-monospace,Menlo,Consolas,monospace" font-size="5" font-weight="700" text-anchor="middle" fill="#000">{WebUtility.HtmlEncode(CopyCode.Format(code))}</text>
            <text x="{dim / 2.0}" y="{dim + 10.5}" font-family="Helvetica,Arial,sans-serif" font-size="3.2" text-anchor="middle" fill="#333">Shelfy · {WebUtility.HtmlEncode(shortTitle)}</text>
            </svg>
            """;
    }
}
