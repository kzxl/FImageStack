using FImageStack.Core;
using FImageStack.Core.Alignment;
using FImageStack.Core.DepthMap;
using FImageStack.Core.FocusMeasure;
using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using FImageStack.Core.PostProcessing;
using FImageStack.Core.Quality;
using Xunit;

namespace FImageStack.Core.Tests;

public class CoreUpgradeTests
{
    [Fact]
    public void AdvancedAlignmentEngine_ShouldCompensateTranslationAndBreathing()
    {
        int size = 64;
        var frame0 = new StackFrame
        {
            Index = 0,
            Width = size,
            Height = size,
            ColorBuffer = new ImageBuffer<float>(size, size, 3),
            GrayBuffer = new ImageBuffer<float>(size, size, 1)
        };
        var frame1 = new StackFrame
        {
            Index = 1,
            Width = size,
            Height = size,
            ColorBuffer = new ImageBuffer<float>(size, size, 3),
            GrayBuffer = new ImageBuffer<float>(size, size, 1)
        };

        // Draw a test box on both frames with a 2px offset on frame1
        for (int y = 20; y < 40; y++)
        {
            for (int x = 20; x < 40; x++)
            {
                frame0.GrayBuffer.At(x, y) = 1.0f;
                frame0.ColorBuffer.At(x, y, 0) = 1.0f;
            }
        }
        for (int y = 22; y < 42; y++)
        {
            for (int x = 22; x < 42; x++)
            {
                frame1.GrayBuffer.At(x, y) = 1.0f;
                frame1.ColorBuffer.At(x, y, 0) = 1.0f;
            }
        }

        var alignEngine = new AdvancedAlignmentEngine();
        var frames = new List<StackFrame> { frame0, frame1 };
        alignEngine.AlignStack(frames, AlignmentMode.Similarity, correctFocusBreathing: true);

        Assert.True(frame0.AlignmentConfidence > 0);
        Assert.True(frame1.AlignmentConfidence > 0);

        frame0.Dispose();
        frame1.Dispose();
    }

    [Fact]
    public unsafe void LocalVarianceAndWavelet_ShouldProduceNonZeroSharpnessOnEdges()
    {
        int size = 32;
        using var gray = new ImageBuffer<float>(size, size, 1);
        using var focusVariance = new ImageBuffer<float>(size, size, 1);
        using var focusWavelet = new ImageBuffer<float>(size, size, 1);

        // Checkerboard pattern
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                gray.At(x, y) = ((x / 4) + (y / 4)) % 2 == 0 ? 1.0f : 0.0f;
            }
        }

        var varEngine = new LocalVarianceFocusMeasure();
        varEngine.ComputeFocusMap(gray, focusVariance, windowRadius: 2);

        var waveletEngine = new WaveletSharpnessMeasure();
        waveletEngine.ComputeFocusMap(gray, focusWavelet, windowRadius: 2);

        float maxVar = 0, maxWavelet = 0;
        for (int i = 0; i < size * size; i++)
        {
            if (focusVariance.DataPointer[i] > maxVar) maxVar = focusVariance.DataPointer[i];
            if (focusWavelet.DataPointer[i] > maxWavelet) maxWavelet = focusWavelet.DataPointer[i];
        }

        Assert.True(maxVar > 0.1f, $"Local variance should be high on checkerboard, got {maxVar}");
        Assert.True(maxWavelet > 0.1f, $"Wavelet energy should be high on checkerboard, got {maxWavelet}");
    }

    [Fact]
    public void WaveletFusion_ShouldFuseMultiFramesCorrectly()
    {
        int size = 32;
        var frame0 = new StackFrame
        {
            Index = 0,
            Width = size,
            Height = size,
            ColorBuffer = new ImageBuffer<float>(size, size, 3),
            FocusMap = new ImageBuffer<float>(size, size, 1)
        };
        var frame1 = new StackFrame
        {
            Index = 1,
            Width = size,
            Height = size,
            ColorBuffer = new ImageBuffer<float>(size, size, 3),
            FocusMap = new ImageBuffer<float>(size, size, 1)
        };

        // Frame0 is sharp red on left half, Frame1 is sharp green on right half
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (x < size / 2)
                {
                    frame0.ColorBuffer.At(x, y, 0) = 1.0f; // Red
                    frame0.FocusMap.At(x, y) = 1.0f;

                    frame1.ColorBuffer.At(x, y, 1) = 0.5f;
                    frame1.FocusMap.At(x, y) = 0.001f;
                }
                else
                {
                    frame0.ColorBuffer.At(x, y, 0) = 0.5f;
                    frame0.FocusMap.At(x, y) = 0.001f;

                    frame1.ColorBuffer.At(x, y, 1) = 1.0f; // Green
                    frame1.FocusMap.At(x, y) = 1.0f;
                }
            }
        }

        var depthEstimator = new StandardDepthMapEstimator();
        using var depthResult = depthEstimator.EstimateDepthMap(new List<StackFrame> { frame0, frame1 }, enableSmoothing: false);

        var waveletFusion = new WaveletFusionEngine();
        using var fused = waveletFusion.Fuse(new List<StackFrame> { frame0, frame1 }, depthResult, new FusionSettings());

        Assert.Equal(size, fused.Width);
        Assert.Equal(size, fused.Height);

        // Left pixel should have strong Red channel, Right pixel strong Green
        float leftRed = fused.At(4, 16, 0);
        float rightGreen = fused.At(28, 16, 1);

        Assert.True(leftRed > 0.7f, $"Left pixel red channel should be > 0.7, got {leftRed}");
        Assert.True(rightGreen > 0.7f, $"Right pixel green channel should be > 0.7, got {rightGreen}");

        frame0.Dispose();
        frame1.Dispose();
    }

    [Fact]
    public void PostProcessAndHistogram_ShouldProcessAndGenerateStats()
    {
        int size = 32;
        using var img = new ImageBuffer<float>(size, size, 3);
        img.AsSpan().Fill(0.5f);

        var ppEngine = new StandardPostProcessEngine();
        using var processed = ppEngine.ApplyPostProcessing(img, new PostProcessSettings
        {
            Exposure = 1.0f,
            Contrast = 1.2f,
            SharpenAmount = 0.5f,
            Saturation = 1.2f
        });

        Assert.Equal(size, processed.Width);
        Assert.True(processed.At(16, 16, 0) > 0.5f, "Exposure +1 should increase brightness");

        var hist = HistogramEngine.Compute(processed);
        Assert.Equal(256, hist.Luminance.Length);
        Assert.True(hist.MaxFrequency > 0);
    }
}
