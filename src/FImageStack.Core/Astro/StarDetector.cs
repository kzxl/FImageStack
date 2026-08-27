using System.Runtime.CompilerServices;
using FImageStack.Core.Models;

namespace FImageStack.Core.Astro;

public interface IStarDetector
{
    (float BackgroundMedian, float BackgroundSigma) EstimateSkyBackground(ImageBuffer<float> grayBuffer);
    List<StarCandidate> DetectStars(ImageBuffer<float> grayBuffer, float thresholdSigma = 3.5f, int maxStars = 100, float minRoundness = 0.5f);
}

public sealed class StarDetector : IStarDetector
{
    public (float BackgroundMedian, float BackgroundSigma) EstimateSkyBackground(ImageBuffer<float> grayBuffer)
    {
        if (grayBuffer == null) throw new ArgumentNullException(nameof(grayBuffer));

        int w = grayBuffer.Width;
        int h = grayBuffer.Height;
        int step = Math.Max(1, (int)Math.Sqrt((w * h) / 10000.0)); // Sample ~10,000 pixels
        var samples = new List<float>(10000);

        for (int y = 0; y < h; y += step)
        {
            for (int x = 0; x < w; x += step)
            {
                samples.Add(grayBuffer.At(x, y));
            }
        }

        samples.Sort();
        float median = samples[samples.Count / 2];

        // Compute MAD (Median Absolute Deviation)
        var deviations = new float[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            deviations[i] = MathF.Abs(samples[i] - median);
        }
        Array.Sort(deviations);
        float mad = deviations[deviations.Length / 2];
        float sigma = MathF.Max(1e-4f, 1.4826f * mad);

        return (median, sigma);
    }

    public List<StarCandidate> DetectStars(
        ImageBuffer<float> grayBuffer, 
        float thresholdSigma = 3.5f, 
        int maxStars = 100, 
        float minRoundness = 0.5f)
    {
        if (grayBuffer == null) throw new ArgumentNullException(nameof(grayBuffer));

        int w = grayBuffer.Width;
        int h = grayBuffer.Height;
        var (bgMedian, bgSigma) = EstimateSkyBackground(grayBuffer);
        float threshold = bgMedian + thresholdSigma * bgSigma;

        var candidates = new List<StarCandidate>();
        int margin = 4;

        for (int y = margin; y < h - margin; y++)
        {
            for (int x = margin; x < w - margin; x++)
            {
                float centerVal = grayBuffer.At(x, y);
                if (centerVal <= threshold) continue;

                // 8-neighborhood local maximum test
                if (centerVal < grayBuffer.At(x - 1, y - 1) ||
                    centerVal < grayBuffer.At(x,     y - 1) ||
                    centerVal < grayBuffer.At(x + 1, y - 1) ||
                    centerVal < grayBuffer.At(x - 1, y)     ||
                    centerVal < grayBuffer.At(x + 1, y)     ||
                    centerVal < grayBuffer.At(x - 1, y + 1) ||
                    centerVal < grayBuffer.At(x,     y + 1) ||
                    centerVal < grayBuffer.At(x + 1, y + 1))
                {
                    continue;
                }

                // Centroid & Moments calculation in a 5x5 window
                float sumWeight = 0f;
                float sumWx = 0f;
                float sumWy = 0f;

                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        float val = MathF.Max(0f, grayBuffer.At(x + dx, y + dy) - bgMedian);
                        sumWeight += val;
                        sumWx += val * (x + dx);
                        sumWy += val * (y + dy);
                    }
                }

                if (sumWeight <= 1e-4f) continue;

                float cx = sumWx / sumWeight;
                float cy = sumWy / sumWeight;

                // Second-order central moments
                float mu20 = 0f;
                float mu02 = 0f;
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        float val = MathF.Max(0f, grayBuffer.At(x + dx, y + dy) - bgMedian);
                        float rx = (x + dx) - cx;
                        float ry = (y + dy) - cy;
                        mu20 += val * rx * rx;
                        mu02 += val * ry * ry;
                    }
                }

                float fwhm = 2.355f * MathF.Sqrt((mu20 + mu02) / (2f * sumWeight + 1e-5f));
                float roundness = 1.0f - MathF.Abs(mu20 - mu02) / (mu20 + mu02 + 1e-5f);

                if (roundness < minRoundness) continue;

                candidates.Add(new StarCandidate
                {
                    X = cx,
                    Y = cy,
                    PeakIntensity = centerVal,
                    TotalFlux = sumWeight,
                    Fwhm = fwhm,
                    Roundness = roundness,
                    Snr = (centerVal - bgMedian) / bgSigma
                });
            }
        }

        // Sort by TotalFlux / PeakIntensity descending and take top maxStars
        candidates.Sort((a, b) => b.TotalFlux.CompareTo(a.TotalFlux));
        if (candidates.Count > maxStars)
        {
            candidates.RemoveRange(maxStars, candidates.Count - maxStars);
        }

        return candidates;
    }
}
