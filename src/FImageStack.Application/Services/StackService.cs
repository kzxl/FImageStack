using System.Diagnostics;
using FImageStack.Core;
using FImageStack.Core.Alignment;
using FImageStack.Core.DepthMap;
using FImageStack.Core.FocusMeasure;
using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using FImageStack.Infrastructure.IO;
using StackFrame = FImageStack.Core.Models.StackFrame;

namespace FImageStack.Application.Services;

public sealed class BenchmarkReport
{
    public int FrameCount { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double LoadTimeMs { get; set; }
    public double AlignmentTimeMs { get; set; }
    public double FocusMeasureTimeMs { get; set; }
    public double DepthMapTimeMs { get; set; }
    public double FusionTimeMs { get; set; }
    public double TotalTimeMs { get; set; }
    public long PeakWorkingSetMb { get; set; }
    public FusionMethod FusionMethod { get; set; }
    public FocusMeasureMethod FocusMethod { get; set; }
}

public interface IStackService
{
    Task<(ImageBuffer<float> FusedImage, DepthMapResult DepthResult, BenchmarkReport Benchmark)> ProcessStackAsync(
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

    public StackService(
        IImageIO imageIO,
        IAlignmentEngine? alignmentEngine = null,
        IDepthMapEstimator? depthMapEstimator = null)
    {
        _imageIO = imageIO;
        _alignmentEngine = alignmentEngine ?? new FocusBreathingCompensator();
        _depthMapEstimator = depthMapEstimator ?? new StandardDepthMapEstimator();
    }

    public async Task<(ImageBuffer<float> FusedImage, DepthMapResult DepthResult, BenchmarkReport Benchmark)> ProcessStackAsync(
        IReadOnlyList<string> filePaths,
        FusionSettings settings,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var benchmark = new BenchmarkReport
        {
            FrameCount = filePaths.Count,
            FusionMethod = settings.Method,
            FocusMethod = settings.FocusMethod
        };

        var totalStopwatch = Stopwatch.StartNew();
        var sw = new Stopwatch();

        // 1. Load Images
        progress?.Report(new StackProgress("Loading Frames", 0, $"Loading {filePaths.Count} frames..."));
        sw.Restart();

        var frames = new List<StackFrame>(filePaths.Count);
        for (int i = 0; i < filePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await Task.Run(() => _imageIO.LoadFrame(filePaths[i], i), cancellationToken);
            frames.Add(frame);
            progress?.Report(new StackProgress("Loading Frames", (double)(i + 1) / filePaths.Count * 100, $"Loaded {Path.GetFileName(filePaths[i])}"));
        }

        sw.Stop();
        benchmark.LoadTimeMs = sw.Elapsed.TotalMilliseconds;
        benchmark.Width = frames[0].Width;
        benchmark.Height = frames[0].Height;

        try
        {
            // 2. Alignment & Breathing Correction
            cancellationToken.ThrowIfCancellationRequested();
            sw.Restart();
            _alignmentEngine.AlignStack(frames, settings.EnableFocusBreathingCorrection, progress);
            sw.Stop();
            benchmark.AlignmentTimeMs = sw.Elapsed.TotalMilliseconds;

            // 3. Focus Measure Calculation
            cancellationToken.ThrowIfCancellationRequested();
            sw.Restart();
            progress?.Report(new StackProgress("Focus Measure", 0, "Computing sharpness maps..."));

            IFocusMeasureEngine focusEngine = settings.FocusMethod switch
            {
                FocusMeasureMethod.Tenengrad => new TenengradFocusMeasure(),
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

            // 4. Depth Map Estimation
            cancellationToken.ThrowIfCancellationRequested();
            sw.Restart();
            progress?.Report(new StackProgress("Depth Estimation", 0, "Estimating continuous depth map..."));

            var depthResult = _depthMapEstimator.EstimateDepthMap(frames, settings.EnableDepthSmoothing, settings.SmoothingRadius);
            progress?.Report(new StackProgress("Depth Estimation", 100, "Depth map computed."));

            sw.Stop();
            benchmark.DepthMapTimeMs = sw.Elapsed.TotalMilliseconds;

            // 5. Fusion Stage
            cancellationToken.ThrowIfCancellationRequested();
            sw.Restart();
            progress?.Report(new StackProgress("Focus Fusion", 0, $"Applying {settings.Method} fusion..."));

            IFusionEngine fusionEngine = settings.Method switch
            {
                FusionMethod.WinnerTakesAll => new WinnerTakesAllFusionEngine(),
                FusionMethod.FocusWeighted => new FocusWeightedFusionEngine(),
                _ => new MultiScalePyramidFusionEngine()
            };

            var fusedImage = await Task.Run(() => fusionEngine.Fuse(frames, depthResult, settings), cancellationToken);
            progress?.Report(new StackProgress("Focus Fusion", 100, "Fusion complete."));

            sw.Stop();
            benchmark.FusionTimeMs = sw.Elapsed.TotalMilliseconds;

            totalStopwatch.Stop();
            benchmark.TotalTimeMs = totalStopwatch.Elapsed.TotalMilliseconds;
            benchmark.PeakWorkingSetMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

            return (fusedImage, depthResult, benchmark);
        }
        finally
        {
            // Clean up individual frames to prevent memory leaks
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }
}
