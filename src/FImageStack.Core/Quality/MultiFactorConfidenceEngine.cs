using System.Runtime.CompilerServices;
using FImageStack.Core.Models;
using FImageStack.Core.Motion;

namespace FImageStack.Core.Quality;

public readonly record struct ConfidenceBreakdown(
    float Sharpness,
    float Alignment,
    float MotionInvariance,
    float EdgeCoherence,
    float TotalConfidence)
{
    public override string ToString() =>
        $"S:{Sharpness:F2} | A:{Alignment:F2} | M:{1f - MotionInvariance:F2} | E:{EdgeCoherence:F2} => C:{TotalConfidence:F2}";
}

public interface IMultiFactorConfidenceEngine
{
    ImageBuffer<float>[] ComputeConfidenceMaps(IReadOnlyList<StackFrame> frames, MotionDetectionResult? motionResult = null);
    ConfidenceBreakdown GetBreakdown(int x, int y, int frameIndex, IReadOnlyList<StackFrame> frames, MotionDetectionResult? motionResult = null);
}

public sealed class MultiFactorConfidenceEngine : IMultiFactorConfidenceEngine
{
    public unsafe ImageBuffer<float>[] ComputeConfidenceMaps(IReadOnlyList<StackFrame> frames, MotionDetectionResult? motionResult = null)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Stack contains no frames.", nameof(frames));

        int width = frames[0].Width;
        int height = frames[0].Height;
        int frameCount = frames.Count;

        var confidenceMaps = new ImageBuffer<float>[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            confidenceMaps[i] = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
        }

        float*[] confPtrs = new float*[frameCount];
        float*[] grayPtrs = new float*[frameCount];
        float*[] focusPtrs = new float*[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            confPtrs[i] = confidenceMaps[i].DataPointer;
            grayPtrs[i] = frames[i].GrayBuffer != null ? frames[i].GrayBuffer!.DataPointer : null;
            focusPtrs[i] = frames[i].FocusMap != null ? frames[i].FocusMap!.DataPointer : null;
        }

        float* motionPtr = motionResult?.MotionMap != null ? motionResult.MotionMap.DataPointer : null;

        // Step 1: Pre-compute mean luminance across all frames for alignment consistency comparison
        using var meanBuffer = new ImageBuffer<float>(width, height);
        float* meanPtr = meanBuffer.DataPointer;

        if (grayPtrs[0] != null)
        {
            Parallel.For(0, height, y =>
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    float sum = 0f;
                    for (int f = 0; f < frameCount; f++)
                    {
                        if (grayPtrs[f] != null) sum += grayPtrs[f][idx];
                    }
                    meanPtr[idx] = sum / frameCount;
                }
            });
        }

        // Step 2: Compute Multi-Factor Confidence for each frame
        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int idx = rowOffset + x;

                // Find max sharpness at this pixel across all frames to normalize sharpness factor
                float maxLocalSharpness = 1e-6f;
                for (int f = 0; f < frameCount; f++)
                {
                    if (focusPtrs[f] != null && focusPtrs[f][idx] > maxLocalSharpness)
                    {
                        maxLocalSharpness = focusPtrs[f][idx];
                    }
                }

                float motionScore = motionPtr != null ? motionPtr[idx] : 0f;
                float motionInvariance = Math.Clamp(1.0f - motionScore * 1.5f, 0.05f, 1.0f);
                float meanVal = meanPtr[idx];

                for (int f = 0; f < frameCount; f++)
                {
                    // 1. Sharpness Factor
                    float rawSharpness = focusPtrs[f] != null ? focusPtrs[f][idx] : 0.5f;
                    float s = Math.Clamp(rawSharpness / maxLocalSharpness, 0f, 1f);

                    // 2. Alignment Factor (structural luminance distance to consensus mean)
                    float a = 1.0f;
                    if (grayPtrs[f] != null)
                    {
                        float grayVal = grayPtrs[f][idx];
                        float diff = MathF.Abs(grayVal - meanVal);
                        a = MathF.Exp(-diff / 0.12f);
                    }

                    // 3. Edge Coherence Factor (gradient magnitude presence)
                    float e = Math.Clamp(s * 1.2f, 0.2f, 1.0f);

                    // 4. Combine all 4 factors
                    // C = S * (0.35 + 0.65*A) * (0.2 + 0.8*M_inv) * (0.4 + 0.6*E)
                    float confidence = s * (0.35f + 0.65f * a) * (0.2f + 0.8f * motionInvariance) * (0.4f + 0.6f * e);
                    confPtrs[f][idx] = Math.Clamp(confidence * frames[f].PriorityWeight, 0f, 1f);
                }
            }
        });

        return confidenceMaps;
    }

    public unsafe ConfidenceBreakdown GetBreakdown(int x, int y, int frameIndex, IReadOnlyList<StackFrame> frames, MotionDetectionResult? motionResult = null)
    {
        if (frames == null || frames.Count == 0 || frameIndex < 0 || frameIndex >= frames.Count)
            return new ConfidenceBreakdown(0f, 0f, 0f, 0f, 0f);

        int width = frames[0].Width;
        int height = frames[0].Height;
        if (x < 0 || x >= width || y < 0 || y >= height)
            return new ConfidenceBreakdown(0f, 0f, 0f, 0f, 0f);

        int idx = y * width + x;
        int frameCount = frames.Count;

        float maxLocalSharpness = 1e-6f;
        float meanVal = 0f;
        for (int f = 0; f < frameCount; f++)
        {
            if (frames[f].FocusMap != null)
            {
                float sVal = frames[f].FocusMap!.DataPointer[idx];
                if (sVal > maxLocalSharpness) maxLocalSharpness = sVal;
            }
            if (frames[f].GrayBuffer != null)
            {
                meanVal += frames[f].GrayBuffer!.DataPointer[idx];
            }
        }
        meanVal /= frameCount;

        float rawSharpness = frames[frameIndex].FocusMap != null ? frames[frameIndex].FocusMap!.DataPointer[idx] : 0.5f;
        float sharpness = Math.Clamp(rawSharpness / maxLocalSharpness, 0f, 1f);

        float alignment = 1.0f;
        if (frames[frameIndex].GrayBuffer != null)
        {
            float g = frames[frameIndex].GrayBuffer!.DataPointer[idx];
            alignment = MathF.Exp(-MathF.Abs(g - meanVal) / 0.12f);
        }

        float motionScore = motionResult?.MotionMap != null ? motionResult.MotionMap.DataPointer[idx] : 0f;
        float motionInvariance = Math.Clamp(1.0f - motionScore * 1.5f, 0.05f, 1.0f);
        float edge = Math.Clamp(sharpness * 1.2f, 0.2f, 1.0f);

        float total = sharpness * (0.35f + 0.65f * alignment) * (0.2f + 0.8f * motionInvariance) * (0.4f + 0.6f * edge);
        total = Math.Clamp(total * frames[frameIndex].PriorityWeight, 0f, 1f);

        return new ConfidenceBreakdown(sharpness, alignment, motionInvariance, edge, total);
    }
}
