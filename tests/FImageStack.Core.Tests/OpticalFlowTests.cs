using FImageStack.Core.Alignment;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class OpticalFlowTests
{
    [Fact]
    public void DenseOpticalFlowEstimator_ShouldComputeDisplacementField()
    {
        int w = 32;
        int h = 32;

        using var refFrame = new StackFrame
        {
            Index = 0,
            Width = w,
            Height = h,
            GrayBuffer = new ImageBuffer<float>(w, h)
        };

        using var targetFrame = new StackFrame
        {
            Index = 1,
            Width = w,
            Height = h,
            GrayBuffer = new ImageBuffer<float>(w, h)
        };

        // Gaussian feature centered at (16, 16) in ref, shifted to (17, 16) in target (dx = 1.0)
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dRef = MathF.Exp(-((x - 16) * (x - 16) + (y - 16) * (y - 16)) / 16f);
                float dTgt = MathF.Exp(-((x - 17) * (x - 17) + (y - 16) * (y - 16)) / 16f);

                refFrame.GrayBuffer.At(x, y) = dRef;
                targetFrame.GrayBuffer.At(x, y) = dTgt;
            }
        }

        var estimator = new DenseOpticalFlowEstimator();
        using var flow = estimator.ComputeDenseFlow(refFrame, targetFrame, pyramidLevels: 2, iterations: 4);

        Assert.Equal(w, flow.Width);
        Assert.Equal(h, flow.Height);

        // Vector Vx around (16, 16) should point towards target (+1.0)
        float estimatedVx = flow.Vx.At(16, 16);
        Assert.True(estimatedVx > 0.3f, $"Expected positive displacement in Vx, got {estimatedVx}");
    }

    [Fact]
    public void OpticalFlowField_ApplyDenseWarp_ShouldRealignTargetFrame()
    {
        int w = 16;
        int h = 16;

        using var frame = new StackFrame
        {
            Index = 0,
            Width = w,
            Height = h,
            GrayBuffer = new ImageBuffer<float>(w, h),
            ColorBuffer = new ImageBuffer<float>(w, h, 3)
        };
        frame.GrayBuffer.AsSpan().Fill(0.2f);
        frame.ColorBuffer.AsSpan().Fill(0.2f);

        // Dot at (10, 10)
        frame.GrayBuffer.At(10, 10) = 0.9f;
        frame.ColorBuffer.At(10, 10, 0) = 0.9f;

        using var flow = new OpticalFlowField(w, h);
        // Warp shift vector Vx = 2, Vy = 2 -> destination at (8, 8) will sample from (8+2, 8+2) = (10, 10)
        flow.Vx.At(8, 8) = 2.0f;
        flow.Vy.At(8, 8) = 2.0f;

        flow.ApplyDenseWarp(frame);

        // Dot should now appear at (8, 8)
        Assert.True(frame.GrayBuffer.At(8, 8) > 0.7f);
        Assert.True(frame.ColorBuffer.At(8, 8, 0) > 0.7f);
    }

    [Fact]
    public void AlignmentEngine_WithOpticalFlowMode_ShouldExecuteEndToEnd()
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
                GrayBuffer = new ImageBuffer<float>(w, h),
                ColorBuffer = new ImageBuffer<float>(w, h, 3)
            };
            frame.GrayBuffer.AsSpan().Fill(0.3f);
            frame.ColorBuffer.AsSpan().Fill(0.3f);
            frames.Add(frame);
        }

        var alignmentEngine = new AdvancedAlignmentEngine();
        alignmentEngine.AlignStack(frames, mode: AlignmentMode.OpticalFlow);

        for (int i = 0; i < frameCount; i++)
        {
            Assert.True(frames[i].AlignmentConfidence > 0.8);
        }

        foreach (var f in frames) f.Dispose();
    }
}
