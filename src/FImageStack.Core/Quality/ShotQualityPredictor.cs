using FImageStack.Core.Models;

namespace FImageStack.Core.Quality;

public interface IShotQualityPredictor
{
    ShotQualityScorecard PredictQuality(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult? depthResult = null);
}

public sealed class ShotQualityPredictor : IShotQualityPredictor
{
    public unsafe ShotQualityScorecard PredictQuality(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult? depthResult = null)
    {
        if (frames == null || frames.Count < 2)
            throw new ArgumentException("Shot Quality Prediction requires at least 2 frames.", nameof(frames));

        int frameCount = frames.Count;
        int w = frames[0].Width;
        int h = frames[0].Height;
        int totalPixels = w * h;

        // 1. Calculate Expected Sharpness
        float sumSharpness = 0f;
        int sampledPixels = 0;
        int step = Math.Max(1, totalPixels / 500);

        for (int i = 0; i < totalPixels; i += step)
        {
            float maxFocus = 0f;
            for (int k = 0; k < frameCount; k++)
            {
                if (frames[k].FocusMap != null)
                {
                    float val = frames[k].FocusMap!.DataPointer[i];
                    if (val > maxFocus) maxFocus = val;
                }
            }
            sumSharpness += maxFocus;
            sampledPixels++;
        }

        float avgMaxSharpness = sampledPixels > 0 ? (sumSharpness / sampledPixels) : 0.8f;
        float expectedSharpnessPct = Math.Clamp(avgMaxSharpness * 110.0f, 40.0f, 99.0f);

        // 2. Calculate Expected Alignment
        float minAlignment = 1.0f;
        for (int k = 0; k < frameCount; k++)
        {
            if (frames[k].AlignmentConfidence < minAlignment)
            {
                minAlignment = (float)frames[k].AlignmentConfidence;
            }
        }
        float expectedAlignmentPct = Math.Clamp(minAlignment * 100.0f, 20.0f, 99.0f);

        // 3. Calculate Expected Coverage & Inter-frame Step Jumps
        int gapCount = 0;
        float worstGapStart = 0f, worstGapEnd = 0f;

        for (int k = 0; k < frameCount - 1; k++)
        {
            var f0 = frames[k];
            var f1 = frames[k + 1];

            if (f0.FocusMap != null && f1.FocusMap != null)
            {
                float intersection = 0f;
                float unionSum = 0f;
                float* p0 = f0.FocusMap.DataPointer;
                float* p1 = f1.FocusMap.DataPointer;

                for (int i = 0; i < totalPixels; i += step)
                {
                    float a = p0[i];
                    float b = p1[i];
                    intersection += MathF.Min(a, b);
                    unionSum += MathF.Max(a, b);
                }

                float overlap = unionSum > 0.001f ? (intersection / unionSum) : 0.5f;
                if (overlap < 0.35f) // Focus Gap threshold
                {
                    gapCount++;
                    worstGapStart = k * 0.20f;
                    worstGapEnd = (k + 1) * 0.20f;
                }
            }
        }

        float expectedCoveragePct = Math.Clamp(100.0f - (gapCount * 12.0f), 30.0f, 99.0f);

        // 4. Calculate Expected Artifact Risk
        float expectedArtifactRiskPct = Math.Clamp((100.0f - expectedAlignmentPct) * 0.4f + (gapCount * 4.0f), 2.0f, 50.0f);

        // 5. Final Weighted Quality Score
        float finalScore = 0.35f * expectedCoveragePct +
                           0.30f * expectedSharpnessPct +
                           0.20f * expectedAlignmentPct +
                           0.15f * (100.0f - expectedArtifactRiskPct);

        finalScore = Math.Clamp(finalScore, 0f, 100f);

        // Determine Quality Grade
        QualityGrade grade;
        string gradeTitle;

        if (finalScore >= 90.0f)
        {
            grade = QualityGrade.GradeAPlus;
            gradeTitle = "Studio Master (Grade A+)";
        }
        else if (finalScore >= 80.0f)
        {
            grade = QualityGrade.GradeA;
            gradeTitle = "High Quality (Grade A)";
        }
        else if (finalScore >= 70.0f)
        {
            grade = QualityGrade.GradeB;
            gradeTitle = "Acceptable (Grade B)";
        }
        else
        {
            grade = QualityGrade.GradeC;
            gradeTitle = "Retake Needed (Grade C)";
        }

        var scorecard = new ShotQualityScorecard
        {
            ExpectedCoveragePercentage = expectedCoveragePct,
            ExpectedSharpnessPercentage = expectedSharpnessPct,
            ExpectedAlignmentPercentage = expectedAlignmentPct,
            ExpectedArtifactRiskPercentage = expectedArtifactRiskPct,
            FinalExpectedQualityScore = finalScore,
            Grade = grade,
            GradeTitle = gradeTitle
        };

        // 6. Generate Actionable Additional Frames Recommendations
        if (expectedCoveragePct < 95.0f || gapCount > 0)
        {
            int recFrames = Math.Max(2, (int)MathF.Ceiling((100.0f - expectedCoveragePct) / 3.0f));
            scorecard.Recommendations.Add(new AdditionalFrameRecommendation
            {
                RecommendedFrameCount = recFrames,
                StartDepthMm = worstGapStart,
                EndDepthMm = worstGapEnd,
                ProjectedQualityGain = Math.Min(18.0f, recFrames * 3.5f),
                Reason = $"Focus step jump detected between frames. Recommend {recFrames} additional frames to bridge gap."
            });

            scorecard.SummaryMessage = $"⚠️ Expected quality: {finalScore:F0}% ({gradeTitle}). Recommend {recFrames} additional frames to reach 99% master quality.";
        }
        else
        {
            scorecard.SummaryMessage = $"✅ Expected quality: {finalScore:F0}% ({gradeTitle}). Stack is in optimal condition, ready to render.";
        }

        return scorecard;
    }
}
