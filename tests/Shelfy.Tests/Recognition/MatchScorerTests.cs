using Shelfy.Features.Recognition.Resolution;

namespace Shelfy.Tests.Recognition;

public class MatchScorerTests
{
    private static double Score(string title, string? author, string candTitle, params string[] candAuthors) =>
        MatchScorer.Score(new BookQuery(title, author), candTitle, candAuthors);

    [Fact]
    public void Exact_Title_And_Author_Scores_One() =>
        Assert.Equal(1.0, Score("The Hobbit", "J.R.R. Tolkien", "The Hobbit", "J.R.R. Tolkien"));

    [Fact]
    public void Ignores_Case_Accents_Punctuation_And_Leading_Articles() =>
        Assert.True(Score("hobbit", "jrr tolkien", "The Hobbit", "J. R. R. Tolkien") > 0.95);

    [Fact]
    public void Initials_Match_Spaced_And_Unspaced() =>
        Assert.Equal(MatchScorer.NormalizeName("J.R.R. Tolkien"), MatchScorer.NormalizeName("J. R. R. Tolkien"));

    [Fact]
    public void Handles_Surname_Comma_Given_Name() =>
        Assert.Equal(MatchScorer.NormalizeName("Herbert, Frank"), MatchScorer.NormalizeName("Frank Herbert"));

    [Fact]
    public void Tolerates_A_Small_Ocr_Error() =>
        Assert.True(Score("Foundatlon", "Isaac Asimov", "Foundation", "Isaac Asimov") >= 0.85);

    [Fact]
    public void Matches_A_Subtitled_Edition_To_The_Short_Spine_Title() =>
        Assert.True(Score("Dune", "Frank Herbert", "Dune: Deluxe Edition", "Frank Herbert") >= 0.95);

    [Fact]
    public void Surname_Alone_Is_A_Strong_Author_Signal() =>
        Assert.True(Score("The Hobbit", "Tolkien", "The Hobbit", "John Ronald Reuel Tolkien") >= 0.9);

    [Fact]
    public void Same_Title_By_Another_Author_Is_Rejected_At_The_Default_Threshold() =>
        Assert.True(Score("Dune", "Frank Herbert", "Dune", "Some Other Person") < 0.72);

    [Fact]
    public void Prefers_The_Exact_Title_Over_A_Longer_Related_Title()
    {
        var exact = Score("Dune", "Frank Herbert", "Dune", "Frank Herbert");
        var sequel = Score("Dune", "Frank Herbert", "Dune Messiah", "Frank Herbert");

        Assert.True(exact > sequel);
        Assert.True(sequel < 0.9);
    }

    [Fact]
    public void Unrelated_Books_Score_Low() =>
        Assert.True(Score("Dune", "Frank Herbert", "Pride and Prejudice", "Jane Austen") < 0.4);

    [Fact]
    public void Without_A_Query_Author_The_Title_Alone_Is_Discounted()
    {
        var withAuthor = Score("Dune", "Frank Herbert", "Dune", "Frank Herbert");
        var without = Score("Dune", null, "Dune", "Frank Herbert");

        Assert.True(without < withAuthor);
        Assert.True(without >= 0.72);
    }

    [Fact]
    public void A_Candidate_Without_Authors_Gets_A_Neutral_Author_Score() =>
        Assert.Equal(0.775, Score("Dune", "Frank Herbert", "Dune"));

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("same", "same", 0)]
    public void Levenshtein_Distance(string a, string b, int expected) => Assert.Equal(expected, MatchScorer.Levenshtein(a, b));
}
