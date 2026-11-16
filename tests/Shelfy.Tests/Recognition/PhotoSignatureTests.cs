using Shelfy.Features.Recognition.UploadShelfPhoto;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Recognition;

public class PhotoSignatureTests
{
    [Fact]
    public void Detects_Jpeg() => Assert.Equal(PhotoFormat.Jpeg, PhotoSignature.Detect(TestImages.Jpeg()));

    [Fact]
    public void Detects_Png() => Assert.Equal(PhotoFormat.Png, PhotoSignature.Detect(TestImages.Png()));

    [Theory]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })] // GIF
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 })]             // PDF
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C })]       // <html
    [InlineData(new byte[] { 0xFF, 0xD8 })]                         // truncated JPEG marker
    [InlineData(new byte[] { })]
    public void Rejects_Everything_Else(byte[] header) => Assert.Equal(PhotoFormat.Unknown, PhotoSignature.Detect(header));

    [Theory]
    [InlineData("shelf.jpg", PhotoFormat.Jpeg, true)]
    [InlineData("SHELF.JPEG", PhotoFormat.Jpeg, true)]
    [InlineData("shelf.png", PhotoFormat.Png, true)]
    [InlineData("shelf", PhotoFormat.Jpeg, true)]
    [InlineData(null, PhotoFormat.Png, true)]
    [InlineData("shelf.png", PhotoFormat.Jpeg, false)]
    [InlineData("shelf.jpg", PhotoFormat.Png, false)]
    [InlineData("shelf.jpg.exe", PhotoFormat.Jpeg, false)]
    [InlineData("shelf.heic", PhotoFormat.Jpeg, false)]
    public void Extension_Must_Agree_When_Present(string? name, PhotoFormat format, bool expected) =>
        Assert.Equal(expected, PhotoSignature.ExtensionMatches(name, format));
}

public class PhotoDimensionsTests
{
    [Fact]
    public void Reads_The_Size_Of_A_Png()
    {
        Assert.Equal((640, 480), PhotoDimensions.Read(TestImages.PngOfSize(640, 480), PhotoFormat.Png));
    }

    [Fact]
    public void Reads_The_Size_Of_A_Jpeg_Past_Its_App_Segments()
    {
        Assert.Equal((4032, 3024), PhotoDimensions.Read(TestImages.JpegOfSize(4032, 3024), PhotoFormat.Jpeg));
    }

    [Fact]
    public void Unknown_When_The_Headers_Do_Not_Say()
    {
        Assert.Null(PhotoDimensions.Read(TestImages.Jpeg(500), PhotoFormat.Jpeg));
        Assert.Null(PhotoDimensions.Read(TestImages.Png(500), PhotoFormat.Png));
        Assert.Null(PhotoDimensions.Read(new byte[] { 0xFF, 0xD8 }, PhotoFormat.Jpeg));
    }

    [Fact]
    public async Task Handler_Rejects_A_Tiny_Jpeg_And_Keeps_Nothing_But_Accepts_A_Normal_One()
    {
        var db = TestDb.Create();
        var storage = new InMemoryPhotoStorage();
        var user = new Shelfy.Domain.User { Id = Guid.NewGuid(), DisplayName = "Louis" };
        var shelf = new Shelfy.Domain.Shelf { Id = Guid.NewGuid(), UserId = user.Id };
        db.Users.Add(user);
        db.Shelves.Add(shelf);
        await db.SaveChangesAsync();
        var clock = new TestClock();
        var handler = new UploadShelfPhotoHandler(db, storage,
            new Shelfy.Features.Recognition.Worker.DbJobQueue(null!, Microsoft.Extensions.Options.Options.Create(new Shelfy.Features.Recognition.RecognitionOptions()), clock),
            Microsoft.Extensions.Options.Options.Create(new Shelfy.Features.Recognition.RecognitionOptions()), clock);

        var tiny = TestImages.JpegOfSize(30, 30);
        var refused = await handler.HandleAsync(new UploadShelfPhotoRequest(shelf.Id, "t.jpg", tiny.Length, new MemoryStream(tiny)));
        Assert.Equal(Shelfy.Features.Common.HandlerStatus.Invalid, refused.Status);
        Assert.Contains("too small (30 x 30", refused.Error);
        Assert.Empty(storage.Files);
        Assert.Empty(db.RecognitionJobs);

        var fine = TestImages.JpegOfSize(1200, 900);
        Assert.True((await handler.HandleAsync(new UploadShelfPhotoRequest(shelf.Id, "f.jpg", fine.Length, new MemoryStream(fine)))).IsSuccess);
    }
}

