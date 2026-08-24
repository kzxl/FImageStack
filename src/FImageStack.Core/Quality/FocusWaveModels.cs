namespace FImageStack.Core.Quality;

public sealed class FocusWavePoint
{
    public int FrameIndex { get; set; }
    public int FrameNumber => FrameIndex + 1;
    public float ContinuousDepth { get; set; }
    public float SharpnessEnergy { get; set; }
    public float StepDeltaZ { get; set; }
    public bool IsStepUniform { get; set; }
    public bool IsGapWarning { get; set; }
}

public sealed class FocusWaveAnalysisResult
{
    public int TotalFrames { get; set; }
    public float MeanStepDeltaZ { get; set; }
    public float StepUniformityScore { get; set; }
    public float DepthCoveragePercentage { get; set; }
    public int GapCount { get; set; }
    public List<FocusWavePoint> WavePoints { get; } = new();
    public string AsciiWaveGraph { get; set; } = string.Empty;
    public string EvaluationSummary { get; set; } = string.Empty;
}
