using FImageStack.Core.DepthMap;
using FImageStack.Core.Models;
using FImageStack.Core.Refocus;
using Xunit;

namespace FImageStack.Core.Tests;

public class RefocusTests
{
    [Fact]
    public void RefocusEngine_QueryFocusAtPoint_ShouldIdentifyBestFrame()
    {
        int w = 20;
        int h = 20;
        int frameCount = 5;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            frames.Add(new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                GrayBuffer = new ImageBuffer<float>(w, h)
            });
        }

        var depthResult = new DepthMapResult(w, h);
        depthResult.DepthMap.At(10, 10) = 2.3f;
        depthResult.ConfidenceMap.At(10, 10) = 0.92f;

        var engine = new RefocusEngine();
        var result = engine.QueryFocusAtPoint(depthResult, frames, 10, 10);

        Assert.Equal(10, result.X);
        Assert.Equal(10, result.Y);
        Assert.Equal(2.3f, result.ContinuousDepth, 2);
        Assert.Equal(2, result.ClosestFrameIndex);
        Assert.Equal(0.92f, result.FrameConfidence, 2);
        Assert.Contains("Point (10, 10) is in focus at depth Z=2.30", result.Description);
        Assert.Contains("Best Frame: #3", result.Description);
        Assert.Contains("confidence 92%", result.Description);

        foreach (var f in frames) f.Dispose();
        depthResult.Dispose();
    }

    [Fact]
    public void RefocusEngine_RenderSyntheticAperture_ShouldBlurDistantPixels()
    {
        int w = 24;
        int h = 24;
        int frameCount = 5;
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
                    // High frequency alternating pattern
                    frame.GrayBuffer.At(x, y) = ((x + y) % 2 == 0) ? 0.9f : 0.1f;
                }
            }
            frames.Add(frame);
        }

        var depthResult = new DepthMapResult(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Left half depth = 1.0 (in-focus target), Right half depth = 4.0 (distant out-of-focus)
                depthResult.DepthMap.At(x, y) = (x < 12) ? 1.0f : 4.0f;
                depthResult.ConfidenceMap.At(x, y) = 1.0f;
            }
        }

        var engine = new RefocusEngine();
        var refocusParams = new SyntheticApertureParams
        {
            TargetFocalDepth = 1.0f,
            ApertureSize = 0.5f,
            BokehBlurRadius = 6.0f
        };

        using var refocused = engine.RenderSyntheticAperture(depthResult, frames, refocusParams);

        // In-focus left half: sharp contrast between adjacent pixels
        float leftContrast = MathF.Abs(refocused.At(4, 4) - refocused.At(5, 4));
        Assert.True(leftContrast > 0.5f, $"Left contrast was {leftContrast}");

        // Out-of-focus right half: smoothed bokeh contrast
        float rightContrast = MathF.Abs(refocused.At(18, 18) - refocused.At(19, 18));
        Assert.True(rightContrast < 0.3f, $"Right contrast was {rightContrast}");

        foreach (var f in frames) f.Dispose();
        depthResult.Dispose();
    }
}
