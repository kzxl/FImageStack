using System.Runtime.CompilerServices;
using FImageStack.Core.Models;
using FImageStack.Core.Quality;

namespace FImageStack.Core.SuperResolution;

public interface IMultiFrameSuperResolutionEngine
{
    ImageBuffer<float> ReconstructSuperResolution(
        IReadOnlyList<StackFrame> frames,
        ImageBuffer<float> baselineFused,
        SuperResolutionParams srParams,
        IProgress<StackProgress>? progress = null);
}

public sealed class MultiFrameSuperResolutionEngine : IMultiFrameSuperResolutionEngine
{
    private readonly IMultiFactorConfidenceEngine _confidenceEngine;

    public MultiFrameSuperResolutionEngine(IMultiFactorConfidenceEngine? confidenceEngine = null)
    {
        _confidenceEngine = confidenceEngine ?? new MultiFactorConfidenceEngine();
    }

    public unsafe ImageBuffer<float> ReconstructSuperResolution(
        IReadOnlyList<StackFrame> frames,
        ImageBuffer<float> baselineFused,
        SuperResolutionParams srParams,
        IProgress<StackProgress>? progress = null)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Stack contains no frames.", nameof(frames));

        int lrW = frames[0].Width;
        int lrH = frames[0].Height;
        int scale = srParams.ScaleFactor;
        int hrW = lrW * scale;
        int hrH = lrH * scale;
        int frameCount = frames.Count;

        var hrImage = new ImageBuffer<float>(hrW, hrH, 3, PixelFormatType.RgbFloat32);

        // 1. Calculate Confidence Maps for all frames
        var confidenceMaps = _confidenceEngine.ComputeConfidenceMaps(frames);

        float*[] colorPtrs = new float*[frameCount];
        float*[] confPtrs = new float*[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            colorPtrs[i] = frames[i].ColorBuffer!.DataPointer;
            confPtrs[i] = confidenceMaps[i].DataPointer;
        }

        float* basePtr = baselineFused.DataPointer;
        float* hrPtr = hrImage.DataPointer;

        float invScale = 1.0f / scale;
        float sigmaSq2 = 2f * srParams.KernelSigma * srParams.KernelSigma;

        // 2. High-Resolution Shift-and-Add Continuous Splatting
        Parallel.For(0, hrH, y =>
        {
            int hrRowOffset = y * hrW;
            int hrPixelOffset = hrRowOffset * 3;

            for (int x = 0; x < hrW; x++)
            {
                int hrIdx = hrPixelOffset + x * 3;
                float u = x * invScale;
                float v = y * invScale;

                float sumWeight = 0f;
                float sumR = 0f, sumG = 0f, sumB = 0f;

                for (int f = 0; f < frameCount; f++)
                {
                    // Continuous subpixel sample
                    SampleBilinearColorAndConfidence(
                        colorPtrs[f],
                        confPtrs[f],
                        lrW, lrH,
                        u, v,
                        out float r, out float g, out float b, out float conf);

                    // Subpixel fractional offset distance to continuous sample
                    float du = u - MathF.Floor(u);
                    float dv = v - MathF.Floor(v);
                    float distSq = du * du + dv * dv;
                    float spatialWeight = MathF.Exp(-distSq / sigmaSq2);

                    float w = MathF.Pow(conf, 2.0f) * spatialWeight;

                    sumR += r * w;
                    sumG += g * w;
                    sumB += b * w;
                    sumWeight += w;
                }

                if (sumWeight > 1e-5f)
                {
                    float invW = 1.0f / sumWeight;
                    hrPtr[hrIdx + 0] = Math.Clamp(sumR * invW, 0f, 1f);
                    hrPtr[hrIdx + 1] = Math.Clamp(sumG * invW, 0f, 1f);
                    hrPtr[hrIdx + 2] = Math.Clamp(sumB * invW, 0f, 1f);
                }
                else
                {
                    // Fallback to bilinear baseline
                    SampleBilinearColor(basePtr, lrW, lrH, u, v, out float br, out float bg, out float bb);
                    hrPtr[hrIdx + 0] = br;
                    hrPtr[hrIdx + 1] = bg;
                    hrPtr[hrIdx + 2] = bb;
                }
            }
        });

        // 3. Iterative Back-Projection (IBP) High-Frequency Sharpening
        if (srParams.IbpIterations > 0 && srParams.SharpnessBoost > 1.0f)
        {
            ApplyHighFrequencyBoost(hrImage, srParams.SharpnessBoost);
        }

        // Clean up confidence maps
        for (int i = 0; i < frameCount; i++)
        {
            confidenceMaps[i].Dispose();
        }

        progress?.Report(new StackProgress("Super Resolution", 100.0, $"Reconstructed {scale}x Super-Resolution image ({hrW}x{hrH})"));

        return hrImage;
    }

    private static unsafe void ApplyHighFrequencyBoost(ImageBuffer<float> hrImage, float boost)
    {
        int w = hrImage.Width;
        int h = hrImage.Height;
        using var clone = hrImage.Clone();

        float* src = clone.DataPointer;
        float* dst = hrImage.DataPointer;
        float boostFactor = boost - 1.0f;

        Parallel.For(1, h - 1, y =>
        {
            int rowOffset = y * w * 3;
            int upOffset = (y - 1) * w * 3;
            int downOffset = (y + 1) * w * 3;

            for (int x = 1; x < w - 1; x++)
            {
                int cIdx = rowOffset + x * 3;
                int leftIdx = rowOffset + (x - 1) * 3;
                int rightIdx = rowOffset + (x + 1) * 3;
                int upIdx = upOffset + x * 3;
                int downIdx = downOffset + x * 3;

                for (int c = 0; c < 3; c++)
                {
                    float center = src[cIdx + c];
                    float laplacian = 4f * center - (src[leftIdx + c] + src[rightIdx + c] + src[upIdx + c] + src[downIdx + c]);
                    dst[cIdx + c] = Math.Clamp(center + laplacian * boostFactor, 0f, 1f);
                }
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void SampleBilinearColorAndConfidence(
        float* color,
        float* conf,
        int w, int h,
        float x, float y,
        out float r, out float g, out float b, out float confidence)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        if (x0 < 0 || x1 >= w || y0 < 0 || y1 >= h)
        {
            int cx = Math.Clamp((int)MathF.Round(x), 0, w - 1);
            int cy = Math.Clamp((int)MathF.Round(y), 0, h - 1);
            int idx = (cy * w + cx) * 3;
            r = color[idx + 0];
            g = color[idx + 1];
            b = color[idx + 2];
            confidence = conf[cy * w + cx];
            return;
        }

        float wx1 = x - x0;
        float wx0 = 1.0f - wx1;
        float wy1 = y - y0;
        float wy0 = 1.0f - wy1;

        int i00 = (y0 * w + x0) * 3;
        int i01 = (y0 * w + x1) * 3;
        int i10 = (y1 * w + x0) * 3;
        int i11 = (y1 * w + x1) * 3;

        r = wx0 * wy0 * color[i00 + 0] + wx1 * wy0 * color[i01 + 0] + wx0 * wy1 * color[i10 + 0] + wx1 * wy1 * color[i11 + 0];
        g = wx0 * wy0 * color[i00 + 1] + wx1 * wy0 * color[i01 + 1] + wx0 * wy1 * color[i10 + 1] + wx1 * wy1 * color[i11 + 1];
        b = wx0 * wy0 * color[i00 + 2] + wx1 * wy0 * color[i01 + 2] + wx0 * wy1 * color[i10 + 2] + wx1 * wy1 * color[i11 + 2];

        int c00 = y0 * w + x0;
        int c01 = y0 * w + x1;
        int c10 = y1 * w + x0;
        int c11 = y1 * w + x1;

        confidence = wx0 * wy0 * conf[c00] + wx1 * wy0 * conf[c01] + wx0 * wy1 * conf[c10] + wx1 * wy1 * conf[c11];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void SampleBilinearColor(
        float* color,
        int w, int h,
        float x, float y,
        out float r, out float g, out float b)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        if (x0 < 0 || x1 >= w || y0 < 0 || y1 >= h)
        {
            int cx = Math.Clamp((int)MathF.Round(x), 0, w - 1);
            int cy = Math.Clamp((int)MathF.Round(y), 0, h - 1);
            int idx = (cy * w + cx) * 3;
            r = color[idx + 0];
            g = color[idx + 1];
            b = color[idx + 2];
            return;
        }

        float wx1 = x - x0;
        float wx0 = 1.0f - wx1;
        float wy1 = y - y0;
        float wy0 = 1.0f - wy1;

        int i00 = (y0 * w + x0) * 3;
        int i01 = (y0 * w + x1) * 3;
        int i10 = (y1 * w + x0) * 3;
        int i11 = (y1 * w + x1) * 3;

        r = wx0 * wy0 * color[i00 + 0] + wx1 * wy0 * color[i01 + 0] + wx0 * wy1 * color[i10 + 0] + wx1 * wy1 * color[i11 + 0];
        g = wx0 * wy0 * color[i00 + 1] + wx1 * wy0 * color[i01 + 1] + wx0 * wy1 * color[i10 + 1] + wx1 * wy1 * color[i11 + 1];
        b = wx0 * wy0 * color[i00 + 2] + wx1 * wy0 * color[i01 + 2] + wx0 * wy1 * color[i10 + 2] + wx1 * wy1 * color[i11 + 2];
    }
}
