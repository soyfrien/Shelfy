using Shelfy.Features.Recognition.Corrections;

namespace Shelfy.Tests.Recognition;

public class CorrectionKeyTests
{
    [Fact]
    public void Case_Accents_And_Punctuation_Do_Not_Change_The_Key()
    {
        Assert.Equal("the hobbit tolkien", CorrectionKey.From("THE HOBBIT | Tolkien"));
        Assert.Equal(CorrectionKey.From("Les Misérables | Victor Hugo"), CorrectionKey.From("LES MISERABLES - victor hugo!"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TH")]
    [InlineData("a b")]
    [InlineData("!!! 1984 !!!")]
    public void Texts_Too_Short_To_Mean_One_Book_Have_No_Key(string? text) => Assert.Null(CorrectionKey.From(text));

    [Fact]
    public void A_Long_Text_Is_Cut_To_The_Column_Size()
    {
        var key = CorrectionKey.From(string.Join(' ', Enumerable.Repeat("wordy", 200)));

        Assert.NotNull(key);
        Assert.True(key!.Length <= CorrectionKey.MaxLength);
    }

    [Fact]
    public void For_Gives_The_Whole_Text_First_And_A_Real_Title_Second()
    {
        Assert.Equal(new[] { "the hobbt tolkien", "the hobbt" }, CorrectionKey.For("THE HOBBT | Tolkien", "The Hobbt"));
        Assert.Equal(new[] { "foundation" }, CorrectionKey.For("Foundation", "Foundation"));       // same key: once
        Assert.Equal(new[] { "dunes world" }, CorrectionKey.For("Dunes World", "Dune"));          // a short one-word title alone is too ambiguous
        Assert.Equal(new[] { "alphabetical text blah", "alphabetical" }, CorrectionKey.For("Alphabetical text blah", "Alphabetical")); // one long word is fine
        Assert.Empty(CorrectionKey.For("TH", "TH"));
    }
}
