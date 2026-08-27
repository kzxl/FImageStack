using FImageStack.Core.Models;
using FImageStack.Core.Raw;
using Xunit;

namespace FImageStack.Core.Tests;

public class BayerRawFusionTests
{
    [Fact]
    public void BayerFusionEngine_NormalizeLinearBayer_ShouldMap14BitTo01()
    {
        var raw = new RawBayerBuffer(8, 8, BayerPatternType.RGGB)
        {
            BlackLevels = new float[] { 512f, 512f, 512f, 512f },
            WhiteLevel = 16383f
        };

        raw.Data.At(0, 0) = 512f;
        raw.Data.At(1, 0) = 16383f;
        raw.Data.At(0, 1) = 8447.5f; // Midpoint

        var engine = new BayerFusionEngine();
        engine.NormalizeLinearBayer(raw);

        Assert.Equal(0.0f, raw.Data.At(0, 0), 3);
        Assert.Equal(1.0f, raw.Data.At(1, 0), 3);
        Assert.True(MathF.Abs(raw.Data.At(0, 1) - 0.5f) < 0.01f);

        raw.Dispose();
    }

    [Fact]
    public void BayerFusionEngine_MergeBayerFrames_ShouldReduceNoiseOnCfaGrid()
    {
        int w = 16;
        int h = 16;
        int frameCount = 5;
        var frames = new List<RawBayerBuffer>();
        var rand = new Random(101);

        for (int i = 0; i < frameCount; i++)
        {
            var raw = new RawBayerBuffer(w, h, BayerPatternType.RGGB)
            {
                WhiteLevel = 1.0f,
                BlackLevels = new float[] { 0f, 0f, 0f, 0f }
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float noise = (float)(rand.NextDouble() - 0.5) * 0.20f;
                    raw.Data.At(x, y) = Math.Clamp(0.5f + noise, 0f, 1f);
                }
            }
            frames.Add(raw);
        }

        var engine = new BayerFusionEngine();
        var settings = new RawStackSettings
        {
            MergeMethod = NoiseStackMethod.Mean
        };

        using var merged = engine.MergeBayerFrames(frames, settings);

        // Measure variance of single frame vs merged frame
        float singleVar = 0f;
        float mergedVar = 0f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float d1 = frames[0].Data.At(x, y) - 0.5f;
                singleVar += d1 * d1;

                float dm = merged.Data.At(x, y) - 0.5f;
                mergedVar += dm * dm;
            }
        }

        Assert.True(mergedVar < singleVar * 0.40f, $"Merged CFA variance was not sufficiently reduced: single={singleVar}, merged={mergedVar}");

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void EdgeDirectedDemosaicEngine_ShouldReconstructRgbImage()
    {
        int w = 16;
        int h = 16;
        using var raw = new RawBayerBuffer(w, h, BayerPatternType.RGGB)
        {
            WhiteLevel = 1.0f,
            BlackLevels = new float[] { 0f, 0f, 0f, 0f },
            WhiteBalanceGains = new float[] { 1.0f, 1.0f, 1.0f },
            ColorMatrix = new float[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f }
        };

        // Fill with a known pure green region
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int cIdx = BayerFusionEngine.GetCfaChannelIndex(BayerPatternType.RGGB, x & 1, y & 1);
                raw.Data.At(x, y) = (cIdx == 1 || cIdx == 2) ? 0.8f : 0.1f;
            }
        }

        var demosaicEngine = new EdgeDirectedDemosaicEngine();
        var settings = new RawStackSettings
        {
            ApplyWhiteBalance = true,
            ApplyColorMatrix = false,
            ToneMapping = ToneMappingOperator.LinearPreserve
        };

        using var rgb = demosaicEngine.Demosaic(raw, settings);

        Assert.Equal(w, rgb.Width);
        Assert.Equal(h, rgb.Height);
        Assert.Equal(3, rgb.Channels);

        // Center pixel should be dominantly Green
        float r = rgb.At(8, 8, 0);
        float g = rgb.At(8, 8, 1);
        float b = rgb.At(8, 8, 2);

        Assert.True(g > 0.6f, $"G was {g}");
        Assert.True(r < 0.3f, $"R was {r}");
        Assert.True(b < 0.3f, $"B was {b}");
    }
}
