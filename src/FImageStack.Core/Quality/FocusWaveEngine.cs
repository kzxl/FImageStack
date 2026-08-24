using System.Text;
using FImageStack.Core.Models;

namespace FImageStack.Core.Quality;

public interface IFocusWaveEngine
{
    FocusWaveAnalysisResult AnalyzeFocusWave(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult? depthResult = null);
}

public sealed class FocusWaveEngine : IFocusWaveEngine
{
    public unsafe FocusWaveAnalysisResult AnalyzeFocusWave(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult? depthResult = null)
    {
        if (frames == null || frames.Count < 2)
            throw new ArgumentException("Focus Wave requires at least 2 frames.", nameof(frames));

        int frameCount = frames.Count;
        int w = frames[0].Width;
        int h = frames[0].Height;
        int totalPixels = w * h;

        var result = new FocusWaveAnalysisResult
        {
            TotalFrames = frameCount
        };

        // 1. Calculate per-frame Depth & Sharpness Energy
        float prevZ = 0f;
        var stepDeltas = new List<float>();

        for (int k = 0; k < frameCount; k++)
        {
            var f = frames[k];
            float sumEnergy = 0f;
            float weightedDepthSum = 0f;

            if (f.FocusMap != null)
            {
                float* p = f.FocusMap.DataPointer;
                for (int y = 0; y < h; y++)
                {
                    int rowOffset = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = rowOffset + x;
                        float val = p[idx];
                        sumEnergy += val;

                        float pixelZ = (depthResult != null)
                            ? depthResult.DepthMap.At(x, y)
                            : ((float)y / Math.Max(1, h - 1) * (frameCount - 1));

                        weightedDepthSum += val * pixelZ;
                    }
                }
            }

            float energy = sumEnergy / totalPixels;
            float continuousZ = (sumEnergy > 0) ? (weightedDepthSum / sumEnergy) : k * 1.0f;
            float deltaZ = (k > 0) ? (continuousZ - prevZ) : 1.0f;
            if (k > 0) stepDeltas.Add(MathF.Abs(deltaZ));

            prevZ = continuousZ;

            result.WavePoints.Add(new FocusWavePoint
            {
                FrameIndex = k,
                ContinuousDepth = continuousZ,
                SharpnessEnergy = energy,
                StepDeltaZ = deltaZ,
                IsStepUniform = true,
                IsGapWarning = false
            });
        }

        // 2. Compute Step Uniformity Statistics
        float meanStep = stepDeltas.Count > 0 ? stepDeltas.Average() : 1.0f;
        float variance = stepDeltas.Count > 0 ? stepDeltas.Sum(d => (d - meanStep) * (d - meanStep)) / stepDeltas.Count : 0f;
        float stdDev = MathF.Sqrt(variance);

        float uniformity = Math.Clamp(100.0f * (1.0f - (stdDev / (meanStep + 1e-4f))), 0f, 100f);
        result.MeanStepDeltaZ = meanStep;
        result.StepUniformityScore = uniformity;

        int gapCount = 0;
        for (int k = 1; k < frameCount; k++)
        {
            var pt = result.WavePoints[k];
            if (pt.StepDeltaZ > 1.8f * meanStep)
            {
                pt.IsGapWarning = true;
                pt.IsStepUniform = false;
                gapCount++;
            }
        }

        result.GapCount = gapCount;
        result.DepthCoveragePercentage = Math.Clamp(100.0f - (gapCount * 15.0f), 20.0f, 100.0f);

        // 3. Generate 2D ASCII Spatio-Temporal Focus Wave Graph
        result.AsciiWaveGraph = GenerateAsciiWaveGraph(result.WavePoints, frameCount);

        result.EvaluationSummary = gapCount == 0
            ? $"Smooth continuous focus wave. Step uniformity: {uniformity:F1}% (Ideal). No gaps detected."
            : $"Focus wave contains {gapCount} step jump gaps. Step uniformity: {uniformity:F1}%. Consider retaking intermediate frames.";

        return result;
    }

    private static string GenerateAsciiWaveGraph(List<FocusWavePoint> points, int frameCount)
    {
        var sb = new StringBuilder();
        int graphWidth = 40;
        int rows = Math.Min(6, frameCount);

        sb.AppendLine("Frame");
        for (int r = rows - 1; r >= 0; r--)
        {
            int frameIdx = (int)MathF.Round((float)r / (rows - 1) * (frameCount - 1));
            int frameNum = frameIdx + 1;
            float normZ = (frameCount > 1) ? (float)frameIdx / (frameCount - 1) : 0f;
            int wavePos = Math.Clamp((int)MathF.Round(normZ * (graphWidth - 8)), 0, graphWidth - 8);

            string leftAxis = $"{frameNum,3} ─";
            string waveLine = new string('─', wavePos) + "╱█████" + new string(' ', Math.Max(0, graphWidth - wavePos - 6));
            sb.AppendLine($"{leftAxis}{waveLine}");
        }

        sb.AppendLine($"      Near {'─',28}► Far");
        return sb.ToString();
    }
}
