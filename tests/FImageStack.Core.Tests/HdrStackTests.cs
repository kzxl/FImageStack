using FImageStack.Core.Hdr;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class HdrStackTests
{
    private static (StackFrame dark, StackFrame bright) CreateExposurePair(int w = 32, int h = 32)
    {
        var dark = new StackFrame
        {
            Index = 0,
            Width = w,
            Height = h,
            ColorBuffer = new ImageBuffer<float>(w, h, 3),
            GrayBuffer = new ImageBuffer<float>(w, h, 1)
        };

        var bright = new StackFrame
        {
            Index = 1,
            Width = w,
            Height = h,
            ColorBuffer = new ImageBuffer<float>(w, h, 3),
            GrayBuffer = new ImageBuffer<float>(w, h, 1)
        };

        // Left half: Bright scene (overexposed in 'bright', well-exposed in 'dark')
        // Right half: Dark shadow (underexposed in 'dark', well-exposed in 'bright')
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (x < w / 2)
                {
                    // Bright window
                    dark.ColorBuffer.At(x, y, 0) = 0.5f;
                    dark.ColorBuffer.At(x, y, 1) = 0.5f;
                    dark.ColorBuffer.At(x, y, 2) = 0.5f;

                    bright.ColorBuffer.At(x, y, 0) = 1.0f; // Blown out
                    bright.ColorBuffer.At(x, y, 1) = 1.0f;
                    bright.ColorBuffer.At(x, y, 2) = 1.0f;
                }
                else
                {
                    // Dark room
                    dark.ColorBuffer.At(x, y, 0) = 0.05f; // Crushed black
                    dark.ColorBuffer.At(x, y, 1) = 0.05f;
                    dark.ColorBuffer.At(x, y, 2) = 0.05f;

                    bright.ColorBuffer.At(x, y, 0) = 0.5f;
                    bright.ColorBuffer.At(x, y, 1) = 0.5f;
                    bright.ColorBuffer.At(x, y, 2) = 0.5f;
                }

                dark.GrayBuffer.At(x, y) = dark.ColorBuffer.At(x, y, 0);
                bright.GrayBuffer.At(x, y) = bright.ColorBuffer.At(x, y, 0);
            }
        }

        return (dark, bright);
    }

    [Fact]
    public void HdrStackEngine_Mertens_ShouldMergeUnderexposedAndOverexposedFrames()
    {
        var (dark, bright) = CreateExposurePair(32, 32);
        var frames = new List<StackFrame> { dark, bright };

        var engine = new HdrStackEngine();
        var settings = new HdrStackSettings
        {
            Method = HdrMergeMethod.MertensFusion,
            PyramidLevels = 3,
            ToneMapping = ToneMappingOperator.ACESFilmic
        };

        using var result = engine.Process(frames, settings);

        Assert.NotNull(result.RadianceMap);
        Assert.NotNull(result.ToneMappedImage);

        // In the fused result:
        // Left side should NOT be blown out (should be close to 0.5)
        float leftVal = result.ToneMappedImage.At(5, 16, 0);
        Assert.True(leftVal > 0.2f && leftVal < 0.85f, $"Left side blown out or crushed: {leftVal}");

        // Right side should NOT be crushed black (should be close to 0.5)
        float rightVal = result.ToneMappedImage.At(25, 16, 0);
        Assert.True(rightVal > 0.2f && rightVal < 0.85f, $"Right side crushed black or blown out: {rightVal}");

        dark.Dispose();
        bright.Dispose();
    }

    [Fact]
    public void HdrStackEngine_RadianceMerge_ShouldProduceValidRadianceMap()
    {
        var (dark, bright) = CreateExposurePair(20, 20);
        var frames = new List<StackFrame> { dark, bright };

        var engine = new HdrStackEngine();
        using var radiance = engine.MergeRadiance(frames, new float[] { 0.01f, 0.1f });

        Assert.Equal(20, radiance.Width);
        Assert.Equal(20, radiance.Height);
        Assert.Equal(3, radiance.Channels);

        dark.Dispose();
        bright.Dispose();
    }

    [Fact]
    public void HdrStackEngine_Deghosting_ShouldFlagMotion()
    {
        var (dark, bright) = CreateExposurePair(32, 32);
        var mid = new StackFrame
        {
            Index = 2,
            Width = 32,
            Height = 32,
            ColorBuffer = new ImageBuffer<float>(32, 32, 3),
            GrayBuffer = new ImageBuffer<float>(32, 32, 1)
        };
        mid.ColorBuffer.AsSpan().Fill(0.5f);
        mid.GrayBuffer.AsSpan().Fill(0.5f);

        // Inject severe moving ghost into bright frame at (10, 10)
        bright.ColorBuffer!.At(10, 10, 0) = 0.0f;
        bright.ColorBuffer!.At(10, 10, 1) = 0.0f;
        bright.ColorBuffer!.At(10, 10, 2) = 0.0f;

        var frames = new List<StackFrame> { dark, mid, bright };
        var engine = new HdrStackEngine();
        var settings = new HdrStackSettings
        {
            Method = HdrMergeMethod.MertensFusion,
            EnableDeghosting = true,
            DeghostingThreshold = 0.25f
        };

        using var result = engine.Process(frames, settings);

        Assert.NotNull(result.DeghostMask);
        Assert.True(result.DeghostMask!.At(10, 10) > 0.5f, "Ghosting at (10,10) was not detected by deghosting mask.");


        dark.Dispose();
        mid.Dispose();
        bright.Dispose();
    }
}
