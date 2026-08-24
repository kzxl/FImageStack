using FImageStack.Core.Models;
using FImageStack.Core.Pipeline;
using Xunit;

namespace FImageStack.Core.Tests;

public class ProcessingGraphTests
{
    [Fact]
    public void ProcessingGraphEngine_FullInitialExecution_ShouldExecuteAllNodes()
    {
        int w = 16;
        int h = 16;
        int frameCount = 4;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                GrayBuffer = new ImageBuffer<float>(w, h)
            };
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    frame.GrayBuffer.At(x, y) = 0.5f + i * 0.1f;
                }
            }
            frames.Add(frame);
        }

        var engine = new ProcessingGraphEngine();
        engine.BuildGraph(frames);

        Assert.Equal(20, engine.Nodes.Count); // 4 frames * 4 per-frame + 4 global

        var result = engine.Execute();

        Assert.NotNull(result);
        Assert.Equal(20, result.ExecutedNodesCount);
        Assert.Equal(0, result.CachedReusedNodesCount);
        Assert.NotNull(result.OutputImage);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void ProcessingGraphEngine_DisableFrame_ShouldOnlyRecomputeDownstreamNodes()
    {
        int w = 16;
        int h = 16;
        int frameCount = 4;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                GrayBuffer = new ImageBuffer<float>(w, h)
            };
            frames.Add(frame);
        }

        var engine = new ProcessingGraphEngine();
        engine.BuildGraph(frames);

        // 1. First initial execution (compiles and caches everything)
        var res1 = engine.Execute();
        Assert.Equal(20, res1.ExecutedNodesCount);

        // 2. User disables Frame #1 (e.g. culling blurry frame)
        engine.SetFrameEnabled(1, false);

        // 3. Incremental Re-execution
        var res2 = engine.Execute();

        // 4 frame nodes from frames 0, 2, 3 = 12 nodes reused from cache!
        // Only 4 downstream aggregator nodes (depth, fusion, repair, output) are recomputed!
        Assert.Equal(4, res2.ExecutedNodesCount);
        Assert.Equal(12, res2.CachedReusedNodesCount);
        Assert.Contains("Reused from Cache: 12 nodes", res2.Summary);

        foreach (var f in frames) f.Dispose();
    }
}
