using FImageStack.Core.Macro;
using FImageStack.Core.Models;
using FImageStack.Infrastructure.IO;

namespace FImageStack.Application.Services;

public interface IMacroService
{
    Task<MacroStackResult> ProcessMacroStackAsync(
        IReadOnlyList<string> filePaths,
        MacroPipelineConfig config,
        int maxDimension = 0,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<MacroStackResult> ProcessMacroFrameSetAsync(
        MacroFrameSet frameSet,
        MacroPipelineConfig config,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task SaveResultAsync(
        MacroStackResult result,
        string outputImagePath,
        string? outputDepthMapPath = null,
        int bitDepth = 8);
}

public sealed class MacroService : IMacroService
{
    private readonly IImageIO _imageIO;
    private readonly IMacroPipelineEngine _macroPipelineEngine;

    public MacroService(
        IImageIO? imageIO = null,
        IMacroPipelineEngine? macroPipelineEngine = null)
    {
        _imageIO = imageIO ?? new ImageSharpIO();
        _macroPipelineEngine = macroPipelineEngine ?? new MacroPipelineEngine();
    }

    public async Task<MacroStackResult> ProcessMacroStackAsync(
        IReadOnlyList<string> filePaths,
        MacroPipelineConfig config,
        int maxDimension = 0,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(config);

        if (filePaths.Count == 0)
        {
            throw new ArgumentException("No file paths provided for macro stacking.", nameof(filePaths));
        }

        progress?.Report(new StackProgress("Loading Macro Frames", 0, $"Loading {filePaths.Count} images..."));

        var frameSet = new MacroFrameSet();
        try
        {
            for (int i = 0; i < filePaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = filePaths[i];

                var stackFrame = _imageIO.LoadFrame(path, i, maxDimension);
                var macroFrame = new MacroFrame
                {
                    Index = i,
                    Label = path,
                    Width = stackFrame.Width,
                    Height = stackFrame.Height,
                    ColorBuffer = stackFrame.ColorBuffer,
                    GrayBuffer = stackFrame.GrayBuffer,
                    FocusMap = stackFrame.FocusMap,
                    SharpnessScore = stackFrame.SharpnessScore,
                    LensFocusDistance = (float)i / Math.Max(1, filePaths.Count - 1)
                };

                frameSet.AddFrame(macroFrame);
                double pct = (double)(i + 1) / filePaths.Count * 20.0;
                progress?.Report(new StackProgress("Loading Macro Frames", pct, $"Loaded frame {i + 1}/{filePaths.Count}"));
            }

            return await _macroPipelineEngine.ProcessAsync(frameSet, config, progress, cancellationToken);
        }
        finally
        {
            // FrameSet buffers are safely released after pipeline completion
            frameSet.Dispose();
        }
    }

    public Task<MacroStackResult> ProcessMacroFrameSetAsync(
        MacroFrameSet frameSet,
        MacroPipelineConfig config,
        IProgress<StackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _macroPipelineEngine.ProcessAsync(frameSet, config, progress, cancellationToken);
    }

    public Task SaveResultAsync(
        MacroStackResult result,
        string outputImagePath,
        string? outputDepthMapPath = null,
        int bitDepth = 8)
    {
        ArgumentNullException.ThrowIfNull(result);

        return Task.Run(() =>
        {
            if (result.FusedImage != null && !string.IsNullOrWhiteSpace(outputImagePath))
            {
                _imageIO.SaveImage(result.FusedImage, outputImagePath, bitDepth);
            }

            if (result.DepthMap?.DepthMap != null && !string.IsNullOrWhiteSpace(outputDepthMapPath))
            {
                _imageIO.SaveImage(result.DepthMap.DepthMap, outputDepthMapPath, bitDepth);
            }
        });
    }
}
