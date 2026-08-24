using FImageStack.Core.Models;
using FImageStack.Core.Quality;
using Xunit;

namespace FImageStack.Core.Tests;

public class ArtifactHunterTests
{
    [Fact]
    public void ArtifactHunterEngine_HuntArtifacts_ShouldDetectMotionGhostHotspot()
    {
        int w = 24;
        int h = 24;
        int frameCount = 3;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                AlignmentConfidence = 0.98,
                GrayBuffer = new ImageBuffer<float>(w, h)
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Constant background
                    frame.GrayBuffer.At(x, y) = 0.5f;
                }
            }

            // Frame 1 has localized object shift at (12, 12)
            if (i == 1)
            {
                frame.GrayBuffer.At(12, 12) = 0.95f;
            }

            frames.Add(frame);
        }

        var engine = new ArtifactHunterEngine();
        using var report = engine.HuntArtifacts(frames);

        Assert.NotNull(report);
        Assert.Equal(3, report.TotalFramesScanned);
        Assert.Equal(6, report.Metrics.Count);

        var ghostMetric = report.Metrics.First(m => m.Type == HunterRiskType.Ghost);
        Assert.True(ghostMetric.RiskScorePercentage > 20.0f);
        Assert.Contains("█", ghostMetric.AsciiBar);

        Assert.NotEmpty(report.Hotspots);
        var hotspot = report.Hotspots[0];
        Assert.Equal(12, hotspot.X);
        Assert.Equal(12, hotspot.Y);
        Assert.Equal(HunterRiskType.Ghost, hotspot.RiskType);
        Assert.Contains("Frame #2 has localized motion drift", hotspot.RootCauseDescription);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void ArtifactHunterEngine_HuntArtifacts_CleanStack_ShouldHaveHighHealthScore()
    {
        int w = 16;
        int h = 16;
        int frameCount = 3;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                AlignmentConfidence = 1.0,
                GrayBuffer = new ImageBuffer<float>(w, h)
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    frame.GrayBuffer.At(x, y) = 0.5f;
                }
            }

            frames.Add(frame);
        }

        var engine = new ArtifactHunterEngine();
        using var report = engine.HuntArtifacts(frames);

        Assert.True(report.HealthScore >= 90, $"Health score was {report.HealthScore}");
        Assert.Empty(report.Hotspots);

        foreach (var f in frames) f.Dispose();
    }
}
