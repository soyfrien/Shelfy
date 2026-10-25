using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Recognition.GetUploadBatch;

public static class GetUploadBatchEndpoint
{
    public static IEndpointRouteBuilder MapGetUploadBatch(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shelves/{shelfId:guid}/batches/{batchId:guid}",
                async (Guid shelfId, Guid batchId, GetUploadBatchHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(shelfId, batchId, ct)).ToHttpResult())
            .WithName("GetUploadBatch")
            .WithTags("Recognition")
            .Produces<UploadBatchResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
