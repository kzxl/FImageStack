using FImageStack.Core.Models;
using FImageStack.Core.Quality;
using Xunit;

namespace FImageStack.Core.Tests;

public class FocusGapDetectionTests
{
    [Fact]
    public void FocusGapDetector_UniformSequence_ShouldReportNoGaps()
    {
        int w = 16;
        int h = 16;
        int frameCount = 5;
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
            // Overlapping continuous focus maps
            frame.FocusMap.AsSpan().Fill(0.70f);
            frames.Add(frame);
        }

        var detector = new FocusGapDetector();
        var report = detector.DetectInterFrameGaps(frames);

        Assert.Equal(frameCount, report.TotalFramesAnalyzed);
        Assert.False(report.HasLargeGaps);
        Assert.Empty(report.LargeGaps);
        Assert.Contains("✅", report.Summary);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void FocusGapDetector_WithLargeStepJump_ShouldDetectGapAndWarn()
    {
        int w = 16;
        int h = 16;
        int frameCount = 4;
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
            frames.Add(frame);
        }

        // Frame 0, 1, 2 have uniform focus on left half (Z=0, 1, 2)
        for (int i = 0; i < 3; i++)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    frames[i].FocusMap!.At(x, y) = x < 8 ? 0.85f : 0.05f;
                }
            }
        }

        // Frame 3 has huge step jump (Z=7): zero overlap with Frame 2
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                frames[3].FocusMap!.At(x, y) = x >= 14 ? 0.85f : 0.05f;
            }
        }

        var detector = new FocusGapDetector();
        var report = detector.DetectInterFrameGaps(frames);

        Assert.True(report.HasLargeGaps);
        Assert.NotEmpty(report.LargeGaps);

        var gap = report.LargeGaps[0];
        Assert.Equal(2, gap.FrameIndexA);
        Assert.Equal(3, gap.FrameIndexB);
        Assert.Contains("Large focus gap detected", gap.WarningMessage);
        Assert.Contains("out-of-focus transition", gap.WarningMessage);

        foreach (var f in frames) f.Dispose();
    }
}
