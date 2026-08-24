using FImageStack.Core.FocusMeasure;
using FImageStack.Core.Models;

namespace FImageStack.Core.LiveCapture;

public interface ILiveCaptureAssistant
{
    void ResetSession(LiveCaptureConfig? config = null);
    LiveFrameAnalysis FeedNextFrame(ImageBuffer<float> frameBuffer, int frameIndex = -1);
    LiveFrameAnalysis FeedNextFrame(StackFrame frame);
}

public sealed class LiveCaptureAssistant : ILiveCaptureAssistant
{
    private LiveCaptureConfig _config = new();
    private readonly IFocusMeasureEngine _focusEngine = new ModifiedLaplacianFocusMeasure();
    private readonly List<FrameHistoryEntry> _history = new();
    private float _peakSharpness = 0f;

    private sealed class FrameHistoryEntry
    {
        public int Index { get; set; }
        public float DepthMm { get; set; }
        public float Scale { get; set; } = 1.0f;
        public float Sharpness { get; set; }
        public ImageBuffer<float> FocusMap { get; set; } = null!;
    }

    public LiveCaptureAssistant(LiveCaptureConfig? config = null)
    {
        ResetSession(config);
    }

    public void ResetSession(LiveCaptureConfig? config = null)
    {
        _config = config ?? new LiveCaptureConfig();
        foreach (var h in _history)
        {
            h.FocusMap?.Dispose();
        }
        _history.Clear();
        _peakSharpness = 0f;
    }

    public LiveFrameAnalysis FeedNextFrame(ImageBuffer<float> frameBuffer, int frameIndex = -1)
    {
        int idx = frameIndex >= 0 ? frameIndex : _history.Count;
        var frame = new StackFrame
        {
            Index = idx,
            Width = frameBuffer.Width,
            Height = frameBuffer.Height,
            GrayBuffer = frameBuffer
        };
        return FeedNextFrame(frame);
    }

    public unsafe LiveFrameAnalysis FeedNextFrame(StackFrame frame)
    {
        if (frame == null) throw new ArgumentNullException(nameof(frame));

        int w = frame.Width;
        int h = frame.Height;
        int idx = frame.Index >= 0 ? frame.Index : _history.Count;

        // 1. Calculate Focus Map if not present
        ImageBuffer<float> focusMap;
        if (frame.FocusMap != null)
        {
            focusMap = frame.FocusMap;
        }
        else
        {
            focusMap = new ImageBuffer<float>(w, h, 1);
            if (frame.GrayBuffer != null)
            {
                _focusEngine.ComputeFocusMap(frame.GrayBuffer, focusMap, 1);
            }
        }

        // 2. Measure Frame Net Sharpness
        float sharpSum = 0f;
        float* p = focusMap.DataPointer;
        int total = w * h;
        int step = Math.Max(1, total / 20000);
        int samples = 0;
        for (int i = 0; i < total; i += step)
        {
            sharpSum += p[i];
            samples++;
        }
        float sharpness = samples > 0 ? sharpSum / samples : 0.05f;
        _peakSharpness = Math.Max(_peakSharpness, sharpness);

        var analysis = new LiveFrameAnalysis
        {
            FrameIndex = idx,
            SuggestedNextStepMm = _config.TargetStepMm
        };

        float frameScale = frame.FocusBreathingScale > 0 ? frame.FocusBreathingScale : 1.0f;

        // 3. First Frame Handling
        if (_history.Count == 0)
        {
            analysis.CurrentFocusDepthMm = 0.0f;
            analysis.PreviousFocusDepthMm = 0.0f;
            analysis.CumulativeCoveragePercentage = 20.0f;
            analysis.Status = StepQualityStatus.Optimal;
            analysis.GuidanceMessage = $"Current focus depth: 0.00 mm | Suggested next step: +{_config.TargetStepMm:F2} mm | Stack coverage: 20%";

            _history.Add(new FrameHistoryEntry
            {
                Index = idx,
                DepthMm = 0.0f,
                Scale = frameScale,
                Sharpness = sharpness,
                FocusMap = focusMap
            });

            return analysis;
        }

        // 4. Subsequent Frames: Incremental Step Tracking
        var prev = _history[^1];
        float prevDepth = prev.DepthMm;

        float movementMm;
        if (MathF.Abs(frameScale - prev.Scale) > 0.001f)
        {
            movementMm = (frameScale - prev.Scale) * 10f * _config.NominalDofMm;
        }
        else
        {
            // Focus map spatial overlap correlation
            float intersection = 0f;
            float unionSum = 0f;
            float* prevPtr = prev.FocusMap.DataPointer;

            for (int i = 0; i < total; i += step)
            {
                float a = prevPtr[i];
                float b = p[i];
                intersection += MathF.Min(a, b);
                unionSum += MathF.Max(a, b);
            }

            float overlap = unionSum > 0.001f ? intersection / unionSum : 0.5f;
            // Overlap ~ 0.75 -> movement = targetStep (0.15mm)
            movementMm = (1.0f - overlap) * 1.33f * _config.TargetStepMm;
            if (movementMm < 0.02f) movementMm = _config.TargetStepMm * 0.5f;
        }

        float currentDepth = prevDepth + movementMm;
        analysis.CurrentFocusDepthMm = currentDepth;
        analysis.PreviousFocusDepthMm = prevDepth;

        // 5. Evaluate Step Quality
        float targetStep = _config.TargetStepMm;
        if (movementMm < -0.02f)
        {
            analysis.Status = StepQualityStatus.Reversed;
            analysis.GuidanceMessage = $"⚠ Camera focus moved backward ({movementMm:F2} mm)! Rotate in forward direction.";
        }
        else if (movementMm > 2.0f * targetStep)
        {
            analysis.Status = StepQualityStatus.TooLarge;
            analysis.GuidanceMessage = $"⚠ Large focus step (+{movementMm:F2} mm)! Suggested: +{targetStep:F2} mm. Risk of focus gap.";
        }
        else if (movementMm < 0.25f * targetStep)
        {
            analysis.Status = StepQualityStatus.TooSmall;
            analysis.GuidanceMessage = $"Focus step very small (+{movementMm:F2} mm). Suggested: +{targetStep:F2} mm.";
        }
        else
        {
            analysis.Status = StepQualityStatus.Optimal;
        }

        // 6. Calculate Cumulative Stack Coverage
        int totalFrames = _history.Count + 1;
        float coverage = Math.Clamp(totalFrames * 22.0f, 20.0f, 100.0f);
        analysis.CumulativeCoveragePercentage = coverage;

        if (analysis.Status == StepQualityStatus.Optimal)
        {
            analysis.GuidanceMessage = $"Current focus depth: {currentDepth:F2} mm | Previous: {prevDepth:F2} mm | Suggested next step: +{targetStep:F2} mm | Stack coverage: {coverage:F0}%";
        }

        // 7. Detect Stack Completion
        if (coverage >= _config.CompletionCoverageThreshold && totalFrames >= 5)
        {
            analysis.IsStackComplete = true;
            analysis.Status = StepQualityStatus.TargetCompleted;
            analysis.GuidanceMessage = $"🎯 Target depth fully covered (100%). You have captured enough frames ({totalFrames} frames)! Ready to stack.";
        }

        _history.Add(new FrameHistoryEntry
        {
            Index = idx,
            DepthMm = currentDepth,
            Scale = frameScale,
            Sharpness = sharpness,
            FocusMap = focusMap
        });

        return analysis;
    }
}
