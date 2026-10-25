using Shelfy.Domain;
using Shelfy.Features.Recognition.Parsing;
using Shelfy.Features.Recognition.Worker;

namespace Shelfy.Tests.Recognition;

public class SpineDeduperTests
{
    [Theory]
    [InlineData("Foundation | Isaac Asimov", "FOUNDATION | ISAAC ASIMOV")]          // same spine, case
    [InlineData("Neuromancer | William Gibson", "Neuromancer | Wiliam Gibson")]      // OCR dropped a letter
    [InlineData("The Hobbit | J.R.R. Tolkien", "J.R.R. TOLKIEN | THE HOBBIT")]       // lines in another order
    [InlineData("Foundation | Isaac Asimov", "Isaac Asimov")]                        // the title line was cut off
    public void Same_Spine_Reads_Are_Detected(string a, string b)
    {
        Assert.True(SpineDeduper.IsSameSpine(a, b));
        Assert.True(SpineDeduper.IsSameSpine(b, a));
    }

    [Theory]
    [InlineData("Foundation", "Foundati")]                                           // title cut by the tile edge
    [InlineData("Foundation | Isaac Asimov", "Foundat")]
    [InlineData("Dune", "Dune")]
    [InlineData("Second Foundation | Isaac", "Second Foundation | Isaac Asimov")]    // the end of the text is cut
    public void A_Word_Cut_By_The_Edge_Or_A_Truncated_Text_Matches_The_Whole(string a, string b)
    {
        Assert.True(SpineDeduper.IsSameSpine(a, b));
        Assert.True(SpineDeduper.IsSameSpine(b, a));
    }

    [Theory]
    [InlineData("Dune", "Dune Messiah")]                                             // neighbouring books of a series
    [InlineData("Foundation", "Foundation and Empire")]
    [InlineData("Foundation | Isaac Asimov", "Foundation and Empire | Isaac Asimov")]
    [InlineData("The Hobbit | Tolkien", "The Silmarillion | Tolkien")]
    [InlineData("Neuromancer", "Count Zero")]
    [InlineData("Foundation | Isaac Asimov", "Foundation")]                          // one exact word is too little evidence (known limitation)
    [InlineData("Dune", "")]
    [InlineData("", "")]
    public void Different_Books_Are_Not_Merged(string a, string b)
    {
        Assert.False(SpineDeduper.IsSameSpine(a, b));
        Assert.False(SpineDeduper.IsSameSpine(b, a));
    }

    [Fact]
    public void Quality_Prefers_Longer_And_More_Confident_Reads()
    {
        Assert.True(SpineDeduper.Quality("Foundation | Isaac Asimov", 0.9) > SpineDeduper.Quality("Isaac Asimov", 0.9));
        Assert.True(SpineDeduper.Quality("Foundation", 0.95) > SpineDeduper.Quality("Foundation", 0.5));
        Assert.True(SpineDeduper.Quality("Foundation", null) > 0);
    }

    [Theory]
    [InlineData("", null, true)]
    [InlineData("A 7", 0.9, true)]
    [InlineData("xy ab", 0.9, true)]
    [InlineData("Foundation", 0.2, true)]       // too unsure
    [InlineData("Foundation", 0.9, false)]
    [InlineData("Dune", null, false)]
    public void Junk_Fragments_Are_Recognized(string text, double? confidence, bool junk) =>
        Assert.Equal(junk, SpineDeduper.IsJunkFragment(text, confidence));

    private static DetectedSpine Spine(string raw, double? conf = 0.9, Book? book = null) => new()
    {
        Id = Guid.NewGuid(), RawText = raw, Confidence = conf,
        Status = book is null ? SpineResolutionStatus.Unresolved : SpineResolutionStatus.Resolved,
        Book = book, BookId = book?.Id
    };

    private static SpineCandidate Candidate(string raw, double? conf = 0.9) => new(0, raw, raw, null, null, conf);

    [Fact]
    public void A_Read_That_Repeats_A_Peer_At_Equal_Or_Lower_Quality_Is_Dropped()
    {
        var peers = new List<DetectedSpine> { Spine("Second Foundation | Isaac Asimov") };

        Assert.False(RecognitionJobProcessor.KeepUnresolved(Candidate("Second Foundation | Isaac Asimov"), peers, out var sup));
        Assert.Empty(sup);
        Assert.False(RecognitionJobProcessor.KeepUnresolved(Candidate("Second Foundation | Isaac"), peers, out _));
    }

    [Fact]
    public void A_Better_Read_Replaces_The_Fragment_From_The_Neighbouring_Tile()
    {
        var fragment = Spine("Second Foundation | Isaac");
        var other = Spine("Neuromancer | William Gibson");
        var peers = new List<DetectedSpine> { fragment, other };

        Assert.True(RecognitionJobProcessor.KeepUnresolved(Candidate("Second Foundation | Isaac Asimov"), peers, out var sup));

        Assert.Equal(new[] { fragment }, sup);
    }

    [Fact]
    public void A_Read_Explained_By_A_Resolved_Peer_Is_Dropped_Even_When_It_Is_Longer()
    {
        var book = new Book { Id = Guid.NewGuid(), Isbn = "9780553293357", Title = "Foundation", Author = "Isaac Asimov" };
        var peers = new List<DetectedSpine> { Spine("Foundation", 0.5, book) };

        Assert.False(RecognitionJobProcessor.KeepUnresolved(Candidate("FOUNDATION | ISAAC ASIMOV | PAPERBACK EDITION", 0.99), peers, out var sup));
        Assert.Empty(sup);
    }

    [Fact]
    public void A_New_Spine_Is_Kept()
    {
        var peers = new List<DetectedSpine> { Spine("Neuromancer | William Gibson") };

        Assert.True(RecognitionJobProcessor.KeepUnresolved(Candidate("Count Zero | William Gibson"), peers, out var sup));
        Assert.Empty(sup);
    }
}
