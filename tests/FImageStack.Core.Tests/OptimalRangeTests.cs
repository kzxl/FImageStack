using FImageStack.Core.Models;
using FImageStack.Core.Quality;
using Xunit;

namespace FImageStack.Core.Tests;

public class OptimalRangeTests
{
    [Fact]
    public void OptimalFrameRangeSelector_ShouldDetectFocusEnvelopeBoundaries()
    {
        int w = 24;
        int h = 24;
        int frameCount = 20;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                GrayBuffer = new ImageBuffer<float>(w, h)
            };

            // Synthetic Focus Curve: Peak around frame 10 (range 4 to 15 is in-focus)
            float focusCurve = MathF.Exp(-MathF.Pow(i - 10, 2) / 10f); // Peak 1.0 at i=10, drop to <0.1 at i<4 or i>16
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Alternate pixel pattern modulated by focus curve to generate Laplacian response
                    float checker = ((x + y) % 2 == 0) ? 0.5f + 0.4f * focusCurve : 0.5f - 0.4f * focusCurve;
                    frame.GrayBuffer.At(x, y) = checker;
                }
            }
            frames.Add(frame);
        }

        var selector = new OptimalFrameRangeSelector();
        var result = selector.AnalyzeOptimalRange(frames, thresholdFactor: 0.15f);

        Assert.Equal(frameCount, result.TotalInputFrames);
        Assert.True(result.RecommendedStartFrame >= 3 && result.RecommendedStartFrame <= 6, $"Start frame was {result.RecommendedStartFrame}");
        Assert.True(result.RecommendedEndFrame >= 14 && result.RecommendedEndFrame <= 17, $"End frame was {result.RecommendedEndFrame}");

        // Frames 0-2 should be culled as PreFocusDeadband
        Assert.False(result.FrameMetrics[0].IsSelected);
        Assert.True((result.FrameMetrics[0].CullReason & FrameCullReason.PreFocusDeadband) != 0);

        // Frames 18-19 should be culled as PostFocusDeadband
        Assert.False(result.FrameMetrics[19].IsSelected);
        Assert.True((result.FrameMetrics[19].CullReason & FrameCullReason.PostFocusDeadband) != 0);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void OptimalFrameRangeSelector_ShouldCullShakyAndExposureOutliers()
    {
        int w = 24;
        int h = 24;
        int frameCount = 10;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                GrayBuffer = new ImageBuffer<float>(w, h)
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float checker = ((x + y) % 2 == 0) ? 0.8f : 0.2f;
                    frame.GrayBuffer.At(x, y) = checker;
                }
            }
            frames.Add(frame);
        }

        // Frame 4: Shaky motion blur -> flat low-contrast
        frames[4].GrayBuffer!.AsSpan().Fill(0.5f);

        // Frame 7: Exposure glitch -> extreme flash overexposure
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                frames[7].GrayBuffer!.At(x, y) = ((x + y) % 2 == 0) ? 0.98f : 0.88f;
            }
        }

        var selector = new OptimalFrameRangeSelector();
        var result = selector.AnalyzeOptimalRange(frames);

        // Frame 4 should be culled as ShakyMotionBlur
        Assert.False(result.FrameMetrics[4].IsSelected);
        Assert.True((result.FrameMetrics[4].CullReason & FrameCullReason.ShakyMotionBlur) != 0);

        // Frame 7 should be culled as ExposureGlitch
        Assert.False(result.FrameMetrics[7].IsSelected);
        Assert.True((result.FrameMetrics[7].CullReason & FrameCullReason.ExposureGlitch) != 0);

        foreach (var f in frames) f.Dispose();
    }
}
