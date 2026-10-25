using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Recognition.GetRecognitionJob;

public static class GetRecognitionJobEndpoint
{
    public static IEndpointRouteBuilder MapGetRecognitionJob(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/recognition-jobs/{jobId:guid}", async (Guid jobId, GetRecognitionJobHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(jobId, ct)).ToHttpResult())
            .WithName("GetRecognitionJob")
            .WithTags("Recognition")
            .Produces<RecognitionJobResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
