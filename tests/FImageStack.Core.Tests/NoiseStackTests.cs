using FImageStack.Core.Models;
using FImageStack.Core.Noise;
using Xunit;

namespace FImageStack.Core.Tests;

public class NoiseStackTests
{
    private static List<StackFrame> CreateNoisyFrames(int count, int w = 32, int h = 32, float trueValue = 0.5f, float noiseAmp = 0.15f)
    {
        var frames = new List<StackFrame>(count);
        var rand = new Random(42);

        for (int i = 0; i < count; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                ColorBuffer = new ImageBuffer<float>(w, h, 3),
                GrayBuffer = new ImageBuffer<float>(w, h, 1)
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        float noise = (float)(rand.NextDouble() - 0.5) * 2f * noiseAmp;
                        float val = Math.Clamp(trueValue + noise, 0f, 1f);
                        frame.ColorBuffer.At(x, y, c) = val;
                    }
                    frame.GrayBuffer.At(x, y) = frame.ColorBuffer.At(x, y, 0);
                }
            }
            frames.Add(frame);
        }

        return frames;
    }

    [Fact]
    public void NoiseStackEngine_Mean_ShouldReduceNoiseVariance()
    {
        int frameCount = 9;
        var frames = CreateNoisyFrames(frameCount, 32, 32, 0.5f, 0.20f);
        var engine = new NoiseStackEngine();

        using var meanBuffer = engine.ProcessMean(frames);

        // Measure single frame variance vs mean stack variance
        float singleVar = 0f;
        float meanVar = 0f;
        int count = 32 * 32 * 3;

        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    float diff1 = frames[0].ColorBuffer!.At(x, y, c) - 0.5f;
                    singleVar += diff1 * diff1;

                    float diffM = meanBuffer.At(x, y, c) - 0.5f;
                    meanVar += diffM * diffM;
                }
            }
        }

        singleVar /= count;
        meanVar /= count;

        // Mean of 9 frames should reduce variance by ~1/9 (factor of 0.11)
        Assert.True(meanVar < singleVar * 0.35f, $"Variance not sufficiently reduced: single={singleVar}, mean={meanVar}");

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void NoiseStackEngine_Median_ShouldRejectSaltAndPepperOutliers()
    {
        int frameCount = 5;
        var frames = CreateNoisyFrames(frameCount, 16, 16, 0.5f, 0.05f);

        // Inject severe cosmic ray / hot pixel into frame 2 at (8, 8)
        frames[2].ColorBuffer!.At(8, 8, 0) = 1.0f;
        frames[2].ColorBuffer!.At(8, 8, 1) = 1.0f;
        frames[2].ColorBuffer!.At(8, 8, 2) = 1.0f;

        var engine = new NoiseStackEngine();
        using var medianBuffer = engine.ProcessMedian(frames);

        // Median should completely reject the outlier at (8, 8)
        Assert.True(Math.Abs(medianBuffer.At(8, 8, 0) - 0.5f) < 0.10f, $"Outlier not rejected by median: val={medianBuffer.At(8, 8, 0)}");

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void NoiseStackEngine_KappaSigma_ShouldFilterOutliersAndGenerateRejectionMap()
    {
        int frameCount = 7;
        var frames = CreateNoisyFrames(frameCount, 16, 16, 0.5f, 0.02f);

        // Add extreme hot pixel spike in frame 1 across all channels at (5, 5)
        frames[1].ColorBuffer!.At(5, 5, 0) = 1.0f;
        frames[1].ColorBuffer!.At(5, 5, 1) = 1.0f;
        frames[1].ColorBuffer!.At(5, 5, 2) = 1.0f;

        var engine = new NoiseStackEngine();
        var (denoised, rejectionMap) = engine.ProcessKappaSigma(frames, kappa: 2.0f, iterations: 3, generateRejectionMap: true);

        Assert.NotNull(rejectionMap);
        // The pixel (5, 5) should have recorded rejections
        Assert.True(rejectionMap.At(5, 5) > 0.0f, $"Rejection map at (5,5) was {rejectionMap.At(5, 5)}");
        // The output value should remain close to 0.5
        Assert.True(Math.Abs(denoised.At(5, 5, 0) - 0.5f) < 0.10f, $"Denoised val was {denoised.At(5, 5, 0)}");

        denoised.Dispose();
        rejectionMap.Dispose();
        foreach (var f in frames) f.Dispose();
    }


    [Fact]
    public void NoiseStackEngine_StreamingAccumulator_ShouldMatchMean()
    {
        int frameCount = 6;
        var frames = CreateNoisyFrames(frameCount, 20, 20, 0.4f, 0.10f);
        var engine = new NoiseStackEngine();

        using var meanBuffer = engine.ProcessMean(frames);
        var (streamMean, streamVar) = engine.ProcessStreaming(frames);

        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    Assert.Equal(meanBuffer.At(x, y, c), streamMean.At(x, y, c), 4);
                }
            }
        }

        streamMean.Dispose();
        streamVar.Dispose();
        foreach (var f in frames) f.Dispose();
    }
}
