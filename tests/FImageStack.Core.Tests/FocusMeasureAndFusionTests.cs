using FImageStack.Core;
using FImageStack.Core.DepthMap;
using FImageStack.Core.FocusMeasure;
using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class FocusMeasureAndFusionTests
{
    [Fact]
    public void ModifiedLaplacian_ShouldYieldHigherScoreOnSharpEdges()
    {
        int size = 32;
        using var sharpImage = new ImageBuffer<float>(size, size, 1);
        using var flatImage = new ImageBuffer<float>(size, size, 1);

        // Flat image: uniform 0.5
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                flatImage.At(x, y) = 0.5f;
            }
        }

        // Sharp image: high contrast checkerboard
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                sharpImage.At(x, y) = ((x + y) % 2 == 0) ? 1.0f : 0.0f;
            }
        }

        using var sharpMap = new ImageBuffer<float>(size, size, 1);
        using var flatMap = new ImageBuffer<float>(size, size, 1);

        var laplacian = new ModifiedLaplacianFocusMeasure();
        laplacian.ComputeFocusMap(sharpImage, sharpMap, windowRadius: 1);
        laplacian.ComputeFocusMap(flatImage, flatMap, windowRadius: 1);

        float sharpScore = sharpMap.At(size / 2, size / 2);
        float flatScore = flatMap.At(size / 2, size / 2);

        Assert.True(sharpScore > 0.5f, $"Expected sharpScore > 0.5 but got {sharpScore}");
        Assert.Equal(0f, flatScore, 4);
    }

    [Fact]
    public void PyramidFusion_ShouldFuseMultipleFramesAccurately()
    {
        int size = 64;

        // Create 2 test frames:
        // Frame 0: Sharp in top half, flat in bottom half
        // Frame 1: Flat in top half, sharp in bottom half
        var frame0 = new StackFrame
        {
            Index = 0,
            Width = size,
            Height = size,
            ColorBuffer = new ImageBuffer<float>(size, size, 3),
            GrayBuffer = new ImageBuffer<float>(size, size, 1),
            FocusMap = new ImageBuffer<float>(size, size, 1)
        };

        var frame1 = new StackFrame
        {
            Index = 1,
            Width = size,
            Height = size,
            ColorBuffer = new ImageBuffer<float>(size, size, 3),
            GrayBuffer = new ImageBuffer<float>(size, size, 1),
            FocusMap = new ImageBuffer<float>(size, size, 1)
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (y < size / 2)
                {
                    // Top half: frame 0 is sharp red, frame 1 is blurred green
                    frame0.ColorBuffer.At(x, y, 0) = 1.0f; // Red
                    frame0.GrayBuffer.At(x, y) = (x % 2 == 0) ? 1.0f : 0.0f;

                    frame1.ColorBuffer.At(x, y, 1) = 1.0f; // Green
                    frame1.GrayBuffer.At(x, y) = 0.5f;
                }
                else
                {
                    // Bottom half: frame 0 is blurred red, frame 1 is sharp green
                    frame0.ColorBuffer.At(x, y, 0) = 1.0f; // Red
                    frame0.GrayBuffer.At(x, y) = 0.5f;

                    frame1.ColorBuffer.At(x, y, 1) = 1.0f; // Green
                    frame1.GrayBuffer.At(x, y) = (x % 2 == 0) ? 1.0f : 0.0f;
                }
            }
        }

        var lap = new ModifiedLaplacianFocusMeasure();
        lap.ComputeFocusMap(frame0.GrayBuffer, frame0.FocusMap);
        lap.ComputeFocusMap(frame1.GrayBuffer, frame1.FocusMap);

        var frames = new List<StackFrame> { frame0, frame1 };
        var depthEstimator = new StandardDepthMapEstimator();
        using var depthResult = depthEstimator.EstimateDepthMap(frames, enableSmoothing: false);

        var pyramidFusion = new MultiScalePyramidFusionEngine();
        using var fused = pyramidFusion.Fuse(frames, depthResult, new FusionSettings { PyramidLevels = 3 });

        // Verify top half is predominantly red and bottom half is predominantly green
        float topRed = fused.At(size / 2, size / 4, 0);
        float botGreen = fused.At(size / 2, size * 3 / 4, 1);

        Assert.True(topRed > 0.6f, $"Expected topRed > 0.6 but was {topRed}");
        Assert.True(botGreen > 0.6f, $"Expected botGreen > 0.6 but was {botGreen}");

        frame0.Dispose();
        frame1.Dispose();
    }
}
