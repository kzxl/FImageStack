using System.Runtime.CompilerServices;
using FImageStack.Core.Models;

namespace FImageStack.Core.Quality;

public interface INoiseEstimator
{
    float EstimateNoiseStandardDeviation(ImageBuffer<float> grayBuffer);
    float EstimateStackNoise(IReadOnlyList<StackFrame> frames);
}

public sealed class NoiseEstimator : INoiseEstimator
{
    public unsafe float EstimateNoiseStandardDeviation(ImageBuffer<float> grayBuffer)
    {
        int w = grayBuffer.Width;
        int h = grayBuffer.Height;
        if (w < 8 || h < 8) return 0.01f;

        float* ptr = grayBuffer.DataPointer;
        int sampleStep = Math.Max(1, (w * h) / 10000); // Sample ~10,000 pixels
        var residuals = new List<float>(10000);

        for (int y = 2; y < h - 2; y += 2)
        {
            int rowOffset = y * w;
            for (int x = 2; x < w - 2; x += 2)
            {
                int idx = rowOffset + x;
                float center = ptr[idx];

                // 3x3 Laplacian / High-pass kernel: [0, 1, 0; 1, -4, 1; 0, 1, 0]
                float lap = (ptr[rowOffset - w + x] + ptr[rowOffset + w + x] +
                             ptr[rowOffset + x - 1] + ptr[rowOffset + x + 1]) - 4f * center;

                // Only take samples from non-edge regions (laplacian residual)
                residuals.Add(MathF.Abs(lap) * 0.5f);
            }
        }

        if (residuals.Count == 0) return 0.01f;

        residuals.Sort();
        // Median Absolute Deviation formula for Gaussian noise: sigma = 1.4826 * median(|residual|)
        int medianIdx = residuals.Count / 2;
        float medianVal = residuals[medianIdx];

        float sigma = 1.4826f * medianVal;
        return Math.Clamp(sigma, 0.001f, 0.25f);
    }

    public float EstimateStackNoise(IReadOnlyList<StackFrame> frames)
    {
        if (frames == null || frames.Count == 0) return 0.01f;

        float sumSigma = 0f;
        int validCount = 0;

        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i].GrayBuffer != null)
            {
                sumSigma += EstimateNoiseStandardDeviation(frames[i].GrayBuffer!);
                validCount++;
            }
        }

        return validCount > 0 ? sumSigma / validCount : 0.01f;
    }
}
