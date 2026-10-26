using Microsoft.AspNetCore.Http;

namespace Shelfy.Features.Common;

public static class HandlerResultExtensions
{
    /// <summary>Maps a handler outcome to an HTTP result.</summary>
    public static IResult ToHttpResult<T>(this HandlerResult<T> result, Func<T, IResult>? onSuccess = null) =>
        result.Status switch
        {
            HandlerStatus.Ok => onSuccess is null ? Results.Ok(result.Value) : onSuccess(result.Value!),
            HandlerStatus.NotFound => Results.NotFound(new { error = result.Error }),
            HandlerStatus.Conflict => Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status409Conflict),
            HandlerStatus.TooLarge => Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status413PayloadTooLarge),
            HandlerStatus.UnsupportedMediaType => Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status415UnsupportedMediaType),
            _ => Results.BadRequest(new { error = result.Error })
        };
}
