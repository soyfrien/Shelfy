using System.Xml.Linq;
using Shelfy.Features.Lending.Labels;
using Shelfy.Features.Users.LibraryCard;

namespace Shelfy.Tests.Features.Users;

public class LibraryCardSvgTests
{
    private static readonly DateTime Since = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_same_member_always_gets_the_same_card()
    {
        var id = Guid.NewGuid();
        Assert.Equal(LibraryCardSvg.Render(id, "Louis", Since, "https://x.test/"), LibraryCardSvg.Render(id, "Louis", Since, "https://x.test/"));
        Assert.Equal(LibraryCardSvg.MemberNumber(id), LibraryCardSvg.MemberNumber(id));
    }

    [Fact]
    public void Different_members_get_different_designs_and_numbers()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToList();
        var designs = ids.Select(LibraryCardSvg.DesignFor).ToList();
        Assert.True(designs.Select(d => (d.Primary, d.Accent)).Distinct().Count() > 190, "palettes should almost never repeat");
        Assert.True(designs.Select(d => d.Rows).Distinct().Count() >= 3);
        Assert.True(designs.Select(d => Math.Round(d.VanishX / 50)).Distinct().Count() >= 5);
        Assert.Equal(200, ids.Select(LibraryCardSvg.MemberNumber).Distinct().Count());
        Assert.All(ids.Select(LibraryCardSvg.MemberNumber), n => Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", n));
    }

    [Fact]
    public void Text_on_the_band_has_strong_contrast_for_every_palette()
    {
        for (var i = 0; i < 500; i++)
        {
            var id = Guid.NewGuid();
            Assert.True(LibraryCardSvg.Contrast(LibraryCardSvg.InkFor(id), "#ffffff") >= 7, "name and number: WCAG AAA");
            Assert.True(LibraryCardSvg.Contrast(LibraryCardSvg.InkSoftFor(id), "#ffffff") >= 4.5, "labels: WCAG AA");
        }
    }

    [Fact]
    public void Is_valid_accessible_svg_with_the_card_text_and_an_escaped_name()
    {
        var id = Guid.NewGuid();
        var svg = LibraryCardSvg.Render(id, "Ann <script>alert(1)</script> & Co", Since, "https://x.test/");

        var doc = XDocument.Parse(svg); // well-formed XML, so the name can't break out
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal("img", doc.Root!.Attribute("role")?.Value);
        Assert.StartsWith("Shelfy Library Card for Ann <script>", doc.Root.Element(ns + "title")!.Value);
        Assert.Contains("member since November 2026", doc.Root.Element(ns + "desc")!.Value);
        Assert.DoesNotContain("<script>", svg);
        Assert.Contains("SHELFY LIBRARY CARD", svg);
        Assert.Contains(LibraryCardSvg.MemberNumber(id), svg);
        Assert.Contains("Member since November 2026", svg);
        Assert.DoesNotContain("http", svg.Replace("http://www.w3.org/2000/svg", "")); // no external assets
    }

    [Fact]
    public void Very_long_names_are_shortened()
    {
        var svg = LibraryCardSvg.Render(Guid.NewGuid(), new string('W', 80), Since, "https://x.test/");
        Assert.Contains(new string('W', 25) + "…", svg);
        Assert.DoesNotContain(new string('W', 26), svg);
    }

    [Fact]
    public void The_qr_code_encodes_the_given_link()
    {
        var svg = LibraryCardSvg.Render(Guid.NewGuid(), "Louis", Since, "https://x.test/s/abc");
        Assert.Contains(QrCode.Encode("https://x.test/s/abc").ToPath(0), svg);
    }
}
