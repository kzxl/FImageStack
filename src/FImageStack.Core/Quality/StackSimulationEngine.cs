using System.Text;
using FImageStack.Core.Models;

namespace FImageStack.Core.Quality;

public interface IStackSimulationEngine
{
    StackSimulationResult SimulateDepthCoverage(
        IReadOnlyList<StackFrame> frames,
        int samplingBins = 200);
}

public sealed class StackSimulationEngine : IStackSimulationEngine
{
    public unsafe StackSimulationResult SimulateDepthCoverage(
        IReadOnlyList<StackFrame> frames,
        int samplingBins = 200)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Stack contains no frames.", nameof(frames));

        int count = frames.Count;
        samplingBins = Math.Clamp(samplingBins, 50, 1000);

        var result = new StackSimulationResult
        {
            TotalFrames = count
        };

        // 1. Calculate Mean Sharpness per Frame
        float[] frameSharpness = new float[count];
        for (int i = 0; i < count; i++)
        {
            var f = frames[i];
            float sharpSum = 0f;

            if (f.FocusMap != null)
            {
                float* p = f.FocusMap.DataPointer;
                int total = f.Width * f.Height;
                for (int pIdx = 0; pIdx < total; pIdx += 8) sharpSum += p[pIdx];
                frameSharpness[i] = sharpSum / (total / 8f);
            }
            else if (f.GrayBuffer != null)
            {
                int w = f.GrayBuffer.Width;
                int h = f.GrayBuffer.Height;
                float* ptr = f.GrayBuffer.DataPointer;
                int total = w * h;
                int samples = 0;

                for (int y = 1; y < h - 1; y += 2)
                {
                    int row = y * w;
                    for (int x = 1; x < w - 1; x += 2)
                    {
                        float c = ptr[row + x];
                        float lx = MathF.Abs(2f * c - ptr[row + x - 1] - ptr[row + x + 1]);
                        float ly = MathF.Abs(2f * c - ptr[(y - 1) * w + x] - ptr[(y + 1) * w + x]);
                        sharpSum += lx + ly;
                        samples++;
                    }
                }
                frameSharpness[i] = samples > 0 ? sharpSum / samples : 0.1f;
            }
            else
            {
                frameSharpness[i] = (float)f.SharpnessScore;
            }
        }

        // 2. Compute Continuous Coverage Curve over Z-Domain
        float[] curve = new float[samplingBins];
        float sigmaDof = 0.85f; // Standard depth of field thickness in frame step units
        float sigmaSq2 = 2f * sigmaDof * sigmaDof;
        float maxCoverage = 0f;

        for (int b = 0; b < samplingBins; b++)
        {
            float z = (float)b / (samplingBins - 1) * (count - 1);
            float maxPointCov = 0f;

            for (int k = 0; k < count; k++)
            {
                float dz = z - k;
                float cov = frameSharpness[k] * MathF.Exp(-(dz * dz) / sigmaSq2);
                if (cov > maxPointCov) maxPointCov = cov;
            }

            curve[b] = maxPointCov;
            if (maxPointCov > maxCoverage) maxCoverage = maxPointCov;
        }

        result.CoverageCurve = curve;

        // 3. Find Active Range and Gap Threshold
        float activeThreshold = maxCoverage * 0.15f;
        float gapThreshold = maxCoverage * 0.35f;

        int binStart = 0;
        for (int b = 0; b < samplingBins; b++)
        {
            if (curve[b] >= activeThreshold) { binStart = b; break; }
        }

        int binEnd = samplingBins - 1;
        for (int b = samplingBins - 1; b >= 0; b--)
        {
            if (curve[b] >= activeThreshold) { binEnd = b; break; }
        }

        if (binStart >= binEnd)
        {
            binStart = 0;
            binEnd = samplingBins - 1;
        }

        // 4. Detect Gaps in Active Focus Range
        int activeBinsCount = binEnd - binStart + 1;
        int coveredBins = 0;
        int inGapStartBin = -1;

        for (int b = binStart; b <= binEnd; b++)
        {
            if (curve[b] >= gapThreshold)
            {
                coveredBins++;
                if (inGapStartBin >= 0)
                {
                    // End of gap segment
                    ProcessGapSegment(inGapStartBin, b - 1, samplingBins, count, result);
                    inGapStartBin = -1;
                }
            }
            else
            {
                if (inGapStartBin < 0) inGapStartBin = b;
            }
        }

        if (inGapStartBin >= 0)
        {
            ProcessGapSegment(inGapStartBin, binEnd, samplingBins, count, result);
        }

        result.DepthCoveragePercentage = activeBinsCount > 0
            ? Math.Clamp((float)coveredBins / activeBinsCount * 100f, 0f, 100f)
            : 100f;

        // 5. Build ASCII Visualization Bar
        int barLength = 30;
        var barSb = new StringBuilder(barLength);
        for (int i = 0; i < barLength; i++)
        {
            int b = (int)((float)i / (barLength - 1) * (samplingBins - 1));
            if (b < binStart || b > binEnd)
            {
                barSb.Append('─');
            }
            else if (curve[b] < gapThreshold)
            {
                barSb.Append('░');
            }
            else
            {
                barSb.Append('█');
            }
        }

        result.CoverageBarAscii = $"Near [{barSb}] Far";

        // 6. Generate Human-Readable Recommendation
        if (result.DetectedGaps.Count == 0)
        {
            result.Recommendation = $"✅ Excellent continuous depth coverage ({result.DepthCoveragePercentage:F1}%). No focus gaps detected.";
        }
        else
        {
            var firstGap = result.DetectedGaps[0];
            int gapFrames = firstGap.GapWidthFrames;
            int neededFrames = Math.Max(1, gapFrames / 2);
            result.Recommendation = $"⚠ Focus gap detected between frame {firstGap.StartFrame + 1} and {firstGap.EndFrame + 1} (Missing ~{gapFrames} frames, Severity: {firstGap.Severity}). Recommend shooting {neededFrames} supplementary frame(s) in this zone.";
        }

        return result;
    }

    private static void ProcessGapSegment(
        int startBin,
        int endBin,
        int totalBins,
        int totalFrames,
        StackSimulationResult result)
    {
        float zStart = (float)startBin / (totalBins - 1) * (totalFrames - 1);
        float zEnd = (float)endBin / (totalBins - 1) * (totalFrames - 1);

        int kStart = (int)MathF.Floor(zStart);
        int kEnd = (int)MathF.Ceiling(zEnd);

        if (kEnd - kStart >= 2)
        {
            int width = kEnd - kStart;
            var severity = width switch
            {
                >= 4 => FocusGapSeverity.Critical,
                >= 3 => FocusGapSeverity.High,
                _ => FocusGapSeverity.Medium
            };

            result.DetectedGaps.Add(new FocusGapInfo
            {
                StartFrame = kStart,
                EndFrame = kEnd,
                MissingDepthRatio = (float)(endBin - startBin + 1) / totalBins,
                Severity = severity,
                Description = $"Focus gap between frame {kStart + 1} and {kEnd + 1} (Width: {width} frames)"
            });
        }
    }
}
