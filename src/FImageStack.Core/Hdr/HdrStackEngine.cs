using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using FImageStack.Core.PostProcessing;

namespace FImageStack.Core.Hdr;

public interface IHdrStackEngine
{
    HdrStackResult Process(IReadOnlyList<StackFrame> frames, HdrStackSettings settings);
    ImageBuffer<float> MergeMertens(IReadOnlyList<StackFrame> frames, HdrStackSettings settings, out ImageBuffer<float>? deghostMask);
    ImageBuffer<float> MergeRadiance(IReadOnlyList<StackFrame> frames, IReadOnlyList<float>? exposureTimes = null);
}

public sealed class HdrStackEngine : IHdrStackEngine
{
    private readonly IToneMappingEngine _toneMappingEngine;

    public HdrStackEngine(IToneMappingEngine? toneMappingEngine = null)
    {
        _toneMappingEngine = toneMappingEngine ?? new ToneMappingEngine();
    }

    public HdrStackResult Process(IReadOnlyList<StackFrame> frames, HdrStackSettings settings)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Frames list cannot be empty.", nameof(frames));

        ImageBuffer<float> radianceMap;
        ImageBuffer<float>? deghostMask = null;

        if (settings.Method == HdrMergeMethod.MertensFusion)
        {
            radianceMap = MergeMertens(frames, settings, out deghostMask);
        }
        else
        {
            radianceMap = MergeRadiance(frames);
        }

        // Calculate Dynamic Range (EV stops)
        float dynamicRangeEv = CalculateDynamicRangeEv(radianceMap);

        // Apply Tone Mapping
        var toneMapped = _toneMappingEngine.ApplyToneMapping(
            radianceMap,
            settings.ToneMapping,
            settings.ExposureCompensation);

        return new HdrStackResult(radianceMap, toneMapped, settings.Method)
        {
            DeghostMask = deghostMask,
            EstimatedDynamicRangeEv = dynamicRangeEv,
            ToneMapperUsed = settings.ToneMapping
        };
    }

    public unsafe ImageBuffer<float> MergeMertens(
        IReadOnlyList<StackFrame> frames, 
        HdrStackSettings settings, 
        out ImageBuffer<float>? deghostMask)
    {
        int width = frames[0].Width;
        int height = frames[0].Height;
        int frameCount = frames.Count;
        int levels = Math.Clamp(settings.PyramidLevels, 2, 7);

        // 1. Calculate Well-Exposedness, Contrast, Saturation weights
        var frameWeights = new ImageBuffer<float>[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            frameWeights[f] = new ImageBuffer<float>(width, height, 1);
        }

        float*[] colorPtrs = new float*[frameCount];
        float*[] weightPtrs = new float*[frameCount];

        for (int f = 0; f < frameCount; f++)
        {
            colorPtrs[f] = frames[f].ColorBuffer!.DataPointer;
            weightPtrs[f] = frameWeights[f].DataPointer;
        }

        float sigmaSq2 = 2f * settings.WellExposednessSigma * settings.WellExposednessSigma;

        ImageBuffer<float>? motionMask = settings.EnableDeghosting
            ? new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32)
            : null;

        float* maskPtr = motionMask != null ? motionMask.DataPointer : null;

        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int pixelIdx = rowOffset + x;
                int colorIdx = pixelIdx * 3;
                float sumWeight = 0f;

                // Deghosting check: find max well-exposedness frame
                int bestExposedFrame = 0;
                float maxWellExp = -1f;
                float maxIntensityDiff = 0f;

                for (int f = 0; f < frameCount; f++)
                {
                    float r = colorPtrs[f][colorIdx];
                    float g = colorPtrs[f][colorIdx + 1];
                    float b = colorPtrs[f][colorIdx + 2];

                    // 1. Well-Exposedness (Gaussian curve around 0.5)
                    float dr = r - 0.5f;
                    float dg = g - 0.5f;
                    float db = b - 0.5f;
                    float wExposure = MathF.Exp(-(dr * dr + dg * dg + db * db) / sigmaSq2);
                    if (wExposure > maxWellExp)
                    {
                        maxWellExp = wExposure;
                        bestExposedFrame = f;
                    }

                    // 2. Color Saturation Weight
                    float mean = (r + g + b) / 3f;
                    float satVar = ((r - mean) * (r - mean) + (g - mean) * (g - mean) + (b - mean) * (b - mean)) / 3f;
                    float wSaturation = MathF.Sqrt(satVar);

                    // 3. Local Contrast Weight (Laplacian gradient)
                    float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    float wContrast = 0.5f; // Baseline
                    if (x > 0 && x < width - 1 && y > 0 && y < height - 1)
                    {
                        int up = ((y - 1) * width + x) * 3;
                        int down = ((y + 1) * width + x) * 3;
                        int left = (y * width + (x - 1)) * 3;
                        int right = (y * width + (x + 1)) * 3;

                        float lumUp = 0.2126f * colorPtrs[f][up] + 0.7152f * colorPtrs[f][up + 1] + 0.0722f * colorPtrs[f][up + 2];
                        float lumDown = 0.2126f * colorPtrs[f][down] + 0.7152f * colorPtrs[f][down + 1] + 0.0722f * colorPtrs[f][down + 2];
                        float lumLeft = 0.2126f * colorPtrs[f][left] + 0.7152f * colorPtrs[f][left + 1] + 0.0722f * colorPtrs[f][left + 2];
                        float lumRight = 0.2126f * colorPtrs[f][right] + 0.7152f * colorPtrs[f][right + 1] + 0.0722f * colorPtrs[f][right + 2];

                        float lap = MathF.Abs(lumUp + lumDown + lumLeft + lumRight - 4f * lum);
                        wContrast = MathF.Max(0.01f, lap);
                    }

                    float weight = MathF.Pow(wExposure + 1e-4f, settings.WellExposednessWeight) *
                                   MathF.Pow(wSaturation + 1e-4f, settings.SaturationWeight) *
                                   MathF.Pow(wContrast + 1e-4f, settings.ContrastWeight);

                    weightPtrs[f][pixelIdx] = weight;
                    sumWeight += weight;
                }

                // Deghosting: If motion disparity detected across exposures
                if (settings.EnableDeghosting && frameCount > 2)
                {
                    for (int f = 0; f < frameCount - 1; f++)
                    {
                        float r1 = colorPtrs[f][colorIdx];
                        float r2 = colorPtrs[f + 1][colorIdx];
                        maxIntensityDiff = MathF.Max(maxIntensityDiff, MathF.Abs(r1 - r2));
                    }

                    if (maxIntensityDiff > settings.DeghostingThreshold && maskPtr != null)
                    {
                        maskPtr[pixelIdx] = 1.0f; // Mark ghosting
                        // Force weight entirely onto best exposed frame to prevent ghost trails
                        for (int f = 0; f < frameCount; f++)
                        {
                            weightPtrs[f][pixelIdx] = (f == bestExposedFrame) ? 1.0f : 0.0f;
                        }
                        sumWeight = 1.0f;
                    }
                }

                // Normalize weights
                float invSum = sumWeight > 0 ? 1f / sumWeight : 1f / frameCount;
                for (int f = 0; f < frameCount; f++)
                {
                    weightPtrs[f][pixelIdx] *= invSum;
                }
            }
        });

        // 2. Multi-Scale Laplacian Pyramid Fusion
        var fusedLaplacians = new ImageBuffer<float>[levels];
        int curW = width;
        int curH = height;
        for (int l = 0; l < levels; l++)
        {
            fusedLaplacians[l] = new ImageBuffer<float>(curW, curH, 3);
            if (l < levels - 1)
            {
                curW = (curW + 1) / 2;
                curH = (curH + 1) / 2;
            }
        }

        for (int f = 0; f < frameCount; f++)
        {
            var colorPyr = MultiScalePyramidFusionEngine.BuildLaplacianPyramid(frames[f].ColorBuffer!, levels);
            var weightPyr = MultiScalePyramidFusionEngine.BuildGaussianPyramid(frameWeights[f], levels);

            for (int l = 0; l < levels; l++)
            {
                MultiScalePyramidFusionEngine.BlendPyramidLevel(fusedLaplacians[l], colorPyr[l], weightPyr[l]);
                colorPyr[l].Dispose();
                weightPyr[l].Dispose();
            }
        }

        for (int f = 0; f < frameCount; f++)
        {
            frameWeights[f].Dispose();
        }

        var finalImage = MultiScalePyramidFusionEngine.ReconstructFromLaplacianPyramid(fusedLaplacians);

        for (int l = 0; l < levels; l++)
        {
            fusedLaplacians[l].Dispose();
        }

        deghostMask = motionMask;
        return finalImage;
    }

    public unsafe ImageBuffer<float> MergeRadiance(
        IReadOnlyList<StackFrame> frames, 
        IReadOnlyList<float>? exposureTimes = null)
    {
        int width = frames[0].Width;
        int height = frames[0].Height;
        int frameCount = frames.Count;

        float[] times = new float[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            times[i] = (exposureTimes != null && i < exposureTimes.Count && exposureTimes[i] > 0)
                ? exposureTimes[i]
                : MathF.Pow(2.0f, i - (frameCount - 1) / 2f); // Default EV progression: 2^(i - mid)
        }

        var radianceMap = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32);
        float* dstPtr = radianceMap.DataPointer;

        float*[] colorPtrs = new float*[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            colorPtrs[f] = frames[f].ColorBuffer!.DataPointer;
        }

        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width * 3;

            for (int x = 0; x < width; x++)
            {
                int baseIdx = rowOffset + x * 3;

                for (int c = 0; c < 3; c++)
                {
                    float sumWeightedRadiance = 0f;
                    float sumWeight = 0f;

                    for (int f = 0; f < frameCount; f++)
                    {
                        float z = colorPtrs[f][baseIdx + c]; // [0.0 - 1.0]
                        float dt = times[f];

                        // Debevec triangular hat weighting function: w(z) = min(z, 1 - z)
                        float w = MathF.Min(z, 1.0f - z);
                        if (w < 1e-4f) w = 1e-4f;

                        // Linear radiance sample L = z / dt
                        float radiance = MathF.Max(0f, z) / dt;

                        sumWeightedRadiance += w * radiance;
                        sumWeight += w;
                    }

                    dstPtr[baseIdx + c] = sumWeight > 0 ? (sumWeightedRadiance / sumWeight) : 0f;
                }
            }
        });

        return radianceMap;
    }

    private static unsafe float CalculateDynamicRangeEv(ImageBuffer<float> buffer)
    {
        float minVal = float.MaxValue;
        float maxVal = float.MinValue;
        float* ptr = buffer.DataPointer;
        int total = buffer.TotalElements;

        for (int i = 0; i < total; i++)
        {
            float v = ptr[i];
            if (v > 1e-4f)
            {
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }
        }

        if (maxVal <= minVal || minVal <= 0f) return 8.0f; // Default baseline ~8 EV
        return MathF.Log2(maxVal / minVal);
    }
}
