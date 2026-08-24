using System.Runtime.CompilerServices;
using FImageStack.Core.Models;
using FImageStack.Core.Motion;

namespace FImageStack.Core.Quality;

public interface ITemporalDenoiseEngine
{
    void DenoiseStack(
        IList<StackFrame> frames,
        MotionDetectionResult? motionResult = null,
        float strength = 1.0f,
        IProgress<StackProgress>? progress = null);
}

public sealed class TemporalDenoiseEngine : ITemporalDenoiseEngine
{
    private readonly INoiseEstimator _noiseEstimator;

    public TemporalDenoiseEngine(INoiseEstimator? noiseEstimator = null)
    {
        _noiseEstimator = noiseEstimator ?? new NoiseEstimator();
    }

    public unsafe void DenoiseStack(
        IList<StackFrame> frames,
        MotionDetectionResult? motionResult = null,
        float strength = 1.0f,
        IProgress<StackProgress>? progress = null)
    {
        if (frames == null || frames.Count <= 1) return;

        int count = frames.Count;
        int width = frames[0].Width;
        int height = frames[0].Height;

        float sigmaNoise = _noiseEstimator.EstimateStackNoise(frames as IReadOnlyList<StackFrame> ?? frames.ToList());
        float sigmaColor = Math.Max(0.005f, sigmaNoise * strength);
        float sigmaColorSq2 = 2f * sigmaColor * sigmaColor;
        float sigmaTempSq2 = 2f * 2.5f * 2.5f; // Temporal Gaussian radius ~ 2.5 frames

        float* motionPtr = motionResult?.MotionMap != null ? motionResult.MotionMap.DataPointer : null;

        float*[] grayPtrs = new float*[count];
        float*[] colorPtrs = new float*[count];

        for (int i = 0; i < count; i++)
        {
            grayPtrs[i] = frames[i].GrayBuffer != null ? frames[i].GrayBuffer!.DataPointer : null;
            colorPtrs[i] = frames[i].ColorBuffer != null ? frames[i].ColorBuffer!.DataPointer : null;
        }

        // Process each frame with temporal multi-frame averaging
        for (int k = 0; k < count; k++)
        {
            if (grayPtrs[k] == null) continue;

            using var cleanGray = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
            using var cleanColor = colorPtrs[k] != null ? new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32) : null;

            float* dstGray = cleanGray.DataPointer;
            float* dstColor = cleanColor != null ? cleanColor.DataPointer : null;
            int currentK = k;

            Parallel.For(0, height, y =>
            {
                int rowOffset = y * width;
                int pixelOffset = rowOffset * 3;

                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    int cIdx = pixelOffset + x * 3;

                    float centerGray = grayPtrs[currentK][idx];
                    float sumWeight = 0f;
                    float sumGray = 0f;
                    float sumR = 0f, sumG = 0f, sumB = 0f;

                    int tMin = Math.Max(0, currentK - 4);
                    int tMax = Math.Min(count - 1, currentK + 4);

                    for (int j = tMin; j <= tMax; j++)
                    {
                        if (grayPtrs[j] == null) continue;

                        float jGray = grayPtrs[j][idx];
                        float grayDiff = jGray - centerGray;
                        float colorWeight = MathF.Exp(-(grayDiff * grayDiff) / sigmaColorSq2);

                        int tDist = j - currentK;
                        float tempWeight = MathF.Exp(-(tDist * tDist) / sigmaTempSq2);

                        float motionScore = motionPtr != null ? motionPtr[idx] : 0f;
                        float motionWeight = 1.0f - Math.Clamp(motionScore * 1.5f, 0f, 0.95f);

                        float w = colorWeight * tempWeight * motionWeight;

                        sumGray += jGray * w;
                        sumWeight += w;

                        if (dstColor != null && colorPtrs[j] != null)
                        {
                            sumR += colorPtrs[j][cIdx + 0] * w;
                            sumG += colorPtrs[j][cIdx + 1] * w;
                            sumB += colorPtrs[j][cIdx + 2] * w;
                        }
                    }

                    if (sumWeight > 1e-6f)
                    {
                        float invW = 1.0f / sumWeight;
                        dstGray[idx] = sumGray * invW;
                        if (dstColor != null)
                        {
                            dstColor[cIdx + 0] = Math.Clamp(sumR * invW, 0f, 1f);
                            dstColor[cIdx + 1] = Math.Clamp(sumG * invW, 0f, 1f);
                            dstColor[cIdx + 2] = Math.Clamp(sumB * invW, 0f, 1f);
                        }
                    }
                    else
                    {
                        dstGray[idx] = centerGray;
                        if (dstColor != null && colorPtrs[currentK] != null)
                        {
                            dstColor[cIdx + 0] = colorPtrs[currentK][cIdx + 0];
                            dstColor[cIdx + 1] = colorPtrs[currentK][cIdx + 1];
                            dstColor[cIdx + 2] = colorPtrs[currentK][cIdx + 2];
                        }
                    }
                }
            });

            // Copy denoised pixels back to target frame
            cleanGray.AsSpan().CopyTo(frames[currentK].GrayBuffer!.AsSpan());
            if (cleanColor != null && frames[currentK].ColorBuffer != null)
            {
                cleanColor.AsSpan().CopyTo(frames[currentK].ColorBuffer!.AsSpan());
            }

            progress?.Report(new StackProgress("Temporal Denoise", (double)(k + 1) / count * 100, $"Denoised frame {k + 1}/{count} (σ_noise={sigmaNoise:F3})"));
        }
    }
}
