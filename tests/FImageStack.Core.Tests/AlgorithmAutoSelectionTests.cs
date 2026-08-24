using FImageStack.Core.Models;
using FImageStack.Core.Selection;
using Xunit;

namespace FImageStack.Core.Tests;

public class AlgorithmAutoSelectionTests
{
    [Fact]
    public void AlgorithmAutoSelector_AutoSelectAlgorithms_ShouldBenchmarkAndSelectBest()
    {
        int w = 24;
        int h = 24;
        int frameCount = 3;
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
                    // High-contrast textured macro image
                    frame.GrayBuffer.At(x, y) = ((x + y + i * 4) % 8 < 4) ? 0.9f : 0.1f;
                }
            }
            frames.Add(frame);
        }

        var selector = new AlgorithmAutoSelector();
        var result = selector.AutoSelectAlgorithms(frames);

        Assert.NotNull(result);
        Assert.Equal(5, result.BenchmarkScores.Count);
        Assert.True(result.BestScore >= 80.0f, $"Best score was {result.BestScore}");
        Assert.Single(result.BenchmarkScores, b => b.IsSelectedBest);
        Assert.Equal(result.BenchmarkScores[0].Score, result.BestScore);
        Assert.Contains("SELECTED BEST", result.SelectionSummary);
        Assert.Contains("Algorithm Auto-Selection Benchmark", result.SelectionSummary);

        foreach (var f in frames) f.Dispose();
    }
}
