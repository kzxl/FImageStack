using FImageStack.Core.Models;
using FImageStack.Core.Quality;
using Xunit;

namespace FImageStack.Core.Tests;

public class StackSimulationTests
{
    [Fact]
    public void StackSimulationEngine_ContinuousStack_ShouldHaveHighCoverageAndNoGaps()
    {
        int w = 16;
        int h = 16;
        int frameCount = 10;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h)
            };
            frame.FocusMap.AsSpan().Fill(0.85f); // Consistent continuous focus
            frames.Add(frame);
        }

        var engine = new StackSimulationEngine();
        var result = engine.SimulateDepthCoverage(frames);

        Assert.Equal(frameCount, result.TotalFrames);
        Assert.True(result.DepthCoveragePercentage >= 95.0f, $"Coverage was {result.DepthCoveragePercentage}%");
        Assert.False(result.HasGaps);
        Assert.Empty(result.DetectedGaps);
        Assert.Contains("█", result.CoverageBarAscii);
        Assert.Contains("✅", result.Recommendation);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void StackSimulationEngine_WithMissingFrames_ShouldDetectFocusGap()
    {
        int w = 16;
        int h = 16;
        int frameCount = 15;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h)
            };

            // Frames 0..4: sharp, Frames 5..8: blurry (GAP), Frames 9..14: sharp
            if (i >= 5 && i <= 8)
            {
                frame.FocusMap.AsSpan().Fill(0.02f); // Blurry gap
            }
            else
            {
                frame.FocusMap.AsSpan().Fill(0.90f); // Sharp
            }
            frames.Add(frame);
        }

        var engine = new StackSimulationEngine();
        var result = engine.SimulateDepthCoverage(frames);

        Assert.True(result.HasGaps);
        Assert.NotEmpty(result.DetectedGaps);

        var gap = result.DetectedGaps[0];
        Assert.True(gap.StartFrame <= 5 && gap.EndFrame >= 8, $"Gap was {gap.StartFrame} to {gap.EndFrame}");
        Assert.Contains("░", result.CoverageBarAscii);
        Assert.Contains("⚠", result.Recommendation);

        foreach (var f in frames) f.Dispose();
    }
}
