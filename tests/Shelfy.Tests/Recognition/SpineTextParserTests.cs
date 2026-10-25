using System.Text.Json;
using Shelfy.Features.Recognition.Ocr;
using Shelfy.Features.Recognition.Parsing;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Recognition;

public class SpineTextParserTests
{
    private static OcrResult Result(params OcrLine[] lines) => new(1000, 800, lines);

    private static OcrLine V(string text, double cx, double top, double bottom, double thickness, double conf = 0.9) =>
        FakeTextRecognizer.VerticalLine(text, cx, top, bottom, thickness, conf);

    /// <summary>Text reading bottom-to-top (rotated counter-clockwise): polygon starts bottom-left and runs up.</summary>
    private static OcrLine VUp(string text, double cx, double top, double bottom, double thickness) =>
        new(text, new OcrPoint[]
        {
            new(cx - thickness / 2, bottom), new(cx - thickness / 2, top),
            new(cx + thickness / 2, top), new(cx + thickness / 2, bottom)
        }, Array.Empty<OcrWord>());

    private static OcrLine H(string text, double left, double right, double cy, double thickness) =>
        new(text, new OcrPoint[]
        {
            new(left, cy - thickness / 2), new(right, cy - thickness / 2),
            new(right, cy + thickness / 2), new(left, cy + thickness / 2)
        }, Array.Empty<OcrWord>());

    [Fact]
    public void Groups_Vertical_Lines_Into_Spines_With_Title_And_Author()
    {
        var spines = SpineTextParser.Parse(FakeTextRecognizer.CannedShelf());

        Assert.Equal(3, spines.Count); // the bestseller banner is noise
        Assert.Collection(spines,
            s => { Assert.Equal("The Hobbit", s.Title); Assert.Equal("J.R.R. Tolkien", s.Author); Assert.Equal(0, s.Position); },
            s => { Assert.Equal("Dune", s.Title); Assert.Equal("Frank Herbert", s.Author); },
            s => { Assert.Equal("Foundation", s.Title); Assert.Equal("Isaac Asimov", s.Author); });
    }

    [Fact]
    public void Keeps_Raw_Text_And_Confidence()
    {
        var spine = SpineTextParser.Parse(FakeTextRecognizer.CannedShelf())[0];

        Assert.Contains("THE HOBBIT", spine.RawText);
        Assert.Contains("J.R.R. TOLKIEN", spine.RawText);
        Assert.Equal(0.95, spine.Confidence);
    }

    [Fact]
    public void Parses_A_Live_Azure_Vision_Response_With_Rotated_Spines()
    {
        // Captured from the real Image Analysis 4.0 service (api-version 2023-10-01) on a synthetic shelf image:
        // two spines rotated clockwise and one counter-clockwise, lines returned out of left-to-right order.
        using var json = JsonDocument.Parse(Fixtures.Read("AzureVision/read-live-rotated-shelf.json"));
        var ocr = AzureVisionTextRecognizer.Parse(json.RootElement);

        Assert.Equal(560, ocr.ImageWidth);
        Assert.Equal(700, ocr.ImageHeight);
        Assert.Equal(6, ocr.Lines.Count);
        Assert.All(ocr.Lines, l => { Assert.Equal(4, l.Polygon.Count); Assert.NotEmpty(l.Words); });

        var spines = SpineTextParser.Parse(ocr);

        Assert.Equal(3, spines.Count);
        Assert.Collection(spines,
            s => { Assert.Equal("The Hobbit", s.Title); Assert.Equal("J.R.R. Tolkien", s.Author); },
            s => { Assert.Equal("Dune", s.Title); Assert.Equal("Frank Herbert", s.Author); },
            s => { Assert.Equal("Foundation", s.Title); Assert.Equal("Isaac Asimov", s.Author); });
    }

    [Fact]
    public void Parses_A_Live_Azure_Vision_Response_With_Horizontal_Text()
    {
        using var json = JsonDocument.Parse(Fixtures.Read("AzureVision/read-live-horizontal.json"));
        var ocr = AzureVisionTextRecognizer.Parse(json.RootElement);

        Assert.Equal(new[] { "THE HOBBIT", "J.R.R. TOLKIEN" }, ocr.Lines.Select(l => l.Text));
        Assert.All(ocr.Lines, l => Assert.Equal(4, l.Polygon.Count));
        // Top-left, top-right, bottom-right, bottom-left: text runs left to right.
        var first = ocr.Lines[0].Polygon;
        Assert.True(first[1].X > first[0].X && first[2].Y > first[1].Y);
        Assert.Equal(new[] { "THE", "HOBBIT" }, ocr.Lines[0].Words.Select(w => w.Text));
        Assert.All(ocr.Lines.SelectMany(l => l.Words), w => Assert.InRange(w.Confidence!.Value, 0.5, 1.0));
    }

    [Fact]
    public void Parses_The_Recorded_Azure_Vision_Response()
    {
        using var json = JsonDocument.Parse(Fixtures.Read("AzureVision/read-vertical-spines.json"));
        var ocr = AzureVisionTextRecognizer.Parse(json.RootElement);

        var spines = SpineTextParser.Parse(ocr);

        Assert.Equal(2, spines.Count); // Penguin Classics is a publisher line
        Assert.Equal(("The Hobbit", "J.R.R. Tolkien"), (spines[0].Title, spines[0].Author));
        Assert.Equal(("Dune", "Frank Herbert"), (spines[1].Title, spines[1].Author));
        Assert.InRange(spines[0].Confidence!.Value, 0.9, 0.99);
    }

    [Fact]
    public void Orders_Spines_Left_To_Right()
    {
        var ocr = Result(V("ZULU", 800, 50, 300, 40), V("ALPHA", 100, 50, 300, 40), V("MIKE", 450, 50, 300, 40));

        Assert.Equal(new[] { "Alpha", "Mike", "Zulu" }, SpineTextParser.Parse(ocr).Select(s => s.Title));
    }

    [Fact]
    public void Does_Not_Merge_Lines_Of_Different_Books()
    {
        var ocr = Result(V("Neuromancer", 100, 50, 400, 40), V("Snow Crash", 200, 50, 400, 40));

        var spines = SpineTextParser.Parse(ocr);

        Assert.Equal(2, spines.Count);
        Assert.All(spines, s => Assert.Null(s.Author));
    }

    [Fact]
    public void Swaps_When_The_Larger_Line_Is_The_Author()
    {
        var ocr = Result(V("STEPHEN KING", 100, 50, 450, 44), V("The Stand", 144, 100, 350, 26));

        var spine = Assert.Single(SpineTextParser.Parse(ocr));

        Assert.Equal("The Stand", spine.Title);
        Assert.Equal("Stephen King", spine.Author);
    }

    [Fact]
    public void Joins_A_Wrapped_Title_Of_Similar_Size()
    {
        // Text rotated clockwise: the first line is on the right, the next line to its left.
        var ocr = Result(V("THE LORD OF", 140, 50, 450, 40), V("THE RINGS", 100, 50, 400, 40), V("J.R.R. TOLKIEN", 176, 100, 400, 22));

        var spine = Assert.Single(SpineTextParser.Parse(ocr));

        Assert.Equal("The Lord of the Rings", spine.Title);
        Assert.Equal("J.R.R. Tolkien", spine.Author);
    }

    [Fact]
    public void Reads_Counter_Clockwise_Text_In_Order()
    {
        var ocr = Result(VUp("LA PESTE", 100, 50, 400, 40), VUp("Albert Camus", 140, 100, 350, 22));

        var spine = Assert.Single(SpineTextParser.Parse(ocr));

        Assert.Equal("La Peste", spine.Title);
        Assert.Equal("Albert Camus", spine.Author);
    }

    [Fact]
    public void Handles_Horizontal_Stacked_Spine_Text()
    {
        var ocr = Result(H("DUNE", 100, 300, 100, 50), H("Frank Herbert", 110, 290, 145, 24));

        var spine = Assert.Single(SpineTextParser.Parse(ocr));

        Assert.Equal("Dune", spine.Title);
        Assert.Equal("Frank Herbert", spine.Author);
    }

    [Fact]
    public void Merges_A_Publisher_Line_At_The_Other_End_Of_The_Same_Column_Without_Using_It()
    {
        var ocr = Result(V("Hyperion", 100, 50, 300, 40), V("Dan Simmons", 140, 50, 250, 22), V("Del Rey", 100, 330, 400, 20));

        var spine = Assert.Single(SpineTextParser.Parse(ocr));

        Assert.Equal("Hyperion", spine.Title);
        Assert.Equal("Dan Simmons", spine.Author);
        Assert.Contains("Del Rey", spine.RawText);
    }

    [Theory]
    [InlineData("The Hobbit by J.R.R. Tolkien", "The Hobbit", "J.R.R. Tolkien")]
    [InlineData("Dune - Frank Herbert", "Dune", "Frank Herbert")]
    [InlineData("Frank Herbert \u2014 Dune", "Dune", "Frank Herbert")]
    public void Splits_Title_And_Author_Printed_On_One_Line(string line, string title, string author)
    {
        var spine = Assert.Single(SpineTextParser.Parse(Result(OcrLine.TextOnly(line))));

        Assert.Equal(title, spine.Title);
        Assert.Equal(author, spine.Author);
    }

    [Fact]
    public void Lines_Without_Geometry_Become_One_Spine_Each()
    {
        var spines = SpineTextParser.Parse(Result(OcrLine.TextOnly("Dune"), OcrLine.TextOnly("Neuromancer")));

        Assert.Equal(new[] { "Dune", "Neuromancer" }, spines.Select(s => s.Title));
        Assert.Equal(new[] { 0, 1 }, spines.Select(s => s.Position));
        Assert.All(spines, s => Assert.Null(s.Confidence));
    }

    [Theory]
    [InlineData("NEW YORK TIMES BESTSELLER")]
    [InlineData("A NOVEL")]
    [InlineData("PENGUIN CLASSICS")]
    [InlineData("12345")]
    [InlineData("#")]
    [InlineData("  ")]
    [InlineData("WINNER OF THE PULITZER PRIZE")]
    public void Drops_Noise_Lines(string noise) =>
        Assert.Empty(SpineTextParser.Parse(Result(OcrLine.TextOnly(noise))));

    [Fact]
    public void Extracts_An_Isbn_And_Keeps_It_Out_Of_The_Title()
    {
        var ocr = Result(V("The Hobbit", 100, 50, 400, 40), V("ISBN 978-0-547-92822-7", 135, 100, 380, 22));

        var spine = Assert.Single(SpineTextParser.Parse(ocr));

        Assert.Equal("9780547928227", spine.Isbn);
        Assert.Equal("The Hobbit", spine.Title);
        Assert.Null(spine.Author);
    }

    [Fact]
    public void A_Lone_Isbn_Is_Still_A_Candidate()
    {
        var spine = Assert.Single(SpineTextParser.Parse(Result(OcrLine.TextOnly("9780547928227"))));

        Assert.Equal("9780547928227", spine.Isbn);
        Assert.Equal("", spine.Title);
    }

    [Fact]
    public void Title_Cases_All_Caps_But_Leaves_Mixed_Case_Alone()
    {
        var spines = SpineTextParser.Parse(Result(
            OcrLine.TextOnly("THE LORD OF THE RINGS"), OcrLine.TextOnly("iPhone Hacks"), OcrLine.TextOnly("WHO MOVED MY CHEESE?")));

        Assert.Equal(new[] { "The Lord of the Rings", "iPhone Hacks", "Who Moved My Cheese?" }, spines.Select(s => s.Title));
    }

    [Fact]
    public void Trims_Decoration_And_Collapses_Whitespace()
    {
        var spine = Assert.Single(SpineTextParser.Parse(Result(OcrLine.TextOnly("  | \u2018Slaughterhouse   Five\u2019 - "))));

        Assert.Equal("Slaughterhouse Five", spine.Title);
    }

    [Fact]
    public void Bounds_The_Number_Of_Spines_And_The_Raw_Text()
    {
        var lines = Enumerable.Range(0, 40).Select(i => OcrLine.TextOnly($"Book Number {i} {new string('x', 2000)}")).ToArray();

        var spines = SpineTextParser.Parse(Result(lines), maxSpines: 25);

        Assert.Equal(25, spines.Count);
        Assert.All(spines, s =>
        {
            Assert.True(s.RawText.Length <= SpineTextParser.MaxRawTextLength);
            Assert.True(s.Title.Length <= 500);
        });
    }

    [Fact]
    public void Empty_Input_Gives_No_Spines() => Assert.Empty(SpineTextParser.Parse(Result()));

    [Theory]
    [InlineData("J.R.R. Tolkien", true)]
    [InlineData("FRANK HERBERT", true)]
    [InlineData("Ursula K. Le Guin", true)]
    [InlineData("Gabriel Garc\u00EDa M\u00E1rquez", true)]
    [InlineData("Martin Luther King Jr.", true)]
    [InlineData("The Hobbit", false)]
    [InlineData("Dune", false)]
    [InlineData("Catch-22", false)]
    [InlineData("1984", false)]
    [InlineData("Of Mice and Men", false)]
    public void Recognizes_Author_Like_Text(string text, bool expected) =>
        Assert.Equal(expected, TextNormalizer.LooksLikeAuthorName(text));
}
