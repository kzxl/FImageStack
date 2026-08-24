using FImageStack.Core;
using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class ExposureFusionTests
{
    [Fact]
    public void ExposureFusionEngine_ShouldFuseBothFocusAndExposureBrackets()
    {
        int size = 32;
        var frames = new List<StackFrame>();

        // Create 3 frames with different focus and exposure brackets:
        // Frame 0: Dark underexposed (0.2), Sharp top half
        // Frame 1: Normal exposed (0.5), Sharp center
        // Frame 2: Bright overexposed (0.9), Sharp bottom half
        for (int f = 0; f < 3; f++)
        {
            var frame = new StackFrame
            {
                Index = f,
                Width = size,
                Height = size,
                ColorBuffer = new ImageBuffer<float>(size, size, 3),
                FocusMap = new ImageBuffer<float>(size, size, 1)
            };

            float baseLum = f switch
            {
                0 => 0.2f,
                1 => 0.5f,
                _ => 0.9f
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isFocusedRegion = (f == 0 && y < 10) || (f == 1 && y >= 10 && y < 20) || (f == 2 && y >= 20);
                    float focusVal = isFocusedRegion ? 1.0f : 0.05f;

                    frame.FocusMap.At(x, y) = focusVal;
                    frame.ColorBuffer.At(x, y, 0) = baseLum;
                    frame.ColorBuffer.At(x, y, 1) = baseLum;
                    frame.ColorBuffer.At(x, y, 2) = baseLum;
                }
            }

            frames.Add(frame);
        }

        var depthResult = new DepthMapResult(size, size);
        var settings = new FusionSettings
        {
            Method = FusionMethod.HDRFocusExposure,
            PyramidLevels = 3
        };

        var engine = new ExposureFusionEngine();
        using var result = engine.Fuse(frames, depthResult, settings);

        Assert.NotNull(result);
        Assert.Equal(size, result.Width);
        Assert.Equal(size, result.Height);
        Assert.Equal(3, result.Channels);

        // Center pixel should be well-exposed and within normal dynamic range [0.1 .. 0.9]
        float centerR = result.At(16, 16, 0);
        Assert.True(centerR >= 0.1f && centerR <= 0.9f, $"Center pixel luminance was {centerR}, expected well-exposed [0.1..0.9]");

        depthResult.Dispose();
        foreach (var f in frames) f.Dispose();
    }
}
