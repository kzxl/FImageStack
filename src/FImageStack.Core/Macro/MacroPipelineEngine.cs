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
        // Stage 1: Quality Scoring & Unique In-Focus Contribution Assessment
        // -------------------------------------------------------------
        progress?.Report(new StackProgress("Macro Quality Assessment", 10, "Evaluating frame sharpness and unique in-focus contributions..."));
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

        // Calculate unique in-focus region dominance for each frame
        int totalPixels = frameSet.Width * frameSet.Height;
        int frameCount = frameSet.TotalFrames;
        int[] winCounts = new int[frameCount];
        float*[] focusPointers = new float*[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            focusPointers[i] = frameSet.Frames[i].FocusMap!.DataPointer;
        }

        Parallel.For(0, frameSet.Height, y =>
        {
            int rowOffset = y * frameSet.Width;
            for (int x = 0; x < frameSet.Width; x++)
            {
                int pIdx = rowOffset + x;
                float maxVal = 0.0005f; // noise floor
                int bestF = -1;

                for (int f = 0; f < frameCount; f++)
                {
                    float val = focusPointers[f][pIdx];
                    if (val > maxVal)
                    {
                        maxVal = val;
                        bestF = f;
                    }
                }

                if (bestF >= 0)
                {
                    Interlocked.Increment(ref winCounts[bestF]);
                }
            }
        });

        int culledCount = 0;
        double sumSharpness = 0;

        for (int i = 0; i < frameCount; i++)
        {
            var frame = frameSet.Frames[i];
            sumSharpness += frame.SharpnessScore;

            double uniqueAreaPct = (double)winCounts[i] / Math.Max(1, totalPixels) * 100.0;
            double relSharpPct = maxSharpness > 0 ? (frame.SharpnessScore / maxSharpness) * 100.0 : 0.0;

            var assessment = new MacroFrameAssessment
            {
                FrameIndex = frame.Index,
                FrameLabel = string.IsNullOrEmpty(frame.Label) ? $"Frame #{frame.Index + 1}" : Path.GetFileName(frame.Label),
                MeanSharpness = frame.SharpnessScore,
                RelativeSharpnessPercent = relSharpPct,
                UniqueContributionPercent = uniqueAreaPct
            };

            if (config.AutoCullBlurFrames && frameCount > 2)
            {
                double threshold = maxSharpness * config.MinSharpnessRatio;
                if (frame.SharpnessScore < threshold)
                {
                    frame.IsCulled = true;
                    frame.CullReason = $"Low sharpness ({relSharpPct:F1}% of peak < {config.MinSharpnessRatio * 100:F0}%)";
                    assessment.IsCulled = true;
                    assessment.CullReason = frame.CullReason;
                    assessment.Recommendation = "❌ Exclude (Blurry out-of-focus deadband)";
                    culledCount++;
                    qualityReport.DiagnosticNotes.Add($"Frame {frame.Index + 1} excluded: {frame.CullReason}");
                }
                else if (uniqueAreaPct < 0.40 && frameCount > 4)
                {
                    frame.IsCulled = true;
                    frame.CullReason = $"Redundant slice (only {uniqueAreaPct:F2}% unique sharp area)";
                    assessment.IsCulled = true;
                    assessment.CullReason = frame.CullReason;
                    assessment.Recommendation = "❌ Exclude (Redundant / Overlapped)";
                    culledCount++;
                    qualityReport.DiagnosticNotes.Add($"Frame {frame.Index + 1} excluded: {frame.CullReason}");
                }
                else
                {
                    assessment.IsCulled = false;
                    assessment.Recommendation = $"✅ Keep ({uniqueAreaPct:F1}% unique in-focus detail)";
                }
            }
            else
            {
                assessment.IsCulled = false;
                assessment.Recommendation = "✅ Keep (Manual mode)";
            }

            qualityReport.FrameAssessments.Add(assessment);
        }

        // Safety fallback: Ensure at least 2 sharpest frames remain active
        if (frameSet.ActiveFramesCount < Math.Min(2, frameCount))
        {
            var bestFrames = frameSet.Frames.OrderByDescending(f => f.SharpnessScore).Take(2);
            foreach (var bf in bestFrames)
            {
                if (bf.IsCulled)
                {
                    bf.IsCulled = false;
                    bf.CullReason = string.Empty;
                    var ass = qualityReport.FrameAssessments.FirstOrDefault(a => a.FrameIndex == bf.Index);
                    if (ass != null)
                    {
                        ass.IsCulled = false;
                        ass.Recommendation = "✅ Keep (Rescued by minimum frame policy)";
                    }
                    culledCount--;
                }
            }
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

            // CRITICAL: Recompute Focus Maps on warped aligned frames so that sharpness measurements perfectly match pixel coordinates
            for (int i = 0; i < stackFrames.Count; i++)
            {
                if (stackFrames[i].GrayBuffer != null)
                {
                    stackFrames[i].FocusMap ??= new ImageBuffer<float>(frameSet.Width, frameSet.Height, 1, PixelFormatType.GrayFloat32);
                    _focusMeasureEngine.ComputeFocusMap(stackFrames[i].GrayBuffer!, stackFrames[i].FocusMap!, windowRadius: 2);
                }
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
