namespace Shelfy.Features.Common;

public enum HandlerStatus
{
    Ok,
    Invalid,
    NotFound,
    TooLarge,
    UnsupportedMediaType
}

/// <summary>Outcome of a slice handler. Endpoints translate it to HTTP; they hold no business logic.</summary>
public record HandlerResult<T>(HandlerStatus Status, T? Value = default, string? Error = null)
{
    public bool IsSuccess => Status == HandlerStatus.Ok;

    public static HandlerResult<T> Ok(T value) => new(HandlerStatus.Ok, value);
    public static HandlerResult<T> Invalid(string error) => new(HandlerStatus.Invalid, default, error);
    public static HandlerResult<T> NotFound(string error) => new(HandlerStatus.NotFound, default, error);
    public static HandlerResult<T> TooLarge(string error) => new(HandlerStatus.TooLarge, default, error);
    public static HandlerResult<T> UnsupportedMediaType(string error) => new(HandlerStatus.UnsupportedMediaType, default, error);
}
