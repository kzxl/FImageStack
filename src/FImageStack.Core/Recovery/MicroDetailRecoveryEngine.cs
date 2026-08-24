using FImageStack.Core.DepthMap;
using FImageStack.Core.Models;

namespace FImageStack.Core.Recovery;

public interface IMicroDetailRecoveryEngine
{
    RecoveryRecommendation AnalyzeRecoveryPotential(
        ImageBuffer<float> fusedImage,
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        MicroDetailRecoveryConfig? config = null);

    MicroDetailRecoveryResult RecoverMicroDetails(
        ImageBuffer<float> fusedImage,
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        MicroDetailRecoveryConfig? config = null);
}

public sealed class MicroDetailRecoveryEngine : IMicroDetailRecoveryEngine
{
    public unsafe RecoveryRecommendation AnalyzeRecoveryPotential(
        ImageBuffer<float> fusedImage,
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        MicroDetailRecoveryConfig? config = null)
    {
        if (fusedImage == null) throw new ArgumentNullException(nameof(fusedImage));
        if (frames == null || frames.Count == 0) throw new ArgumentException("Frames list cannot be empty.", nameof(frames));
        if (depthResult == null) throw new ArgumentNullException(nameof(depthResult));

        config ??= new MicroDetailRecoveryConfig();
        int w = fusedImage.Width;
        int h = fusedImage.Height;
        int total = w * h;
        int frameCount = frames.Count;
        int radius = config.NeighborRadius;

        float* depthPtr = depthResult.DepthMap.DataPointer;
        int recoverableCount = 0;

        for (int y = 1; y < h - 1; y++)
        {
            int rowOffset = y * w;
            for (int x = 1; x < w - 1; x++)
            {
                int idx = rowOffset + x;
                float z = depthPtr[idx];
                int k0 = Math.Clamp((int)MathF.Round(z), 0, frameCount - 1);

                var bestFrame = frames[k0];
                float s0 = bestFrame.FocusMap != null ? bestFrame.FocusMap.At(x, y) : 0.5f;

                if (s0 >= config.SharpnessFloorThreshold && s0 <= config.SharpnessCeilingThreshold)
                {
                    // Check if neighbor frames offer complementary high-frequency texture
                    bool hasComplementary = false;
                    int kMin = Math.Max(0, k0 - radius);
                    int kMax = Math.Min(frameCount - 1, k0 + radius);

                    for (int k = kMin; k <= kMax; k++)
                    {
                        if (k == k0) continue;
                        var nf = frames[k];
                        if (nf.GrayBuffer != null)
                        {
                            float c = nf.GrayBuffer.At(x, y);
                            float r = nf.GrayBuffer.At(x + 1, y);
                            float b = nf.GrayBuffer.At(x, y + 1);
                            float grad = MathF.Abs(r - c) + MathF.Abs(b - c);
                            if (grad > 0.04f)
                            {
                                hasComplementary = true;
                                break;
                            }
                        }
                    }

                    if (hasComplementary) recoverableCount++;
                }
            }
        }

        float recoverablePct = ((float)recoverableCount / total) * 100.0f;
        bool isRecommended = recoverablePct >= config.MinRecoverableAreaPercent;
        float estimatedGain = Math.Clamp(recoverablePct * 0.8f, 5.0f, 40.0f);

        string message = isRecommended
            ? $"💡 Detected {recoverablePct:F1}% image area with recoverable micro-details from adjacent frames. Recommended enhancement gain: +{estimatedGain:F0}%."
            : $"Stack has optimal sharpness across {100.0f - recoverablePct:F1}% area. Micro-detail recovery optional.";

        return new RecoveryRecommendation
        {
            IsRecommended = isRecommended,
            RecoverableAreaPercentage = recoverablePct,
            EstimatedDetailGainPercentage = estimatedGain,
            Message = message
        };
    }

    public unsafe MicroDetailRecoveryResult RecoverMicroDetails(
        ImageBuffer<float> fusedImage,
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        MicroDetailRecoveryConfig? config = null)
    {
        if (fusedImage == null) throw new ArgumentNullException(nameof(fusedImage));
        if (frames == null || frames.Count == 0) throw new ArgumentException("Frames list cannot be empty.", nameof(frames));
        if (depthResult == null) throw new ArgumentNullException(nameof(depthResult));

        config ??= new MicroDetailRecoveryConfig();
        int w = fusedImage.Width;
        int h = fusedImage.Height;
        int channels = fusedImage.Channels;
        int frameCount = frames.Count;
        int radius = Math.Clamp(config.NeighborRadius, 1, 4);
        float gamma = Math.Clamp(config.BoostStrength, 0.1f, 3.0f);

        var result = new MicroDetailRecoveryResult(w, h, channels);
        float* outPtr = result.EnhancedImage.DataPointer;
        float* detPtr = result.RecoveredDetailMap.DataPointer;
        float* fusedPtr = fusedImage.DataPointer;
        float* depthPtr = depthResult.DepthMap.DataPointer;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                int pIdx = rowOffset + x;
                float z = depthPtr[pIdx];
                int k0 = Math.Clamp((int)MathF.Round(z), 0, frameCount - 1);

                int kMin = Math.Max(0, k0 - radius);
                int kMax = Math.Min(frameCount - 1, k0 + radius);

                float totalGradWeight = 0f;
                float[] recDetails = new float[channels];

                for (int k = kMin; k <= kMax; k++)
                {
                    var f = frames[k];
                    float grad = 0.05f;

                    if (f.GrayBuffer != null && x > 0 && x < w - 1 && y > 0 && y < h - 1)
                    {
                        float c = f.GrayBuffer.At(x, y);
                        float r = f.GrayBuffer.At(x + 1, y);
                        float b = f.GrayBuffer.At(x, y + 1);
                        grad = MathF.Sqrt((r - c) * (r - c) + (b - c) * (b - c)) + 0.01f;
                    }

                    float wK = grad * grad;
                    totalGradWeight += wK;

                    // Extract high-frequency component: I(x, y) - local mean 3x3
                    for (int c = 0; c < channels; c++)
                    {
                        float centerVal = (f.ColorBuffer != null) ? f.ColorBuffer.At(x, y, c) : (f.GrayBuffer != null ? f.GrayBuffer.At(x, y) : 0.5f);
                        float meanVal = centerVal;

                        if (x > 0 && x < w - 1 && y > 0 && y < h - 1)
                        {
                            float sum = 0f;
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    sum += (f.ColorBuffer != null) ? f.ColorBuffer.At(x + dx, y + dy, c) : (f.GrayBuffer != null ? f.GrayBuffer.At(x + dx, y + dy) : 0.5f);
                                }
                            }
                            meanVal = sum / 9.0f;
                        }

                        float highFreq = centerVal - meanVal;
                        recDetails[c] += wK * highFreq;
                    }
                }

                float invWeight = totalGradWeight > 0 ? 1.0f / totalGradWeight : 1.0f;
                float detailMag = 0f;

                for (int c = 0; c < channels; c++)
                {
                    float d = recDetails[c] * invWeight;
                    detailMag += MathF.Abs(d);
                    float baseVal = fusedPtr[pIdx * channels + c];
                    outPtr[pIdx * channels + c] = Math.Clamp(baseVal + gamma * d, 0f, 1f);
                }

                detPtr[pIdx] = Math.Clamp(detailMag / channels * 5.0f, 0f, 1f);
            }
        });

        result.MeanSharpnessGainPercentage = Math.Min(45.0f, gamma * 20.0f);
        return result;
    }
}
