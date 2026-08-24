using FImageStack.Core;
using FImageStack.Core.Models;

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

public sealed class StackQualityReport
{
    public double OverallScore { get; set; } // 0 - 100%
    public double FocusCoveragePercentage { get; set; }
    public string FocusCoverageRating { get; set; } = "Good";
    public List<FocusGap> DetectedGaps { get; } = new();
    public List<FrameQualityScore> FrameScores { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Recommendations { get; } = new();
}

public interface IStackQualityAnalyzer
{
    StackQualityReport AnalyzeQuality(IReadOnlyList<StackFrame> frames, DepthMapResult depthResult);
}

public sealed class StandardStackQualityAnalyzer : IStackQualityAnalyzer
{
    public unsafe StackQualityReport AnalyzeQuality(IReadOnlyList<StackFrame> frames, DepthMapResult depthResult)
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

        // 1. Evaluate per-frame sharpness and exposure
        double totalStackSharpness = 0;
        for (int f = 0; f < frameCount; f++)
        {
            var frame = frames[f];
            float* focusPtr = frame.FocusMap!.DataPointer;
            float* grayPtr = frame.GrayBuffer!.DataPointer;

            double sumSharp = 0;
            float maxSharp = 0;
            double sumExp = 0;

            for (int i = 0; i < totalPixels; i++)
            {
                float s = focusPtr[i];
                sumSharp += s;
                if (s > maxSharp) maxSharp = s;
                sumExp += grayPtr[i];
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

        // 2. Evaluate Focus Coverage & Gaps across Depth Map
        int* srcMap = depthResult.SourceFrameMap.DataPointer;
        float* confMap = depthResult.ConfidenceMap.DataPointer;

        int[] frameCoveragePixels = new int[frameCount];
        int highConfidencePixels = 0;

        for (int i = 0; i < totalPixels; i++)
        {
            int f = Math.Clamp(srcMap[i], 0, frameCount - 1);
            frameCoveragePixels[f]++;
            if (confMap[i] > 0.35f) highConfidencePixels++;
        }

        report.FocusCoveragePercentage = Math.Clamp((double)highConfidencePixels / totalPixels * 100.0, 0, 100);

        // Detect Focus Gaps (consecutive unused or severely under-represented focal planes in high contrast areas)
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
                    Description = $"Possible focus gap between frame {f + 1} and {f + 2} (Z range: {zStart:F2} - {zEnd:F2})"
                });
            }
        }

        if (report.DetectedGaps.Count > 0)
        {
            report.Warnings.Add($"Detected {report.DetectedGaps.Count} focus gap(s) where focal planes may have jumped too far.");
            report.Recommendations.Add("Consider capturing with smaller step increments around detected gap zones.");
        }

        // 3. Compute Overall Score
        double coverageFactor = report.FocusCoveragePercentage / 100.0;
        double gapPenalty = report.DetectedGaps.Count * 8.0;
        report.OverallScore = Math.Clamp(coverageFactor * 95.0 + 5.0 - gapPenalty, 10.0, 100.0);

        if (report.OverallScore >= 85) report.FocusCoverageRating = "Excellent";
        else if (report.OverallScore >= 70) report.FocusCoverageRating = "Good";
        else if (report.OverallScore >= 50) report.FocusCoverageRating = "Fair";
        else report.FocusCoverageRating = "Poor (Gaps Detected)";

        return report;
    }
}
