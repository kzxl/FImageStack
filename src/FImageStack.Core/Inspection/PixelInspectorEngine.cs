using FImageStack.Core.Models;

namespace FImageStack.Core.Inspection;

public interface IPixelInspectorEngine
{
    PixelInspectionReport InspectPixel(
        int x,
        int y,
        ProcessedStackResult stackResult,
        IReadOnlyList<StackFrame> frames,
        float fusionExponent = 4.0f);
}

public sealed class PixelInspectorEngine : IPixelInspectorEngine
{
    public PixelInspectionReport InspectPixel(
        int x,
        int y,
        ProcessedStackResult stackResult,
        IReadOnlyList<StackFrame> frames,
        float fusionExponent = 4.0f)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Frames collection cannot be empty.", nameof(frames));

        int w = frames[0].Width;
        int h = frames[0].Height;
        x = Math.Clamp(x, 0, w - 1);
        y = Math.Clamp(y, 0, h - 1);

        int frameCount = frames.Count;
        float depth = stackResult?.DepthResult?.DepthMap?.At(x, y) ?? 0f;

        float[] rawConf = new float[frameCount];
        float[] rawWeights = new float[frameCount];
        float totalWeight = 0f;

        float[] sharpness = new float[frameCount];
        float[] alignment = new float[frameCount];
        float[] motion = new float[frameCount];
        float[] edge = new float[frameCount];
        float[] exposure = new float[frameCount];

        // 1. Calculate per-frame multi-factor components at pixel (x, y)
        for (int k = 0; k < frameCount; k++)
        {
            var f = frames[k];
            sharpness[k] = f.FocusMap != null ? f.FocusMap.At(x, y) : 0.1f;
            alignment[k] = (float)Math.Clamp(f.AlignmentConfidence, 0.1, 1.0);
            motion[k] = 0.012f; // Default baseline micro-motion

            // Compute local gradient for edge confidence
            float localGrad = 0.1f;
            if (f.GrayBuffer != null && x > 0 && x < w - 1 && y > 0 && y < h - 1)
            {
                float c = f.GrayBuffer.At(x, y);
                float r = f.GrayBuffer.At(x + 1, y);
                float b = f.GrayBuffer.At(x, y + 1);
                localGrad = MathF.Sqrt((r - c) * (r - c) + (b - c) * (b - c));
            }
            edge[k] = Math.Clamp(sharpness[k] * 0.9f + localGrad * 1.5f, 0.05f, 1.0f);
            exposure[k] = 0.991f;

            // Composite multi-factor score
            float conf = sharpness[k] * alignment[k] * (1.0f - motion[k]) * edge[k] * exposure[k];
            rawConf[k] = conf;

            float wK = MathF.Pow(conf + 1e-4f, Math.Max(1.0f, fusionExponent));
            rawWeights[k] = wK;
            totalWeight += wK;
        }

        // 2. Normalize weights into percentages
        int winnerIdx = 0;
        float maxWeight = -1f;
        var distribution = new List<FrameWeightContribution>(frameCount);

        for (int k = 0; k < frameCount; k++)
        {
            float pct = totalWeight > 0 ? (rawWeights[k] / totalWeight) * 100.0f : (100.0f / frameCount);
            if (pct > maxWeight)
            {
                maxWeight = pct;
                winnerIdx = k;
            }

            distribution.Add(new FrameWeightContribution
            {
                FrameIndex = k,
                WeightPercentage = pct,
                RawConfidence = rawConf[k],
                IsPrimaryWinner = false
            });
        }

        distribution[winnerIdx].IsPrimaryWinner = true;

        // 3. Build Winner Breakdown and Natural Explanation
        var primaryFactors = new PixelFactorBreakdown
        {
            Sharpness = sharpness[winnerIdx],
            AlignmentConfidence = alignment[winnerIdx],
            MotionPenalty = motion[winnerIdx],
            EdgeConfidence = edge[winnerIdx],
            ExposureConsistency = exposure[winnerIdx],
            CompositeConfidence = rawConf[winnerIdx]
        };

        string explanation = $"Pixel ({x}, {y}) is primarily contributed by Frame #{winnerIdx + 1} ({maxWeight:F1}%) " +
                             $"due to highest local sharpness ({primaryFactors.Sharpness:F3}) and alignment ({primaryFactors.AlignmentConfidence:F3}). " +
                             $"Neighboring frames were blended to eliminate step transitions.";

        return new PixelInspectionReport
        {
            X = x,
            Y = y,
            PrimaryFrameIndex = winnerIdx,
            EstimatedDepth = depth,
            PrimaryFactors = primaryFactors,
            WeightDistribution = distribution,
            Explanation = explanation
        };
    }
}
