using FImageStack.Core.Models;

namespace FImageStack.Core.Quality;

public interface IFocusGapDetector
{
    FocusGapAnalysisReport DetectInterFrameGaps(
        IReadOnlyList<StackFrame> frames,
        float maxAllowedDofStep = 2.5f);
}

public sealed class FocusGapDetector : IFocusGapDetector
{
    public unsafe FocusGapAnalysisReport DetectInterFrameGaps(
        IReadOnlyList<StackFrame> frames,
        float maxAllowedDofStep = 2.5f)
    {
        if (frames == null || frames.Count <= 1)
            return new FocusGapAnalysisReport { TotalFramesAnalyzed = frames?.Count ?? 0, Summary = "Insufficient frames to analyze gaps." };

        int count = frames.Count;
        var report = new FocusGapAnalysisReport
        {
            TotalFramesAnalyzed = count
        };

        float sumStep = 0f;
        int pairCount = 0;

        for (int k = 0; k < count - 1; k++)
        {
            var fA = frames[k];
            var fB = frames[k + 1];

            float stepDistance = CalculateOpticalStepDistance(fA, fB);
            sumStep += stepDistance;
            pairCount++;

            if (stepDistance > maxAllowedDofStep)
            {
                int missingPlanes = Math.Max(1, (int)MathF.Round(stepDistance - 1.0f));
                var gap = new InterFrameGapDetail
                {
                    FrameIndexA = k,
                    FrameIndexB = k + 1,
                    EstimatedDepthA = k,
                    EstimatedDepthB = k + stepDistance,
                    OpticalStepDistance = stepDistance,
                    MissingPlanesEstimate = missingPlanes,
                    WarningMessage = $"⚠ Large focus gap detected between Frame {k + 1} and Frame {k + 2}. Step jump was {stepDistance:F1}x DOF (missing ~{missingPlanes} focus planes). Result may contain an out-of-focus transition."
                };
                report.LargeGaps.Add(gap);
            }
        }

        report.AverageStepDistance = pairCount > 0 ? sumStep / pairCount : 1.0f;

        if (report.LargeGaps.Count == 0)
        {
            report.Summary = $"✅ Uniform focus progression (Avg step: {report.AverageStepDistance:F2}x DOF). No large focus gaps detected.";
        }
        else
        {
            report.Summary = $"⚠ Detected {report.LargeGaps.Count} large focus gap(s). First gap between Frame {report.LargeGaps[0].FrameIndexA + 1} and Frame {report.LargeGaps[0].FrameIndexB + 1}.";
        }

        return report;
    }

    private static unsafe float CalculateOpticalStepDistance(StackFrame fA, StackFrame fB)
    {
        // 1. If explicit Z / FocusBreathingScale differences exist
        if (MathF.Abs(fA.FocusBreathingScale - fB.FocusBreathingScale) > 0.05f)
        {
            float scaleDeltaRatio = MathF.Abs(fA.FocusBreathingScale - fB.FocusBreathingScale) / 0.01f;
            return Math.Clamp(scaleDeltaRatio, 1.0f, 10.0f);
        }

        // 2. Measure Focus Map Overlap (Intersection over Union)
        if (fA.FocusMap != null && fB.FocusMap != null)
        {
            int w = fA.FocusMap.Width;
            int h = fA.FocusMap.Height;
            float* pA = fA.FocusMap.DataPointer;
            float* pB = fB.FocusMap.DataPointer;
            int total = w * h;

            float intersection = 0f;
            float unionSum = 0f;
            float sumA = 0f, sumB = 0f;

            for (int i = 0; i < total; i += 4)
            {
                float a = pA[i];
                float b = pB[i];
                sumA += a;
                sumB += b;
                intersection += MathF.Min(a, b);
                unionSum += MathF.Max(a, b);
            }

            if (sumA > 0.05f && sumB > 0.05f && unionSum > 0.01f)
            {
                float overlap = intersection / unionSum;
                // High overlap (0.6) -> step ~1.0 DOF; Zero overlap (0.0) -> step >= 5.0 DOF
                float stepDist = 1.0f + (1.0f - overlap) * 4.5f;
                return stepDist;
            }
        }

        return 1.0f;
    }
}
