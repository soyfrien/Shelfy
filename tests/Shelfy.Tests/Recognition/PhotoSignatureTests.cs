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
