using FImageStack.Core.Models;
using FImageStack.Core.SuperResolution;
using Xunit;

namespace FImageStack.Core.Tests;

public class SuperResolutionTests
{
    [Fact]
    public void SuperResolutionEngine_ShouldReconstructDoubleResolutionImage()
    {
        int lrW = 16;
        int lrH = 16;
        int frameCount = 3;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = lrW,
                Height = lrH,
                GrayBuffer = new ImageBuffer<float>(lrW, lrH),
                ColorBuffer = new ImageBuffer<float>(lrW, lrH, 3),
                FocusMap = new ImageBuffer<float>(lrW, lrH)
            };
            frame.GrayBuffer.AsSpan().Fill(0.4f);
            frame.ColorBuffer.AsSpan().Fill(0.4f);
            frame.FocusMap.AsSpan().Fill(0.8f);
            frames.Add(frame);
        }

        using var baseline = new ImageBuffer<float>(lrW, lrH, 3);
        baseline.AsSpan().Fill(0.4f);

        var engine = new MultiFrameSuperResolutionEngine();
        var srParams = new SuperResolutionParams(scaleFactor: 2, sharpnessBoost: 1.2f, ibpIterations: 2);

        using var hrResult = engine.ReconstructSuperResolution(frames, baseline, srParams);

        Assert.Equal(lrW * 2, hrResult.Width);
        Assert.Equal(lrH * 2, hrResult.Height);
        Assert.Equal(3, hrResult.Channels);

        // Verify values are in valid dynamic range
        for (int y = 0; y < hrResult.Height; y++)
        {
            for (int x = 0; x < hrResult.Width; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    float val = hrResult.At(x, y, c);
                    Assert.True(val >= 0f && val <= 1.0f);
                }
            }
        }

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void SuperResolutionEngine_ShouldPreserveAndBoostEdges()
    {
        int lrW = 20;
        int lrH = 20;
        int frameCount = 2;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = lrW,
                Height = lrH,
                GrayBuffer = new ImageBuffer<float>(lrW, lrH),
                ColorBuffer = new ImageBuffer<float>(lrW, lrH, 3),
                FocusMap = new ImageBuffer<float>(lrW, lrH)
            };

            for (int y = 0; y < lrH; y++)
            {
                for (int x = 0; x < lrW; x++)
                {
                    float val = x < 10 ? 0.1f : 0.9f;
                    frame.GrayBuffer.At(x, y) = val;
                    frame.ColorBuffer.At(x, y, 0) = val;
                    frame.ColorBuffer.At(x, y, 1) = val;
                    frame.ColorBuffer.At(x, y, 2) = val;
                    frame.FocusMap.At(x, y) = 0.9f;
                }
            }
            frames.Add(frame);
        }

        using var baseline = new ImageBuffer<float>(lrW, lrH, 3);
        baseline.AsSpan().Fill(0.5f);

        var engine = new MultiFrameSuperResolutionEngine();
        var srParams = new SuperResolutionParams(scaleFactor: 2, sharpnessBoost: 1.35f, ibpIterations: 2);

        using var hrResult = engine.ReconstructSuperResolution(frames, baseline, srParams);

        Assert.Equal(40, hrResult.Width);
        Assert.Equal(40, hrResult.Height);

        // Left side should be dark, right side should be bright
        Assert.True(hrResult.At(5, 20, 0) < 0.2f);
        Assert.True(hrResult.At(35, 20, 0) > 0.8f);

        foreach (var f in frames) f.Dispose();
    }
}
