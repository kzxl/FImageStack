using FImageStack.Core;
using FImageStack.Core.Models;
using FImageStack.Core.Selection;
using Xunit;

namespace FImageStack.Core.Tests;

public class SmartSelectionTests
{
    [Fact]
    public void SmartFrameSelector_ShouldDetectMotionBlurAndOutliers()
    {
        int size = 32;
        var frames = new List<StackFrame>();

        // Create 5 frames with varied sharpness patterns
        for (int i = 0; i < 5; i++)
        {
            var f = new StackFrame
            {
                Index = i,
                Width = size,
                Height = size,
                GrayBuffer = new ImageBuffer<float>(size, size, 1)
            };

            if (i != 2)
            {
                // Checkerboard pattern (high sharpness) with slight index variation
                int step = 2 + (i % 2);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        f.GrayBuffer.At(x, y) = ((x / step) + (y / step)) % 2 == 0 ? 1.0f : 0.0f;
                    }
                }
            }
            else
            {
                // Smooth flat gray (blurry / zero sharpness)
                f.GrayBuffer.AsSpan().Fill(0.5f);
            }

            frames.Add(f);
        }

        var selector = new SmartFrameSelector();
        var diags = selector.AnalyzeStack(frames);

        Assert.Equal(5, diags.Count);
        Assert.False(diags[0].IsBadFrame, "Frame 0 should be sharp");
        Assert.False(diags[1].IsBadFrame, "Frame 1 should be sharp");
        Assert.True(diags[2].IsBadFrame, "Frame 2 should be flagged as BAD");
        Assert.True(diags[2].IsMotionBlurred, "Frame 2 should be flagged with motion blur/shake");
        Assert.False(diags[3].IsBadFrame, "Frame 3 should be sharp");
        Assert.False(diags[4].IsBadFrame, "Frame 4 should be sharp");

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void SmartFrameSelector_ShouldDetectDuplicatesAndExposureAnomalies()
    {
        int size = 32;
        var frames = new List<StackFrame>();

        // Frame 0: normal
        var f0 = new StackFrame { Index = 0, Width = size, Height = size, GrayBuffer = new ImageBuffer<float>(size, size, 1) };
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) f0.GrayBuffer.At(x, y) = ((x / 4) + (y / 4)) % 2 == 0 ? 0.8f : 0.2f;
        frames.Add(f0);

        // Frame 1: identical duplicate of Frame 0
        var f1 = new StackFrame { Index = 1, Width = size, Height = size, GrayBuffer = new ImageBuffer<float>(size, size, 1) };
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) f1.GrayBuffer.At(x, y) = ((x / 4) + (y / 4)) % 2 == 0 ? 0.8f : 0.2f;
        frames.Add(f1);

        // Frame 2: blown-out white overexposed anomaly
        var f2 = new StackFrame { Index = 2, Width = size, Height = size, GrayBuffer = new ImageBuffer<float>(size, size, 1) };
        f2.GrayBuffer.AsSpan().Fill(1.0f);
        frames.Add(f2);

        var selector = new SmartFrameSelector();
        var diags = selector.AnalyzeStack(frames);

        Assert.True(diags[1].IsDuplicate, "Frame 1 should be flagged as duplicate of Frame 0");
        Assert.True(diags[2].IsExposureAnomaly, "Frame 2 should be flagged as exposure anomaly");

        foreach (var f in frames) f.Dispose();
    }
}
