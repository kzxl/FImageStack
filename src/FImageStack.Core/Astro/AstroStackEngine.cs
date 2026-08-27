using FImageStack.Core.Models;
using FImageStack.Core.Noise;

namespace FImageStack.Core.Astro;

public interface IAstroStackEngine
{
    AstroStackResult Stack(
        IReadOnlyList<StackFrame> lightFrames,
        AstroCalibrationFrames? calibrationFrames,
        AstroStackSettings settings,
        IProgress<StackProgress>? progress = null);
}

public sealed class AstroStackEngine : IAstroStackEngine
{
    private readonly IStarDetector _starDetector;
    private readonly IAstroAlignmentEngine _alignmentEngine;
    private readonly IAstroCalibrationEngine _calibrationEngine;
    private readonly INoiseStackEngine _noiseEngine;

    public AstroStackEngine(
        IStarDetector? starDetector = null,
        IAstroAlignmentEngine? alignmentEngine = null,
        IAstroCalibrationEngine? calibrationEngine = null,
        INoiseStackEngine? noiseEngine = null)
    {
        _starDetector = starDetector ?? new StarDetector();
        _alignmentEngine = alignmentEngine ?? new AstroAlignmentEngine(_starDetector);
        _calibrationEngine = calibrationEngine ?? new AstroCalibrationEngine(_noiseEngine);
        _noiseEngine = noiseEngine ?? new NoiseStackEngine();
    }

    public AstroStackResult Stack(
        IReadOnlyList<StackFrame> lightFrames,
        AstroCalibrationFrames? calibrationFrames,
        AstroStackSettings settings,
        IProgress<StackProgress>? progress = null)
    {
        if (lightFrames == null || lightFrames.Count == 0)
            throw new ArgumentException("Light frames cannot be empty.", nameof(lightFrames));

        // 1. Master Calibration
        ImageBuffer<float>? masterDark = null;
        ImageBuffer<float>? masterFlat = null;
        ImageBuffer<float>? masterBias = null;

        if (settings.EnableCalibration && calibrationFrames != null)
        {
            progress?.Report(new StackProgress("Astro Calibration", 0, "Creating master dark/flat/bias frames..."));
            masterDark = _calibrationEngine.CreateMasterDark(calibrationFrames.DarkFrames);
            masterBias = _calibrationEngine.CreateMasterBias(calibrationFrames.BiasFrames);
            masterFlat = _calibrationEngine.CreateMasterFlat(calibrationFrames.FlatFrames, masterDark);

            // Calibrate all light frames
            for (int i = 0; i < lightFrames.Count; i++)
            {
                _calibrationEngine.CalibrateLightFrame(lightFrames[i], masterDark, masterFlat, masterBias);
            }
            progress?.Report(new StackProgress("Astro Calibration", 100, "Light frames calibrated."));
        }

        // 2. Star Detection & Alignment
        var detectedStarCounts = new List<int>();
        if (settings.EnableStarAlignment && lightFrames.Count > 1)
        {
            progress?.Report(new StackProgress("Star Alignment", 0, "Detecting stars and aligning frames..."));
            for (int i = 0; i < lightFrames.Count; i++)
            {
                if (lightFrames[i].GrayBuffer != null)
                {
                    var stars = _starDetector.DetectStars(lightFrames[i].GrayBuffer!, settings.StarDetectionSigma, settings.MaxStarsPerFrame, settings.MinStarRoundness);
                    detectedStarCounts.Add(stars.Count);
                }
            }

            _alignmentEngine.AlignAstroStack(lightFrames, progress);
        }

        // 3. Stacking
        progress?.Report(new StackProgress("Astro Stacking", 0, "Merging lights via Kappa-Sigma clipping..."));
        ImageBuffer<float> stackedImage;

        if (settings.EnableKappaSigmaClipping)
        {
            var (denoised, _) = _noiseEngine.ProcessKappaSigma(lightFrames, settings.Kappa, settings.StackingIterations);
            stackedImage = denoised;
        }
        else
        {
            stackedImage = _noiseEngine.ProcessMean(lightFrames);
        }

        // 4. Background Neutralization
        float skyLuminance = 0f;
        if (settings.EnableBackgroundNeutralization)
        {
            skyLuminance = NeutralizeBackground(stackedImage);
        }

        // 5. Auto-Stretch (Midtone Transfer Function)
        if (settings.EnableAutoStretch)
        {
            ApplyAutoStretch(stackedImage, settings.TargetBackgroundLevel);
        }

        progress?.Report(new StackProgress("Astro Stacking", 100, $"Astro stack complete ({lightFrames.Count} lights merged)."));

        return new AstroStackResult(stackedImage, lightFrames.Count)
        {
            CalibratedMasterDark = masterDark,
            CalibratedMasterFlat = masterFlat,
            SkyBackgroundLuminance = skyLuminance
        };
    }

    private static unsafe float NeutralizeBackground(ImageBuffer<float> image)
    {
        int w = image.Width;
        int h = image.Height;
        int ch = image.Channels;
        float* ptr = image.DataPointer;

        // Estimate median per channel
        float[] medians = new float[ch];
        int sampleStep = Math.Max(1, (w * h) / 5000);

        for (int c = 0; c < ch; c++)
        {
            var samples = new List<float>(5000);
            for (int y = 0; y < h; y += sampleStep)
            {
                for (int x = 0; x < w; x += sampleStep)
                {
                    samples.Add(ptr[(y * w + x) * ch + c]);
                }
            }
            samples.Sort();
            medians[c] = samples[samples.Count / 2];
        }

        float targetBg = medians.Average();

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w * ch;
            for (int x = 0; x < w; x++)
            {
                int baseIdx = rowOffset + x * ch;
                for (int c = 0; c < ch; c++)
                {
                    float offset = medians[c] - targetBg;
                    ptr[baseIdx + c] = Math.Clamp(ptr[baseIdx + c] - offset, 0f, 1f);
                }
            }
        });

        return targetBg;
    }

    private static unsafe void ApplyAutoStretch(ImageBuffer<float> image, float targetBg = 0.12f)
    {
        int total = image.TotalElements;
        float* ptr = image.DataPointer;

        // Find median
        var samples = new List<float>(2000);
        int step = Math.Max(1, total / 2000);
        for (int i = 0; i < total; i += step)
        {
            samples.Add(ptr[i]);
        }
        samples.Sort();
        float median = samples[samples.Count / 2];

        // Midtone Transfer Function (MTF): MTF(m, x) = (m - 1)x / ((2m - 1)x - m)
        // Set midtone m such that median maps to targetBg:
        // m = (median * (1 - targetBg)) / (median * (1 - 2*targetBg) + targetBg)
        float num = median * (1.0f - targetBg);
        float den = median * (1.0f - 2.0f * targetBg) + targetBg;
        if (MathF.Abs(den) < 1e-5f) return;

        float m = Math.Clamp(num / den, 0.001f, 0.999f);

        for (int i = 0; i < total; i++)
        {
            float x = ptr[i];
            if (x <= 0f) continue;
            if (x >= 1f) continue;

            float stretched = ((m - 1.0f) * x) / ((2.0f * m - 1.0f) * x - m);
            ptr[i] = Math.Clamp(stretched, 0f, 1f);
        }
    }
}
