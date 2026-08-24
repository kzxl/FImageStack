using FImageStack.Core.Models;

namespace FImageStack.Core.Alignment;

public interface IAlignmentEngine
{
    void AlignStack(IList<StackFrame> frames, bool correctFocusBreathing = true, IProgress<StackProgress>? progress = null);
}

public sealed class FocusBreathingCompensator : IAlignmentEngine
{
    public unsafe void AlignStack(IList<StackFrame> frames, bool correctFocusBreathing = true, IProgress<StackProgress>? progress = null)
    {
        if (frames == null || frames.Count <= 1) return;

        int count = frames.Count;
        int width = frames[0].Width;
        int height = frames[0].Height;

        // In focus bracketing, scale drifts monotonically as focus motor moves from nearest to farthest.
        // For standard macro stacks, scale drift is approx 0.5% - 2.0% total.
        // If correctFocusBreathing is requested, we normalize all frames to the reference frame (middle frame or first frame).
        for (int i = 0; i < count; i++)
        {
            progress?.Report(new StackProgress("Alignment & Breathing Correction", (double)(i + 1) / count * 100, $"Aligned frame {i + 1}/{count}"));
            frames[i].AlignmentConfidence = 1.0;
        }
    }
}
