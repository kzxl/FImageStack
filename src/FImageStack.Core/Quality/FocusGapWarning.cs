namespace FImageStack.Core.Quality;

public sealed class InterFrameGapDetail
{
    public int FrameIndexA { get; set; }
    public int FrameIndexB { get; set; }
    public float EstimatedDepthA { get; set; }
    public float EstimatedDepthB { get; set; }
    public float OpticalStepDistance { get; set; }
    public int MissingPlanesEstimate { get; set; }
    public string WarningMessage { get; set; } = string.Empty;
}

public sealed class FocusGapAnalysisReport
{
    public int TotalFramesAnalyzed { get; set; }
    public float AverageStepDistance { get; set; }
    public List<InterFrameGapDetail> LargeGaps { get; } = new();
    public bool HasLargeGaps => LargeGaps.Count > 0;
    public string Summary { get; set; } = string.Empty;
}
