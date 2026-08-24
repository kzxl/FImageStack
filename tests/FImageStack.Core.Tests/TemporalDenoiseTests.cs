using FImageStack.Core.Models;
using FImageStack.Core.Quality;
using Xunit;

namespace FImageStack.Core.Tests;

public class TemporalDenoiseTests
{
    [Fact]
    public void NoiseEstimator_ShouldEstimateNoiseLevelAccurately()
    {
        int w = 64;
        int h = 64;
        using var grayBuffer = new ImageBuffer<float>(w, h);

        var rand = new Random(42);
        // Base smooth signal + random high ISO grain noise
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float noise = (float)(rand.NextDouble() - 0.5) * 0.10f; // range [-0.05, +0.05]
                grayBuffer.At(x, y) = Math.Clamp(0.5f + noise, 0f, 1f);
            }
        }

        var estimator = new NoiseEstimator();
        float estimatedSigma = estimator.EstimateNoiseStandardDeviation(grayBuffer);

        Assert.True(estimatedSigma >= 0.01f && estimatedSigma <= 0.10f, $"Estimated sigma was {estimatedSigma}");
    }

    [Fact]
    public void TemporalDenoiseEngine_ShouldReduceNoiseAcrossMultiFrameStack()
    {
        int w = 32;
        int h = 32;
        int frameCount = 5;
        var frames = new List<StackFrame>();
        var rand = new Random(123);

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                GrayBuffer = new ImageBuffer<float>(w, h),
                ColorBuffer = new ImageBuffer<float>(w, h, 3)
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float noise = (float)(rand.NextDouble() - 0.5) * 0.12f;
                    float val = Math.Clamp(0.5f + noise, 0f, 1f);
                    frame.GrayBuffer.At(x, y) = val;
                    frame.ColorBuffer.At(x, y, 0) = val;
                    frame.ColorBuffer.At(x, y, 1) = val;
                    frame.ColorBuffer.At(x, y, 2) = val;
                }
            }
            frames.Add(frame);
        }

        // Measure variance of Frame 2 before denoising
        float sumVarBefore = 0f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float diff = frames[2].GrayBuffer!.At(x, y) - 0.5f;
                sumVarBefore += diff * diff;
            }
        }

        var denoiseEngine = new TemporalDenoiseEngine();
        denoiseEngine.DenoiseStack(frames, strength: 1.5f);

        // Measure variance of Frame 2 after denoising
        float sumVarAfter = 0f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float diff = frames[2].GrayBuffer!.At(x, y) - 0.5f;
                sumVarAfter += diff * diff;
            }
        }

        // Denoising should significantly reduce the noise variance
        Assert.True(sumVarAfter < sumVarBefore * 0.60f, $"Noise variance was not reduced sufficiently: before={sumVarBefore}, after={sumVarAfter}");

        foreach (var f in frames) f.Dispose();
    }
}
