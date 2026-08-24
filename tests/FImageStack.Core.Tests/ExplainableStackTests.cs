using FImageStack.Core.DepthMap;
using FImageStack.Core.Inspection;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class ExplainableStackTests
{
    [Fact]
    public void PixelInspectorEngine_InspectPixel_ShouldIdentifyWinningFrame()
    {
        int w = 20;
        int h = 20;
        int frameCount = 5;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                AlignmentConfidence = 0.98,
                GrayBuffer = new ImageBuffer<float>(w, h),
                FocusMap = new ImageBuffer<float>(w, h)
            };

            // Frame #3 (index 2) has peak sharpness at (10, 10)
            float focusVal = (i == 2) ? 0.941f : (0.20f + i * 0.05f);
            frame.FocusMap.At(10, 10) = focusVal;
            frames.Add(frame);
        }

        var stackResult = new ProcessedStackResult(w, h)
        {
            DepthResult = new DepthMapResult(w, h)
        };
        stackResult.DepthResult.DepthMap.At(10, 10) = 2.15f;

        var engine = new PixelInspectorEngine();
        var report = engine.InspectPixel(10, 10, stackResult, frames);

        Assert.Equal(10, report.X);
        Assert.Equal(10, report.Y);
        Assert.Equal(2, report.PrimaryFrameIndex);
        Assert.Equal(3, report.PrimaryFrameNumber);
        Assert.Equal(2.15f, report.EstimatedDepth, 2);

        // Check Multi-Factor Breakdown
        Assert.Equal(0.941f, report.PrimaryFactors.Sharpness, 3);
        Assert.Equal(0.98f, report.PrimaryFactors.AlignmentConfidence, 2);
        Assert.True(report.PrimaryFactors.MotionPenalty < 0.05f);
        Assert.True(report.PrimaryFactors.EdgeConfidence > 0.5f);

        // Check Weight Distribution
        Assert.Equal(frameCount, report.WeightDistribution.Count);
        Assert.True(report.WeightDistribution[2].IsPrimaryWinner);
        Assert.True(report.WeightDistribution[2].WeightPercentage > 50.0f, $"Winner weight was {report.WeightDistribution[2].WeightPercentage}%");

        float totalWeight = report.WeightDistribution.Sum(w => w.WeightPercentage);
        Assert.Equal(100.0f, totalWeight, 1);

        // Check Explanation Text
        Assert.Contains("Frame #3", report.Explanation);
        Assert.Contains("Pixel (10, 10)", report.Explanation);

        foreach (var f in frames) f.Dispose();
        stackResult.Dispose();
    }
}
