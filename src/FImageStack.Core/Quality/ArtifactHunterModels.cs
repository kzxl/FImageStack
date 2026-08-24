using FImageStack.Core.Models;

namespace FImageStack.Core.Quality;

public enum HunterRiskType
{
    Ghost,
    Halo,
    Motion,
    Blur,
    Alignment,
    Exposure
}

public sealed class HunterMetric
{
    public HunterRiskType Type { get; set; }
    public float RiskScorePercentage { get; set; }
    public string AsciiBar { get; set; } = string.Empty;
    public int IssueCount { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public sealed class PreStackHotspot
{
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Radius { get; set; }
    public HunterRiskType RiskType { get; set; }
    public float Severity { get; set; }
    public int ProblematicFrameIndex { get; set; }
    public int ProblematicFrameNumber => ProblematicFrameIndex + 1;
    public string RootCauseDescription { get; set; } = string.Empty;
}

public sealed class ArtifactHunterReport : IDisposable
{
    public int TotalFramesScanned { get; set; }
    public int HealthScore { get; set; }
    public List<HunterMetric> Metrics { get; } = new();
    public List<PreStackHotspot> Hotspots { get; } = new();
    public ImageBuffer<float>? GlobalRiskHeatmap { get; set; }
    public List<string> RecommendedActions { get; } = new();

    public void Dispose()
    {
        GlobalRiskHeatmap?.Dispose();
    }
}
