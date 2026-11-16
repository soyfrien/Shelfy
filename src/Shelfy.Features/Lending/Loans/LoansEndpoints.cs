using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Lending.Loans;

public static class LoansEndpoints
{
    public static IEndpointRouteBuilder MapLoans(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/copies/{copyId:guid}/borrow", async (Guid copyId, AskRequest? request, ClaimsPrincipal user, LoansHandler handler, CancellationToken ct) =>
                (await handler.BorrowAsync(user.UserId(), copyId, request ?? new AskRequest(), ct)).ToHttpResult(l => Results.Created($"/api/loans/{l.Id}", l)))
            .RequireAuthorization()
            .WithName("RequestBorrow")
            .WithTags("Loans")
            .Produces<LoanDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/copies/{copyId:guid}/reserve", async (Guid copyId, AskRequest? request, ClaimsPrincipal user, LoansHandler handler, CancellationToken ct) =>
                (await handler.ReserveAsync(user.UserId(), copyId, request ?? new AskRequest(), ct)).ToHttpResult(l => Results.Created($"/api/loans/{l.Id}", l)))
            .RequireAuthorization()
            .WithName("ReserveCopy")
            .WithTags("Loans")
            .Produces<LoanDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/copies/by-code/{code}/received", async (string code, ReceiveRequest? request, ClaimsPrincipal user, LoansHandler handler, CancellationToken ct) =>
                (await handler.ReceiveByCodeAsync(user.UserId(), code, request ?? new ReceiveRequest(), ct)).ToHttpResult())
            .RequireAuthorization()
            .WithName("ReceiveCopyByCode")
            .WithTags("Loans")
            .Produces<LoanDto>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/api/loans", async (bool? all, ClaimsPrincipal user, LoansHandler handler, CancellationToken ct) =>
                (await handler.MineAsync(user.UserId(), all == true, ct)).ToHttpResult())
            .RequireAuthorization()
            .WithName("GetMyLoans")
            .WithTags("Loans")
            .Produces<MyLoansResponse>();

        app.MapGet("/api/loans/{loanId:guid}", async (Guid loanId, ClaimsPrincipal user, LoansHandler handler, CancellationToken ct) =>
                (await handler.GetAsync(user.UserId(), loanId, ct)).ToHttpResult())
            .RequireAuthorization()
            .WithName("GetLoan")
            .WithTags("Loans")
            .Produces<LoanDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // approve | decline | cancel | mailed | received | request-return | return-mailed | returned | pass-along
        app.MapPost("/api/loans/{loanId:guid}/{action}", async (Guid loanId, string action, ReceiveRequest? request, ClaimsPrincipal user, LoansHandler handler, CancellationToken ct) =>
                (await handler.ActAsync(user.UserId(), loanId, action, request, ct)).ToHttpResult())
            .RequireAuthorization()
            .WithName("LoanStep")
            .WithTags("Loans")
            .Produces<LoanDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
