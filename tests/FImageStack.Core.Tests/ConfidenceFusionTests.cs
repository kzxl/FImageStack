using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using FImageStack.Core.Motion;
using FImageStack.Core.Quality;
using Xunit;

namespace FImageStack.Core.Tests;

public class ConfidenceFusionTests
{
    [Fact]
    public void MultiFactorConfidenceEngine_ComputeConfidenceMaps_ShouldIncorporateSharpnessAndAlignment()
    {
        int w = 10;
        int h = 10;
        int frameCount = 3;

        var frames = new List<StackFrame>();
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h),
                GrayBuffer = new ImageBuffer<float>(w, h),
                ColorBuffer = new ImageBuffer<float>(w, h, 3)
            };
            frame.GrayBuffer.AsSpan().Fill(0.5f);
            frames.Add(frame);
        }

        // Frame 1 has highest sharpness with continuous optical support from neighbors
        frames[0].FocusMap!.AsSpan().Fill(0.45f);
        frames[1].FocusMap!.AsSpan().Fill(0.90f);
        frames[2].FocusMap!.AsSpan().Fill(0.65f);

        var engine = new MultiFactorConfidenceEngine();
        var confMaps = engine.ComputeConfidenceMaps(frames);

        Assert.Equal(frameCount, confMaps.Length);
        // Frame 1 confidence should be significantly higher than Frame 0 and Frame 2
        float c0 = confMaps[0].At(5, 5);
        float c1 = confMaps[1].At(5, 5);
        float c2 = confMaps[2].At(5, 5);

        Assert.True(c1 > c0);
        Assert.True(c1 > c2);
        Assert.InRange(c1, 0.7f, 1.0f);

        foreach (var m in confMaps) m.Dispose();
        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void MultiFactorConfidenceEngine_MotionPenalty_ShouldSuppressMovingFrameConfidence()
    {
        int w = 8;
        int h = 8;
        int frameCount = 2;

        var frames = new List<StackFrame>();
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h),
                GrayBuffer = new ImageBuffer<float>(w, h),
                ColorBuffer = new ImageBuffer<float>(w, h, 3)
            };
            frame.GrayBuffer.AsSpan().Fill(0.5f);
            frames.Add(frame);
        }

        // Frame 1 has higher raw sharpness
        frames[0].FocusMap!.AsSpan().Fill(0.6f);
        frames[1].FocusMap!.AsSpan().Fill(0.9f);

        // But Frame 1 has heavy motion detected across the frame
        using var motionResult = new MotionDetectionResult(w, h);
        motionResult.MotionMap.AsSpan().Fill(0.95f);

        var engine = new MultiFactorConfidenceEngine();
        var confMaps = engine.ComputeConfidenceMaps(frames, motionResult);

        float c0 = confMaps[0].At(3, 3);
        float c1 = confMaps[1].At(3, 3);

        // Motion penalty suppresses overall confidence
        Assert.True(c1 < 0.5f, $"Expected motion suppressed confidence < 0.5, got {c1}");

        foreach (var m in confMaps) m.Dispose();
        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void ConfidenceWeightedFusionEngine_ShouldSmoothlyBlendFrames()
    {
        int w = 6;
        int h = 6;
        int frameCount = 2;

        var frames = new List<StackFrame>();
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h),
                GrayBuffer = new ImageBuffer<float>(w, h),
                ColorBuffer = new ImageBuffer<float>(w, h, 3)
            };
            frames.Add(frame);
        }

        // Frame 0: Red (1, 0, 0) with sharpness 0.8 on left half (x < 3)
        // Frame 1: Blue (0, 0, 1) with sharpness 0.8 on right half (x >= 3)
        frames[0].ColorBuffer!.AsSpan().Fill(0f);
        frames[1].ColorBuffer!.AsSpan().Fill(0f);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Set colors
                frames[0].ColorBuffer!.At(x, y, 0) = 1.0f; // Red
                frames[1].ColorBuffer!.At(x, y, 2) = 1.0f; // Blue

                frames[0].FocusMap!.At(x, y) = x < 3 ? 0.9f : 0.1f;
                frames[1].FocusMap!.At(x, y) = x >= 3 ? 0.9f : 0.1f;

                frames[0].GrayBuffer!.At(x, y) = 0.5f;
                frames[1].GrayBuffer!.At(x, y) = 0.5f;
            }
        }

        var fusionEngine = new ConfidenceWeightedFusionEngine();
        using var depthResult = new DepthMapResult(w, h);
        var settings = new FusionSettings { Method = FusionMethod.ConfidenceWeighted };

        using var fused = fusionEngine.Fuse(frames, depthResult, settings);

        Assert.Equal(w, fused.Width);
        Assert.Equal(h, fused.Height);
        Assert.Equal(3, fused.Channels);

        // Left pixel (0, 0) should be predominantly Red
        Assert.True(fused.At(0, 0, 0) > 0.8f);
        Assert.True(fused.At(0, 0, 2) < 0.2f);

        // Right pixel (5, 5) should be predominantly Blue
        Assert.True(fused.At(5, 5, 2) > 0.8f);
        Assert.True(fused.At(5, 5, 0) < 0.2f);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void MultiFactorConfidenceEngine_GetBreakdown_ShouldReturnAccurateIndividualFactors()
    {
        int w = 4;
        int h = 4;
        var frames = new List<StackFrame>();
        for (int i = 0; i < 2; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h),
                GrayBuffer = new ImageBuffer<float>(w, h),
                ColorBuffer = new ImageBuffer<float>(w, h, 3)
            };
            frame.FocusMap.AsSpan().Fill(0.75f);
            frame.GrayBuffer.AsSpan().Fill(0.4f);
            frames.Add(frame);
        }

        var engine = new MultiFactorConfidenceEngine();
        var breakdown = engine.GetBreakdown(2, 2, 0, frames, null);

        Assert.True(breakdown.Sharpness > 0.5f);
        Assert.True(breakdown.Alignment > 0.5f);
        Assert.True(breakdown.MotionInvariance > 0.8f);
        Assert.True(breakdown.TotalConfidence > 0.4f);
        Assert.False(string.IsNullOrEmpty(breakdown.ToString()));

        foreach (var f in frames) f.Dispose();
    }
}
