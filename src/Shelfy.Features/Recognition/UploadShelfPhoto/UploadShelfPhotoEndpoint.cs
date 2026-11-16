using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Recognition.UploadShelfPhoto;

public static class UploadShelfPhotoEndpoint
{
    /// <summary>Request body cap: the 20 MB photo plus multipart overhead (nginx allows 25 MB).</summary>
    public const long RequestLimitBytes = 24L * 1024 * 1024;

    public static IEndpointRouteBuilder MapUploadShelfPhoto(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/shelves/{shelfId:guid}/photos",
                async (Guid shelfId, HttpRequest request, UploadShelfPhotoHandler handler, CancellationToken ct) =>
                {
                    if (!request.HasFormContentType)
                    {
                        return Results.Json(new { error = "Send the photo as multipart/form-data in a field named 'file'." },
                            statusCode: StatusCodes.Status415UnsupportedMediaType);
                    }

                    IFormFile? file;
                    try
                    {
                        file = (await request.ReadFormAsync(ct)).Files.GetFile("file");
                    }
                    catch (InvalidDataException ex) when (ex.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase))
                    {
                        // FormOptions.MultipartBodyLengthLimit: same friendly JSON as the handler's own size check.
                        return TooLarge();
                    }
                    catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
                    {
                        // Kestrel's request size limit (RequestSizeLimit below).
                        return TooLarge();
                    }
                    catch (InvalidDataException)
                    {
                        return Results.BadRequest(new { error = "The multipart form could not be read." });
                    }

                    if (file is null)
                    {
                        return Results.BadRequest(new { error = "A multipart form field named 'file' is required." });
                    }

                    // Optional batch fields: batchId (GUID), part and parts (integers).
                    Guid? batchId = null;
                    int part = 0, parts = 0;
                    var form = request.Form;
                    if (form.TryGetValue("batchId", out var rawBatch) && !string.IsNullOrWhiteSpace(rawBatch))
                    {
                        if (!Guid.TryParse(rawBatch.ToString(), out var parsed))
                        {
                            return Results.BadRequest(new { error = "batchId must be a GUID." });
                        }

                        if (!int.TryParse(form["part"], out part) || !int.TryParse(form["parts"], out parts))
                        {
                            return Results.BadRequest(new { error = "With a batchId, integer fields 'part' and 'parts' are required." });
                        }

                        batchId = parsed;
                    }

                    await using var content = file.OpenReadStream();
                    var result = await handler.HandleAsync(
                        new UploadShelfPhotoRequest(shelfId, file.FileName, file.Length, content, batchId, part, parts), ct);
                    return result.ToHttpResult(r => Results.Accepted(UploadShelfPhotoHandler.StatusUrl(r.JobId), r));
                })
            .AddEndpointFilter<ShelfOwnerFilter>()
            .WithName("UploadShelfPhoto")
            .WithTags("Recognition")
            .DisableAntiforgery() // API clients use bearer tokens (ADR-006), not cookies; there is no browser form to protect.
            .WithMetadata(new RequestSizeLimitAttribute(RequestLimitBytes))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<UploadShelfPhotoResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        return app;
    }

    private static IResult TooLarge() =>
        Results.Json(new { error = UploadShelfPhotoHandler.TooLargeMessage }, statusCode: StatusCodes.Status413PayloadTooLarge);
}
