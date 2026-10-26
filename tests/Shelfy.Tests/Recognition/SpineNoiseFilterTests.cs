using Shelfy.Features.Recognition.GetShelfUnmatched;
using Shelfy.Features.Recognition.Parsing;

namespace Shelfy.Tests.Recognition;

/// <summary>The unmatched-list noise filter. The "keep" cases are real unmatched reads from a real 56-book shelf: real titles must stay listed.</summary>
public class SpineNoiseFilterTests
{
    [Theory]
    [InlineData("TH", 0.62, "short")]
    [InlineData("I", 0.9, "short")]
    [InlineData("HEN", 0.93, "short")]
    [InlineData("OPE", 0.99, "short")]
    [InlineData("FAN", 0.73, "short")]
    [InlineData("a b c", 0.9, "short")]
    [InlineData("12 34 5", 0.9, "punctuation")]
    [InlineData("| . - |", 0.9, "punctuation")]
    [InlineData("--- ... !!! ???", 0.9, "punctuation")]
    [InlineData("x1 y2 z3 ## .. 44 55", 0.9, "punctuation")]
    [InlineData("THE", 0.99, "filler")]
    [InlineData("WITH", 0.98, "filler")]
    [InlineData("the | of | and", 0.9, "filler")]
    [InlineData("OGET'S TI", 0.44, "unsure")]
    [InlineData("putnam", 0.22, "unsure")]
    [InlineData("FOWLER'S", 0.28, "unsure")]
    [InlineData("TALE", 0.57, "unsure")]
    [InlineData("SHEET | LESLIE", 0.45, "unsure")]
    public void Noise_Is_Hidden_With_A_Reason(string text, double confidence, string reason) =>
        Assert.Equal(reason, SpineNoiseFilter.Reason(text, confidence));

    [Theory]
    [InlineData("Dune", 0.9)]                                    // a real one-word title
    [InlineData("Emma", 0.75)]
    [InlineData("Ulysses", null)]
    [InlineData("Rice", 0.98)]
    [InlineData("SPANISH", 0.94)]
    [InlineData("NORWEGIAN", 0.98)]
    [InlineData("Dune Messiah Herbert", 0.4)]
    [InlineData("RECURSION BLAKE CROUCH", 0.79)]
    [InlineData("The Last Romantics Tara Conklin", 0.95)]
    [InlineData("MEG & JO Virginia Kantr", 0.74)]
    [InlineData("TH E LONG GOODBYE", 0.5)]
    [InlineData("ENGLISH-BYELORUSSIAN 41", 0.26)]               // low confidence but long enough to hold real words
    [InlineData("Langenscheidt Dictionary of Italian", 0.45)]
    public void Reads_That_Look_Like_Words_Are_Kept(string text, double? confidence)
    {
        Assert.Null(SpineNoiseFilter.Reason(text, confidence));
    }

    [Fact]
    public void A_Missing_Confidence_Never_Hides_A_Real_Word()
    {
        Assert.Null(SpineNoiseFilter.Reason("Ulysses", null));
        Assert.Null(SpineNoiseFilter.Reason("A Tale of Two Cities", null));
    }

    private static GetShelfUnmatchedHandler.Read R(string text, double? conf = 0.9) => new(Guid.NewGuid(), text, text, conf);

    [Fact]
    public void Build_Lists_Each_Text_Once_Ignoring_Case_And_Punctuation()
    {
        var (spines, hidden) = GetShelfUnmatchedHandler.Build(new[] { R("Old Man's War"), R("OLD MAN'S WAR"), R("Old man's war!"), R("Ender's Game") }, includeHidden: false);

        Assert.Equal(new[] { "Old Man's War", "Ender's Game" }, spines.Select(s => s.Text));
        Assert.Equal(0, hidden);
    }

    [Fact]
    public void Build_Hides_Noise_And_Counts_It_And_Shows_It_On_Request()
    {
        var rows = new[] { R("The Left Hand of Darkness"), R("TH"), R("WITH"), R("putnam", 0.2), R("Kindred") };

        var (normal, hidden) = GetShelfUnmatchedHandler.Build(rows, includeHidden: false);
        Assert.Equal(new[] { "The Left Hand of Darkness", "Kindred" }, normal.Select(s => s.Text));
        Assert.All(normal, s => Assert.False(s.LowQuality));
        Assert.Equal(3, hidden);

        var (all, hidden2) = GetShelfUnmatchedHandler.Build(rows, includeHidden: true);
        Assert.Equal(5, all.Count);
        Assert.Equal(3, hidden2);
        Assert.Equal(new[] { "short", "filler", "unsure" }, all.Where(s => s.LowQuality).Select(s => s.Reason));
    }

    [Fact]
    public void Build_Hides_A_Cut_Off_Repeat_Of_A_Better_Read()
    {
        var rows = new[]
        {
            R("William Safire In Love with Norma Loqu , E", 0.77),
            R("WILLIAM | SAFIRE", 0.82),
            R("DICTIONARY", 0.96),
            R("DICTIONA | DICTIONARY", 0.76),
            R("Spanish Grammar", 0.9),                 // different spine: kept
            R("Spanish Dictionary", 0.9)               // neighbouring books are not merged
        };

        var (spines, hidden) = GetShelfUnmatchedHandler.Build(rows, includeHidden: false);

        Assert.Equal(new[] { "William Safire In Love with Norma Loqu , E", "DICTIONA | DICTIONARY", "Spanish Grammar", "Spanish Dictionary" }, spines.Select(s => s.Text));
        Assert.Equal(2, hidden);
        var dupReasons = GetShelfUnmatchedHandler.Build(rows, true).Spines.Where(s => s.LowQuality).Select(s => s.Reason).Distinct();
        Assert.Equal(new[] { "duplicate" }, dupReasons);
    }
}
