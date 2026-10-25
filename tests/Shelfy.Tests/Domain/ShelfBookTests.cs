using Shelfy.Domain;

namespace Shelfy.Tests.Domain;

public class ShelfBookTests
{
    [Fact]
    public void New_ShelfBook_Defaults_To_Private_Without_Price()
    {
        var shelfBook = new ShelfBook { ShelfId = Guid.NewGuid(), BookId = Guid.NewGuid() };

        Assert.Equal(AvailabilityStatus.Private, shelfBook.Status);
        Assert.Null(shelfBook.SalePrice);
    }

    [Fact]
    public void OfferForSale_Sets_Status_And_Price()
    {
        var shelfBook = new ShelfBook();

        shelfBook.OfferForSale(12.50m);

        Assert.Equal(AvailabilityStatus.Sale, shelfBook.Status);
        Assert.Equal(12.50m, shelfBook.SalePrice);
    }

    [Fact]
    public void OfferForSale_Rejects_Negative_Price()
    {
        var shelfBook = new ShelfBook();

        Assert.Throws<ArgumentOutOfRangeException>(() => shelfBook.OfferForSale(-1m));
    }

    [Fact]
    public void OfferForLoan_Clears_Price()
    {
        var shelfBook = new ShelfBook();
        shelfBook.OfferForSale(5m);

        shelfBook.OfferForLoan();

        Assert.Equal(AvailabilityStatus.Loan, shelfBook.Status);
        Assert.Null(shelfBook.SalePrice);
    }

    [Fact]
    public void MakePrivate_Clears_Price()
    {
        var shelfBook = new ShelfBook();
        shelfBook.OfferForSale(5m);

        shelfBook.MakePrivate();

        Assert.Equal(AvailabilityStatus.Private, shelfBook.Status);
        Assert.Null(shelfBook.SalePrice);
    }

    [Fact]
    public void AvailabilityStatus_Has_The_Frozen_Values()
    {
        Assert.Equal(["Private", "Loan", "Sale"], Enum.GetNames<AvailabilityStatus>());
    }
}
