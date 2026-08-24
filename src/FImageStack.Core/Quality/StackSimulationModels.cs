namespace FImageStack.Core.Quality;

public enum FocusGapSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public sealed class FocusGapInfo
{
    public int StartFrame { get; set; }
    public int EndFrame { get; set; }
    public int GapWidthFrames => EndFrame - StartFrame;
    public float MissingDepthRatio { get; set; }
    public FocusGapSeverity Severity { get; set; } = FocusGapSeverity.Medium;
    public string Description { get; set; } = string.Empty;
}

public sealed class StackSimulationResult
{
    public int TotalFrames { get; set; }
    public float DepthCoveragePercentage { get; set; }
    public float AverageStepOverlapPercentage { get; set; }
    public List<FocusGapInfo> DetectedGaps { get; } = new();
    public float[] CoverageCurve { get; set; } = Array.Empty<float>();
    public string CoverageBarAscii { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public bool HasGaps => DetectedGaps.Count > 0;
}
