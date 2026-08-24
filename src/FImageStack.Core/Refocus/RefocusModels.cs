namespace FImageStack.Core.Refocus;

public sealed class RefocusPointResult
{
    public int X { get; set; }
    public int Y { get; set; }
    public float ContinuousDepth { get; set; }
    public int ClosestFrameIndex { get; set; }
    public float FrameConfidence { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class SyntheticApertureParams
{
    public float TargetFocalDepth { get; set; }
    public float ApertureSize { get; set; } = 1.0f;
    public float BokehBlurRadius { get; set; } = 6.0f;
    public bool EnableSelectiveRange { get; set; } = false;
    public float RangeMinDepth { get; set; } = 0.0f;
    public float RangeMaxDepth { get; set; } = 100.0f;
}
