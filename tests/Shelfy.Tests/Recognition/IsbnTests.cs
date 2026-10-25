using Shelfy.Features.Recognition.Parsing;

namespace Shelfy.Tests.Recognition;

public class IsbnTests
{
    [Theory]
    [InlineData("9780547928227", "9780547928227")]
    [InlineData("978-0-547-92822-7", "9780547928227")]
    [InlineData("054792822X", "9780547928227")]
    [InlineData("0-441-17271-7", "9780441172719")]
    [InlineData(" 0441172717 ", "9780441172719")]
    public void Normalizes_Valid_Isbns_To_Isbn13(string input, string expected) =>
        Assert.Equal(expected, Isbn.Normalize(input));

    [Theory]
    [InlineData("9780547928228")] // bad check digit
    [InlineData("0441172718")]
    [InlineData("12345")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abcdefghij")]
    public void Rejects_Invalid_Isbns(string? input) => Assert.Null(Isbn.Normalize(input));

    [Fact]
    public void Finds_An_Isbn_In_Free_Text() =>
        Assert.Equal("9780547928227", Isbn.FindIn("ISBN 978-0-547-92822-7 HOUGHTON MIFFLIN"));

    [Fact]
    public void Ignores_Text_Without_A_Valid_Isbn() =>
        Assert.Null(Isbn.FindIn("Call number 1234567890123 shelf 7"));

    [Fact]
    public void Prefers_An_English_Registration_Group_When_Choosing_Among_Editions()
    {
        // 9786555110913 is a Brazilian-Portuguese edition; 0547928220 is the English edition.
        var picked = Isbn.PickPreferred(new[] { "9786555110913", "bogus", "054792822X", "9780261102217" });

        Assert.Equal("9780547928227", picked);
    }

    [Fact]
    public void Falls_Back_To_The_First_Valid_Isbn_When_None_Is_English()
    {
        Assert.Equal("9786555110913", Isbn.PickPreferred(new[] { "nope", "9786555110913" }));
        Assert.Null(Isbn.PickPreferred(new[] { "nope", null }));
    }
}
