using System.Runtime.CompilerServices;
using FImageStack.Core.Models;
using FImageStack.Core.Noise;

namespace FImageStack.Core.Raw;

public interface IBayerFusionEngine
{
    RawBayerBuffer MergeBayerFrames(IReadOnlyList<RawBayerBuffer> rawFrames, RawStackSettings settings);
    void NormalizeLinearBayer(RawBayerBuffer raw);
}

public sealed class BayerFusionEngine : IBayerFusionEngine
{
    public RawBayerBuffer MergeBayerFrames(IReadOnlyList<RawBayerBuffer> rawFrames, RawStackSettings settings)
    {
        if (rawFrames == null || rawFrames.Count == 0)
            throw new ArgumentException("RAW frames cannot be empty.", nameof(rawFrames));

        int w = rawFrames[0].Width;
        int h = rawFrames[0].Height;
        var pattern = rawFrames[0].Pattern;
        int frameCount = rawFrames.Count;

        // 1. Normalize all input frames into linear normalized [0.0 - 1.0] range
        for (int i = 0; i < frameCount; i++)
        {
            NormalizeLinearBayer(rawFrames[i]);
        }

        var merged = new RawBayerBuffer(w, h, pattern)
        {
            WhiteLevel = 1.0f,
            BlackLevels = new float[] { 0f, 0f, 0f, 0f },
            WhiteBalanceGains = (float[])rawFrames[0].WhiteBalanceGains.Clone(),
            ColorMatrix = (float[])rawFrames[0].ColorMatrix.Clone()
        };

        if (frameCount == 1)
        {
            rawFrames[0].Data.CopyTo(merged.Data);
            return merged;
        }

        float kappa = settings.Kappa;
        int iterations = settings.Iterations;
        bool useKappaSigma = settings.MergeMethod == NoiseStackMethod.KappaSigmaClipping;

        // 2. Perform direct per-photosite multi-frame statistical fusion on Bayer grid
        Parallel.For(0, h, () => (Values: new float[frameCount], Mask: new bool[frameCount]), (y, state, tls) =>
        {
            var values = tls.Values;
            var mask = tls.Mask;

            for (int x = 0; x < w; x++)
            {
                for (int i = 0; i < frameCount; i++)
                {
                    values[i] = rawFrames[i].Data.At(x, y);
                    mask[i] = true;
                }

                if (useKappaSigma && frameCount > 2)
                {
                    for (int iter = 0; iter < iterations; iter++)
                    {
                        float sum = 0f;
                        int validCount = 0;
                        for (int i = 0; i < frameCount; i++)
                        {
                            if (mask[i])
                            {
                                sum += values[i];
                                validCount++;
                            }
                        }

                        if (validCount <= 1) break;

                        float mean = sum / validCount;
                        float sumSq = 0f;
                        for (int i = 0; i < frameCount; i++)
                        {
                            if (mask[i])
                            {
                                float diff = values[i] - mean;
                                sumSq += diff * diff;
                            }
                        }

                        float sigma = MathF.Sqrt(sumSq / (validCount - 1));
                        if (sigma < 1e-6f) break;

                        float threshold = kappa * sigma;
                        bool changed = false;

                        for (int i = 0; i < frameCount; i++)
                        {
                            if (mask[i] && MathF.Abs(values[i] - mean) > threshold)
                            {
                                mask[i] = false;
                                changed = true;
                            }
                        }

                        if (!changed) break;
                    }

                    float finalSum = 0f;
                    int finalCount = 0;
                    for (int i = 0; i < frameCount; i++)
                    {
                        if (mask[i])
                        {
                            finalSum += values[i];
                            finalCount++;
                        }
                    }

                    merged.Data.At(x, y) = finalCount > 0 ? (finalSum / finalCount) : values[0];
                }
                else
                {
                    // Fast Mean
                    float sum = 0f;
                    for (int i = 0; i < frameCount; i++) sum += values[i];
                    merged.Data.At(x, y) = sum / frameCount;
                }
            }

            return tls;
        }, _ => { });

        // 3. Highlight Recovery
        if (settings.EnableHighlightRecovery)
        {
            ApplyHighlightRecovery(merged);
        }

        return merged;
    }

    public unsafe void NormalizeLinearBayer(RawBayerBuffer raw)
    {
        if (raw.WhiteLevel <= 1.001f) return; // Already normalized

        int w = raw.Width;
        int h = raw.Height;
        float* ptr = raw.Data.DataPointer;
        float wLevel = raw.WhiteLevel;
        var bLevels = raw.BlackLevels;
        var pattern = raw.Pattern;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w;
            int yMod = y & 1;

            for (int x = 0; x < w; x++)
            {
                int xMod = x & 1;
                int cIdx = GetCfaChannelIndex(pattern, xMod, yMod);
                float bLevel = bLevels[cIdx];
                float denom = MathF.Max(1.0f, wLevel - bLevel);

                float rawVal = ptr[rowOffset + x];
                float linVal = Math.Clamp((rawVal - bLevel) / denom, 0f, 1f);
                ptr[rowOffset + x] = linVal;
            }
        });

        raw.WhiteLevel = 1.0f;
        raw.BlackLevels = new float[] { 0f, 0f, 0f, 0f };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCfaChannelIndex(BayerPatternType pattern, int xMod, int yMod)
    {
        // Channel indices: 0 = R, 1 = Gr, 2 = Gb, 3 = B
        return pattern switch
        {
            BayerPatternType.RGGB => (yMod == 0) ? (xMod == 0 ? 0 : 1) : (xMod == 0 ? 2 : 3),
            BayerPatternType.BGGR => (yMod == 0) ? (xMod == 0 ? 3 : 2) : (xMod == 0 ? 1 : 0),
            BayerPatternType.GRBG => (yMod == 0) ? (xMod == 0 ? 1 : 0) : (xMod == 0 ? 3 : 2),
            BayerPatternType.GBRG => (yMod == 0) ? (xMod == 0 ? 2 : 3) : (xMod == 0 ? 0 : 1),
            _ => 0
        };
    }

    private static unsafe void ApplyHighlightRecovery(RawBayerBuffer raw)
    {
        int w = raw.Width;
        int h = raw.Height;
        float* ptr = raw.Data.DataPointer;

        // If a green pixel saturates at 1.0, look at neighboring unclipped channels to reconstruct ratio
        Parallel.For(1, h - 1, y =>
        {
            int row = y * w;
            for (int x = 1; x < w - 1; x++)
            {
                float v = ptr[row + x];
                if (v >= 0.99f)
                {
                    // Average 4 cross neighbors
                    float neighborAvg = (ptr[row - w + x] + ptr[row + w + x] + ptr[row + x - 1] + ptr[row + x + 1]) * 0.25f;
                    if (neighborAvg < 0.90f)
                    {
                        ptr[row + x] = Math.Min(1.0f, neighborAvg * 1.15f);
                    }
                }
            }
        });
    }
}
