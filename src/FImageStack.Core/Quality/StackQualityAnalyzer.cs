using FImageStack.Core;
using FImageStack.Core.Artifact;
using FImageStack.Core.Models;
using FImageStack.Core.Motion;

namespace FImageStack.Core.Quality;

public sealed class FocusGap
{
    public int FromFrameIndex { get; set; }
    public int ToFrameIndex { get; set; }
    public float DepthRangeStart { get; set; }
    public float DepthRangeEnd { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class FrameQualityScore
{
    public int FrameIndex { get; set; }
    public string FileName { get; set; } = string.Empty;
    public double MeanSharpness { get; set; }
    public double PeakSharpness { get; set; }
    public double RelativeExposure { get; set; }
    public bool IsUsable { get; set; } = true;
    public string Note { get; set; } = string.Empty;
}

public sealed class ArtifactRegionItem
{
    public int Id { get; set; }
    public ArtifactType Type { get; set; }
    public string TypeName => Type switch
    {
        ArtifactType.Halo => "HALO",
        ArtifactType.Ghost => "GHOST",
        ArtifactType.Seam => "SEAM",
        ArtifactType.FocusGap => "FOCUS GAP",
        _ => "ARTIFACT"
    };
    public int CenterX { get; set; }
    public int CenterY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float Severity { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class StackQualityReport
{
    public double OverallScore { get; set; } // 0 - 100%
    public double AlignmentScore { get; set; } = 98.0; // 0 - 100%
    public double FocusCoverageScore { get; set; } = 95.0; // 0 - 100%
    public double GhostingPercent { get; set; } = 2.0; // 0 - 100%
    public double HaloPercent { get; set; } = 3.0; // 0 - 100%
    public double NoisePercent { get; set; } = 2.5; // 0 - 100%
    public double EdgeQualityScore { get; set; } = 96.0; // 0 - 100%

    public double FocusCoveragePercentage => FocusCoverageScore;
    public string FocusCoverageRating { get; set; } = "Good";
    public List<FocusGap> DetectedGaps { get; } = new();
    public List<FrameQualityScore> FrameScores { get; } = new();
    public List<ArtifactRegionItem> TopArtifacts { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Recommendations { get; } = new();
}

public interface IStackQualityAnalyzer
{
    StackQualityReport AnalyzeQuality(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        ArtifactMap? artifactMap = null,
        MotionDetectionResult? motionResult = null);
}

public sealed class StandardStackQualityAnalyzer : IStackQualityAnalyzer
{
    public unsafe StackQualityReport AnalyzeQuality(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        ArtifactMap? artifactMap = null,
        MotionDetectionResult? motionResult = null)
    {
        var report = new StackQualityReport();
        int frameCount = frames.Count;
        int width = depthResult.Width;
        int height = depthResult.Height;
        int totalPixels = width * height;

        if (frameCount < 2)
        {
            report.OverallScore = 0;
            report.Warnings.Add("Insufficient frames to evaluate focus stacking quality.");
            return report;
        }

        // 1. Alignment Score calculation
        double sumAlignmentConf = 0;
        for (int f = 0; f < frameCount; f++)
        {
            sumAlignmentConf += frames[f].AlignmentConfidence;
        }
        report.AlignmentScore = Math.Clamp((sumAlignmentConf / frameCount) * 100.0, 50.0, 99.8);

        // 2. Per-frame sharpness and exposure
        double totalStackSharpness = 0;
        for (int f = 0; f < frameCount; f++)
        {
            var frame = frames[f];
            float* focusPtr = frame.FocusMap != null ? frame.FocusMap.DataPointer : null;
            float* grayPtr = frame.GrayBuffer != null ? frame.GrayBuffer.DataPointer : null;

            double sumSharp = 0;
            float maxSharp = 0;
            double sumExp = 0;

            if (focusPtr != null && grayPtr != null)
            {
                for (int i = 0; i < totalPixels; i++)
                {
                    float s = focusPtr[i];
                    sumSharp += s;
                    if (s > maxSharp) maxSharp = s;
                    sumExp += grayPtr[i];
                }
            }

            double meanSharp = sumSharp / totalPixels;
            double meanExp = sumExp / totalPixels;
            totalStackSharpness += meanSharp;

            report.FrameScores.Add(new FrameQualityScore
            {
                FrameIndex = f,
                FileName = frame.FileName,
                MeanSharpness = meanSharp,
                PeakSharpness = maxSharp,
                RelativeExposure = meanExp,
                IsUsable = meanSharp > 0.0001
            });
        }

        // 3. Focus Coverage & Gaps across Depth Map
        int* srcMap = depthResult.SourceFrameMap.DataPointer;
        float* confMap = depthResult.ConfidenceMap.DataPointer;

        int[] frameCoveragePixels = new int[frameCount];
        int highConfidencePixels = 0;

        for (int i = 0; i < totalPixels; i++)
        {
            int f = Math.Clamp(srcMap[i], 0, frameCount - 1);
            frameCoveragePixels[f]++;
            if (confMap[i] > 0.25f) highConfidencePixels++;
        }

        report.FocusCoverageScore = Math.Clamp((double)highConfidencePixels / totalPixels * 100.0, 10.0, 100.0);

        // Detect Focus Gaps
        int minExpectedPixelsPerFrame = totalPixels / (frameCount * 15);
        for (int f = 0; f < frameCount - 1; f++)
        {
            if (frameCoveragePixels[f] < minExpectedPixelsPerFrame && frameCoveragePixels[f + 1] < minExpectedPixelsPerFrame)
            {
                float zStart = (float)f / (frameCount - 1);
                float zEnd = (float)(f + 1) / (frameCount - 1);
                report.DetectedGaps.Add(new FocusGap
                {
                    FromFrameIndex = f,
                    ToFrameIndex = f + 1,
                    DepthRangeStart = zStart,
                    DepthRangeEnd = zEnd,
                    Description = $"Focus gap between frame #{f + 1} and #{f + 2} (Z: {zStart:F2} - {zEnd:F2})"
                });
            }
        }

        // 4. Ghosting and Halo evaluation
        int motionPixels = 0;
        if (motionResult?.MotionMap != null)
        {
            float* mPtr = motionResult.MotionMap.DataPointer;
            for (int i = 0; i < totalPixels; i++)
            {
                if (mPtr[i] > 0.15f) motionPixels++;
            }
        }
        report.GhostingPercent = Math.Clamp((double)motionPixels / totalPixels * 100.0, 0.0, 25.0);

        int artifactPixels = 0;
        if (artifactMap?.ArtifactMask != null)
        {
            byte* aPtr = artifactMap.ArtifactMask.DataPointer;
            for (int i = 0; i < totalPixels; i++)
            {
                if (aPtr[i] > 30) artifactPixels++;
            }

            int regionId = 1;
            foreach (var r in artifactMap.Regions.OrderByDescending(x => x.Severity).Take(8))
            {
                int cX = r.X + (r.Width / 2);
                int cY = r.Y + (r.Height / 2);
                report.TopArtifacts.Add(new ArtifactRegionItem
                {
                    Id = regionId++,
                    Type = r.Type,
                    CenterX = cX,
                    CenterY = cY,
                    Width = r.Width,
                    Height = r.Height,
                    Severity = r.Severity,
                    Description = $"{r.Type} at ({cX}, {cY}) - Severity: {r.Severity * 100:F0}%"
                });
            }
        }
        report.HaloPercent = Math.Clamp((double)artifactPixels / totalPixels * 100.0, 0.0, 20.0);

        // 5. Edge Quality & Noise scores
        report.NoisePercent = Math.Clamp(1.5 + (report.HaloPercent * 0.2), 0.5, 8.0);
        report.EdgeQualityScore = Math.Clamp(98.0 - (report.HaloPercent * 0.8) - (report.GhostingPercent * 0.5), 60.0, 99.5);

        // 6. Unified Overall Score
        double overall = (report.AlignmentScore * 0.25) +
                         (report.FocusCoverageScore * 0.35) +
                         (report.EdgeQualityScore * 0.25) +
                         ((100.0 - report.GhostingPercent * 2.0) * 0.08) +
                         ((100.0 - report.HaloPercent * 2.0) * 0.07);

        report.OverallScore = Math.Clamp(overall, 15.0, 99.8);

        if (report.OverallScore >= 90) report.FocusCoverageRating = "Excellent";
        else if (report.OverallScore >= 75) report.FocusCoverageRating = "Good";
        else if (report.OverallScore >= 55) report.FocusCoverageRating = "Fair";
        else report.FocusCoverageRating = "Poor (Gaps Detected)";

        return report;
    }
}
