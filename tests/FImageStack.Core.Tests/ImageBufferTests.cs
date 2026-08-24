using FImageStack.Core;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class ImageBufferTests
{
    [Fact]
    public unsafe void ImageBuffer_AllocationAndAccess_ShouldWorkCorrectly()
    {
        using var buffer = new ImageBuffer<float>(100, 50, 3, PixelFormatType.RgbFloat32);

        Assert.Equal(100, buffer.Width);
        Assert.Equal(50, buffer.Height);
        Assert.Equal(3, buffer.Channels);
        Assert.Equal(15000, buffer.TotalElements);
        Assert.Equal(15000 * sizeof(float), buffer.ByteSize);

        buffer.At(10, 20, 0) = 0.5f;
        buffer.At(10, 20, 1) = 0.75f;
        buffer.At(10, 20, 2) = 1.0f;

        Assert.Equal(0.5f, buffer.At(10, 20, 0));
        Assert.Equal(0.75f, buffer.At(10, 20, 1));
        Assert.Equal(1.0f, buffer.At(10, 20, 2));
    }

    [Fact]
    public void ImageBuffer_Clone_ShouldCreateIndependentCopy()
    {
        using var original = new ImageBuffer<float>(10, 10, 1);
        original.At(5, 5) = 42.0f;

        using var clone = original.Clone();
        Assert.Equal(42.0f, clone.At(5, 5));

        original.At(5, 5) = 100.0f;
        Assert.Equal(42.0f, clone.At(5, 5));
    }
}
