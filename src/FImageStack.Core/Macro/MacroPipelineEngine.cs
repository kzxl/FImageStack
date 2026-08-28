using System.Diagnostics;
using FImageStack.Core.Alignment;
using FImageStack.Core.DepthMap;
using FImageStack.Core.FocusMeasure;
using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using FImageStack.Core.Quality;
using FImageStack.Core.Restoration;
using CoreStackFrame = FImageStack.Core.Models.StackFrame;

namespace FImageStack.Core.Macro;

public interface IMacroPipelineEngine
{
    Task<MacroStackResult> ProcessAsync(
        MacroFrameSet frameSet,
        MacroPipelineConfig config,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);

    MacroStackResult Process(
        MacroFrameSet frameSet,
        MacroPipelineConfig config,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// High-performance Macro Computational Photography Engine orchestrating the 5-stage pipeline:
/// 1. Culling & Quality Filtering -> 2. Focus Breathing Alignment -> 3. Sub-Part Fusion ->
/// 4. Micro-Detail Recovery -> 5. Quality Telemetry & Depth Generation.
/// </summary>
public sealed class MacroPipelineEngine : IMacroPipelineEngine
{
    private readonly IAlignmentEngine _alignmentEngine;
    private readonly IFocusMeasureEngine _focusMeasureEngine;
    private readonly IDepthMapEstimator _depthMapEstimator;
    private readonly IRichardsonLucyEngine _richardsonLucyEngine;

    public MacroPipelineEngine(
        IAlignmentEngine? alignmentEngine = null,
        IFocusMeasureEngine? focusMeasureEngine = null,
        IDepthMapEstimator? depthMapEstimator = null,
        IRichardsonLucyEngine? richardsonLucyEngine = null)
    {
        _alignmentEngine = alignmentEngine ?? new AdvancedAlignmentEngine();
        _focusMeasureEngine = focusMeasureEngine ?? new ModifiedLaplacianFocusMeasure();
        _depthMapEstimator = depthMapEstimator ?? new StandardDepthMapEstimator();
        _richardsonLucyEngine = richardsonLucyEngine ?? new RichardsonLucyEngine();
    }

    public Task<MacroStackResult> ProcessAsync(
        MacroFrameSet frameSet,
        MacroPipelineConfig config,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Process(frameSet, config, progress, cancellationToken), cancellationToken);
    }

    public unsafe MacroStackResult Process(
        MacroFrameSet frameSet,
        MacroPipelineConfig config,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frameSet);
        ArgumentNullException.ThrowIfNull(config);

        if (frameSet.TotalFrames < 1)
        {
            throw new ArgumentException("MacroFrameSet must contain at least 1 frame.", nameof(frameSet));
        }

        var totalSw = Stopwatch.StartNew();
        var benchmark = new BenchmarkReport
        {
            FrameCount = frameSet.TotalFrames,
            Width = frameSet.Width,
            Height = frameSet.Height,
            FusionMethod = config.FusionMethod,
            FocusMethod = config.FocusMeasureMethod
        };

        var qualityReport = new MacroQualityReport
        {
            TotalFrames = frameSet.TotalFrames
        };

        // -------------------------------------------------------------
        // Stage 1: Quality Scoring & Intelligent Frame Culling
        // -------------------------------------------------------------
        progress?.Report(new StackProgress("Macro Quality Assessment", 10, "Evaluating frame sharpness and culling blurry shots..."));
        cancellationToken.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();

        double maxSharpness = 0.0;
        foreach (var frame in frameSet.Frames)
        {
            EnsureGrayscaleAndFocusMap(frame);
            if (frame.SharpnessScore > maxSharpness)
            {
                maxSharpness = frame.SharpnessScore;
            }
        }

        int culledCount = 0;
        double sumSharpness = 0;
        foreach (var frame in frameSet.Frames)
        {
            sumSharpness += frame.SharpnessScore;

            if (config.AutoCullBlurFrames && frameSet.TotalFrames > 2)
            {
                double threshold = maxSharpness * config.MinSharpnessRatio;
                if (frame.SharpnessScore < threshold)
                {
                    frame.IsCulled = true;
                    frame.CullReason = $"Sharpness score ({frame.SharpnessScore:F3}) below {config.MinSharpnessRatio * 100:F0}% peak ({threshold:F3})";
                    culledCount++;
                    qualityReport.DiagnosticNotes.Add($"Frame {frame.Index} culled: {frame.CullReason}");
                }
            }
        }

        // Safety fallback: If all frames were culled, un-cull the sharpest frame
        if (frameSet.ActiveFramesCount == 0)
        {
            var bestFrame = frameSet.Frames.OrderByDescending(f => f.SharpnessScore).First();
            bestFrame.IsCulled = false;
            bestFrame.CullReason = string.Empty;
            culledCount--;
        }

        qualityReport.ActiveFrames = frameSet.ActiveFramesCount;
        qualityReport.CulledFrames = culledCount;
        qualityReport.AverageSharpness = sumSharpness / Math.Max(1, frameSet.TotalFrames);

        sw.Stop();
        benchmark.QualityAnalysisTimeMs = sw.Elapsed.TotalMilliseconds;

        var activeFrames = frameSet.ActiveFrames;

        // -------------------------------------------------------------
        // Stage 2: Optical Breathing & Multi-Frame Alignment
        // -------------------------------------------------------------
        progress?.Report(new StackProgress("Macro Alignment", 30, $"Aligning {activeFrames.Count} active frames and correcting focus breathing..."));
        cancellationToken.ThrowIfCancellationRequested();
        sw.Restart();

        var stackFrames = new List<CoreStackFrame>(activeFrames.Count);
        foreach (var f in activeFrames)
        {
            stackFrames.Add(f.ToStackFrame());
        }

        if (activeFrames.Count > 1 && config.AlignmentMode != AlignmentMode.None)
        {
            _alignmentEngine.AlignStack(
                stackFrames,
                mode: config.AlignmentMode,
                correctFocusBreathing: config.EnableFocusBreathingCorrection,
                enableLocalAlignment: false,
                progress: progress);

            for (int i = 0; i < activeFrames.Count; i++)
            {
                activeFrames[i].FocusBreathingScale = stackFrames[i].FocusBreathingScale;
                activeFrames[i].AlignmentHomography = stackFrames[i].AlignmentHomography;
            }
        }

        sw.Stop();
        benchmark.AlignmentTimeMs = sw.Elapsed.TotalMilliseconds;

        // -------------------------------------------------------------
        // Stage 3: Depth Map Estimation & Sub-Part Focus Fusion
        // -------------------------------------------------------------
        progress?.Report(new StackProgress("Macro Focus Fusion", 60, "Executing seamless sub-part focus fusion..."));
        cancellationToken.ThrowIfCancellationRequested();
        sw.Restart();

        var depthResult = _depthMapEstimator.EstimateDepthMap(stackFrames, enableSmoothing: true, smoothRadius: 2);
        qualityReport.EstimatedDofCoverage = CalculateDofCoverage(depthResult);

        var fusionSettings = new FusionSettings
        {
            Method = config.FusionMethod,
            FocusMethod = config.FocusMeasureMethod,
            AlignmentMode = config.AlignmentMode,
            EnableDepthSmoothing = true
        };

        var fusionEngine = CreateFusionEngine(config.FusionMethod);
        var fusedImage = fusionEngine.Fuse(stackFrames, depthResult, fusionSettings);

        sw.Stop();
        benchmark.FusionTimeMs = sw.Elapsed.TotalMilliseconds;

        // -------------------------------------------------------------
        // Stage 4: Micro-Detail Enhancement & Restoration
        // -------------------------------------------------------------
        if (config.EnableMicroDetailRecovery || config.EnableDeconvolution)
        {
            progress?.Report(new StackProgress("Micro-Detail Enhancement", 85, "Restoring micro-contrast and edge details..."));
            cancellationToken.ThrowIfCancellationRequested();
            sw.Restart();

            if (config.EnableMicroDetailRecovery && config.MicroDetailStrength > 0.01f)
            {
                ApplyMicroContrastBoost(fusedImage, config.MicroDetailStrength);
            }

            if (config.EnableDeconvolution)
            {
                using var psf = PsfGenerator.CreatePsf(PsfKernelType.Gaussian, radius: 1.2f);
                var deconvOptions = new DeconvolutionOptions
                {
                    Iterations = Math.Max(1, config.DeconvolutionIterations),
                    TvDampingWeight = 0.001f
                };
                var deconvolved = _richardsonLucyEngine.Deconvolve(fusedImage, psf, deconvOptions);
                fusedImage.Dispose();
                fusedImage = deconvolved;
            }

            sw.Stop();
            benchmark.AutoRepairTimeMs = sw.Elapsed.TotalMilliseconds;
        }

        totalSw.Stop();
        benchmark.TotalTimeMs = totalSw.Elapsed.TotalMilliseconds;
        benchmark.PeakWorkingSetMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

        progress?.Report(new StackProgress("Completed", 100, $"Macro stack complete in {benchmark.TotalTimeMs:F0}ms"));

        return new MacroStackResult
        {
            FusedImage = fusedImage,
            DepthMap = depthResult,
            QualityReport = qualityReport,
            Benchmark = benchmark
        };
    }

    private unsafe void EnsureGrayscaleAndFocusMap(MacroFrame frame)
    {
        int width = frame.Width;
        int height = frame.Height;

        // 1. Create Grayscale if missing
        if (frame.GrayBuffer == null && frame.ColorBuffer != null)
        {
            frame.GrayBuffer = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
            float* src = frame.ColorBuffer.DataPointer;
            float* dst = frame.GrayBuffer.DataPointer;

            Parallel.For(0, height, y =>
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    float r = src[idx * 3];
                    float g = src[idx * 3 + 1];
                    float b = src[idx * 3 + 2];
                    dst[idx] = 0.299f * r + 0.587f * g + 0.114f * b;
                }
            });
        }

        // 2. Create FocusMap if missing
        if (frame.FocusMap == null && frame.GrayBuffer != null)
        {
            frame.FocusMap = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
            _focusMeasureEngine.ComputeFocusMap(frame.GrayBuffer, frame.FocusMap, windowRadius: 2);
        }

        // 3. Compute Sharpness Score (mean energy)
        if (frame.FocusMap != null)
        {
            float* focusPtr = frame.FocusMap.DataPointer;
            int total = frame.FocusMap.TotalElements;
            double sum = 0.0;
            for (int i = 0; i < total; i++)
            {
                sum += focusPtr[i];
            }
            frame.SharpnessScore = sum / Math.Max(1, total);
        }
    }

    private static unsafe float CalculateDofCoverage(DepthMapResult depthResult)
    {
        int total = depthResult.ConfidenceMap.TotalElements;
        float* conf = depthResult.ConfidenceMap.DataPointer;
        int sharpPixels = 0;

        for (int i = 0; i < total; i++)
        {
            if (conf[i] > 0.35f)
            {
                sharpPixels++;
            }
        }

        return (float)sharpPixels / Math.Max(1, total);
    }

    private static unsafe void ApplyMicroContrastBoost(ImageBuffer<float> image, float strength)
    {
        int width = image.Width;
        int height = image.Height;
        int channels = image.Channels;
        float* ptr = image.DataPointer;

        // Unsharp masking filter (Laplacian high-pass micro-boost)
        using var blurred = image.Clone();
        float* blurPtr = blurred.DataPointer;

        // 3x3 Box Blur
        Parallel.For(1, height - 1, y =>
        {
            for (int x = 1; x < width - 1; x++)
            {
                for (int c = 0; c < channels; c++)
                {
                    float sum = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            sum += ptr[((y + dy) * width + (x + dx)) * channels + c];
                        }
                    }
                    blurPtr[(y * width + x) * channels + c] = sum / 9f;
                }
            }
        });

        // Boost high-frequency detail: I_out = I + strength * (I - I_blur)
        Parallel.For(0, image.TotalElements, i =>
        {
            float diff = ptr[i] - blurPtr[i];
            ptr[i] = Math.Clamp(ptr[i] + strength * diff, 0f, 1f);
        });
    }

    private static IFusionEngine CreateFusionEngine(FusionMethod method)
    {
        return method switch
        {
            FusionMethod.WinnerTakesAll => new WinnerTakesAllFusionEngine(),
            FusionMethod.FocusWeighted => new FocusWeightedFusionEngine(),
            FusionMethod.MultiScalePyramid => new MultiScalePyramidFusionEngine(),
            FusionMethod.WaveletDWT => new WaveletFusionEngine(),
            FusionMethod.RegionAdaptive => new RegionAdaptiveFusionEngine(),
            FusionMethod.ConfidenceWeighted => new ConfidenceWeightedFusionEngine(),
            FusionMethod.OcclusionAware => new OcclusionAwareFusionEngine(),
            _ => new RegionAdaptiveFusionEngine()
        };
    }
}
