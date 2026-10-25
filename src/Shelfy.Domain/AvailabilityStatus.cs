namespace Shelfy.Domain;

/// <summary>Whether a ShelfBook is offered to others. Frozen term, see docs/domain.md.</summary>
public enum AvailabilityStatus
{
    Private,
    Loan,
    Sale
}
