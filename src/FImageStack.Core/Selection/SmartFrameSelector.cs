using FImageStack.Core.Models;

namespace FImageStack.Core.Selection;

public sealed class FrameQualityDiagnostic
{
    public int FrameIndex { get; set; }
    public double SharpnessScore { get; set; }
    public double ExposureMean { get; set; }
    public double HighlightClipPercent { get; set; }
    public double ShadowClipPercent { get; set; }
    public bool IsDuplicate { get; set; }
    public int DuplicateOfIndex { get; set; } = -1;
    public bool IsMotionBlurred { get; set; }
    public bool IsExposureAnomaly { get; set; }
    public bool IsBadFrame => IsMotionBlurred || IsExposureAnomaly || SharpnessScore < 8.0;
    public string Reason { get; set; } = string.Empty;
    public string BadgeText { get; set; } = "✅ OK";
}

public interface ISmartFrameSelector
{
    List<FrameQualityDiagnostic> AnalyzeStack(IReadOnlyList<StackFrame> frames);
}

public sealed class SmartFrameSelector : ISmartFrameSelector
{
    public unsafe List<FrameQualityDiagnostic> AnalyzeStack(IReadOnlyList<StackFrame> frames)
    {
        var diagnostics = new List<FrameQualityDiagnostic>(frames.Count);
        if (frames == null || frames.Count == 0) return diagnostics;

        int count = frames.Count;
        int width = frames[0].Width;
        int height = frames[0].Height;

        // Step 1: Calculate individual frame metrics (Sharpness & Exposure)
        double[] sharpnessArray = new double[count];
        double[] exposureArray = new double[count];

        for (int i = 0; i < count; i++)
        {
            var f = frames[i];
            float* gray = f.GrayBuffer != null ? f.GrayBuffer.DataPointer : null;

            double sumLum = 0;
            double sumGrad = 0;
            int highClip = 0;
            int shadowClip = 0;

            if (gray != null)
            {
                for (int y = 1; y < height - 1; y += 2) // Step 2 sampling for fast analysis
                {
                    int row = y * width;
                    int nextRow = (y + 1) * width;

                    for (int x = 1; x < width - 1; x += 2)
                    {
                        float val = gray[row + x];
                        sumLum += val;

                        if (val >= 0.99f) highClip++;
                        if (val <= 0.01f) shadowClip++;

                        // Fast gradient magnitude
                        float gx = gray[row + x + 1] - gray[row + x - 1];
                        float gy = gray[nextRow + x] - gray[row - width + x];
                        sumGrad += MathF.Abs(gx) + MathF.Abs(gy);
                    }
                }
            }

            int sampledPixels = (width / 2) * (height / 2);
            double meanLum = sumLum / sampledPixels;
            double rawSharp = sumGrad / sampledPixels * 100.0;

            sharpnessArray[i] = rawSharp;
            exposureArray[i] = meanLum;

            diagnostics.Add(new FrameQualityDiagnostic
            {
                FrameIndex = i,
                ExposureMean = meanLum,
                HighlightClipPercent = (double)highClip / sampledPixels * 100.0,
                ShadowClipPercent = (double)shadowClip / sampledPixels * 100.0,
                SharpnessScore = rawSharp
            });
        }

        // Normalize sharpness scores to [0 .. 100%]
        double maxSharp = sharpnessArray.Max();
        if (maxSharp > 1e-5)
        {
            for (int i = 0; i < count; i++)
            {
                diagnostics[i].SharpnessScore = (sharpnessArray[i] / maxSharp) * 100.0;
            }
        }

        // Median exposure calculation
        var sortedExposure = exposureArray.OrderBy(x => x).ToArray();
        double medianExposure = sortedExposure[sortedExposure.Length / 2];

        // Step 2: Anomaly, Duplicate & Outlier Detection
        for (int i = 0; i < count; i++)
        {
            var diag = diagnostics[i];

            // 1. Severe Exposure Anomaly check (blown out white / pitch black / severe shift)
            if (diag.ExposureMean > 0.93 || diag.ExposureMean < 0.04 || Math.Abs(diag.ExposureMean - medianExposure) > 0.40)
            {
                diag.IsExposureAnomaly = true;
                diag.Reason = $"Exposure anomaly ({diag.ExposureMean * 100:F0}% vs median {medianExposure * 100:F0}%)";
                diag.BadgeText = "⚠️ BAD: Exp";
            }

            // 2. Motion Blur / Sudden Sharpness Drop check (Outlier vs adjacent frames)
            if (count >= 3 && i > 0 && i < count - 1)
            {
                double neighborAvg = (diagnostics[i - 1].SharpnessScore + diagnostics[i + 1].SharpnessScore) / 2.0;
                if (diag.SharpnessScore < neighborAvg * 0.45 && neighborAvg > 35.0)
                {
                    diag.IsMotionBlurred = true;
                    diag.Reason = $"Motion blur / Shake (Sharpness dropped to {diag.SharpnessScore:F0}% vs neighbors {neighborAvg:F0}%)";
                    diag.BadgeText = "⚠️ BAD: Blur";
                }
            }
            else if (diag.SharpnessScore < 8.0 && maxSharp > 50.0)
            {
                diag.IsMotionBlurred = true;
                diag.Reason = $"Severe focus failure ({diag.SharpnessScore:F0}%)";
                diag.BadgeText = "⚠️ BAD: Focus";
            }

            // 3. Duplicate Frame check (Near-identical pixel values & exposure to previous frame)
            if (i > 0 && !diag.IsBadFrame)
            {
                var prev = diagnostics[i - 1];
                double diffSharp = Math.Abs(diag.SharpnessScore - prev.SharpnessScore);
                double diffExp = Math.Abs(diag.ExposureMean - prev.ExposureMean);

                if (diffSharp < 0.15 && diffExp < 0.002)
                {
                    diag.IsDuplicate = true;
                    diag.DuplicateOfIndex = i - 1;
                    diag.Reason = $"Redundant duplicate of Frame #{i} (Sharpness Δ {diffSharp:F2}%)";
                    diag.BadgeText = "⚠️ DUP";
                }
            }
        }

        return diagnostics;
    }
}
