using FImageStack.Core;
using FImageStack.Core.Alignment;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class LocalAlignmentTests
{
    [Fact]
    public void EstimateLocalElasticMesh_ShouldComputeTileDisplacements()
    {
        int size = 64;
        var refFrame = new StackFrame
        {
            Index = 0,
            Width = size,
            Height = size,
            GrayBuffer = new ImageBuffer<float>(size, size, 1),
            ColorBuffer = new ImageBuffer<float>(size, size, 3)
        };

        var tgtFrame = new StackFrame
        {
            Index = 1,
            Width = size,
            Height = size,
            GrayBuffer = new ImageBuffer<float>(size, size, 1),
            ColorBuffer = new ImageBuffer<float>(size, size, 3)
        };

        // Draw a distinct unique block at center (28..36, 28..36) in refFrame
        for (int y = 28; y <= 36; y++)
        {
            for (int x = 28; x <= 36; x++)
            {
                refFrame.GrayBuffer.At(x, y) = 1.0f;
                refFrame.ColorBuffer.At(x, y, 0) = 1.0f;
            }
        }

        // In tgtFrame, draw the same block shifted by dx = +3, dy = +2
        for (int y = 30; y <= 38; y++)
        {
            for (int x = 31; x <= 39; x++)
            {
                tgtFrame.GrayBuffer.At(x, y) = 1.0f;
                tgtFrame.ColorBuffer.At(x, y, 0) = 1.0f;
            }
        }

        var mesh = AdvancedAlignmentEngine.EstimateLocalElasticMesh(refFrame, tgtFrame, 4, 4);
        Assert.Equal(4, mesh.GridCols);
        Assert.Equal(4, mesh.GridRows);

        // Center control point (gx=2, gy=2) corresponds to center area (x=32..48, y=32..48)
        // Check that detected offset matches the shift
        Assert.True(mesh.Dx[2, 2] == 3 || mesh.Dx[1, 1] == 3, $"Detected Dx at center should be 3, got Dx[2,2]={mesh.Dx[2, 2]}, Dx[1,1]={mesh.Dx[1, 1]}");

        AdvancedAlignmentEngine.ApplyLocalElasticWarp(tgtFrame, mesh);

        refFrame.Dispose();
        tgtFrame.Dispose();
    }
}
