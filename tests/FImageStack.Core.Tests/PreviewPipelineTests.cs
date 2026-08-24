using FImageStack.Core;
using FImageStack.Core.Models;
using FImageStack.Infrastructure.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace FImageStack.Core.Tests;

public class PreviewPipelineTests
{
    [Fact]
    public void ImageSharpIO_ShouldDownsampleImageWhenMaxDimensionSpecified()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"test_hires_{Guid.NewGuid():N}.png");
        try
        {
            // Create a high resolution 2048x1536 synthetic image
            using (var img = new Image<Rgb24>(2048, 1536))
            {
                img.Save(tempPath);
            }

            var io = new ImageSharpIO();

            // 1. Load with Fast Preview maxDimension = 1024
            using var previewFrame = io.LoadFrame(tempPath, 0, maxDimension: 1024);
            Assert.NotNull(previewFrame.ColorBuffer);
            Assert.True(previewFrame.Width <= 1024);
            Assert.True(previewFrame.Height <= 1024);
            Assert.Equal(1024, Math.Max(previewFrame.Width, previewFrame.Height));

            // 2. Load with Full Resolution (maxDimension = 0)
            using var fullFrame = io.LoadFrame(tempPath, 0, maxDimension: 0);
            Assert.NotNull(fullFrame.ColorBuffer);
            Assert.Equal(2048, fullFrame.Width);
            Assert.Equal(1536, fullFrame.Height);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void FusionSettings_ShouldSupportResolutionModes()
    {
        var settings = new FusionSettings
        {
            RenderMode = ResolutionMode.FastPreview1280,
            PreviewMaxDimension = 1280
        };

        Assert.Equal(ResolutionMode.FastPreview1280, settings.RenderMode);
        Assert.Equal(1280, settings.PreviewMaxDimension);

        settings.RenderMode = ResolutionMode.FullMaster;
        Assert.Equal(ResolutionMode.FullMaster, settings.RenderMode);
    }
}
