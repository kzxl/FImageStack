using FImageStack.Core.Models;

namespace FImageStack.Core.Quality;

public interface IOptimalFrameRangeSelector
{
    OptimalFrameRangeResult AnalyzeOptimalRange(
        IReadOnlyList<StackFrame> frames,
        float thresholdFactor = 0.15f);
}

public sealed class OptimalFrameRangeSelector : IOptimalFrameRangeSelector
{
    public unsafe OptimalFrameRangeResult AnalyzeOptimalRange(
        IReadOnlyList<StackFrame> frames,
        float thresholdFactor = 0.15f)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Stack contains no frames.", nameof(frames));

        int count = frames.Count;
        var result = new OptimalFrameRangeResult
        {
            TotalInputFrames = count
        };

        // 1. Calculate Net Sharpness and Mean Luminance for each frame
        var metrics = new List<FrameQualityMetric>(count);
        float peakSharpness = 0f;
        var luminances = new List<float>(count);

        for (int i = 0; i < count; i++)
        {
            var f = frames[i];
            float netSharpness = 0f;
            float sumLum = 0f;

            if (f.GrayBuffer != null)
            {
                int w = f.GrayBuffer.Width;
                int h = f.GrayBuffer.Height;
                float* ptr = f.GrayBuffer.DataPointer;
                int totalPixels = w * h;

                // Fast stride sample for speed
                int step = Math.Max(1, totalPixels / 40000);
                int samples = 0;

                for (int y = 1; y < h - 1; y++)
                {
                    int rowOffset = y * w;
                    for (int x = 1; x < w - 1; x++)
                    {
                        int idx = rowOffset + x;
                        float center = ptr[idx];
                        sumLum += center;

                        float lx = MathF.Abs(2f * center - ptr[rowOffset + x - 1] - ptr[rowOffset + x + 1]);
                        float ly = MathF.Abs(2f * center - ptr[(y - 1) * w + x] - ptr[(y + 1) * w + x]);
                        float sharp = lx + ly;

                        if (sharp > 0.005f) netSharpness += sharp;
                        samples++;
                    }
                }

                if (samples > 0)
                {
                    sumLum /= samples;
                }
            }
            else if (f.FocusMap != null)
            {
                netSharpness = (float)f.SharpnessScore;
                sumLum = 0.5f;
            }

            peakSharpness = Math.Max(peakSharpness, netSharpness);
            luminances.Add(sumLum);

            metrics.Add(new FrameQualityMetric
            {
                FrameIndex = i,
                NetSharpness = netSharpness,
                MeanLuminance = sumLum,
                AlignmentConfidence = f.AlignmentConfidence
            });
        }

        // 2. Focus Envelope Start / End Boundary Detection
        float threshold = peakSharpness * Math.Clamp(thresholdFactor, 0.05f, 0.50f);

        int startFrame = 0;
        for (int i = 0; i < count; i++)
        {
            if (metrics[i].NetSharpness >= threshold)
            {
                startFrame = i;
                break;
            }
        }

        int endFrame = count - 1;
        for (int i = count - 1; i >= 0; i--)
        {
            if (metrics[i].NetSharpness >= threshold)
            {
                endFrame = i;
                break;
            }
        }

        if (startFrame > endFrame)
        {
            startFrame = 0;
            endFrame = count - 1;
        }

        result.RecommendedStartFrame = startFrame;
        result.RecommendedEndFrame = endFrame;

        // 3. Mark Deadbands
        for (int i = 0; i < startFrame; i++)
        {
            metrics[i].IsSelected = false;
            metrics[i].CullReason |= FrameCullReason.PreFocusDeadband;
        }

        for (int i = endFrame + 1; i < count; i++)
        {
            metrics[i].IsSelected = false;
            metrics[i].CullReason |= FrameCullReason.PostFocusDeadband;
        }

        // 4. Outlier Analysis in Active Range
        var sortedLum = new List<float>(luminances.Skip(startFrame).Take(endFrame - startFrame + 1));
        sortedLum.Sort();
        float medianLum = sortedLum.Count > 0 ? sortedLum[sortedLum.Count / 2] : 0.5f;

        for (int i = startFrame; i <= endFrame; i++)
        {
            var m = metrics[i];

            // Check Exposure Outlier
            if (MathF.Abs(m.MeanLuminance - medianLum) > 0.25f)
            {
                m.IsSelected = false;
                m.CullReason |= FrameCullReason.ExposureGlitch;
            }

            // Check Shaky / Motion Blur Outlier
            if (i > startFrame && i < endFrame)
            {
                float neighborAvg = (metrics[i - 1].NetSharpness + metrics[i + 1].NetSharpness) * 0.5f;
                if (neighborAvg > threshold && m.NetSharpness < neighborAvg * 0.45f)
                {
                    m.IsSelected = false;
                    m.CullReason |= FrameCullReason.ShakyMotionBlur;
                }
            }

            // Check Severe Misalignment
            if (m.AlignmentConfidence < 0.50)
            {
                m.IsSelected = false;
                m.CullReason |= FrameCullReason.SevereMisalignment;
            }

            if (m.IsSelected)
            {
                result.SelectedIndices.Add(i);
            }
        }

        result.FrameMetrics.AddRange(metrics);
        result.Summary = $"Recommended: Frame {startFrame + 1}–{endFrame + 1} (Selected {result.SelectedFrameCount}/{count} frames, Culled {result.CulledFrameCount})";

        return result;
    }
}
