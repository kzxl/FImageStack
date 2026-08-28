using FImageStack.Core.FocusPeaking;
using FImageStack.Core.Models;
using FImageStack.Core.Native;
using Xunit;

namespace FImageStack.Core.Tests;

public unsafe class FocusPeakingTests
{
    [Fact]
    public void FocusPeaking_MonochromeMode_IdentifiesSharpEdges()
    {
        var engine = new FocusPeakingEngine();
        int width = 64;
        int height = 64;

        // Image with high contrast sharp vertical stripe in center
        var colorImage = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32);
        float* ptr = colorImage.DataPointer;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 3;
                float v = (x >= 30 && x <= 34) ? 1.0f : 0.0f;
                ptr[idx + 0] = v;
                ptr[idx + 1] = v;
                ptr[idx + 2] = v;
            }
        }

        var settings = new FocusPeakingSettings
        {
            Color = PeakingColor.NeonGreen,
            Mode = PeakingDisplayMode.MonochromeBackground,
            Threshold = 0.05f
        };

        using var result = engine.RenderFocusPeaking(colorImage, null, settings);
        colorImage.Dispose();

        Assert.NotNull(result.PeakingImage);
        Assert.True(result.InFocusPixelCount > 0, "Peaking engine must detect sharp vertical stripes.");
        Assert.True(result.InFocusPercentage > 0.0f);

        // Check that neon green peaking color (R < 0.2, G > 0.8, B < 0.2) is present along stripe edge
        float* resPtr = result.PeakingImage.DataPointer;
        int edgeIdx = (32 * width + 30) * 3; // Along edge
        Assert.True(resPtr[edgeIdx + 1] > 0.8f, "Green channel must be high for NeonGreen peaking color.");
    }

    [Fact]
    public void FocusPeaking_DirectRgbaNative_ProcessesBufferWithoutError()
    {
        int width = 32;
        int height = 32;
        int size = width * height * 4;

        byte[] src = new byte[size];
        byte[] dst = new byte[size];

        // Draw sharp checkerboard in src
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                byte v = (byte)(((x / 4) + (y / 4)) % 2 == 0 ? 255 : 0);
                src[idx + 0] = v;
                src[idx + 1] = v;
                src[idx + 2] = v;
                src[idx + 3] = 255;
            }
        }

        fixed (byte* pSrc = src)
        fixed (byte* pDst = dst)
        {
            var engine = new FocusPeakingEngine();
            var settings = new FocusPeakingSettings
            {
                Color = PeakingColor.Red,
                Mode = PeakingDisplayMode.MonochromeBackground,
                Threshold = 0.05f
            };
            engine.RenderFocusPeakingRgbaDirect(pSrc, width, height, pDst, settings);
            // Verify red peaking channel was populated
            bool foundRed = false;
            for (int i = 0; i < size; i += 4)
            {
                if (pDst[i + 0] == 255 && pDst[i + 1] < 50)
                {
                    foundRed = true;
                    break;
                }
            }
            Assert.True(foundRed, "Red peaking color must be present on checkerboard edges.");
        }
    }
}
