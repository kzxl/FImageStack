using FImageStack.Core;
using FImageStack.Core.Models;
using FImageStack.Core.PostProcessing;
using FImageStack.Infrastructure.IO;
using Xunit;

namespace FImageStack.Core.Tests;

public class RawAndToneMappingTests
{
    [Fact]
    public void RawDecoderEngine_ShouldDemosaicBayerCfaCorrectly()
    {
        int w = 16;
        int h = 16;
        var cfa = new ushort[w * h];

        // Synthesize a gradient sensor CFA array with BlackLevel = 512, WhiteLevel = 16383
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                cfa[y * w + x] = (ushort)(512 + (x * 800));
            }
        }

        var decoder = new RawDecoderEngine();
        var meta = new RawFrameMetadata
        {
            Width = w,
            Height = h,
            BlackLevel = 512,
            WhiteLevel = 16383,
            RedGain = 1.0f,
            GreenGain = 1.0f,
            BlueGain = 1.0f,
            Pattern = BayerPatternType.RGGB
        };

        using var linearRgb = decoder.DemosaicBayerCfa(cfa, meta);

        Assert.Equal(w, linearRgb.Width);
        Assert.Equal(h, linearRgb.Height);
        Assert.Equal(3, linearRgb.Channels);

        // Center pixel should have positive, non-clamped linear values
        float r = linearRgb.At(8, 8, 0);
        float g = linearRgb.At(8, 8, 1);
        float b = linearRgb.At(8, 8, 2);

        Assert.True(r >= 0f && r <= 1.0f);
        Assert.True(g >= 0f && g <= 1.0f);
        Assert.True(b >= 0f && b <= 1.0f);
    }

    [Fact]
    public void ToneMappingEngine_ShouldMapHighDynamicRangeToNormalizedRange()
    {
        int size = 8;
        var linearHdr = new ImageBuffer<float>(size, size, 3);

        // Create high-dynamic range values > 1.0 (overexposed / highlight areas)
        for (int i = 0; i < size * size * 3; i++)
        {
            linearHdr.AsSpan()[i] = 3.5f; // Super bright HDR highlight
        }

        var toneMapper = new ToneMappingEngine();

        // 1. ACES Filmic
        using var aces = toneMapper.ApplyToneMapping(linearHdr, ToneMappingOperator.ACESFilmic);
        Assert.True(aces.At(4, 4, 0) <= 1.0f, "ACES Filmic should map 3.5 HDR highlight into <= 1.0f");
        Assert.True(aces.At(4, 4, 0) >= 0.8f, "ACES Filmic should smoothly roll off high values");

        // 2. Reinhard Extended
        using var reinhard = toneMapper.ApplyToneMapping(linearHdr, ToneMappingOperator.ReinhardExtended, whitePoint: 4.0f);
        Assert.True(reinhard.At(4, 4, 0) <= 1.0f, "Reinhard should map highlight into <= 1.0f");

        linearHdr.Dispose();
    }

    [Fact]
    public void ImageSharpIO_ShouldSave16BitTiffAndPng()
    {
        int size = 16;
        var buffer = new ImageBuffer<float>(size, size, 3);
        buffer.AsSpan().Fill(0.75f);

        var io = new ImageSharpIO();
        string tempTif = Path.Combine(Path.GetTempPath(), $"test_16bit_{Guid.NewGuid():N}.tif");
        string tempPng = Path.Combine(Path.GetTempPath(), $"test_16bit_{Guid.NewGuid():N}.png");

        try
        {
            io.SaveImage(buffer, tempTif, bitDepth: 16);
            Assert.True(File.Exists(tempTif), "16-bit TIFF should be created successfully");
            Assert.True(new FileInfo(tempTif).Length > 0);

            io.SaveImage(buffer, tempPng, bitDepth: 16);
            Assert.True(File.Exists(tempPng), "16-bit PNG should be created successfully");
            Assert.True(new FileInfo(tempPng).Length > 0);
        }
        finally
        {
            buffer.Dispose();
            if (File.Exists(tempTif)) File.Delete(tempTif);
            if (File.Exists(tempPng)) File.Delete(tempPng);
        }
    }
}
