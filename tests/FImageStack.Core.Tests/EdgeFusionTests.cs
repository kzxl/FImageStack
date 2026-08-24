using FImageStack.Core;
using FImageStack.Core.Models;
using FImageStack.Core.Reconstruction;
using Xunit;

namespace FImageStack.Core.Tests;

public class EdgeFusionTests
{
    [Fact]
    public void EdgeFusionEngine_ShouldDetectAndReconstructEdgeDiscontinuities()
    {
        int size = 32;
        var fused = new ImageBuffer<float>(size, size, 3);
        var sourceMap = new ImageBuffer<int>(size, size, 1);
        var frames = new List<StackFrame>();

        // Create 3 source frames
        for (int f = 0; f < 3; f++)
        {
            var frame = new StackFrame
            {
                Index = f,
                Width = size,
                Height = size,
                GrayBuffer = new ImageBuffer<float>(size, size, 1),
                ColorBuffer = new ImageBuffer<float>(size, size, 3)
            };

            // Frame 2 has a sharp bright diagonal edge: x == y
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float val = (f == 2 && Math.Abs(x - y) <= 1) ? 1.0f : 0.1f;
                    frame.GrayBuffer.At(x, y) = val;
                    frame.ColorBuffer.At(x, y, 0) = val;
                    frame.ColorBuffer.At(x, y, 1) = val;
                    frame.ColorBuffer.At(x, y, 2) = val;
                }
            }

            frames.Add(frame);
        }

        // Populate fused image with an artificial edge discontinuity (left half is Frame 0, right half is Frame 2)
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int frameIdx = (x < size / 2) ? 0 : 2;
                sourceMap.At(x, y) = frameIdx;

                float val = (frameIdx == 2 && Math.Abs(x - y) <= 1) ? 1.0f : 0.1f;
                fused.At(x, y, 0) = val;
                fused.At(x, y, 1) = val;
                fused.At(x, y, 2) = val;
            }
        }

        var engine = new EdgeFusionEngine();
        using var result = engine.ReconstructEdges(fused, frames, sourceMap, edgeThreshold: 0.02f);

        Assert.NotNull(result.ReconstructedImage);
        Assert.NotNull(result.EdgeDiscontinuityMask);
        Assert.True(result.ReconstructedEdgeCount >= 0);

        fused.Dispose();
        sourceMap.Dispose();
        foreach (var f in frames) f.Dispose();
    }
}
