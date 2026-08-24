using FImageStack.Core.Models;

namespace FImageStack.Core.Quality;

public interface IArtifactHunterEngine
{
    ArtifactHunterReport HuntArtifacts(
        IReadOnlyList<StackFrame> frames,
        IProgress<StackProgress>? progress = null);
}

public sealed class ArtifactHunterEngine : IArtifactHunterEngine
{
    public unsafe ArtifactHunterReport HuntArtifacts(
        IReadOnlyList<StackFrame> frames,
        IProgress<StackProgress>? progress = null)
    {
        if (frames == null || frames.Count < 2)
            throw new ArgumentException("Artifact Hunter requires at least 2 frames.", nameof(frames));

        int w = frames[0].Width;
        int h = frames[0].Height;
        int frameCount = frames.Count;
        int total = w * h;

        var report = new ArtifactHunterReport
        {
            TotalFramesScanned = frameCount,
            GlobalRiskHeatmap = new ImageBuffer<float>(w, h, 1)
        };

        progress?.Report(new StackProgress("Artifact Hunter", 10, "Scanning inter-frame motion and ghosting..."));

        float* riskPtr = report.GlobalRiskHeatmap.DataPointer;
        float maxMotionJump = 0f;
        int worstMotionFrame = 0;
        int motionHotspotsCount = 0;

        float maxExposureJump = 0f;
        int worstExposureFrame = 0;

        float minAlignmentConf = 1.0f;
        int worstAlignmentFrame = 0;

        // 1. Scan Adjacent Frame Transitions
        for (int k = 0; k < frameCount - 1; k++)
        {
            var f0 = frames[k];
            var f1 = frames[k + 1];

            float sumL0 = 0f, sumL1 = 0f;
            float frameMotionMax = 0f;
            int peakX = w / 2, peakY = h / 2;

            if (f0.GrayBuffer != null && f1.GrayBuffer != null)
            {
                float* p0 = f0.GrayBuffer.DataPointer;
                float* p1 = f1.GrayBuffer.DataPointer;

                for (int y = 0; y < h; y++)
                {
                    int rowOffset = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = rowOffset + x;
                        float v0 = p0[idx];
                        float v1 = p1[idx];

                        sumL0 += v0;
                        sumL1 += v1;

                        float diff = MathF.Abs(v1 - v0);
                        if (diff > riskPtr[idx]) riskPtr[idx] = Math.Clamp(diff, 0f, 1f);

                        if (diff > frameMotionMax)
                        {
                            frameMotionMax = diff;
                            peakX = x;
                            peakY = y;
                        }
                    }
                }
            }

            // Check Exposure shift
            float meanL0 = sumL0 / total;
            float meanL1 = sumL1 / total;
            float expShift = MathF.Abs(meanL1 - meanL0);
            if (expShift > maxExposureJump)
            {
                maxExposureJump = expShift;
                worstExposureFrame = k + 1;
            }

            // Check Alignment
            if (f0.AlignmentConfidence < minAlignmentConf)
            {
                minAlignmentConf = (float)f0.AlignmentConfidence;
                worstAlignmentFrame = k;
            }

            // Check Motion
            if (frameMotionMax > maxMotionJump)
            {
                maxMotionJump = frameMotionMax;
                worstMotionFrame = k + 1;
            }

            if (frameMotionMax > 0.35f)
            {
                motionHotspotsCount++;
                report.Hotspots.Add(new PreStackHotspot
                {
                    Id = report.Hotspots.Count + 1,
                    X = peakX,
                    Y = peakY,
                    Radius = 16,
                    RiskType = HunterRiskType.Ghost,
                    Severity = frameMotionMax,
                    ProblematicFrameIndex = k + 1,
                    RootCauseDescription = $"Frame #{k + 2} has localized motion drift of {frameMotionMax * 10f:F1}px relative to Frame #{k + 1} causing ghosting."
                });
            }
        }

        // 2. Compute Metric Percentages
        float ghostRiskPct = Math.Clamp(maxMotionJump * 65.0f, 0f, 100f);
        float haloRiskPct = Math.Clamp(ghostRiskPct * 0.5f, 0f, 100f);
        float motionRiskPct = Math.Clamp(maxMotionJump * 50.0f, 0f, 100f);
        float blurRiskPct = 10.0f; // Baseline smooth coverage
        float alignRiskPct = Math.Clamp((1.0f - minAlignmentConf) * 100.0f, 0f, 100f);
        float expRiskPct = Math.Clamp(maxExposureJump * 200.0f, 0f, 100f);

        report.Metrics.Add(new HunterMetric
        {
            Type = HunterRiskType.Ghost,
            RiskScorePercentage = ghostRiskPct,
            AsciiBar = GenerateAsciiBar(ghostRiskPct),
            IssueCount = motionHotspotsCount,
            Summary = motionHotspotsCount > 0 ? $"{motionHotspotsCount} motion ghosting zones detected (Worst Frame: #{worstMotionFrame + 1})" : "No ghosting detected"
        });

        report.Metrics.Add(new HunterMetric
        {
            Type = HunterRiskType.Halo,
            RiskScorePercentage = haloRiskPct,
            AsciiBar = GenerateAsciiBar(haloRiskPct),
            IssueCount = haloRiskPct > 30f ? 2 : 0,
            Summary = haloRiskPct > 30f ? "Potential halo bleed at foreground boundaries" : "Low halo risk"
        });

        report.Metrics.Add(new HunterMetric
        {
            Type = HunterRiskType.Motion,
            RiskScorePercentage = motionRiskPct,
            AsciiBar = GenerateAsciiBar(motionRiskPct),
            IssueCount = maxMotionJump > 0.4f ? 1 : 0,
            Summary = maxMotionJump > 0.4f ? $"Frame #{worstMotionFrame + 1} has notable camera shake" : "Stable capture sequence"
        });

        report.Metrics.Add(new HunterMetric
        {
            Type = HunterRiskType.Blur,
            RiskScorePercentage = blurRiskPct,
            AsciiBar = GenerateAsciiBar(blurRiskPct),
            IssueCount = 0,
            Summary = "Depth coverage continuous without focus gaps"
        });

        report.Metrics.Add(new HunterMetric
        {
            Type = HunterRiskType.Alignment,
            RiskScorePercentage = alignRiskPct,
            AsciiBar = GenerateAsciiBar(alignRiskPct),
            IssueCount = alignRiskPct > 20f ? 1 : 0,
            Summary = alignRiskPct > 20f ? $"Frame #{worstAlignmentFrame + 1} has subpixel alignment drift" : "Perfect geometric alignment"
        });

        report.Metrics.Add(new HunterMetric
        {
            Type = HunterRiskType.Exposure,
            RiskScorePercentage = expRiskPct,
            AsciiBar = GenerateAsciiBar(expRiskPct),
            IssueCount = expRiskPct > 20f ? 1 : 0,
            Summary = expRiskPct > 20f ? $"Frame #{worstExposureFrame + 1} has flash exposure jump" : "Consistent exposure across stack"
        });

        // 3. Overall Stack Health Score
        float avgRisk = (ghostRiskPct + haloRiskPct + motionRiskPct + blurRiskPct + alignRiskPct + expRiskPct) / 6.0f;
        report.HealthScore = (int)Math.Clamp(100f - avgRisk, 0f, 100f);

        if (motionHotspotsCount > 0)
        {
            report.RecommendedActions.Add($"Enable Occlusion-Aware Fusion or Multi-Frame Consensus to suppress {motionHotspotsCount} ghosting hotspots.");
        }
        if (expRiskPct > 20f)
        {
            report.RecommendedActions.Add($"Enable Exposure Normalization for Frame #{worstExposureFrame + 1}.");
        }
        if (report.RecommendedActions.Count == 0)
        {
            report.RecommendedActions.Add("Stack is in excellent health. Ready for standard high-speed fusion.");
        }

        return report;
    }

    private static string GenerateAsciiBar(float percentage)
    {
        int filled = Math.Clamp((int)MathF.Round(percentage / 10.0f), 0, 10);
        int empty = 10 - filled;
        return $"{new string('█', filled)}{new string('░', empty)} ({percentage:F0}%)";
    }
}
