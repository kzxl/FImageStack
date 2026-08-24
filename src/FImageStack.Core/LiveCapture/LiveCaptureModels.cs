namespace FImageStack.Core.LiveCapture;

public enum StepQualityStatus
{
    Optimal,
    TooSmall,
    TooLarge,
    Reversed,
    TargetCompleted
}

public sealed class LiveCaptureConfig
{
    public float NominalDofMm { get; set; } = 0.20f;
    public float StepOverlapRatio { get; set; } = 0.75f;
    public float TargetStepMm => NominalDofMm * StepOverlapRatio;
    public float CompletionCoverageThreshold { get; set; } = 95.0f;
}

public sealed class LiveFrameAnalysis
{
    public int FrameIndex { get; set; }
    public float CurrentFocusDepthMm { get; set; }
    public float PreviousFocusDepthMm { get; set; }
    public float StepMovementMm => CurrentFocusDepthMm - PreviousFocusDepthMm;
    public float SuggestedNextStepMm { get; set; }
    public float CumulativeCoveragePercentage { get; set; }
    public StepQualityStatus Status { get; set; } = StepQualityStatus.Optimal;
    public string GuidanceMessage { get; set; } = string.Empty;
    public bool IsStackComplete { get; set; }
}
