using FImageStack.Core.Models;
using FImageStack.Core.Quality;
using Xunit;

namespace FImageStack.Core.Tests;

public class FocusWaveTests
{
    [Fact]
    public void FocusWaveEngine_AnalyzeFocusWave_LinearSequence_ShouldReturnHighUniformity()
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
                FocusMap = new ImageBuffer<float>(w, h)
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Linear focus progression
                    bool inFocus = (y >= i * 4 && y < (i + 1) * 4);
                    frame.FocusMap.At(x, y) = inFocus ? 0.9f : 0.1f;
                }
            }
            frames.Add(frame);
        }

        var engine = new FocusWaveEngine();
        var result = engine.AnalyzeFocusWave(frames);

        Assert.Equal(5, result.TotalFrames);
        Assert.Equal(5, result.WavePoints.Count);
        Assert.True(result.StepUniformityScore >= 75.0f, $"Uniformity score was {result.StepUniformityScore}%");
        Assert.Equal(0, result.GapCount);
        Assert.Contains("Frame", result.AsciiWaveGraph);
        Assert.Contains("Near", result.AsciiWaveGraph);
        Assert.Contains("Far", result.AsciiWaveGraph);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void FocusWaveEngine_AnalyzeFocusWave_JumpySequence_ShouldDetectGaps()
    {
        int w = 20;
        int h = 20;
        int frameCount = 5;
        var frames = new List<StackFrame>();

        // Normal step 0->1, huge gap jump 1->2
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h)
            };

            int focusRow = (i <= 1) ? i * 2 : (i * 4 + 4);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool inFocus = (y == focusRow);
                    frame.FocusMap.At(x, y) = inFocus ? 1.0f : 0.0f;
                }
            }
            frames.Add(frame);
        }

        var engine = new FocusWaveEngine();
        var result = engine.AnalyzeFocusWave(frames);

        Assert.True(result.GapCount > 0);
        Assert.Contains(result.WavePoints, p => p.IsGapWarning);

        foreach (var f in frames) f.Dispose();
    }
}
