using FImageStack.Core.Depth3D;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class Depth3DTests
{
    [Fact]
    public void DepthMeshExporter_NormalMap_FlatSurfaceShouldPointUp()
    {
        int w = 16;
        int h = 16;
        using var flatDepth = new ImageBuffer<float>(w, h, 1);
        flatDepth.AsSpan().Fill(0.5f); // Constant flat plane

        var exporter = new DepthMeshExporter();
        using var normalMap = exporter.GenerateNormalMap(flatDepth, zScale: 10.0f);

        Assert.Equal(w, normalMap.Width);
        Assert.Equal(h, normalMap.Height);
        Assert.Equal(3, normalMap.Channels);

        // Center pixel on flat plane should have normal (0, 0, 1), which maps to RGB (0.5, 0.5, 1.0)
        float r = normalMap.At(8, 8, 0);
        float g = normalMap.At(8, 8, 1);
        float b = normalMap.At(8, 8, 2);

        Assert.True(Math.Abs(r - 0.5f) < 0.05f, $"R={r}");
        Assert.True(Math.Abs(g - 0.5f) < 0.05f, $"G={g}");
        Assert.True(Math.Abs(b - 1.0f) < 0.05f, $"B={b}");
    }

    [Fact]
    public void DepthMeshExporter_ExportToPly_ShouldGenerateValidAsciiHeaderAndData()
    {
        int w = 8;
        int h = 8;
        using var depth = new ImageBuffer<float>(w, h, 1);
        using var color = new ImageBuffer<float>(w, h, 3);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                depth.At(x, y) = (float)x / w;
                color.At(x, y, 0) = 1.0f; // Red
                color.At(x, y, 1) = 0.0f;
                color.At(x, y, 2) = 0.0f;
            }
        }

        var exporter = new DepthMeshExporter();
        using var ms = new MemoryStream();
        var options = new DepthMeshOptions
        {
            ZScale = 50.0f,
            DecimationStep = 1
        };

        exporter.ExportToPly(depth, color, ms, options);
        ms.Position = 0;

        using var reader = new StreamReader(ms);
        string text = reader.ReadToEnd();

        Assert.Contains("ply", text);
        Assert.Contains("format ascii 1.0", text);
        Assert.Contains("element vertex 64", text);
        Assert.Contains("property float x", text);
        Assert.Contains("property uchar red", text);
        Assert.Contains("end_header", text);
    }

    [Fact]
    public void DepthMeshExporter_ExportToObj_ShouldGenerateVerticesAndFaces()
    {
        int w = 4;
        int h = 4;
        using var depth = new ImageBuffer<float>(w, h, 1);
        depth.AsSpan().Fill(0.2f);

        var exporter = new DepthMeshExporter();
        using var sw = new StringWriter();
        var options = new DepthMeshOptions
        {
            ZScale = 10.0f,
            DecimationStep = 1
        };

        exporter.ExportToObj(depth, null, sw, options);
        string objContent = sw.ToString();

        Assert.Contains("# FImageStack Computational 3D Surface Mesh", objContent);
        Assert.Contains("v ", objContent);
        Assert.Contains("f ", objContent);
    }
}
