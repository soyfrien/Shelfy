using Shelfy.Domain;

namespace Shelfy.Features.Lending;

// Requests
public record RegisterCopyRequest(string? Condition = null, string? Notes = null, bool AllowPassAlong = false);

public record UpdateCopyRequest(bool? Available = null, bool? AllowPassAlong = null, string? Condition = null, string? Notes = null);

public record AskRequest(string? Message = null);

public record ReceiveRequest(Guid? ShelfId = null);

// Responses

/// <summary>What the caller may do with a copy right now (all false for an anonymous caller).</summary>
/// <summary><see cref="AwaitingReturn"/>: the viewer owns the copy and it has been mailed back to them (show "I got it back").</summary>
public record CopyViewerDto(bool SignedIn, bool IsOwner, bool IsHolder, bool CanBorrow, bool CanReserve, LoanSummaryDto? MyLoan, bool AwaitingReturn = false);

/// <summary>A copy on a shelf page.</summary>
public record ShelfCopyDto(
    Guid Id, string Code, Guid BookId, string Title, string Author, string? CoverUrl, CopyStatus Status,
    string OwnerName, bool IsHome, bool IsVisiting, int Waiting, CopyViewerDto Viewer);

/// <summary>One step of a copy's journey. Names only: no addresses and no shelf ids of borrowers.</summary>
public record MovementDto(CopyMovementKind Kind, string? From, string To, DateTime StartedAt, DateTime? ReceivedAt);

public record CopyDetailResponse(
    Guid Id, string Code, string DisplayCode, Guid BookId, string Title, string Author, string? Isbn, string? CoverUrl,
    CopyStatus Status, string OwnerName, Guid? HomeShelfId, string HolderName, bool AllowPassAlong, string? Condition,
    string? Notes, int Waiting, DateTime CreatedAt, IReadOnlyList<MovementDto> History, CopyViewerDto Viewer);

public record LoanSummaryDto(Guid Id, LoanStatus Status, IReadOnlyList<string> Actions);

/// <summary>
/// A loan as one of its parties sees it. <see cref="ShipTo"/> is the address the caller needs to mail to (or the other party's
/// address while the loan is approved and in progress); null otherwise. Never given to anyone outside the loan.
/// </summary>
public record LoanDto(
    Guid Id, LoanStatus Status, string Role, Guid CopyId, string CopyCode, string Title, string Author, string? CoverUrl,
    string OwnerName, string BorrowerName, string? SenderName, bool IsPassAlong, int? WaitlistPosition, string? Message,
    string? ShipToName, string? ShipTo, IReadOnlyList<string> Actions,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? MailedAt, DateTime? ReceivedAt);

public record MyLoansResponse(IReadOnlyList<LoanDto> Borrowing, IReadOnlyList<LoanDto> Lending);
