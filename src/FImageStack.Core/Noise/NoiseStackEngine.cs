using System.Numerics;
using System.Runtime.CompilerServices;
using FImageStack.Core.Models;

namespace FImageStack.Core.Noise;

public interface INoiseStackEngine
{
    NoiseStackResult Process(IReadOnlyList<StackFrame> frames, NoiseStackSettings settings);
    ImageBuffer<float> ProcessMean(IReadOnlyList<StackFrame> frames);
    ImageBuffer<float> ProcessMedian(IReadOnlyList<StackFrame> frames);
    (ImageBuffer<float> Denoised, ImageBuffer<float>? RejectionMap) ProcessKappaSigma(
        IReadOnlyList<StackFrame> frames, 
        float kappa = 2.5f, 
        int iterations = 3,
        bool generateRejectionMap = false);
    ImageBuffer<float> ProcessMinMaxRejection(IReadOnlyList<StackFrame> frames, int trimCount = 1);
    ImageBuffer<float> ProcessWinsorizedMean(IReadOnlyList<StackFrame> frames, float lowerQuantile = 0.10f, float upperQuantile = 0.90f);
    (ImageBuffer<float> Mean, ImageBuffer<float> Variance) ProcessStreaming(IEnumerable<StackFrame> frames);
}

public sealed class NoiseStackEngine : INoiseStackEngine
{
    public NoiseStackResult Process(IReadOnlyList<StackFrame> frames, NoiseStackSettings settings)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Frames list cannot be empty.", nameof(frames));

        int n = frames.Count;
        ImageBuffer<float> output;
        ImageBuffer<float>? rejectionMap = null;

        switch (settings.Method)
        {
            case NoiseStackMethod.Mean:
                output = ProcessMean(frames);
                break;

            case NoiseStackMethod.Median:
                output = ProcessMedian(frames);
                break;

            case NoiseStackMethod.KappaSigmaClipping:
                (output, rejectionMap) = ProcessKappaSigma(frames, settings.Kappa, settings.Iterations, settings.GenerateRejectionMap);
                break;

            case NoiseStackMethod.MinMaxRejection:
                output = ProcessMinMaxRejection(frames, settings.MinMaxTrimCount);
                break;

            case NoiseStackMethod.WinsorizedMean:
                output = ProcessWinsorizedMean(frames, settings.WinsorLowerQuantile, settings.WinsorUpperQuantile);
                break;

            case NoiseStackMethod.StreamingAccumulator:
                var (streamMean, _) = ProcessStreaming(frames);
                output = streamMean;
                break;

            default:
                output = ProcessMean(frames);
                break;
        }

        // Theoretical SNR improvement in dB: 10 * log10(N)
        float snrImprovementDb = 10.0f * (float)Math.Log10(Math.Max(1, n));

        return new NoiseStackResult(output, n, settings.Method)
        {
            RejectionMap = rejectionMap,
            EstimatedSnrImprovementDb = snrImprovementDb
        };
    }

    public ImageBuffer<float> ProcessMean(IReadOnlyList<StackFrame> frames)
    {
        ValidateFrames(frames);
        int w = frames[0].Width;
        int h = frames[0].Height;
        int channels = frames[0].ColorBuffer?.Channels ?? 3;
        int frameCount = frames.Count;
        float invCount = 1.0f / frameCount;

        var result = new ImageBuffer<float>(w, h, channels, frames[0].Format);

        Parallel.For(0, h, y =>
        {
            var dstRow = result.GetRowSpan(y);
            int rowLen = dstRow.Length;

            for (int i = 0; i < frameCount; i++)
            {
                var srcBuffer = frames[i].ColorBuffer ?? throw new InvalidOperationException($"Frame {i} ColorBuffer is null.");
                var srcRow = srcBuffer.GetRowSpan(y);

                int vectorSize = Vector<float>.Count;
                int x = 0;

                if (i == 0)
                {
                    // First frame: initialize
                    for (; x <= rowLen - vectorSize; x += vectorSize)
                    {
                        var vSrc = new Vector<float>(srcRow.Slice(x, vectorSize));
                        vSrc.CopyTo(dstRow.Slice(x, vectorSize));
                    }
                    for (; x < rowLen; x++)
                    {
                        dstRow[x] = srcRow[x];
                    }
                }
                else
                {
                    // Accumulate
                    for (; x <= rowLen - vectorSize; x += vectorSize)
                    {
                        var vDst = new Vector<float>(dstRow.Slice(x, vectorSize));
                        var vSrc = new Vector<float>(srcRow.Slice(x, vectorSize));
                        (vDst + vSrc).CopyTo(dstRow.Slice(x, vectorSize));
                    }
                    for (; x < rowLen; x++)
                    {
                        dstRow[x] += srcRow[x];
                    }
                }
            }

            // Multiply by invCount
            int vecSize = Vector<float>.Count;
            var vInv = new Vector<float>(invCount);
            int idx = 0;
            for (; idx <= rowLen - vecSize; idx += vecSize)
            {
                var vDst = new Vector<float>(dstRow.Slice(idx, vecSize));
                (vDst * vInv).CopyTo(dstRow.Slice(idx, vecSize));
            }
            for (; idx < rowLen; idx++)
            {
                dstRow[idx] *= invCount;
            }
        });

        return result;
    }

    public ImageBuffer<float> ProcessMedian(IReadOnlyList<StackFrame> frames)
    {
        ValidateFrames(frames);
        int w = frames[0].Width;
        int h = frames[0].Height;
        int channels = frames[0].ColorBuffer?.Channels ?? 3;
        int frameCount = frames.Count;

        var result = new ImageBuffer<float>(w, h, channels, frames[0].Format);

        Parallel.For(0, h, () => new float[frameCount], (y, state, pixelValues) =>
        {
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < channels; c++)
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        pixelValues[i] = frames[i].ColorBuffer!.At(x, y, c);
                    }

                    Array.Sort(pixelValues);

                    float median;
                    if ((frameCount & 1) == 1)
                    {
                        median = pixelValues[frameCount / 2];
                    }
                    else
                    {
                        median = 0.5f * (pixelValues[frameCount / 2 - 1] + pixelValues[frameCount / 2]);
                    }

                    result.At(x, y, c) = median;
                }
            }
            return pixelValues;
        }, _ => { });

        return result;
    }

    public (ImageBuffer<float> Denoised, ImageBuffer<float>? RejectionMap) ProcessKappaSigma(
        IReadOnlyList<StackFrame> frames,
        float kappa = 2.5f,
        int iterations = 3,
        bool generateRejectionMap = false)
    {
        ValidateFrames(frames);
        int w = frames[0].Width;
        int h = frames[0].Height;
        int channels = frames[0].ColorBuffer?.Channels ?? 3;
        int frameCount = frames.Count;

        var result = new ImageBuffer<float>(w, h, channels, frames[0].Format);
        ImageBuffer<float>? rejectionMap = generateRejectionMap
            ? new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32)
            : null;

        Parallel.For(0, h, () => (Values: new float[frameCount], Mask: new bool[frameCount]), (y, state, tls) =>
        {
            var values = tls.Values;
            var mask = tls.Mask;

            for (int x = 0; x < w; x++)
            {
                int totalRejectedChannels = 0;

                for (int c = 0; c < channels; c++)
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        values[i] = frames[i].ColorBuffer!.At(x, y, c);
                        mask[i] = true; // Included
                    }

                    for (int iter = 0; iter < iterations; iter++)
                    {
                        float sum = 0f;
                        int validCount = 0;
                        for (int i = 0; i < frameCount; i++)
                        {
                            if (mask[i])
                            {
                                sum += values[i];
                                validCount++;
                            }
                        }

                        if (validCount <= 1) break;

                        float mean = sum / validCount;
                        float sumSq = 0f;
                        for (int i = 0; i < frameCount; i++)
                        {
                            if (mask[i])
                            {
                                float diff = values[i] - mean;
                                sumSq += diff * diff;
                            }
                        }

                        float sigma = (float)Math.Sqrt(sumSq / (validCount - 1));
                        if (sigma < 1e-6f) break;

                        float threshold = kappa * sigma;
                        bool changed = false;

                        for (int i = 0; i < frameCount; i++)
                        {
                            if (mask[i] && Math.Abs(values[i] - mean) > threshold)
                            {
                                mask[i] = false;
                                changed = true;
                            }
                        }

                        if (!changed) break;
                    }

                    // Compute final mean of surviving pixels
                    float finalSum = 0f;
                    int finalCount = 0;
                    for (int i = 0; i < frameCount; i++)
                    {
                        if (mask[i])
                        {
                            finalSum += values[i];
                            finalCount++;
                        }
                        else
                        {
                            totalRejectedChannels++;
                        }
                    }

                    result.At(x, y, c) = finalCount > 0 ? (finalSum / finalCount) : values[0];
                }

                if (rejectionMap != null)
                {
                    // Fraction of rejected samples across channels and frames
                    float rejectionRatio = (float)totalRejectedChannels / (frameCount * channels);
                    rejectionMap.At(x, y) = rejectionRatio;
                }
            }

            return tls;
        }, _ => { });

        return (result, rejectionMap);
    }

    public ImageBuffer<float> ProcessMinMaxRejection(IReadOnlyList<StackFrame> frames, int trimCount = 1)
    {
        ValidateFrames(frames);
        int w = frames[0].Width;
        int h = frames[0].Height;
        int channels = frames[0].ColorBuffer?.Channels ?? 3;
        int frameCount = frames.Count;

        if (trimCount * 2 >= frameCount)
            trimCount = Math.Max(0, (frameCount - 1) / 2);

        var result = new ImageBuffer<float>(w, h, channels, frames[0].Format);

        Parallel.For(0, h, () => new float[frameCount], (y, state, pixelValues) =>
        {
            int keepCount = frameCount - 2 * trimCount;
            float invKeep = 1.0f / keepCount;

            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < channels; c++)
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        pixelValues[i] = frames[i].ColorBuffer!.At(x, y, c);
                    }

                    Array.Sort(pixelValues);

                    float sum = 0f;
                    for (int i = trimCount; i < frameCount - trimCount; i++)
                    {
                        sum += pixelValues[i];
                    }

                    result.At(x, y, c) = sum * invKeep;
                }
            }
            return pixelValues;
        }, _ => { });

        return result;
    }

    public ImageBuffer<float> ProcessWinsorizedMean(
        IReadOnlyList<StackFrame> frames, 
        float lowerQuantile = 0.10f, 
        float upperQuantile = 0.90f)
    {
        ValidateFrames(frames);
        int w = frames[0].Width;
        int h = frames[0].Height;
        int channels = frames[0].ColorBuffer?.Channels ?? 3;
        int frameCount = frames.Count;

        lowerQuantile = Math.Clamp(lowerQuantile, 0f, 0.49f);
        upperQuantile = Math.Clamp(upperQuantile, 0.51f, 1.0f);

        int lowerIdx = (int)Math.Floor(lowerQuantile * (frameCount - 1));
        int upperIdx = (int)Math.Ceiling(upperQuantile * (frameCount - 1));

        var result = new ImageBuffer<float>(w, h, channels, frames[0].Format);
        float invCount = 1.0f / frameCount;

        Parallel.For(0, h, () => new float[frameCount], (y, state, pixelValues) =>
        {
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < channels; c++)
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        pixelValues[i] = frames[i].ColorBuffer!.At(x, y, c);
                    }

                    Array.Sort(pixelValues);
                    float lowVal = pixelValues[lowerIdx];
                    float highVal = pixelValues[upperIdx];

                    float sum = 0f;
                    for (int i = 0; i < frameCount; i++)
                    {
                        float v = pixelValues[i];
                        if (v < lowVal) v = lowVal;
                        else if (v > highVal) v = highVal;
                        sum += v;
                    }

                    result.At(x, y, c) = sum * invCount;
                }
            }
            return pixelValues;
        }, _ => { });

        return result;
    }

    public (ImageBuffer<float> Mean, ImageBuffer<float> Variance) ProcessStreaming(IEnumerable<StackFrame> frames)
    {
        if (frames == null) throw new ArgumentNullException(nameof(frames));

        ImageBuffer<float>? mean = null;
        ImageBuffer<float>? m2 = null;
        int k = 0;

        foreach (var frame in frames)
        {
            var src = frame.ColorBuffer ?? throw new InvalidOperationException($"Frame {frame.Index} ColorBuffer is null.");
            k++;

            if (mean == null || m2 == null)
            {
                mean = new ImageBuffer<float>(src.Width, src.Height, src.Channels, src.Format);
                m2 = new ImageBuffer<float>(src.Width, src.Height, src.Channels, src.Format);
                src.CopyTo(mean);
                continue;
            }

            int count = k;
            float invK = 1.0f / count;

            Parallel.For(0, src.Height, y =>
            {
                var srcRow = src.GetRowSpan(y);
                var meanRow = mean.GetRowSpan(y);
                var m2Row = m2.GetRowSpan(y);
                int len = srcRow.Length;

                for (int i = 0; i < len; i++)
                {
                    float x = srcRow[i];
                    float delta = x - meanRow[i];
                    meanRow[i] += delta * invK;
                    float delta2 = x - meanRow[i];
                    m2Row[i] += delta * delta2;
                }
            });
        }

        if (mean == null || m2 == null || k == 0)
            throw new InvalidOperationException("No frames provided for streaming accumulation.");

        var variance = new ImageBuffer<float>(mean.Width, mean.Height, mean.Channels, mean.Format);
        float invKMinus1 = k > 1 ? (1.0f / (k - 1)) : 1.0f;

        Parallel.For(0, mean.Height, y =>
        {
            var m2Row = m2.GetRowSpan(y);
            var varRow = variance.GetRowSpan(y);
            int len = m2Row.Length;
            for (int i = 0; i < len; i++)
            {
                varRow[i] = m2Row[i] * invKMinus1;
            }
        });

        m2.Dispose();
        return (mean, variance);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateFrames(IReadOnlyList<StackFrame> frames)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Frames list cannot be empty.", nameof(frames));

        int w = frames[0].Width;
        int h = frames[0].Height;

        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i].ColorBuffer == null)
                throw new InvalidOperationException($"Frame {i} ColorBuffer is null.");

            if (frames[i].Width != w || frames[i].Height != h)
                throw new InvalidOperationException($"Frame {i} dimensions ({frames[i].Width}x{frames[i].Height}) do not match reference ({w}x{h}).");
        }
    }
}
