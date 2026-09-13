using System.Diagnostics;
using FImageStack.Core;
using FImageStack.Core.Acceleration;
using FImageStack.Core.Alignment;
using FImageStack.Core.Artifact;
using FImageStack.Core.Astro;
using FImageStack.Core.Depth3D;
using FImageStack.Core.DepthMap;
using FImageStack.Core.FocusMeasure;
using FImageStack.Core.Fusion;
using FImageStack.Core.Hdr;
using FImageStack.Core.Models;
using FImageStack.Core.Motion;
using FImageStack.Core.Noise;
using FImageStack.Core.Quality;
using FImageStack.Core.Raw;
using FImageStack.Core.Reconstruction;
using FImageStack.Core.Restoration;
using FImageStack.Core.SuperResolution;
using FImageStack.Core.SuperResolution.Drizzle;
using FImageStack.Core.Stitching;
using FImageStack.Core.Tiling;
using FImageStack.Infrastructure.IO;
using StackFrame = FImageStack.Core.Models.StackFrame;

namespace FImageStack.Application.Services;

public sealed class AstroCalibrationPathSets
{
    public IReadOnlyList<string>? DarkPaths { get; set; }
    public IReadOnlyList<string>? FlatPaths { get; set; }
    public IReadOnlyList<string>? BiasPaths { get; set; }
}

public interface IStackService
{
    Task<ProcessedStackResult> ProcessStackAsync(
        IReadOnlyList<string> filePaths,
        FusionSettings settings,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<NoiseStackResult> ProcessNoiseStackAsync(
        IReadOnlyList<string> filePaths,
        NoiseStackSettings settings,
        AlignmentMode alignmentMode = AlignmentMode.Similarity,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<HdrStackResult> ProcessHdrStackAsync(
        IReadOnlyList<string> filePaths,
        HdrStackSettings settings,
        AlignmentMode alignmentMode = AlignmentMode.Similarity,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<AstroStackResult> ProcessAstroStackAsync(
        IReadOnlyList<string> lightPaths,
        AstroStackSettings settings,
        AstroCalibrationPathSets? calibrationPaths = null,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<RawStackResult> ProcessRawStackAsync(
        IReadOnlyList<RawBayerBuffer> rawFrames,
        RawStackSettings settings,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<DrizzleResult> ProcessDrizzleSuperResAsync(
        IReadOnlyList<string> filePaths,
        DrizzleSettings settings,
        AlignmentMode alignmentMode = AlignmentMode.Similarity,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<StitchResult> ProcessMosaicStitchAsync(
        IReadOnlyList<string> filePaths,
        StitchSettings settings,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ImageBuffer<float>> DeconvolveImageAsync(
        ImageBuffer<float> input,
        DeconvolutionOptions options);

    Task<DehazeResult> DehazeImageAsync(
        ImageBuffer<float> input,
        DehazeOptions options);

    Task ExportDepthMeshAsync(
        ImageBuffer<float> depthMap,
        ImageBuffer<float>? colorMap,
        string outputPath,
        DepthMeshOptions options);
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
    private readonly IStackSimulationEngine _simulationEngine;
    private readonly IFocusGapDetector _focusGapDetector;
    private readonly INoiseStackEngine _noiseStackEngine;
    private readonly IHdrStackEngine _hdrStackEngine;
    private readonly IDepthMeshExporter _depthMeshExporter;
    private readonly IAstroStackEngine _astroStackEngine;
    private readonly IRichardsonLucyEngine _richardsonLucyEngine;
    private readonly IDehazeEngine _dehazeEngine;
    private readonly IBayerFusionEngine _bayerFusionEngine;
    private readonly IDemosaicEngine _demosaicEngine;
    private readonly IDrizzleEngine _drizzleEngine;
    private readonly IMosaicStitchEngine _mosaicStitchEngine;
    private readonly IGpuAccelerationEngine _gpuEngine;

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
        IOptimalFrameRangeSelector? optimalRangeSelector = null,
        IStackSimulationEngine? simulationEngine = null,
        IFocusGapDetector? focusGapDetector = null,
        INoiseStackEngine? noiseStackEngine = null,
        IHdrStackEngine? hdrStackEngine = null,
        IDepthMeshExporter? depthMeshExporter = null,
        IAstroStackEngine? astroStackEngine = null,
        IRichardsonLucyEngine? richardsonLucyEngine = null,
        IDehazeEngine? dehazeEngine = null,
        IBayerFusionEngine? bayerFusionEngine = null,
        IDemosaicEngine? demosaicEngine = null,
        IDrizzleEngine? drizzleEngine = null,
        IMosaicStitchEngine? mosaicStitchEngine = null,
        IGpuAccelerationEngine? gpuEngine = null)
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
        _simulationEngine = simulationEngine ?? new StackSimulationEngine();
        _focusGapDetector = focusGapDetector ?? new FocusGapDetector();
        _noiseStackEngine = noiseStackEngine ?? new NoiseStackEngine();
        _hdrStackEngine = hdrStackEngine ?? new HdrStackEngine();
        _depthMeshExporter = depthMeshExporter ?? new DepthMeshExporter();
        _astroStackEngine = astroStackEngine ?? new AstroStackEngine();
        _richardsonLucyEngine = richardsonLucyEngine ?? new RichardsonLucyEngine();
        _dehazeEngine = dehazeEngine ?? new DehazeEngine();
        _bayerFusionEngine = bayerFusionEngine ?? new BayerFusionEngine();
        _demosaicEngine = demosaicEngine ?? new EdgeDirectedDemosaicEngine();
        _drizzleEngine = drizzleEngine ?? new DrizzleEngine();
        _mosaicStitchEngine = mosaicStitchEngine ?? new MosaicStitchEngine();
        _gpuEngine = gpuEngine ?? new StandardGpuAccelerationEngine();
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
            try
            {
                var frame = await Task.Run(() => _imageIO.LoadFrame(filePaths[i], i, maxDim), cancellationToken);
                frames.Add(frame);
                progress?.Report(new StackProgress("Loading Frames", (double)(i + 1) / filePaths.Count * 100, $"Loaded {Path.GetFileName(filePaths[i])}"));
            }
            catch (Exception ex)
            {
                progress?.Report(new StackProgress("Loading Frames", (double)(i + 1) / filePaths.Count * 100, $"Skipped unreadable {Path.GetFileName(filePaths[i])}: {ex.Message}"));
            }
        }

        if (frames.Count < 2)
            throw new InvalidOperationException("At least 2 readable image frames are required to perform focus stacking.");

        int targetW = frames[0].Width;
        int targetH = frames[0].Height;
        if (frames.Any(f => f.Width != targetW || f.Height != targetH))
        {
            progress?.Report(new StackProgress("Normalizing Frames", 0, "Resampling heterogeneous frame resolutions..."));
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                if (f.Width != targetW || f.Height != targetH)
                {
                    NormalizeFrameResolution(f, targetW, targetH);
                }
            }
            progress?.Report(new StackProgress("Normalizing Frames", 100, $"All frames normalized to {targetW}x{targetH}"));
        }

        sw.Stop();
        benchmark.LoadTimeMs = sw.Elapsed.TotalMilliseconds;
        benchmark.Width = targetW;
        benchmark.Height = targetH;

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

            // 1.8 Stack Depth Simulation (Focus Gap Analysis)
            if (settings.EnableStackSimulation && frames.Count >= 2)
            {
                progress?.Report(new StackProgress("Stack Simulation", 0, "Simulating continuous depth coverage..."));
                result.SimulationResult = _simulationEngine.SimulateDepthCoverage(frames);
                progress?.Report(new StackProgress("Stack Simulation", 100, $"{result.SimulationResult.CoverageBarAscii} | {result.SimulationResult.Recommendation}"));
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

            bool isGpuActive = settings.EnableGpuAcceleration && _gpuEngine.GetCurrentDevice().IsHardwareAccelerated;

            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var f = frames[i];
                if (isGpuActive)
                {
                    f.FocusMap = _gpuEngine.ComputeFocusMeasureGpu(f.GrayBuffer!, settings.FocusMethod);
                }
                else
                {
                    f.FocusMap = new ImageBuffer<float>(f.Width, f.Height, 1);
                    focusEngine.ComputeFocusMap(f.GrayBuffer!, f.FocusMap, settings.SmoothingRadius);
                }
                progress?.Report(new StackProgress("Focus Measure", (double)(i + 1) / frames.Count * 100, $"Sharpness calculated for frame {i + 1}/{frames.Count}"));
            }

            sw.Stop();
            benchmark.FocusMeasureTimeMs = sw.Elapsed.TotalMilliseconds;

            // 4.5 Inter-Frame Focus Gap Detection
            if (frames.Count >= 2)
            {
                result.FocusGapReport = _focusGapDetector.DetectInterFrameGaps(frames);
                if (result.FocusGapReport.HasLargeGaps)
                {
                    progress?.Report(new StackProgress("Focus Gap Analysis", 100, result.FocusGapReport.Summary));
                }
            }

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
                FusionMethod.RegionAdaptive => new RegionAdaptiveFusionEngine(),
                _ => new MultiScalePyramidFusionEngine()
            };

            if (isGpuActive && settings.Method == FusionMethod.WinnerTakesAll && !settings.EnableTiledProcessing)
            {
                progress?.Report(new StackProgress("Focus Fusion", 0, "Applying Direct3D 11 GPU hardware fusion..."));
                result.FusedImage = await Task.Run(() => _gpuEngine.FuseStackGpu(frames.Select(f => f.ColorBuffer!).ToList()), cancellationToken);
            }
            else if (settings.EnableTiledProcessing)
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

    public async Task<NoiseStackResult> ProcessNoiseStackAsync(
        IReadOnlyList<string> filePaths,
        NoiseStackSettings settings,
        AlignmentMode alignmentMode = AlignmentMode.Similarity,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths == null || filePaths.Count == 0)
            throw new ArgumentException("At least 1 file path is required.", nameof(filePaths));

        progress?.Report(new StackProgress("Loading Frames", 0, $"Loading {filePaths.Count} frames for noise reduction..."));
        var frames = new List<StackFrame>(filePaths.Count);

        for (int i = 0; i < filePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await Task.Run(() => _imageIO.LoadFrame(filePaths[i], i), cancellationToken);
            frames.Add(frame);
            progress?.Report(new StackProgress("Loading Frames", (double)(i + 1) / filePaths.Count * 100, $"Loaded {Path.GetFileName(filePaths[i])}"));
        }

        try
        {
            if (alignmentMode != AlignmentMode.None && frames.Count > 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new StackProgress("Alignment", 0, "Aligning frames..."));
                _alignmentEngine.AlignStack(frames, alignmentMode, false, false, 8, default, progress);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new StackProgress("Noise Stacking", 0, $"Applying {settings.Method} noise reduction..."));
            var result = await Task.Run(() => _noiseStackEngine.Process(frames, settings), cancellationToken);
            progress?.Report(new StackProgress("Noise Stacking", 100, $"Noise reduction complete (+{result.EstimatedSnrImprovementDb:F1} dB SNR)."));

            return result;
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    public async Task<HdrStackResult> ProcessHdrStackAsync(
        IReadOnlyList<string> filePaths,
        HdrStackSettings settings,
        AlignmentMode alignmentMode = AlignmentMode.Similarity,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths == null || filePaths.Count < 2)
            throw new ArgumentException("At least 2 bracketed exposure frames are required for HDR.", nameof(filePaths));

        progress?.Report(new StackProgress("Loading Frames", 0, $"Loading {filePaths.Count} exposure frames..."));
        var frames = new List<StackFrame>(filePaths.Count);

        for (int i = 0; i < filePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await Task.Run(() => _imageIO.LoadFrame(filePaths[i], i), cancellationToken);
            frames.Add(frame);
            progress?.Report(new StackProgress("Loading Frames", (double)(i + 1) / filePaths.Count * 100, $"Loaded {Path.GetFileName(filePaths[i])}"));
        }

        try
        {
            if (alignmentMode != AlignmentMode.None)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new StackProgress("Alignment", 0, "Aligning bracketed exposures..."));
                _alignmentEngine.AlignStack(frames, alignmentMode, false, false, 8, default, progress);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new StackProgress("HDR Merging", 0, $"Merging {settings.Method} HDR radiance..."));
            var result = await Task.Run(() => _hdrStackEngine.Process(frames, settings), cancellationToken);
            progress?.Report(new StackProgress("HDR Merging", 100, $"HDR complete ({result.EstimatedDynamicRangeEv:F1} EV Dynamic Range)."));

            return result;
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    public async Task ExportDepthMeshAsync(
        ImageBuffer<float> depthMap,
        ImageBuffer<float>? colorMap,
        string outputPath,
        DepthMeshOptions options)
    {
        if (depthMap == null) throw new ArgumentNullException(nameof(depthMap));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path cannot be empty.", nameof(outputPath));

        await Task.Run(() =>
        {
            string ext = Path.GetExtension(outputPath).ToLowerInvariant();
            if (ext == ".ply" || options.Format == MeshExportFormat.PlyPointCloud)
            {
                using var fs = File.Create(outputPath);
                _depthMeshExporter.ExportToPly(depthMap, colorMap, fs, options);
            }
            else if (ext == ".obj" || options.Format == MeshExportFormat.ObjSurfaceMesh)
            {
                using var sw = new StreamWriter(outputPath);
                _depthMeshExporter.ExportToObj(depthMap, colorMap, sw, options);
            }
            else if (options.Format == MeshExportFormat.NormalMapPng)
            {
                using var normalMap = _depthMeshExporter.GenerateNormalMap(depthMap, options.ZScale);
                _imageIO.SaveImage(normalMap, outputPath);
            }
        });
    }

    public async Task<AstroStackResult> ProcessAstroStackAsync(
        IReadOnlyList<string> lightPaths,
        AstroStackSettings settings,
        AstroCalibrationPathSets? calibrationPaths = null,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (lightPaths == null || lightPaths.Count == 0)
            throw new ArgumentException("At least 1 light frame path is required.", nameof(lightPaths));

        progress?.Report(new StackProgress("Loading Frames", 0, $"Loading {lightPaths.Count} astro light frames..."));
        var lightFrames = new List<StackFrame>(lightPaths.Count);

        for (int i = 0; i < lightPaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await Task.Run(() => _imageIO.LoadFrame(lightPaths[i], i), cancellationToken);
            lightFrames.Add(frame);
            progress?.Report(new StackProgress("Loading Frames", (double)(i + 1) / lightPaths.Count * 100, $"Loaded light {Path.GetFileName(lightPaths[i])}"));
        }

        AstroCalibrationFrames? calFrames = null;
        if (calibrationPaths != null)
        {
            calFrames = new AstroCalibrationFrames();
            if (calibrationPaths.DarkPaths != null)
            {
                for (int i = 0; i < calibrationPaths.DarkPaths.Count; i++)
                {
                    calFrames.DarkFrames.Add(_imageIO.LoadFrame(calibrationPaths.DarkPaths[i], i));
                }
            }
            if (calibrationPaths.FlatPaths != null)
            {
                for (int i = 0; i < calibrationPaths.FlatPaths.Count; i++)
                {
                    calFrames.FlatFrames.Add(_imageIO.LoadFrame(calibrationPaths.FlatPaths[i], i));
                }
            }
            if (calibrationPaths.BiasPaths != null)
            {
                for (int i = 0; i < calibrationPaths.BiasPaths.Count; i++)
                {
                    calFrames.BiasFrames.Add(_imageIO.LoadFrame(calibrationPaths.BiasPaths[i], i));
                }
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Task.Run(() => _astroStackEngine.Stack(lightFrames, calFrames, settings, progress), cancellationToken);
            return result;
        }
        finally
        {
            calFrames?.Dispose();
            foreach (var frame in lightFrames)
            {
                frame.Dispose();
            }
        }
    }

    public async Task<ImageBuffer<float>> DeconvolveImageAsync(
        ImageBuffer<float> input,
        DeconvolutionOptions options)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));

        return await Task.Run(() =>
        {
            using var psf = PsfGenerator.CreatePsf(options.PsfType, options.PsfRadius, options.MotionAngleDegrees);
            return _richardsonLucyEngine.Deconvolve(input, psf, options);
        });
    }

    public async Task<DehazeResult> DehazeImageAsync(
        ImageBuffer<float> input,
        DehazeOptions options)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));

        return await Task.Run(() => _dehazeEngine.Dehaze(input, options));
    }

    public async Task<RawStackResult> ProcessRawStackAsync(
        IReadOnlyList<RawBayerBuffer> rawFrames,
        RawStackSettings settings,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (rawFrames == null || rawFrames.Count == 0)
            throw new ArgumentException("RAW frames list cannot be empty.", nameof(rawFrames));

        progress?.Report(new StackProgress("Computational RAW", 0, $"Merging {rawFrames.Count} raw Bayer CFA frames..."));

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mergedBayer = _bayerFusionEngine.MergeBayerFrames(rawFrames, settings);

            progress?.Report(new StackProgress("Computational RAW", 50, "Applying Edge-Directed Adaptive Demosaicing..."));
            cancellationToken.ThrowIfCancellationRequested();
            var demosaiced = _demosaicEngine.Demosaic(mergedBayer, settings);

            progress?.Report(new StackProgress("Computational RAW", 100, "RAW processing complete."));

            return new RawStackResult(demosaiced, mergedBayer, rawFrames.Count)
            {
                EstimatedDynamicRangeEv = 12.5f + (float)Math.Log2(Math.Max(1, rawFrames.Count))
            };
        }, cancellationToken);
    }

    public async Task<DrizzleResult> ProcessDrizzleSuperResAsync(
        IReadOnlyList<string> filePaths,
        DrizzleSettings settings,
        AlignmentMode alignmentMode = AlignmentMode.Similarity,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths == null || filePaths.Count == 0)
            throw new ArgumentException("Frames list cannot be empty for Drizzle Super-Resolution.", nameof(filePaths));

        progress?.Report(new StackProgress("Loading Frames", 0, $"Loading {filePaths.Count} frames for Drizzle..."));
        var frames = new List<StackFrame>(filePaths.Count);

        for (int i = 0; i < filePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await Task.Run(() => _imageIO.LoadFrame(filePaths[i], i), cancellationToken);
            frames.Add(frame);
            progress?.Report(new StackProgress("Loading Frames", (double)(i + 1) / filePaths.Count * 100, $"Loaded {Path.GetFileName(filePaths[i])}"));
        }

        try
        {
            if (alignmentMode != AlignmentMode.None && frames.Count > 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new StackProgress("Alignment", 0, "Computing subpixel alignment homographies..."));
                _alignmentEngine.AlignStack(frames, alignmentMode, false, false, 8, default, progress);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new StackProgress("Drizzle Super-Resolution", 0, $"Reconstructing {settings.ScaleFactor:F1}x Drizzle field..."));
            var result = await Task.Run(() => _drizzleEngine.DrizzleStack(frames, settings, progress), cancellationToken);
            progress?.Report(new StackProgress("Drizzle Super-Resolution", 100, "Drizzle Super-Resolution complete."));

            return result;
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    private static void NormalizeFrameResolution(StackFrame frame, int targetW, int targetH)
    {
        if (frame.Width == targetW && frame.Height == targetH) return;

        if (frame.ColorBuffer != null)
        {
            var newColor = ResampleBilinear(frame.ColorBuffer, targetW, targetH);
            frame.ColorBuffer.Dispose();
            frame.ColorBuffer = newColor;
        }

        if (frame.GrayBuffer != null)
        {
            var newGray = ResampleBilinear(frame.GrayBuffer, targetW, targetH);
            frame.GrayBuffer.Dispose();
            frame.GrayBuffer = newGray;
        }

        frame.Width = targetW;
        frame.Height = targetH;
    }

    private static ImageBuffer<float> ResampleBilinear(ImageBuffer<float> src, int dstW, int dstH)
    {
        int srcW = src.Width;
        int srcH = src.Height;
        int channels = src.Channels;
        var dst = new ImageBuffer<float>(dstW, dstH, channels, src.Format);

        float scaleX = (float)srcW / dstW;
        float scaleY = (float)srcH / dstH;

        Parallel.For(0, dstH, y =>
        {
            float srcY = (y + 0.5f) * scaleY - 0.5f;
            int y0 = Math.Clamp((int)MathF.Floor(srcY), 0, srcH - 1);
            int y1 = Math.Clamp(y0 + 1, 0, srcH - 1);
            float fy = Math.Clamp(srcY - y0, 0f, 1f);

            var row0 = src.GetRowSpan(y0);
            var row1 = src.GetRowSpan(y1);
            var dstRow = dst.GetRowSpan(y);

            for (int x = 0; x < dstW; x++)
            {
                float srcX = (x + 0.5f) * scaleX - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(srcX), 0, srcW - 1);
                int x1 = Math.Clamp(x0 + 1, 0, srcW - 1);
                float fx = Math.Clamp(srcX - x0, 0f, 1f);

                int dIdx = x * channels;
                int sIdx0 = x0 * channels;
                int sIdx1 = x1 * channels;

                for (int c = 0; c < channels; c++)
                {
                    float c00 = row0[sIdx0 + c];
                    float c10 = row0[sIdx1 + c];
                    float c01 = row1[sIdx0 + c];
                    float c11 = row1[sIdx1 + c];

                    float top = c00 + fx * (c10 - c00);
                    float bottom = c01 + fx * (c11 - c01);

                    dstRow[dIdx + c] = top + fy * (bottom - top);
                }
            }
        });

        return dst;
    }

    public async Task<StitchResult> ProcessMosaicStitchAsync(
        IReadOnlyList<string> filePaths,
        StitchSettings settings,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths == null || filePaths.Count == 0)
            throw new ArgumentException("Frames list cannot be empty for Mosaic Stitching.", nameof(filePaths));

        progress?.Report(new StackProgress("Loading Frames", 0, $"Loading {filePaths.Count} tiles for Mosaic Stitching..."));
        var frames = new List<StackFrame>(filePaths.Count);

        for (int i = 0; i < filePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await Task.Run(() => _imageIO.LoadFrame(filePaths[i], i), cancellationToken);
            frames.Add(frame);
            progress?.Report(new StackProgress("Loading Frames", (double)(i + 1) / filePaths.Count * 100, $"Loaded {Path.GetFileName(filePaths[i])}"));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Task.Run(() => _mosaicStitchEngine.Stitch(frames, settings, progress), cancellationToken);
            return result;
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }
}



