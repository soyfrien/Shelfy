using Shelfy.Domain;

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

    [Theory]
    [InlineData("9780547928227", "054792822X")]
    [InlineData("978-0-547-92822-7", "054792822X")]
    [InlineData("054792822X", "054792822X")]
    [InlineData("0-441-17271-7", "0441172717")]
    [InlineData("9780441172719", "0441172717")]
    public void Converts_Isbn13_And_Isbn10_Back_To_Isbn10(string input, string expected) =>
        Assert.Equal(expected, Isbn.ToIsbn10(input));

    [Fact]
    public void ToIsbn10_Is_Null_For_979_And_Invalid_Values()
    {
        Assert.Null(Isbn.ToIsbn10("9786555110913".Replace("978", "979"))); // may also be invalid check
        Assert.Null(Isbn.ToIsbn10("9791234567896"));
        Assert.Null(Isbn.ToIsbn10("not-an-isbn"));
        Assert.Null(Isbn.ToIsbn10(null));
    }

    [Fact]
    public void Round_Trips_Isbn10_And_Isbn13()
    {
        foreach (var raw in new[] { "054792822X", "0-441-17271-7", "9780547928227" })
        {
            var isbn13 = Isbn.Normalize(raw);
            var isbn10 = Isbn.ToIsbn10(raw);
            Assert.Equal(isbn13, Isbn.Normalize(isbn10));
            Assert.Equal(isbn10, Isbn.ToIsbn10(isbn13));
        }
    }

    [Fact]
    public void Digits_Strips_Hyphens_And_Spaces()
    {
        Assert.Equal("9780547928227", Isbn.Digits("978-0-547-92822-7"));
        Assert.Equal("054792822X", Isbn.Digits(" 0-547-92822-x "));
        Assert.Equal("", Isbn.Digits(" "));
    }
}
