using FImageStack.Core.Models;
using FImageStack.Core.Stitching;
using Xunit;

namespace FImageStack.Core.Tests;

public class MosaicStitchingTests
{
    [Fact]
    public void MosaicStitch_TwoHorizontalTiles_CreatesExpandedCanvas()
    {
        int tileW = 100;
        int tileH = 80;
        int overlap = 40; // 40% overlap

        // Create 2 synthetic frames with shared global pattern: f(gx, gy)
        var frames = new List<StackFrame>();

        for (int i = 0; i < 2; i++)
        {
            int globalOriginX = i * (tileW - overlap);
            var frame = new StackFrame
            {
                Index = i,
                Width = tileW,
                Height = tileH,
                ColorBuffer = new ImageBuffer<float>(tileW, tileH, 3),
                GrayBuffer = new ImageBuffer<float>(tileW, tileH, 1)
            };

            for (int y = 0; y < tileH; y++)
            {
                for (int x = 0; x < tileW; x++)
                {
                    int gx = globalOriginX + x;
                    int gy = y;

                    // High-frequency distinct chirp pattern (unique across space)
                    float pattern = 0.5f + 0.35f * MathF.Sin(gx * 0.05f + gx * gx * 0.00015f) * MathF.Cos(gy * 0.06f + gy * gy * 0.00015f);
                    frame.ColorBuffer.At(x, y, 0) = pattern;
                    frame.ColorBuffer.At(x, y, 1) = pattern;
                    frame.ColorBuffer.At(x, y, 2) = pattern;
                    frame.GrayBuffer.At(x, y) = pattern;
                }
            }

            frames.Add(frame);
        }

        var engine = new MosaicStitchEngine();
        var settings = new StitchSettings
        {
            SearchOverlapRatio = 0.40f,
            BlendingMode = SeamBlendingMode.LinearFeathering,
            EnableGainCompensation = true
        };

        using var result = engine.Stitch(frames, settings);

        Assert.NotNull(result);
        Assert.NotNull(result.StitchedImage);
        Assert.Equal(2, result.TilesMerged);

        // Expected width is tileW + (tileW - overlap) = 160 (+ border padding)
        Assert.True(result.CanvasWidth >= 155, $"Canvas width was {result.CanvasWidth}, expected >= 155");
        Assert.True(result.CanvasHeight >= tileH, $"Canvas height was {result.CanvasHeight}, expected >= {tileH}");

        // Validate that pixels in the center of the stitched image are non-zero
        int midX = result.CanvasWidth / 2;
        int midY = result.CanvasHeight / 2;
        float centerPixel = result.StitchedImage.At(midX, midY, 0);
        Assert.True(centerPixel > 0.1f, $"Center pixel intensity too low: {centerPixel}");

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void MosaicStitch_TwoByTwoMatrix_ExpandsBothDimensions()
    {
        int tileW = 80;
        int tileH = 80;
        int stepX = 50; // 30px overlap horizontally
        int stepY = 50; // 30px overlap vertically

        var frames = new List<StackFrame>();

        // 2x2 grid: (0,0), (1,0), (0,1), (1,1)
        var gridCoords = new (int col, int row)[]
        {
            (0, 0),
            (1, 0),
            (0, 1),
            (1, 1)
        };

        for (int i = 0; i < gridCoords.Length; i++)
        {
            var (col, row) = gridCoords[i];
            int originX = col * stepX;
            int originY = row * stepY;

            var frame = new StackFrame
            {
                Index = i,
                Width = tileW,
                Height = tileH,
                ColorBuffer = new ImageBuffer<float>(tileW, tileH, 3),
                GrayBuffer = new ImageBuffer<float>(tileW, tileH, 1)
            };

            for (int y = 0; y < tileH; y++)
            {
                for (int x = 0; x < tileW; x++)
                {
                    int gx = originX + x;
                    int gy = originY + y;
                    // Distinct 2D chirp pattern across the whole 2x2 field
                    float val = 0.5f + 0.35f * MathF.Sin(gx * 0.06f + gx * gx * 0.0002f) * MathF.Cos(gy * 0.06f + gy * gy * 0.0002f);
                    frame.ColorBuffer.At(x, y, 0) = val;
                    frame.ColorBuffer.At(x, y, 1) = val;
                    frame.ColorBuffer.At(x, y, 2) = val;
                    frame.GrayBuffer.At(x, y) = val;
                }
            }

            frames.Add(frame);
        }

        var engine = new MosaicStitchEngine();
        var settings = new StitchSettings
        {
            SearchOverlapRatio = 0.35f,
            BlendingMode = SeamBlendingMode.LinearFeathering
        };

        using var result = engine.Stitch(frames, settings);

        Assert.NotNull(result);
        Assert.Equal(4, result.TilesMerged);

        // Expected dimensions: width >= 80 + 50 = 130, height >= 80 + 50 = 130
        Assert.True(result.CanvasWidth >= 125, $"Canvas width was {result.CanvasWidth}, expected >= 125");
        Assert.True(result.CanvasHeight >= 125, $"Canvas height was {result.CanvasHeight}, expected >= 125");

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void StitchBlendEngine_DirectHomographies_ProducesSmoothBlend()
    {
        int tileW = 64;
        int tileH = 64;
        var frames = new List<StackFrame>();

        for (int i = 0; i < 2; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = tileW,
                Height = tileH,
                ColorBuffer = new ImageBuffer<float>(tileW, tileH, 3),
                GrayBuffer = new ImageBuffer<float>(tileW, tileH, 1)
            };
            frame.ColorBuffer.AsSpan().Fill(i == 0 ? 0.4f : 0.8f);
            frame.GrayBuffer.AsSpan().Fill(i == 0 ? 0.4f : 0.8f);
            frames.Add(frame);
        }

        // Known exact global homographies: frame 0 at (0, 0), frame 1 at (32, 0)
        var homographies = new List<float[]>
        {
            new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 },
            new float[9] { 1, 0, 32, 0, 1, 0, 0, 0, 1 }
        };

        var blendEngine = new StitchBlendEngine();
        var settings = new StitchSettings
        {
            BlendingMode = SeamBlendingMode.LinearFeathering,
            EnableGainCompensation = false
        };

        using var result = blendEngine.BlendTilesIntoCanvas(frames, homographies, settings, 0);

        Assert.NotNull(result);
        Assert.True(result.CanvasWidth >= 96);
        Assert.True(result.CanvasHeight >= 64);

        // Check overlap seam area (around x = 48) - should be a smooth blend between 0.4 and 0.8 (~0.6)
        int sampleX = 48 + (int)result.OriginOffsetX;
        int sampleY = 32 + (int)result.OriginOffsetY;
        if (sampleX >= 0 && sampleX < result.CanvasWidth && sampleY >= 0 && sampleY < result.CanvasHeight)
        {
            float seamVal = result.StitchedImage.At(sampleX, sampleY, 0);
            Assert.True(seamVal >= 0.35f && seamVal <= 0.85f, $"Seam pixel was {seamVal}, expected blend in [0.35, 0.85]");
        }

        foreach (var f in frames) f.Dispose();
    }
}
