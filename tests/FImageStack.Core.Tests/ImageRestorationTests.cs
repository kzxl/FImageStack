using FImageStack.Core.Models;
using FImageStack.Core.Restoration;
using Xunit;

namespace FImageStack.Core.Tests;

public class ImageRestorationTests
{
    [Theory]
    [InlineData(PsfKernelType.Gaussian)]
    [InlineData(PsfKernelType.DefocusDisc)]
    [InlineData(PsfKernelType.MotionBlur)]
    [InlineData(PsfKernelType.AiryDisk)]
    public unsafe void PsfGenerator_KernelShouldSumToOne(PsfKernelType type)
    {
        using var psf = PsfGenerator.CreatePsf(type, radius: 2.5f, angleDegrees: 45f);

        Assert.True(psf.Width > 0 && psf.Height > 0);
        Assert.Equal(psf.Width, psf.Height);

        float sum = 0f;
        float* ptr = psf.DataPointer;
        int total = psf.TotalElements;

        for (int i = 0; i < total; i++)
        {
            sum += ptr[i];
            Assert.True(ptr[i] >= 0f, "PSF values must be non-negative");
        }

        Assert.True(MathF.Abs(sum - 1.0f) < 1e-4f, $"PSF sum was {sum}");
    }

    [Fact]
    public void RichardsonLucy_ShouldSharpenBlurredEdge()
    {
        int w = 32;
        int h = 32;
        using var sharpImg = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);

        // Step function: left is 0.1, right is 0.9
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                sharpImg.At(x, y) = x < 16 ? 0.1f : 0.9f;
            }
        }

        // Simulate Gaussian blur
        using var psf = PsfGenerator.CreatePsf(PsfKernelType.Gaussian, radius: 1.5f);
        using var blurredImg = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);

        // Convolve manually or via small Gaussian
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                int kRadius = psf.Width / 2;
                for (int ky = -kRadius; ky <= kRadius; ky++)
                {
                    int sy = Math.Clamp(y + ky, 0, h - 1);
                    for (int kx = -kRadius; kx <= kRadius; kx++)
                    {
                        int sx = Math.Clamp(x + kx, 0, w - 1);
                        sum += sharpImg.At(sx, sy) * psf.At(kx + kRadius, ky + kRadius);
                    }
                }
                blurredImg.At(x, y) = sum;
            }
        }

        // Edge gradient in blurred image between x=15 and x=16
        float blurredSlope = blurredImg.At(17, 16) - blurredImg.At(14, 16);

        var rlEngine = new RichardsonLucyEngine();
        var options = new DeconvolutionOptions
        {
            Iterations = 15,
            TvDampingWeight = 0.001f
        };

        using var restoredImg = rlEngine.Deconvolve(blurredImg, psf, options);

        // Edge gradient in restored image should be steeper (sharper)
        float restoredSlope = restoredImg.At(17, 16) - restoredImg.At(14, 16);

        Assert.True(restoredSlope > blurredSlope * 1.15f, $"Deconvolution did not sharpen edge: blurred={blurredSlope}, restored={restoredSlope}");
    }

    [Fact]
    public void DehazeEngine_ShouldRestoreContrastFromSyntheticHaze()
    {
        int w = 24;
        int h = 24;
        using var clearImg = new ImageBuffer<float>(w, h, 3, PixelFormatType.RgbFloat32);

        // Checkerboard high contrast scene
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float v = ((x / 4) + (y / 4)) % 2 == 0 ? 0.8f : 0.1f;
                clearImg.At(x, y, 0) = v;
                clearImg.At(x, y, 1) = v;
                clearImg.At(x, y, 2) = v;
            }
        }

        // Add heavy atmospheric haze: I = J * t + A * (1 - t) with t = 0.4, A = 0.95
        using var hazyImg = new ImageBuffer<float>(w, h, 3, PixelFormatType.RgbFloat32);
        float t = 0.45f;
        float a = 0.90f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    hazyImg.At(x, y, c) = clearImg.At(x, y, c) * t + a * (1.0f - t);
                }
            }
        }

        // Measure hazy contrast
        float hazyContrast = hazyImg.At(2, 2, 0) - hazyImg.At(6, 2, 0);

        var dehazeEngine = new DehazeEngine();
        var options = new DehazeOptions
        {
            PatchRadius = 3,
            Omega = 0.90f,
            MinTransmission = 0.15f,
            GuidedFilterRadius = 4
        };

        using var result = dehazeEngine.Dehaze(hazyImg, options);

        Assert.NotNull(result.DehazedImage);
        Assert.NotNull(result.TransmissionMap);

        // Dehazed contrast should be significantly higher than hazy contrast
        float dehazedContrast = result.DehazedImage.At(2, 2, 0) - result.DehazedImage.At(6, 2, 0);
        Assert.True(dehazedContrast > hazyContrast * 1.30f, $"Dehazed contrast was {dehazedContrast} vs hazy {hazyContrast}");
    }
}
