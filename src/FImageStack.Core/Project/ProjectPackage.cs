using FImageStack.Core.Models;
using FImageStack.Core.PostProcessing;
using FImageStack.Core.Quality;
using FImageStack.Core.Retouch;

namespace FImageStack.Core.Project;

public sealed class RetouchStrokeData
{
    public int StrokeId { get; set; }
    public RetouchToolType Tool { get; set; } = RetouchToolType.SourceBrush;
    public int SourceFrameIndex { get; set; }
    public float CenterX { get; set; }
    public float CenterY { get; set; }
    public float Radius { get; set; } = 25.0f;
    public float Feather { get; set; } = 0.5f;
    public float Opacity { get; set; } = 1.0f;
}

public sealed class FStackProject
{
    public string Version { get; set; } = "1.0";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
    public int Width { get; set; }
    public int Height { get; set; }
    public List<string> SourceFilePaths { get; set; } = new();
    public FusionSettings Settings { get; set; } = new();
    public PostProcessSettings PostProcess { get; set; } = new();
    public List<RetouchStrokeData> RetouchStrokes { get; set; } = new();
    public StackQualityReport? QualityReport { get; set; }
    public BenchmarkReport? Benchmark { get; set; }
}

public sealed class LoadedProjectResult : IDisposable
{
    public FStackProject Project { get; set; } = new();
    public ProcessedStackResult? CachedResult { get; set; }
    public Retouch.RetouchLayer? RestoredRetouchLayer { get; set; }

    public void Dispose()
    {
        CachedResult?.Dispose();
        RestoredRetouchLayer?.Dispose();
    }
}
