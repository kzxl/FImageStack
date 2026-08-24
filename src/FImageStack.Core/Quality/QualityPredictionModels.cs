namespace FImageStack.Core.Quality;

public enum QualityGrade
{
    GradeAPlus, // >= 90% (Studio Quality)
    GradeA,     // 80..89% (High Quality)
    GradeB,     // 70..79% (Acceptable Quality)
    GradeC      // < 70% (Retake Needed)
}

public sealed class AdditionalFrameRecommendation
{
    public int RecommendedFrameCount { get; set; }
    public float StartDepthMm { get; set; }
    public float EndDepthMm { get; set; }
    public float ProjectedQualityGain { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ShotQualityScorecard
{
    public float ExpectedCoveragePercentage { get; set; }
    public float ExpectedSharpnessPercentage { get; set; }
    public float ExpectedAlignmentPercentage { get; set; }
    public float ExpectedArtifactRiskPercentage { get; set; }
    public float FinalExpectedQualityScore { get; set; }
    public QualityGrade Grade { get; set; }
    public string GradeTitle { get; set; } = string.Empty;
    public List<AdditionalFrameRecommendation> Recommendations { get; } = new();
    public string SummaryMessage { get; set; } = string.Empty;
}
