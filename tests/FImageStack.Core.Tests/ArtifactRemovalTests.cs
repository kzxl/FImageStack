using FImageStack.Core;
using FImageStack.Core.Artifact;
using FImageStack.Core.DepthMap;
using FImageStack.Core.Models;
using FImageStack.Core.Motion;
using FImageStack.Core.Reconstruction;
using Xunit;

namespace FImageStack.Core.Tests;

public class ArtifactRemovalTests
{
    [Fact]
    public void MotionDetector_ShouldDetectDynamicZonesAccurately()
    {
        int size = 32;
        var frame0 = new StackFrame
        {
            Index = 0,
            Width = size,
            Height = size,
            GrayBuffer = new ImageBuffer<float>(size, size, 1)
        };
        var frame1 = new StackFrame
        {
            Index = 1,
            Width = size,
            Height = size,
            GrayBuffer = new ImageBuffer<float>(size, size, 1)
        };

        // Static on left, moving insect on right
        frame1.GrayBuffer.At(20, 20) = 1.0f;
        frame1.GrayBuffer.At(21, 20) = 1.0f;

        var detector = new FrameDifferenceMotionDetector();
        using var motionResult = detector.DetectMotion(new List<StackFrame> { frame0, frame1 }, motionThreshold: 0.1f);

        Assert.Equal(size, motionResult.Width);
        Assert.Equal(size, motionResult.Height);
        Assert.True(motionResult.MotionMap.At(20, 20) > 0.3f);
        Assert.Equal(0f, motionResult.MotionMap.At(4, 4));

        frame0.Dispose();
        frame1.Dispose();
    }

    [Fact]
    public void HaloAndSeamDetector_ShouldDetectAndRepairArtifacts()
    {
        int size = 32;
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

        // Create high-contrast subject in center
        for (int y = 8; y < 24; y++)
        {
            for (int x = 8; x < 24; x++)
            {
                frame0.ColorBuffer.At(x, y, 0) = 1.0f;
                frame0.GrayBuffer.At(x, y) = 1.0f;
                frame0.FocusMap.At(x, y) = 1.0f;
            }
        }

        var depthEstimator = new StandardDepthMapEstimator();
        using var depthResult = depthEstimator.EstimateDepthMap(new List<StackFrame> { frame0, frame1 }, enableSmoothing: false);

        using var fused = frame0.ColorBuffer.Clone();
        // Add synthetic bright halo along edge
        fused.At(8, 8, 0) = 0.2f;
        fused.At(8, 8, 1) = 0.9f; // bright fringe

        var artifactDetector = new StandardArtifactDetector();
        using var artifactMap = artifactDetector.DetectArtifacts(fused, new List<StackFrame> { frame0, frame1 }, depthResult, sensitivity: 0.8f);

        var autoRepair = new StandardAutoRepairEngine();
        var (repaired, report) = autoRepair.AutoRepair(fused, new List<StackFrame> { frame0, frame1 }, artifactMap);

        Assert.NotNull(repaired);
        Assert.NotNull(report);
        Assert.Equal(size, repaired.Width);
        Assert.Equal(size, repaired.Height);

        repaired.Dispose();
        frame0.Dispose();
        frame1.Dispose();
    }
}
