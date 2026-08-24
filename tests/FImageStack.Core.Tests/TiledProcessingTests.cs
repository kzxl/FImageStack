using FImageStack.Core;
using FImageStack.Core.DepthMap;
using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using FImageStack.Core.Tiling;
using Xunit;

namespace FImageStack.Core.Tests;

public class TiledProcessingTests
{
    [Fact]
    public void StandardTiledProcessor_ShouldFuseImageInTilesWithCosineFeathering()
    {
        int size = 64;
        int frameCount = 3;
        var frames = new List<StackFrame>();

        for (int f = 0; f < frameCount; f++)
        {
            var frame = new StackFrame
            {
                Index = f,
                Width = size,
                Height = size,
                ColorBuffer = new ImageBuffer<float>(size, size, 3),
                GrayBuffer = new ImageBuffer<float>(size, size, 1),
                FocusMap = new ImageBuffer<float>(size, size, 1)
            };

            frame.ColorBuffer.AsSpan().Fill(0.3f * (f + 1));
            frame.GrayBuffer.AsSpan().Fill(0.3f * (f + 1));
            frame.FocusMap.AsSpan().Fill(f == 1 ? 0.9f : 0.2f);
            frames.Add(frame);
        }

        var depthResult = new DepthMapResult(size, size);
        depthResult.SourceFrameMap.AsSpan().Fill(1);
        depthResult.ConfidenceMap.AsSpan().Fill(0.95f);

        var fusionEngine = new WinnerTakesAllFusionEngine();
        var settings = new FusionSettings { Method = FusionMethod.WinnerTakesAll };
        var processor = new StandardTiledProcessor();

        // Process in 32x32 tiles with 8px overlap margin
        using var result = processor.ProcessTiled(frames, depthResult, fusionEngine, settings, tileSize: 32, overlapMargin: 8);

        Assert.NotNull(result);
        Assert.Equal(size, result.Width);
        Assert.Equal(size, result.Height);
        Assert.Equal(3, result.Channels);

        // Center pixel should be smoothly fused from frame 1 (0.6f)
        float r = result.At(32, 32, 0);
        Assert.True(r >= 0.5f && r <= 0.7f, $"Center pixel was {r}, expected ~0.6f");

        depthResult.Dispose();
        foreach (var f in frames) f.Dispose();
    }
}
