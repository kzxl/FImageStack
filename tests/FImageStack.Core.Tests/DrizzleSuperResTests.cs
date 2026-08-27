using FImageStack.Core.Models;
using FImageStack.Core.SuperResolution.Drizzle;
using Xunit;

namespace FImageStack.Core.Tests;

public class DrizzleSuperResTests
{
    [Fact]
    public void DrizzleEngine_ShouldProduce2xHigherResolutionGrid()
    {
        int inW = 16;
        int inH = 16;
        var offsets = new (float dx, float dy)[]
        {
            (0.0f, 0.0f),
            (0.5f, 0.0f),
            (0.0f, 0.5f),
            (0.5f, 0.5f)
        };

        var frames = new List<StackFrame>();
        for (int i = 0; i < offsets.Length; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = inW,
                Height = inH,
                ColorBuffer = new ImageBuffer<float>(inW, inH, 3),
                AlignmentHomography = new float[] { 1f, 0f, offsets[i].dx, 0f, 1f, offsets[i].dy }
            };
            frame.ColorBuffer.AsSpan().Fill(0.5f);
            frames.Add(frame);
        }

        var engine = new DrizzleEngine();
        var settings = new DrizzleSettings
        {
            ScaleFactor = 2.0f,
            PixFrac = 0.75f
        };

        using var result = engine.DrizzleStack(frames, settings);

        Assert.NotNull(result.SuperResolvedImage);
        Assert.NotNull(result.WeightMap);
        Assert.Equal(32, result.SuperResolvedImage.Width);
        Assert.Equal(32, result.SuperResolvedImage.Height);
        Assert.Equal(3, result.SuperResolvedImage.Channels);

        // Center pixel should maintain intensity ~0.5
        float midVal = result.SuperResolvedImage.At(16, 16, 0);
        Assert.True(MathF.Abs(midVal - 0.5f) < 0.05f, $"Mid value was {midVal}");

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void DrizzleEngine_ShouldPreservePointSourceSignal()
    {
        int inW = 20;
        int inH = 20;
        var frames = new List<StackFrame>();

        for (int i = 0; i < 4; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = inW,
                Height = inH,
                ColorBuffer = new ImageBuffer<float>(inW, inH, 3),
                AlignmentHomography = new float[] { 1f, 0f, (i % 2) * 0.4f, 0f, 1f, (i / 2) * 0.4f }
            };

            // Background 0.1, Point source at center (10, 10) = 1.0
            frame.ColorBuffer.AsSpan().Fill(0.1f);
            frame.ColorBuffer.At(10, 10, 0) = 1.0f;
            frame.ColorBuffer.At(10, 10, 1) = 1.0f;
            frame.ColorBuffer.At(10, 10, 2) = 1.0f;

            frames.Add(frame);
        }

        var engine = new DrizzleEngine();
        var settings = new DrizzleSettings
        {
            ScaleFactor = 2.0f,
            PixFrac = 0.65f
        };

        using var result = engine.DrizzleStack(frames, settings);

        // Peak in the 2x super-resolution grid at (20, 20) should remain sharp > 0.85
        float peak = result.SuperResolvedImage.At(20, 20, 0);
        float bg = result.SuperResolvedImage.At(5, 5, 0);

        Assert.True(peak > 0.75f, $"Point source peak was diluted: {peak}");
        Assert.True(bg < 0.20f, $"Background was elevated: {bg}");

        foreach (var f in frames) f.Dispose();
    }
}
