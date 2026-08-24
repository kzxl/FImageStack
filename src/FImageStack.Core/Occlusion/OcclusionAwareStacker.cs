using System.Runtime.CompilerServices;
using FImageStack.Core.Models;

namespace FImageStack.Core.Occlusion;

public interface IOcclusionAwareStacker
{
    OcclusionMapResult AnalyzeOcclusion(IReadOnlyList<StackFrame> frames, DepthMapResult? depthResult = null);
}

public sealed class OcclusionAwareStacker : IOcclusionAwareStacker
{
    public unsafe OcclusionMapResult AnalyzeOcclusion(IReadOnlyList<StackFrame> frames, DepthMapResult? depthResult = null)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Frames collection is empty.", nameof(frames));

        int width = frames[0].Width;
        int height = frames[0].Height;
        int frameCount = frames.Count;

        var result = new OcclusionMapResult(width, height, frameCount);

        float*[] focusPtrs = new float*[frameCount];
        byte*[] statePtrs = new byte*[frameCount];
        float*[] alphaPtrs = new float*[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            focusPtrs[i] = frames[i].FocusMap != null ? frames[i].FocusMap!.DataPointer : null;
            statePtrs[i] = result.StateMaps[i].DataPointer;
            alphaPtrs[i] = result.ForegroundAlphaMaps[i].DataPointer;
        }

        float* riskPtr = result.OcclusionRiskMap.DataPointer;
        int* srcMapPtr = depthResult?.SourceFrameMap != null ? depthResult.SourceFrameMap.DataPointer : null;

        // 1. Initial pass: Compute per-frame foreground masks and alpha matting
        for (int f = 0; f < frameCount; f++)
        {
            float* fFocus = focusPtrs[f];
            float* fAlpha = alphaPtrs[f];
            if (fFocus == null) continue;

            Parallel.For(0, height, y =>
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    float s = fFocus[idx];
                    bool isBestFrame = srcMapPtr != null && srcMapPtr[idx] == f;
                    float alpha = isBestFrame || s > 0.40f ? Math.Clamp((s - 0.20f) / 0.60f, 0f, 1f) : 0f;
                    fAlpha[idx] = alpha;
                }
            });
        }

        // 2. Multi-layer Occlusion Detection & Visibility State Classification
        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width;

            for (int x = 0; x < width; x++)
            {
                int idx = rowOffset + x;
                float maxForegroundSharpness = 0f;
                int foregroundBestFrame = -1;

                // Scan forward from frame 0 to frameCount - 1 (near to far depth)
                for (int f = 0; f < frameCount; f++)
                {
                    float currentSharpness = focusPtrs[f] != null ? focusPtrs[f][idx] : 0f;
                    float currentAlpha = alphaPtrs[f][idx];

                    if (f == 0 || foregroundBestFrame < 0)
                    {
                        statePtrs[f][idx] = (byte)OcclusionState.Visible;
                        if (currentAlpha > 0.4f && currentSharpness > maxForegroundSharpness)
                        {
                            maxForegroundSharpness = currentSharpness;
                            foregroundBestFrame = f;
                        }
                        continue;
                    }

                    // For subsequent deeper frames: check if occluded by earlier foreground layers
                    if (maxForegroundSharpness > 0.35f && f > foregroundBestFrame)
                    {
                        if (currentSharpness < 0.25f || currentSharpness < maxForegroundSharpness * 0.65f)
                        {
                            // Obscured by foreground defocus blur
                            statePtrs[f][idx] = (byte)OcclusionState.Occluded;
                            riskPtr[idx] = Math.Max(riskPtr[idx], maxForegroundSharpness);
                        }
                        else if (currentSharpness >= 0.40f)
                        {
                            // Sharp background feature revealed behind/around foreground
                            statePtrs[f][idx] = (byte)OcclusionState.Revealed;
                        }
                        else
                        {
                            statePtrs[f][idx] = (byte)OcclusionState.Visible;
                        }
                    }
                    else
                    {
                        statePtrs[f][idx] = (byte)OcclusionState.Visible;
                        if (currentAlpha > 0.4f && currentSharpness > maxForegroundSharpness)
                        {
                            maxForegroundSharpness = currentSharpness;
                            foregroundBestFrame = f;
                        }
                    }
                }
            }
        });

        return result;
    }
}
