namespace FImageStack.Core.Quality;

[Flags]
public enum FrameCullReason
{
    None = 0,
    PreFocusDeadband = 1 << 0,     // Out-of-focus at beginning before subject
    PostFocusDeadband = 1 << 1,    // Out-of-focus at end past subject
    ShakyMotionBlur = 1 << 2,      // Sudden drop in sharpness / camera shake
    ExposureGlitch = 1 << 3,       // Flash misfire / extreme luminance jump
    SevereMisalignment = 1 << 4    // Poor alignment confidence
}

public sealed class FrameQualityMetric
{
    public int FrameIndex { get; set; }
    public float NetSharpness { get; set; }
    public float MeanLuminance { get; set; }
    public double AlignmentConfidence { get; set; } = 1.0;
    public bool IsSelected { get; set; } = true;
    public FrameCullReason CullReason { get; set; } = FrameCullReason.None;
}

public sealed class OptimalFrameRangeResult
{
    public int TotalInputFrames { get; set; }
    public int RecommendedStartFrame { get; set; }
    public int RecommendedEndFrame { get; set; }
    public int SelectedFrameCount => SelectedIndices.Count;
    public int CulledFrameCount => TotalInputFrames - SelectedFrameCount;
    public List<int> SelectedIndices { get; } = new();
    public List<FrameQualityMetric> FrameMetrics { get; } = new();
    public string Summary { get; set; } = string.Empty;
}
