using System.Runtime.CompilerServices;
using FImageStack.Core.Models;
using FImageStack.Core.Occlusion;
using FImageStack.Core.Quality;

namespace FImageStack.Core.Fusion;

public sealed class OcclusionAwareFusionEngine : IFusionEngine
{
    public FusionMethod Method => FusionMethod.OcclusionAware;

    private readonly IOcclusionAwareStacker _occlusionStacker;
    private readonly IMultiFactorConfidenceEngine _confidenceEngine;

    public OcclusionAwareFusionEngine(
        IOcclusionAwareStacker? occlusionStacker = null,
        IMultiFactorConfidenceEngine? confidenceEngine = null)
    {
        _occlusionStacker = occlusionStacker ?? new OcclusionAwareStacker();
        _confidenceEngine = confidenceEngine ?? new MultiFactorConfidenceEngine();
    }

    public unsafe ImageBuffer<float> Fuse(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        FusionSettings settings)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Stack contains no frames.", nameof(frames));

        int width = frames[0].Width;
        int height = frames[0].Height;
        int frameCount = frames.Count;

        var fused = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32);

        // 1. Compute Multi-Factor Confidence Maps
        var confidenceMaps = _confidenceEngine.ComputeConfidenceMaps(frames);

        // 2. Perform Multi-Layer Occlusion Analysis
        using var occlusionResult = _occlusionStacker.AnalyzeOcclusion(frames, depthResult);

        float*[] colorPtrs = new float*[frameCount];
        float*[] confPtrs = new float*[frameCount];
        byte*[] statePtrs = new byte*[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            colorPtrs[i] = frames[i].ColorBuffer!.DataPointer;
            confPtrs[i] = confidenceMaps[i].DataPointer;
            statePtrs[i] = occlusionResult.StateMaps[i].DataPointer;
        }

        float* dstPtr = fused.DataPointer;
        float powerExponent = 4.0f;

        // 3. Occlusion-Modulated Soft Blending
        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width;
            int pixelOffset = rowOffset * 3;

            for (int x = 0; x < width; x++)
            {
                int idx = rowOffset + x;
                int dstIdx = pixelOffset + x * 3;

                float sumWeight = 0f;
                float sumR = 0f;
                float sumG = 0f;
                float sumB = 0f;

                for (int f = 0; f < frameCount; f++)
                {
                    float rawConf = confPtrs[f][idx];
                    byte state = statePtrs[f][idx];

                    // Modulate confidence based on Occlusion State
                    float occlusionMultiplier = state switch
                    {
                        (byte)OcclusionState.Occluded => 0.08f, // Suppress foreground defocus blur
                        (byte)OcclusionState.Revealed => 1.30f, // Boost clean background structures
                        _ => 1.00f                             // Visible
                    };

                    float effectiveConf = rawConf * occlusionMultiplier;
                    float weight = MathF.Pow(effectiveConf, powerExponent);

                    int cIdx = (rowOffset + x) * 3;
                    float r = colorPtrs[f][cIdx + 0];
                    float g = colorPtrs[f][cIdx + 1];
                    float b = colorPtrs[f][cIdx + 2];

                    sumR += r * weight;
                    sumG += g * weight;
                    sumB += b * weight;
                    sumWeight += weight;
                }

                if (sumWeight > 1e-6f)
                {
                    float invWeight = 1.0f / sumWeight;
                    dstPtr[dstIdx + 0] = Math.Clamp(sumR * invWeight, 0f, 1f);
                    dstPtr[dstIdx + 1] = Math.Clamp(sumG * invWeight, 0f, 1f);
                    dstPtr[dstIdx + 2] = Math.Clamp(sumB * invWeight, 0f, 1f);
                }
                else
                {
                    // Fallback to highest confidence frame
                    int bestFrame = 0;
                    float maxC = confPtrs[0][idx];
                    for (int f = 1; f < frameCount; f++)
                    {
                        if (confPtrs[f][idx] > maxC)
                        {
                            maxC = confPtrs[f][idx];
                            bestFrame = f;
                        }
                    }
                    int cIdx = (rowOffset + x) * 3;
                    dstPtr[dstIdx + 0] = colorPtrs[bestFrame][cIdx + 0];
                    dstPtr[dstIdx + 1] = colorPtrs[bestFrame][cIdx + 1];
                    dstPtr[dstIdx + 2] = colorPtrs[bestFrame][cIdx + 2];
                }
            }
        });

        // Clean up confidence maps
        for (int i = 0; i < frameCount; i++)
        {
            confidenceMaps[i].Dispose();
        }

        return fused;
    }
}
