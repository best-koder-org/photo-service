using PhotoService.Services;

namespace PhotoService.Tests.Services;

public class ImageProcessingServiceTests
{
    // CalculateOptimalDimensions tests

    [Fact]
    public void CalculateOptimalDimensions_SmallImage_NoResize()
    {
        var (w, h, resized) = ImageProcessingService.CalculateOptimalDimensions(400, 300);
        Assert.Equal(400, w);
        Assert.Equal(300, h);
        Assert.False(resized);
    }

    [Fact]
    public void CalculateOptimalDimensions_ExactMaxDimension_NoResize()
    {
        var (w, h, resized) = ImageProcessingService.CalculateOptimalDimensions(800, 600);
        Assert.Equal(800, w);
        Assert.Equal(600, h);
        Assert.False(resized);
    }

    [Fact]
    public void CalculateOptimalDimensions_TooWide_ScalesDown()
    {
        var (w, h, resized) = ImageProcessingService.CalculateOptimalDimensions(1600, 600);
        Assert.True(resized);
        Assert.True(w <= 800);
        Assert.True(h <= 800);
    }

    [Fact]
    public void CalculateOptimalDimensions_TooTall_ScalesDown()
    {
        var (w, h, resized) = ImageProcessingService.CalculateOptimalDimensions(600, 1600);
        Assert.True(resized);
        Assert.True(w <= 800);
        Assert.True(h <= 800);
    }

    [Fact]
    public void CalculateOptimalDimensions_TooManyPixels_ScalesDown()
    {
        // 2000x2000 = 4MP, way over the 1MP limit
        var (w, h, resized) = ImageProcessingService.CalculateOptimalDimensions(2000, 2000);
        Assert.True(resized);
        Assert.True(w * h <= 1_000_000);
    }

    [Fact]
    public void CalculateOptimalDimensions_PreservesAspectRatio()
    {
        var (w, h, _) = ImageProcessingService.CalculateOptimalDimensions(1600, 800);
        double ratio = (double)w / h;
        Assert.InRange(ratio, 1.9, 2.1); // Original is 2:1
    }

    [Fact]
    public void CalculateOptimalDimensions_MinimumDimensions()
    {
        // Very small but wide image
        var (w, h, _) = ImageProcessingService.CalculateOptimalDimensions(10000, 50);
        Assert.True(w >= 200 || h >= 200, "Should enforce minimum dimensions");
    }

    // DetermineOptimalFormat tests

    [Theory]
    [InlineData("photo.png", "PNG")]
    [InlineData("image.webp", "WebP")]
    [InlineData("photo.jpg", "JPEG")]
    [InlineData("photo.jpeg", "JPEG")]
    [InlineData("photo.bmp", "JPEG")]
    [InlineData("photo.gif", "JPEG")]
    public void DetermineOptimalFormat_ReturnsCorrectFormat(string filename, string expected)
    {
        var result = ImageProcessingService.DetermineOptimalFormat(filename);
        Assert.Equal(expected, result);
    }

    // GetExtensionForFormat tests

    [Theory]
    [InlineData("PNG", ".png")]
    [InlineData("WEBP", ".webp")]
    [InlineData("JPEG", ".jpg")]
    [InlineData("unknown", ".jpg")]
    public void GetExtensionForFormat_ReturnsCorrectExtension(string format, string expected)
    {
        var result = ImageProcessingService.GetExtensionForFormat(format);
        Assert.Equal(expected, result);
    }

    // GetExpectedFormatsForExtension tests

    [Theory]
    [InlineData(".jpg", new[] { "JPEG", "JPG" })]
    [InlineData(".jpeg", new[] { "JPEG", "JPG" })]
    [InlineData(".png", new[] { "PNG" })]
    [InlineData(".bmp", new string[] { })]
    public void GetExpectedFormatsForExtension_ReturnsCorrectFormats(string ext, string[] expected)
    {
        var result = ImageProcessingService.GetExpectedFormatsForExtension(ext);
        Assert.Equal(expected, result);
    }
}
