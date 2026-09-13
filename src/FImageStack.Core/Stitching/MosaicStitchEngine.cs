using FImageStack.Core.Models;

namespace FImageStack.Core.Stitching;

public interface IMosaicStitchEngine
{
    StitchResult Stitch(
        IReadOnlyList<StackFrame> frames,
        StitchSettings settings,
        IProgress<StackProgress>? progress = null);
}

public sealed class MosaicStitchEngine : IMosaicStitchEngine
{
    private readonly IStitchRegistrationEngine _registrationEngine;
    private readonly IStitchBlendEngine _blendEngine;

    public MosaicStitchEngine(
        IStitchRegistrationEngine? registrationEngine = null,
        IStitchBlendEngine? blendEngine = null)
    {
        _registrationEngine = registrationEngine ?? new StitchRegistrationEngine();
        _blendEngine = blendEngine ?? new StitchBlendEngine();
    }

    public StitchResult Stitch(
        IReadOnlyList<StackFrame> frames,
        StitchSettings settings,
        IProgress<StackProgress>? progress = null)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Frames list cannot be empty for Mosaic Stitching.", nameof(frames));

        settings ??= new StitchSettings();

        // 1. Determine Reference / Anchor Frame (Middle frame if -1)
        int refIndex = settings.ReferenceFrameIndex;
        if (refIndex < 0 || refIndex >= frames.Count)
        {
            refIndex = frames.Count / 2;
        }

        progress?.Report(new StackProgress("Mosaic Stitching", 0, $"Starting mosaic registration with anchor tile #{refIndex + 1}..."));

        // 2. Multi-tile Pairwise Registration & Global Coordinate Graph Resolution
        var globalHomographies = _registrationEngine.ComputeGlobalHomographies(frames, settings, refIndex, progress);

        // 3. Global Canvas Projection, Feathered Seam Blending & Exposure Balancing
        progress?.Report(new StackProgress("Mosaic Stitching", 50, "Projecting and blending tiles into global expanded canvas..."));
        var result = _blendEngine.BlendTilesIntoCanvas(frames, globalHomographies, settings, refIndex, progress);

        progress?.Report(new StackProgress("Mosaic Stitching", 100, $"Mosaic complete: {result.CanvasWidth}x{result.CanvasHeight} ({result.TilesMerged} tiles merged)"));
        return result;
    }
}
