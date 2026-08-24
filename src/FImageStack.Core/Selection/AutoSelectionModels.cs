using FImageStack.Core.Models;

namespace FImageStack.Core.Selection;

public sealed class AlgorithmBenchmarkScore
{
    public string AlgorithmName { get; set; } = string.Empty;
    public FocusMeasureMethod FocusMethod { get; set; }
    public FusionMethod FusionMethod { get; set; }
    public float Score { get; set; }
    public float SignalToNoiseRatio { get; set; }
    public float DynamicRange { get; set; }
    public float SpatialContinuity { get; set; }
    public bool IsSelectedBest { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class AutoSelectionResult
{
    public FocusMeasureMethod SelectedFocusMethod { get; set; }
    public FusionMethod SelectedFusionMethod { get; set; }
    public float BestScore { get; set; }
    public List<AlgorithmBenchmarkScore> BenchmarkScores { get; } = new();
    public string SelectionSummary { get; set; } = string.Empty;
}
