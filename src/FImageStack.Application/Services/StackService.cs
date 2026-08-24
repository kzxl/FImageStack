using System.Diagnostics;
using FImageStack.Core;
using FImageStack.Core.Alignment;
using FImageStack.Core.Artifact;
using FImageStack.Core.DepthMap;
using FImageStack.Core.FocusMeasure;
using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using FImageStack.Core.Motion;
using FImageStack.Core.Quality;
using FImageStack.Core.Reconstruction;
using FImageStack.Core.SuperResolution;
using FImageStack.Core.Tiling;
using FImageStack.Infrastructure.IO;
using StackFrame = FImageStack.Core.Models.StackFrame;

namespace FImageStack.Application.Services;

public interface IStackService
{
    Task<ProcessedStackResult> ProcessStackAsync(
        IReadOnlyList<string> filePaths,
        FusionSettings settings,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class StackService : IStackService
{
    private readonly IImageIO _imageIO;
    private readonly IAlignmentEngine _alignmentEngine;
    private readonly IDepthMapEstimator _depthMapEstimator;
    private readonly IMotionDetector _motionDetector;
    private readonly IStackQualityAnalyzer _qualityAnalyzer;
    private readonly IArtifactDetector _artifactDetector;
    private readonly IAutoRepairEngine _autoRepairEngine;
    private readonly IEdgeFusionEngine _edgeFusionEngine;
    private readonly ITiledProcessor _tiledProcessor;
    private readonly ITemporalDenoiseEngine _temporalDenoiseEngine;
    private readonly IMultiFrameSuperResolutionEngine _superResolutionEngine;
    private readonly IOptimalFrameRangeSelector _optimalRangeSelector;

    public StackService(
        IImageIO imageIO,
        IAlignmentEngine? alignmentEngine = null,
        IDepthMapEstimator? depthMapEstimator = null,
        IMotionDetector? motionDetector = null,
        IStackQualityAnalyzer? qualityAnalyzer = null,
        IArtifactDetector? artifactDetector = null,
        IAutoRepairEngine? autoRepairEngine = null,
        IEdgeFusionEngine? edgeFusionEngine = null,
        ITiledProcessor? tiledProcessor = null,
        ITemporalDenoiseEngine? temporalDenoiseEngine = null,
        IMultiFrameSuperResolutionEngine? superResolutionEngine = null,
        IOptimalFrameRangeSelector? optimalRangeSelector = null)
    {
        _imageIO = imageIO;
        _alignmentEngine = alignmentEngine ?? new AdvancedAlignmentEngine();
        _depthMapEstimator = depthMapEstimator ?? new StandardDepthMapEstimator();
        _motionDetector = motionDetector ?? new FrameDifferenceMotionDetector();
        _qualityAnalyzer = qualityAnalyzer ?? new StandardStackQualityAnalyzer();
        _artifactDetector = artifactDetector ?? new StandardArtifactDetector();
        _autoRepairEngine = autoRepairEngine ?? new StandardAutoRepairEngine();
        _edgeFusionEngine = edgeFusionEngine ?? new EdgeFusionEngine();
        _tiledProcessor = tiledProcessor ?? new StandardTiledProcessor();
        _temporalDenoiseEngine = temporalDenoiseEngine ?? new TemporalDenoiseEngine();
        _superResolutionEngine = superResolutionEngine ?? new MultiFrameSuperResolutionEngine();
        _optimalRangeSelector = optimalRangeSelector ?? new OptimalFrameRangeSelector();
    }

    public async Task<ProcessedStackResult> ProcessStackAsync(
        IReadOnlyList<string> filePaths,
        FusionSettings settings,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ProcessedStackResult();
        var benchmark = new BenchmarkReport
        {
            FrameCount = filePaths.Count,
            FusionMethod = settings.Method,
            FocusMethod = settings.FocusMethod
        };
        result.Benchmark = benchmark;

        var totalStopwatch = Stopwatch.StartNew();
        var sw = new Stopwatch();

        // 1. Load Images
        progress?.Report(new StackProgress("Loading Frames", 0, $"Loading {filePaths.Count} frames..."));
        sw.Restart();

        int maxDim = settings.RenderMode == ResolutionMode.FastPreview1280 ? Math.Max(512, settings.PreviewMaxDimension) : 0;
        var frames = new List<StackFrame>(filePaths.Count);
        for (int i = 0; i < filePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await Task.Run(() => _imageIO.LoadFrame(filePaths[i], i, maxDim), cancellationToken);
            frames.Add(frame);
            progress?.Report(new StackProgress("Loading Frames", (double)(i + 1) / filePaths.Count * 100, $"Loaded {Path.GetFileName(filePaths[i])}"));
        }

        sw.Stop();
        benchmark.LoadTimeMs = sw.Elapsed.TotalMilliseconds;
        benchmark.Width = frames[0].Width;
        benchmark.Height = frames[0].Height;

        try
        {
            // 1.5 Automatic Optimal Frame Range (Frame Culling)
            if (settings.EnableAutoFrameSelection && frames.Count > 3)
            {
                progress?.Report(new StackProgress("Frame Selection", 0, "Analyzing focus envelope & culling outliers..."));
                var rangeResult = _optimalRangeSelector.AnalyzeOptimalRange(frames);
                progress?.Report(new StackProgress("Frame Selection", 100, rangeResult.Summary));

                if (rangeResult.SelectedIndices.Count >= 2 && rangeResult.SelectedIndices.Count < frames.Count)
                {
                    var selectedFrames = new List<StackFrame>(rangeResult.SelectedIndices.Count);
                    var selectedSet = new HashSet<int>(rangeResult.SelectedIndices);

                    for (int i = 0; i < frames.Count; i++)
                    {
                        if (selectedSet.Contains(i))
                        {
                            selectedFrames.Add(frames[i]);
                        }
                        else
                        {
                            frames[i].Dispose();
                        }
                    }
                    frames = selectedFrames;
                }
            }

            // 2. Alignment & Sub-Pixel Warp
            cancellationToken.ThrowIfCancellationRequested();
            sw.Restart();
            _alignmentEngine.AlignStack(
                frames,
                settings.AlignmentMode,
                settings.EnableFocusBreathingCorrection,
                settings.EnableLocalAlignment,
                settings.LocalAlignmentGridSize,
                settings.LensDistortion,
                progress);
            sw.Stop();
            benchmark.AlignmentTimeMs = sw.Elapsed.TotalMilliseconds;

            // 3. Motion Detection (Phase 9)
            if (settings.EnableMotionSuppression)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sw.Restart();
                progress?.Report(new StackProgress("Motion Detection", 0, "Analyzing motion across frames..."));
                result.MotionResult = _motionDetector.DetectMotion(frames);
                progress?.Report(new StackProgress("Motion Detection", 100, $"Motion analyzed ({result.MotionResult.OverallMotionPercentage:F1}% dynamic)"));
                sw.Stop();
                benchmark.MotionTimeMs = sw.Elapsed.TotalMilliseconds;
            }

            // 3.5 Temporal Noise Reduction (Multi-Frame SNR Boost)
            if (settings.EnableTemporalDenoising)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sw.Restart();
                progress?.Report(new StackProgress("Temporal Denoise", 0, "Applying motion-adaptive multi-frame denoising..."));
                _temporalDenoiseEngine.DenoiseStack(frames, result.MotionResult, settings.DenoiseStrength, progress);
                sw.Stop();
            }

            // 4. Focus Measure Calculation
            cancellationToken.ThrowIfCancellationRequested();
            sw.Restart();
            progress?.Report(new StackProgress("Focus Measure", 0, "Computing sharpness maps..."));

            IFocusMeasureEngine focusEngine = settings.FocusMethod switch
            {
                FocusMeasureMethod.Tenengrad => new TenengradFocusMeasure(),
                FocusMeasureMethod.LocalVariance => new LocalVarianceFocusMeasure(),
                FocusMeasureMethod.Wavelet => new WaveletSharpnessMeasure(),
                _ => new ModifiedLaplacianFocusMeasure()
            };

            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var f = frames[i];
                f.FocusMap = new ImageBuffer<float>(f.Width, f.Height, 1);
                focusEngine.ComputeFocusMap(f.GrayBuffer!, f.FocusMap, settings.SmoothingRadius);
                progress?.Report(new StackProgress("Focus Measure", (double)(i + 1) / frames.Count * 100, $"Sharpness calculated for frame {i + 1}/{frames.Count}"));
            }

            sw.Stop();
            benchmark.FocusMeasureTimeMs = sw.Elapsed.TotalMilliseconds;

            // 5. Depth Map Estimation
            cancellationToken.ThrowIfCancellationRequested();
            sw.Restart();
            progress?.Report(new StackProgress("Depth Estimation", 0, "Estimating continuous depth map..."));

            result.DepthResult = _depthMapEstimator.EstimateDepthMap(frames, settings.EnableDepthSmoothing, settings.SmoothingRadius);
            progress?.Report(new StackProgress("Depth Estimation", 100, "Depth map computed."));

            sw.Stop();
            benchmark.DepthMapTimeMs = sw.Elapsed.TotalMilliseconds;

            // 6. Fusion Stage
            cancellationToken.ThrowIfCancellationRequested();
            sw.Restart();

            IFusionEngine fusionEngine = settings.Method switch
            {
                FusionMethod.WinnerTakesAll => new WinnerTakesAllFusionEngine(),
                FusionMethod.FocusWeighted => new FocusWeightedFusionEngine(),
                FusionMethod.ConfidenceWeighted => new ConfidenceWeightedFusionEngine(),
                FusionMethod.OcclusionAware => new OcclusionAwareFusionEngine(),
                FusionMethod.WaveletDWT => new WaveletFusionEngine(),
                FusionMethod.HDRFocusExposure => new ExposureFusionEngine(),
                _ => new MultiScalePyramidFusionEngine()
            };

            if (settings.EnableTiledProcessing)
            {
                progress?.Report(new StackProgress("Tiled Fusion", 0, $"Fusing in {settings.TileSize}x{settings.TileSize} memory-bounded tiles..."));
                result.FusedImage = await Task.Run(() => _tiledProcessor.ProcessTiled(frames, result.DepthResult, fusionEngine, settings, settings.TileSize, 64, progress), cancellationToken);
            }
            else
            {
                progress?.Report(new StackProgress("Focus Fusion", 0, $"Applying {settings.Method} fusion..."));
                result.FusedImage = await Task.Run(() => fusionEngine.Fuse(frames, result.DepthResult, settings), cancellationToken);
            }

            progress?.Report(new StackProgress("Focus Fusion", 100, "Fusion complete."));
            sw.Stop();
            benchmark.FusionTimeMs = sw.Elapsed.TotalMilliseconds;

            // 7.5 Multi-Frame Super-Resolution (MFSR)
            if (settings.EnableSuperResolution && result.FusedImage != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sw.Restart();
                progress?.Report(new StackProgress("Super Resolution", 0, $"Reconstructing {settings.SuperResolutionParams.ScaleFactor}x Super-Resolution image..."));
                var hrImage = await Task.Run(() => _superResolutionEngine.ReconstructSuperResolution(frames, result.FusedImage, settings.SuperResolutionParams, progress), cancellationToken);
                result.FusedImage.Dispose();
                result.FusedImage = hrImage;
                sw.Stop();
            }

            // 8. Artifact Detection (Phase 7)
            if (settings.EnableArtifactDetection)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sw.Restart();
                progress?.Report(new StackProgress("Artifact Detection", 0, "Scanning for halos, ghosts, and seams..."));
                result.ArtifactMap = _artifactDetector.DetectArtifacts(result.FusedImage, frames, result.DepthResult);
                progress?.Report(new StackProgress("Artifact Detection", 100, $"Detected {result.ArtifactMap.Regions.Count} artifact regions"));
                sw.Stop();
                benchmark.ArtifactDetectionTimeMs = sw.Elapsed.TotalMilliseconds;
            }

            // 9. Auto Repair (Phase 8)
            if (settings.EnableAutoRepair && result.ArtifactMap != null && result.ArtifactMap.Regions.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sw.Restart();
                progress?.Report(new StackProgress("Auto Reconstruction", 0, "Repairing artifacts from source frames..."));
                var (repaired, repairReport) = _autoRepairEngine.AutoRepair(result.FusedImage, frames, result.ArtifactMap);
                result.RepairedImage = repaired;
                result.RepairReport = repairReport;
                progress?.Report(new StackProgress("Auto Reconstruction", 100, $"Auto-repaired {repairReport.RepairedRegionsCount} regions"));
                sw.Stop();
                benchmark.AutoRepairTimeMs = sw.Elapsed.TotalMilliseconds;
            }

            // 10. Edge Discontinuity Reconstruction
            if (settings.EnableEdgeReconstruction)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sw.Restart();
                progress?.Report(new StackProgress("Edge Reconstruction", 0, "Reconstructing edge discontinuities..."));
                var currentImg = result.RepairedImage ?? result.FusedImage;
                using var edgeRes = _edgeFusionEngine.ReconstructEdges(currentImg, frames, result.DepthResult.SourceFrameMap);
                if (edgeRes.ReconstructedEdgeCount > 0)
                {
                    result.RepairedImage?.Dispose();
                    result.RepairedImage = edgeRes.ReconstructedImage.Clone();
                }
                progress?.Report(new StackProgress("Edge Reconstruction", 100, $"Reconstructed {edgeRes.ReconstructedEdgeCount} edge pixels"));
                sw.Stop();
            }

            // 11. Comprehensive Quality & Multi-Metric Analysis
            if (settings.EnableQualityAnalysis)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sw.Restart();
                progress?.Report(new StackProgress("Quality Analysis", 0, "Evaluating multi-metric stack quality..."));
                result.QualityReport = _qualityAnalyzer.AnalyzeQuality(frames, result.DepthResult, result.ArtifactMap, result.MotionResult);
                progress?.Report(new StackProgress("Quality Analysis", 100, $"Quality: {result.QualityReport.OverallScore:F1}% ({result.QualityReport.FocusCoverageRating})"));
                sw.Stop();
                benchmark.QualityAnalysisTimeMs = sw.Elapsed.TotalMilliseconds;
            }

            totalStopwatch.Stop();
            benchmark.TotalTimeMs = totalStopwatch.Elapsed.TotalMilliseconds;
            benchmark.PeakWorkingSetMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

            return result;
        }
        finally
        {
            // Dispose source frames to ensure zero memory leak
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }
}
