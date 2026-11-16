using Shelfy.Domain;

namespace Shelfy.Tests.Domain;

public class CopyCodeTests
{
    [Fact]
    public void New_codes_are_8_unambiguous_crockford_characters()
    {
        for (var i = 0; i < 1000; i++)
        {
            var code = CopyCode.New();
            Assert.Equal(8, code.Length);
            Assert.All(code, c => Assert.Contains(c, CopyCode.Alphabet));
            Assert.DoesNotContain('I', code);
            Assert.DoesNotContain('L', code);
            Assert.DoesNotContain('O', code);
            Assert.DoesNotContain('U', code);
        }
    }

    [Fact]
    public void New_codes_do_not_repeat_in_a_large_sample()
    {
        var codes = Enumerable.Range(0, 20000).Select(_ => CopyCode.New()).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Theory]
    [InlineData("7K3MQ9XA", "7K3MQ9XA")]
    [InlineData("7k3m-q9xa", "7K3MQ9XA")]
    [InlineData(" 7K3M Q9XA ", "7K3MQ9XA")]
    [InlineData("O0IL-1234", "00111234")]
    public void Normalize_is_forgiving(string input, string expected) => Assert.Equal(expected, CopyCode.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7K3MQ9X")]
    [InlineData("7K3MQ9XAB")]
    [InlineData("7K3MQ9XU")]
    [InlineData("7K3M/Q9X")]
    public void Normalize_rejects_what_cannot_be_a_code(string? input) => Assert.Null(CopyCode.Normalize(input));

    [Fact]
    public void Format_adds_the_dash() => Assert.Equal("7K3M-Q9XA", CopyCode.Format("7K3MQ9XA"));
}
