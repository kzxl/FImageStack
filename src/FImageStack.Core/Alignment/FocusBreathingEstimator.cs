using FImageStack.Core.Models;

namespace FImageStack.Core.Alignment;

public sealed class FocusBreathingResult
{
    public float[] RawScales { get; init; } = Array.Empty<float>();
    public float[] FittedScales { get; init; } = Array.Empty<float>();
    public float BreathingRatePerFrame { get; init; }
    public float TotalMagnificationShiftPercentage { get; init; }
    public double R2 { get; init; }
}

public interface IFocusBreathingEstimator
{
    FocusBreathingResult EstimateScaleCurve(IList<StackFrame> frames, int refIndex = -1);
    float MeasureRadialScale(StackFrame refFrame, StackFrame targetFrame);
}

public sealed class FocusBreathingEstimator : IFocusBreathingEstimator
{
    public unsafe float MeasureRadialScale(StackFrame refFrame, StackFrame targetFrame)
    {
        if (refFrame.GrayBuffer == null || targetFrame.GrayBuffer == null)
            return 1.0f;

        int w = refFrame.Width;
        int h = refFrame.Height;
        float cx = w * 0.5f;
        float cy = h * 0.5f;

        float* refGray = refFrame.GrayBuffer.DataPointer;
        float* tgtGray = targetFrame.GrayBuffer.DataPointer;

        int patchSize = 16;
        int searchRadius = 8;

        // Sample points across a grid
        int gridCols = 7;
        int gridRows = 7;
        float sumRdotD = 0f;
        float sumRsq = 0f;
        int validPoints = 0;

        for (int gy = 1; gy < gridRows; gy++)
        {
            int py = (int)(gy * (h / (float)gridRows));
            float ry = py - cy;

            for (int gx = 1; gx < gridCols; gx++)
            {
                int px = (int)(gx * (w / (float)gridCols));
                float rx = px - cx;

                float rDistSq = rx * rx + ry * ry;
                if (rDistSq < (w * 0.12f) * (w * 0.12f))
                    continue; // Skip points too close to center

                // Check patch contrast / texture in reference frame
                float patchMin = float.MaxValue;
                float patchMax = float.MinValue;
                for (int sy = -patchSize / 2; sy < patchSize / 2; sy += 2)
                {
                    int y0 = py + sy;
                    if (y0 < 0 || y0 >= h) continue;
                    for (int sx = -patchSize / 2; sx < patchSize / 2; sx += 2)
                    {
                        int x0 = px + sx;
                        if (x0 < 0 || x0 >= w) continue;
                        float val = refGray[y0 * w + x0];
                        if (val < patchMin) patchMin = val;
                        if (val > patchMax) patchMax = val;
                    }
                }

                // If patch is flat / lacks contrast, skip
                if ((patchMax - patchMin) < 0.05f)
                    continue;

                // Search best displacement (dx, dy) in target frame
                float bestScore = float.MaxValue;
                int bestDx = 0, bestDy = 0;

                for (int dy = -searchRadius; dy <= searchRadius; dy++)
                {
                    for (int dx = -searchRadius; dx <= searchRadius; dx++)
                    {
                        float sumDiff = 0f;
                        int samples = 0;

                        for (int sy = -patchSize / 2; sy < patchSize / 2; sy += 2)
                        {
                            int y0 = py + sy;
                            int y1 = py + sy + dy;
                            if (y0 < 0 || y0 >= h || y1 < 0 || y1 >= h) continue;

                            for (int sx = -patchSize / 2; sx < patchSize / 2; sx += 2)
                            {
                                int x0 = px + sx;
                                int x1 = px + sx + dx;
                                if (x0 < 0 || x0 >= w || x1 < 0 || x1 >= w) continue;

                                sumDiff += MathF.Abs(refGray[y0 * w + x0] - tgtGray[y1 * w + x1]);
                                samples++;
                            }
                        }

                        if (samples > 0 && sumDiff < bestScore)
                        {
                            bestScore = sumDiff;
                            bestDx = dx;
                            bestDy = dy;
                        }
                    }
                }

                // Dot product: r . d
                float rDotD = rx * bestDx + ry * bestDy;
                sumRdotD += rDotD;
                sumRsq += rDistSq;
                validPoints++;
            }
        }

        if (validPoints >= 2 && sumRsq > 1e-4f)
        {
            float deltaScale = sumRdotD / sumRsq;
            return 1.0f + Math.Clamp(deltaScale, -0.15f, 0.15f);
        }

        return 1.0f;
    }

    public FocusBreathingResult EstimateScaleCurve(IList<StackFrame> frames, int refIndex = -1)
    {
        int count = frames.Count;
        if (count == 0) return new FocusBreathingResult();

        if (refIndex < 0 || refIndex >= count)
            refIndex = count / 2;

        var refFrame = frames[refIndex];
        float[] rawScales = new float[count];
        float[] fittedScales = new float[count];

        rawScales[refIndex] = 1.0f;

        // Measure raw scales for all other frames
        for (int i = 0; i < count; i++)
        {
            if (i == refIndex) continue;
            rawScales[i] = MeasureRadialScale(refFrame, frames[i]);
        }

        // Fit linear magnification curve passing through (refIndex, 1.0): y = beta * x
        float sumXY = 0f;
        float sumX2 = 0f;

        for (int i = 0; i < count; i++)
        {
            float x = i - refIndex;
            float y = rawScales[i] - 1.0f;

            sumXY += x * y;
            sumX2 += x * x;
        }

        float beta = sumX2 > 1e-7f ? sumXY / sumX2 : 0f;

        float ssTot = 0f;
        float ssRes = 0f;
        float sumY = 0f;
        for (int i = 0; i < count; i++) sumY += (rawScales[i] - 1.0f);
        float meanY = sumY / count;

        for (int i = 0; i < count; i++)
        {
            float x = i - refIndex;
            float fitted = 1.0f + beta * x;
            fittedScales[i] = Math.Clamp(fitted, 0.85f, 1.25f);
            frames[i].FocusBreathingScale = fittedScales[i];

            float actual = rawScales[i] - 1.0f;
            float pred = beta * x;
            ssTot += (actual - meanY) * (actual - meanY);
            ssRes += (actual - pred) * (actual - pred);
        }

        double r2 = ssTot > 1e-6f ? Math.Clamp(1.0 - (ssRes / ssTot), 0.0, 1.0) : 1.0;
        float totalShift = (fittedScales[^1] - fittedScales[0]) * 100f;

        return new FocusBreathingResult
        {
            RawScales = rawScales,
            FittedScales = fittedScales,
            BreathingRatePerFrame = beta,
            TotalMagnificationShiftPercentage = totalShift,
            R2 = r2
        };
    }
}
