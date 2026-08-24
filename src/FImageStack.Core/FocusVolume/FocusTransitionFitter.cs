using System.Runtime.CompilerServices;
using FImageStack.Core.Models;

namespace FImageStack.Core.FocusVolume;

public interface IFocusTransitionFitter
{
    FocusTransitionModel FitTransition(ReadOnlySpan<float> profile);
    void FitTransitionVolume(FocusVolume volume, ImageBuffer<float> outputMuMap, ImageBuffer<float> outputR2Map, ImageBuffer<float> outputSigmaMap);
    void SynthesizeSubFrameColor(float mu, int width, int height, IReadOnlyList<StackFrame> frames, int pixelIdx, Span<float> outputRgb);
}

public sealed class FocusTransitionFitter : IFocusTransitionFitter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FocusTransitionModel FitTransition(ReadOnlySpan<float> profile)
    {
        int count = profile.Length;
        if (count == 0) return new FocusTransitionModel(0f, 0f, 1f, 0f, 0f, 0f);
        if (count == 1) return new FocusTransitionModel(0f, profile[0], 1f, 0f, 1f, 0f);

        // 1. Find discrete peak and baseline floor
        int peakIdx = 0;
        float maxVal = profile[0];
        float minVal = profile[0];
        float sumVal = profile[0];

        for (int i = 1; i < count; i++)
        {
            float v = profile[i];
            sumVal += v;
            if (v > maxVal)
            {
                maxVal = v;
                peakIdx = i;
            }
            if (v < minVal)
            {
                minVal = v;
            }
        }

        float baseline = minVal;
        float mu = peakIdx;
        float sigma = 1.0f;
        float amplitude = Math.Max(0f, maxVal - baseline);

        // 2. Interior Peak Regression
        if (peakIdx > 0 && peakIdx < count - 1)
        {
            float vPrev = Math.Max(profile[peakIdx - 1] - baseline, 1e-4f);
            float vCurr = Math.Max(profile[peakIdx] - baseline, 1e-4f);
            float vNext = Math.Max(profile[peakIdx + 1] - baseline, 1e-4f);

            float y1 = MathF.Log(vPrev);
            float y2 = MathF.Log(vCurr);
            float y3 = MathF.Log(vNext);

            float denom = 2f * (y1 - 2f * y2 + y3);
            if (denom < -1e-5f) // Concave downward
            {
                float delta = Math.Clamp((y1 - y3) / denom, -0.5f, 0.5f);
                mu = peakIdx + delta;

                float c2 = (y1 - 2f * y2 + y3) * 0.5f;
                sigma = Math.Clamp(MathF.Sqrt(-1f / (2f * c2)), 0.2f, 10.0f);
                amplitude = vCurr * MathF.Exp((delta * delta) / (2f * sigma * sigma));
            }
            else
            {
                // Parabolic fallback
                float pDenom = 2f * (vPrev - 2f * vCurr + vNext);
                if (MathF.Abs(pDenom) > 1e-6f)
                {
                    float delta = Math.Clamp((vPrev - vNext) / pDenom, -0.5f, 0.5f);
                    mu = peakIdx + delta;
                }
            }
        }

        // 3. Compute Goodness of Fit (R^2)
        float meanVal = sumVal / count;
        float ssTot = 0f;
        float ssRes = 0f;

        for (int i = 0; i < count; i++)
        {
            float actual = profile[i];
            float diffMean = actual - meanVal;
            ssTot += diffMean * diffMean;

            float zDiff = i - mu;
            float predicted = amplitude * MathF.Exp(-(zDiff * zDiff) / (2f * sigma * sigma)) + baseline;
            float diffPred = actual - predicted;
            ssRes += diffPred * diffPred;
        }

        float r2 = ssTot > 1e-6f ? Math.Clamp(1.0f - (ssRes / ssTot), 0.0f, 1.0f) : 1.0f;
        float slope = amplitude / (sigma * 1.6487f); // Maximum slope of Gaussian at inflection point

        return new FocusTransitionModel(mu, amplitude, sigma, baseline, r2, slope);
    }

    public unsafe void FitTransitionVolume(
        FocusVolume volume,
        ImageBuffer<float> outputMuMap,
        ImageBuffer<float> outputR2Map,
        ImageBuffer<float> outputSigmaMap)
    {
        int width = volume.Width;
        int height = volume.Height;
        int slices = volume.Slices;

        float* muPtr = outputMuMap.DataPointer;
        float* r2Ptr = outputR2Map.DataPointer;
        float* sigPtr = outputSigmaMap.DataPointer;

        Parallel.For(0, height, y =>
        {
            Span<float> profile = stackalloc float[slices];
            int rowOffset = y * width;

            for (int x = 0; x < width; x++)
            {
                int idx = rowOffset + x;
                volume.CopyProfile(x, y, profile);

                var model = FitTransition(profile);
                muPtr[idx] = model.OptimalMu;
                r2Ptr[idx] = model.GoodnessOfFit;
                sigPtr[idx] = model.TransitionSpread;
            }
        });
    }

    public unsafe void SynthesizeSubFrameColor(
        float mu,
        int width,
        int height,
        IReadOnlyList<StackFrame> frames,
        int pixelIdx,
        Span<float> outputRgb)
    {
        int frameCount = frames.Count;
        if (frameCount == 0) return;

        int f0 = Math.Clamp((int)MathF.Floor(mu), 0, frameCount - 1);
        int f1 = Math.Clamp(f0 + 1, 0, frameCount - 1);
        float alpha = Math.Clamp(mu - f0, 0f, 1f);

        float* c0 = frames[f0].ColorBuffer!.DataPointer + pixelIdx * 3;
        float* c1 = frames[f1].ColorBuffer!.DataPointer + pixelIdx * 3;

        outputRgb[0] = (1f - alpha) * c0[0] + alpha * c1[0];
        outputRgb[1] = (1f - alpha) * c0[1] + alpha * c1[1];
        outputRgb[2] = (1f - alpha) * c0[2] + alpha * c1[2];
    }
}
