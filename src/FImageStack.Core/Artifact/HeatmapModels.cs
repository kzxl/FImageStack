using FImageStack.Core.Models;

namespace FImageStack.Core.Artifact;

public enum ArtifactHeatmapType
{
    Ghost,
    Halo,
    Blur,
    AlignmentError,
    FocusConfidence,
    ReconstructionRisk,
    CompositeDefect
}

public sealed class DefectHotspot
{
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Radius { get; set; }
    public ArtifactHeatmapType DefectType { get; set; }
    public float Severity { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class HeatmapLayerResult : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public ArtifactHeatmapType Type { get; set; }
    public ImageBuffer<float> IntensityMap { get; }
    public ImageBuffer<float> RgbHeatmap { get; }
    public List<DefectHotspot> Hotspots { get; } = new();

    public HeatmapLayerResult(int width, int height, ArtifactHeatmapType type)
    {
        Width = width;
        Height = height;
        Type = type;
        IntensityMap = new ImageBuffer<float>(width, height, 1);
        RgbHeatmap = new ImageBuffer<float>(width, height, 3);
    }

    public void Dispose()
    {
        IntensityMap?.Dispose();
        RgbHeatmap?.Dispose();
    }
}
