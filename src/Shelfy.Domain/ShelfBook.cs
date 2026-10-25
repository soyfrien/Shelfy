namespace Shelfy.Domain;

/// <summary>One book on one shelf, with its availability status and optional price.</summary>
public class ShelfBook
{
    public Guid ShelfId { get; set; }
    public Guid BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>New books default to Private.</summary>
    public AvailabilityStatus Status { get; private set; } = AvailabilityStatus.Private;

    /// <summary>Only meaningful when <see cref="Status"/> is Sale.</summary>
    public decimal? SalePrice { get; private set; }

    public void MakePrivate()
    {
        Status = AvailabilityStatus.Private;
        SalePrice = null;
    }

    public void OfferForLoan()
    {
        Status = AvailabilityStatus.Loan;
        SalePrice = null;
    }

    public void OfferForSale(decimal price)
    {
        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Sale price cannot be negative.");
        }

        Status = AvailabilityStatus.Sale;
        SalePrice = price;
    }
}
