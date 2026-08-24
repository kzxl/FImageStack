using FImageStack.Core.Alignment;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class FocusBreathingTests
{
    [Fact]
    public void FocusBreathingEstimator_MeasureRadialScale_ShouldDetectMagnification()
    {
        int w = 64;
        int h = 64;
        float cx = w * 0.5f;
        float cy = h * 0.5f;

        using var refFrame = new StackFrame
        {
            Index = 0,
            Width = w,
            Height = h,
            GrayBuffer = new ImageBuffer<float>(w, h)
        };

        // Create harmonic texture pattern
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float rx = x - cx;
                float ry = y - cy;
                refFrame.GrayBuffer.At(x, y) = MathF.Sin(rx * 0.4f) * MathF.Cos(ry * 0.4f) * 0.4f + 0.5f;
            }
        }

        // Target frame scaled by 1.03 (3% magnification)
        float targetScale = 1.03f;
        using var targetFrame = new StackFrame
        {
            Index = 1,
            Width = w,
            Height = h,
            GrayBuffer = new ImageBuffer<float>(w, h)
        };

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float rx = (x - cx) / targetScale;
                float ry = (y - cy) / targetScale;
                targetFrame.GrayBuffer.At(x, y) = MathF.Sin(rx * 0.4f) * MathF.Cos(ry * 0.4f) * 0.4f + 0.5f;
            }
        }

        var estimator = new FocusBreathingEstimator();
        float measuredScale = estimator.MeasureRadialScale(refFrame, targetFrame);

        // Measured scale should accurately detect magnification > 1.0
        Assert.InRange(measuredScale, 1.01f, 1.06f);
    }

    [Fact]
    public void FocusBreathingEstimator_EstimateScaleCurve_ShouldFitSequence()
    {
        int frameCount = 5;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = 32,
                Height = 32,
                GrayBuffer = new ImageBuffer<float>(32, 32)
            };
            frame.GrayBuffer.AsSpan().Fill(0.5f);
            frames.Add(frame);
        }

        var estimator = new FocusBreathingEstimator();
        var result = estimator.EstimateScaleCurve(frames, refIndex: 2);

        Assert.Equal(frameCount, result.RawScales.Length);
        Assert.Equal(frameCount, result.FittedScales.Length);
        Assert.Equal(1.0f, frames[2].FocusBreathingScale, 2);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void AlignmentEngine_WithBreathingCompensation_ShouldAlignAndNormalizeScale()
    {
        int w = 32;
        int h = 32;
        int frameCount = 3;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                GrayBuffer = new ImageBuffer<float>(w, h),
                ColorBuffer = new ImageBuffer<float>(w, h, 3)
            };
            frame.GrayBuffer.AsSpan().Fill(0.3f);
            frame.ColorBuffer.AsSpan().Fill(0.3f);
            frames.Add(frame);
        }

        var alignmentEngine = new AdvancedAlignmentEngine();
        alignmentEngine.AlignStack(frames, AlignmentMode.Similarity, correctFocusBreathing: true);

        // All frames should have valid breathing scale and confidence
        for (int i = 0; i < frameCount; i++)
        {
            Assert.True(frames[i].FocusBreathingScale > 0.8f);
            Assert.True(frames[i].AlignmentConfidence > 0.5);
        }

        foreach (var f in frames) f.Dispose();
    }
}
