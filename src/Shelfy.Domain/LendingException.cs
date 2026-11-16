namespace Shelfy.Domain;

public enum LendingError
{
    /// <summary>The caller is not the party allowed to take this step.</summary>
    Forbidden,

    /// <summary>The step does not fit the current state.</summary>
    Conflict,

    /// <summary>The input is not acceptable.</summary>
    Invalid
}

/// <summary>A refused lending step. Handlers translate it to 403 / 409 / 400.</summary>
public class LendingException(LendingError error, string message) : Exception(message)
{
    public LendingError Error { get; } = error;

    public static LendingException Forbidden(string message) => new(LendingError.Forbidden, message);
    public static LendingException Conflict(string message) => new(LendingError.Conflict, message);
}
