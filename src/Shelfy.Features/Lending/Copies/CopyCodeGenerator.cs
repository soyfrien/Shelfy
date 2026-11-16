using Shelfy.Domain;

namespace Shelfy.Features.Lending.Copies;

/// <summary>Source of new copy codes. Replaceable in tests to force a collision.</summary>
public interface ICopyCodeGenerator
{
    string Next();
}

public sealed class RandomCopyCodeGenerator : ICopyCodeGenerator
{
    public string Next() => CopyCode.New();
}
