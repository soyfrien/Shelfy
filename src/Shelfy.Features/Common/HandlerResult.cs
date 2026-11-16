namespace Shelfy.Features.Common;

public enum HandlerStatus
{
    Ok,
    Invalid,
    NotFound,
    Conflict,
    TooLarge,
    UnsupportedMediaType,
    Unauthorized,
    Forbidden
}

/// <summary>Outcome of a slice handler. Endpoints translate it to HTTP; they hold no business logic.</summary>
public record HandlerResult<T>(HandlerStatus Status, T? Value = default, string? Error = null)
{
    public bool IsSuccess => Status == HandlerStatus.Ok;

    public static HandlerResult<T> Ok(T value) => new(HandlerStatus.Ok, value);
    public static HandlerResult<T> Invalid(string error) => new(HandlerStatus.Invalid, default, error);
    public static HandlerResult<T> NotFound(string error) => new(HandlerStatus.NotFound, default, error);
    public static HandlerResult<T> Conflict(string error) => new(HandlerStatus.Conflict, default, error);
    public static HandlerResult<T> TooLarge(string error) => new(HandlerStatus.TooLarge, default, error);
    public static HandlerResult<T> UnsupportedMediaType(string error) => new(HandlerStatus.UnsupportedMediaType, default, error);
    public static HandlerResult<T> Unauthorized(string error) => new(HandlerStatus.Unauthorized, default, error);
    public static HandlerResult<T> Forbidden(string error) => new(HandlerStatus.Forbidden, default, error);

    /// <summary>Translates a refused lending step (ADR-012).</summary>
    public static HandlerResult<T> From(Shelfy.Domain.LendingException ex) => ex.Error switch
    {
        Shelfy.Domain.LendingError.Forbidden => Forbidden(ex.Message),
        Shelfy.Domain.LendingError.Conflict => Conflict(ex.Message),
        _ => Invalid(ex.Message)
    };
}
