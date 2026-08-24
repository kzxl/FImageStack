using FImageStack.Core.FocusVolume;
using FImageStack.Core.Models;

namespace FImageStack.Core.DepthMap;

public interface IDepthMapEstimator
{
    DepthMapResult EstimateDepthMap(IReadOnlyList<StackFrame> frames, bool enableSmoothing = true, int smoothRadius = 2);
}

public sealed class StandardDepthMapEstimator : IDepthMapEstimator
{
    private readonly IFocusVolumeEngine _focusVolumeEngine;

    public StandardDepthMapEstimator(IFocusVolumeEngine? focusVolumeEngine = null)
    {
        _focusVolumeEngine = focusVolumeEngine ?? new FocusVolumeEngine();
    }

    public DepthMapResult EstimateDepthMap(IReadOnlyList<StackFrame> frames, bool enableSmoothing = true, int smoothRadius = 2)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Stack contains no frames.", nameof(frames));

        // 1. Build 3D Focus Volume from stack frames
        var volume = _focusVolumeEngine.BuildVolume(frames);

        // 2. Process volume: Sub-frame Gaussian/Parabolic peak fitting, DOF estimation, Focus gap detection & 3D Regularization
        return _focusVolumeEngine.ProcessVolume(volume, frames, enableSmoothing, smoothRadius);
    }
}
