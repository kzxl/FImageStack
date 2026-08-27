using FImageStack.Core.Models;
using FImageStack.Core.Noise;

namespace FImageStack.Core.Astro;

public interface IAstroCalibrationEngine
{
    ImageBuffer<float>? CreateMasterDark(IReadOnlyList<StackFrame> darkFrames);
    ImageBuffer<float>? CreateMasterFlat(IReadOnlyList<StackFrame> flatFrames, ImageBuffer<float>? masterDark = null);
    ImageBuffer<float>? CreateMasterBias(IReadOnlyList<StackFrame> biasFrames);
    void CalibrateLightFrame(
        StackFrame lightFrame, 
        ImageBuffer<float>? masterDark, 
        ImageBuffer<float>? masterFlat, 
        ImageBuffer<float>? masterBias);
}

public sealed class AstroCalibrationEngine : IAstroCalibrationEngine
{
    private readonly INoiseStackEngine _noiseEngine;

    public AstroCalibrationEngine(INoiseStackEngine? noiseEngine = null)
    {
        _noiseEngine = noiseEngine ?? new NoiseStackEngine();
    }

    public ImageBuffer<float>? CreateMasterDark(IReadOnlyList<StackFrame> darkFrames)
    {
        if (darkFrames == null || darkFrames.Count == 0) return null;
        return _noiseEngine.ProcessMedian(darkFrames);
    }

    public ImageBuffer<float>? CreateMasterFlat(IReadOnlyList<StackFrame> flatFrames, ImageBuffer<float>? masterDark = null)
    {
        if (flatFrames == null || flatFrames.Count == 0) return null;

        var rawFlat = _noiseEngine.ProcessMedian(flatFrames);

        // If master dark provided, subtract it from raw flat
        if (masterDark != null)
        {
            SubtractBuffer(rawFlat, masterDark);
        }

        // Normalize flat so that the mean/max intensity is 1.0
        NormalizeFlat(rawFlat);
        return rawFlat;
    }

    public ImageBuffer<float>? CreateMasterBias(IReadOnlyList<StackFrame> biasFrames)
    {
        if (biasFrames == null || biasFrames.Count == 0) return null;
        return _noiseEngine.ProcessMedian(biasFrames);
    }

    public unsafe void CalibrateLightFrame(
        StackFrame lightFrame,
        ImageBuffer<float>? masterDark,
        ImageBuffer<float>? masterFlat,
        ImageBuffer<float>? masterBias)
    {
        if (lightFrame.ColorBuffer == null) return;

        var color = lightFrame.ColorBuffer;
        int w = color.Width;
        int h = color.Height;
        int ch = color.Channels;

        float* cPtr = color.DataPointer;
        float* dPtr = masterDark?.DataPointer;
        float* fPtr = masterFlat?.DataPointer;
        float* bPtr = masterBias?.DataPointer;

        int dCh = masterDark?.Channels ?? ch;
        int fCh = masterFlat?.Channels ?? ch;
        int bCh = masterBias?.Channels ?? ch;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w * ch;

            for (int x = 0; x < w; x++)
            {
                int baseIdx = rowOffset + x * ch;

                for (int c = 0; c < ch; c++)
                {
                    float val = cPtr[baseIdx + c];

                    // 1. Subtract Dark or Bias
                    if (dPtr != null)
                    {
                        int dIdx = (y * w + x) * dCh + (c % dCh);
                        val -= dPtr[dIdx];
                    }
                    else if (bPtr != null)
                    {
                        int bIdx = (y * w + x) * bCh + (c % bCh);
                        val -= bPtr[bIdx];
                    }

                    // 2. Divide Flat
                    if (fPtr != null)
                    {
                        int fIdx = (y * w + x) * fCh + (c % fCh);
                        float flatGain = MathF.Max(0.05f, fPtr[fIdx]);
                        val /= flatGain;
                    }

                    cPtr[baseIdx + c] = MathF.Max(0f, val);
                }

                if (lightFrame.GrayBuffer != null)
                {
                    lightFrame.GrayBuffer.At(x, y) = 0.2126f * cPtr[baseIdx] + 0.7152f * cPtr[baseIdx + 1] + 0.0722f * cPtr[baseIdx + 2];
                }
            }
        });
    }

    private static unsafe void SubtractBuffer(ImageBuffer<float> target, ImageBuffer<float> subtractor)
    {
        int total = target.TotalElements;
        float* tPtr = target.DataPointer;
        float* sPtr = subtractor.DataPointer;

        for (int i = 0; i < total; i++)
        {
            tPtr[i] = MathF.Max(0f, tPtr[i] - sPtr[i]);
        }
    }

    private static unsafe void NormalizeFlat(ImageBuffer<float> flat)
    {
        int total = flat.TotalElements;
        float* ptr = flat.DataPointer;
        float maxVal = 0f;

        for (int i = 0; i < total; i++)
        {
            if (ptr[i] > maxVal) maxVal = ptr[i];
        }

        if (maxVal <= 1e-4f) return;
        float invMax = 1.0f / maxVal;

        for (int i = 0; i < total; i++)
        {
            ptr[i] *= invMax;
        }
    }
}
