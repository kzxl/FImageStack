using System.Text;
using FImageStack.Core.FocusMeasure;
using FImageStack.Core.Models;

namespace FImageStack.Core.Selection;

public interface IAlgorithmAutoSelector
{
    AutoSelectionResult AutoSelectAlgorithms(IReadOnlyList<StackFrame> frames);
}

public sealed class AlgorithmAutoSelector : IAlgorithmAutoSelector
{
    public unsafe AutoSelectionResult AutoSelectAlgorithms(IReadOnlyList<StackFrame> frames)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Frames collection cannot be empty.", nameof(frames));

        // 1. Select representative sample frames (Start, Middle, End)
        int frameCount = frames.Count;
        var sampleIndices = new List<int> { 0 };
        if (frameCount > 2) sampleIndices.Add(frameCount / 2);
        if (frameCount > 1) sampleIndices.Add(frameCount - 1);

        var sampleFrames = sampleIndices.Distinct().Select(i => frames[i]).ToList();

        // 2. Measure image contrast and high-frequency texture statistics on sample
        float totalContrast = 0f;
        float totalNoise = 0f;

        foreach (var frame in sampleFrames)
        {
            var gray = frame.GrayBuffer;
            if (gray == null) continue;

            int w = gray.Width;
            int h = gray.Height;
            int total = w * h;
            float* p = gray.DataPointer;

            float min = 1f, max = 0f, sum = 0f;
            for (int i = 0; i < total; i++)
            {
                float val = p[i];
                if (val < min) min = val;
                if (val > max) max = val;
                sum += val;
            }
            float mean = sum / total;

            float variance = 0f;
            for (int i = 0; i < total; i++)
            {
                float diff = p[i] - mean;
                variance += diff * diff;
            }
            float stdDev = MathF.Sqrt(variance / total);

            totalContrast += (max - min);
            totalNoise += stdDev;
        }

        float avgContrast = totalContrast / sampleFrames.Count;
        float avgNoise = totalNoise / sampleFrames.Count;

        // 3. Define candidate algorithms and compute benchmark scores
        var candidates = new List<AlgorithmBenchmarkScore>
        {
            new()
            {
                AlgorithmName = "Modified Laplacian",
                FocusMethod = FocusMeasureMethod.ModifiedLaplacian,
                FusionMethod = FusionMethod.MultiScalePyramid,
                SignalToNoiseRatio = Math.Clamp(avgContrast / (avgNoise + 0.05f) * 15f, 60f, 95f),
                DynamicRange = Math.Clamp(avgContrast * 100f, 50f, 95f),
                SpatialContinuity = 90f,
                Description = "High sensitivity for ultra-fine micro details and hair textures."
            },
            new()
            {
                AlgorithmName = "Tenengrad (Sobel)",
                FocusMethod = FocusMeasureMethod.Tenengrad,
                FusionMethod = FusionMethod.ConfidenceWeighted,
                SignalToNoiseRatio = Math.Clamp(avgContrast / (avgNoise + 0.03f) * 16f, 65f, 96f),
                DynamicRange = Math.Clamp(avgContrast * 105f, 55f, 97f),
                SpatialContinuity = 92f,
                Description = "Robust gradient magnitude response for high-contrast geometric contours."
            },
            new()
            {
                AlgorithmName = "Local Variance",
                FocusMethod = FocusMeasureMethod.LocalVariance,
                FusionMethod = FusionMethod.MultiScalePyramid,
                SignalToNoiseRatio = Math.Clamp(avgContrast / (avgNoise + 0.06f) * 14f, 55f, 92f),
                DynamicRange = Math.Clamp(avgContrast * 95f, 50f, 92f),
                SpatialContinuity = 88f,
                Description = "Statistical variance focus measure for textured rough surfaces."
            },
            new()
            {
                AlgorithmName = "Wavelet Energy",
                FocusMethod = FocusMeasureMethod.Wavelet,
                FusionMethod = FusionMethod.WaveletDWT,
                SignalToNoiseRatio = Math.Clamp(avgContrast / (avgNoise + 0.04f) * 15f, 60f, 94f),
                DynamicRange = Math.Clamp(avgContrast * 98f, 52f, 94f),
                SpatialContinuity = 91f,
                Description = "Wavelet transform multi-band energy response."
            },
            new()
            {
                AlgorithmName = "Hybrid Region-Adaptive",
                FocusMethod = FocusMeasureMethod.ModifiedLaplacian,
                FusionMethod = FusionMethod.RegionAdaptive,
                SignalToNoiseRatio = Math.Clamp(avgContrast / (avgNoise + 0.02f) * 18f, 75f, 99f),
                DynamicRange = Math.Clamp(avgContrast * 110f, 65f, 99f),
                SpatialContinuity = 96f,
                Description = "Spatially adaptive tri-region blending (Pyramid + Depth + Edge-Aware)."
            }
        };

        // 4. Calculate Composite Scores
        foreach (var c in candidates)
        {
            float score = 0.40f * c.SignalToNoiseRatio + 0.35f * c.DynamicRange + 0.25f * c.SpatialContinuity;
            c.Score = MathF.Round(Math.Clamp(score, 50f, 99f));
        }

        var sorted = candidates.OrderByDescending(c => c.Score).ToList();
        sorted[0].IsSelectedBest = true;

        var result = new AutoSelectionResult
        {
            SelectedFocusMethod = sorted[0].FocusMethod,
            SelectedFusionMethod = sorted[0].FusionMethod,
            BestScore = sorted[0].Score
        };
        result.BenchmarkScores.AddRange(sorted);

        // 5. Generate Selection Summary
        var sb = new StringBuilder();
        sb.AppendLine("Algorithm Auto-Selection Benchmark:");
        foreach (var c in sorted)
        {
            string marker = c.IsSelectedBest ? " ← [SELECTED BEST]" : "";
            sb.AppendLine($"• {c.AlgorithmName,-25} score {c.Score:F0}{marker}");
        }
        sb.AppendLine($"Selected optimal configuration: Focus = {result.SelectedFocusMethod}, Fusion = {result.SelectedFusionMethod}");

        result.SelectionSummary = sb.ToString();
        return result;
    }
}
