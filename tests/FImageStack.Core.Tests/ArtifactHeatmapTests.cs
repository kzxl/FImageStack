using FImageStack.Core.Artifact;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class ArtifactHeatmapTests
{
    [Fact]
    public void ArtifactHeatmapEngine_ConvertScalarToTurboHeatmap_ShouldMapColors()
    {
        int w = 8;
        int h = 8;
        using var intensity = new ImageBuffer<float>(w, h, 1);

        // (0,0) is zero intensity (safe), (7,7) is 1.0 intensity (defect)
        intensity.At(0, 0) = 0.0f;
        intensity.At(7, 7) = 1.0f;

        var engine = new ArtifactHeatmapEngine();
        using var rgb = engine.ConvertScalarToTurboHeatmap(intensity);

        Assert.Equal(3, rgb.Channels);

        // Zero intensity should be deep blue (B > R)
        float blueR = rgb.At(0, 0, 0);
        float blueB = rgb.At(0, 0, 2);
        Assert.True(blueB > blueR, $"Safe color was R:{blueR}, B:{blueB}");

        // 1.0 intensity should be bright red (R > B)
        float redR = rgb.At(7, 7, 0);
        float redB = rgb.At(7, 7, 2);
        Assert.True(redR > redB, $"Defect color was R:{redR}, B:{redB}");
    }

    [Fact]
    public void ArtifactHeatmapEngine_ExtractHotspots_ShouldLocateDefectPeaks()
    {
        int w = 32;
        int h = 32;
        using var intensity = new ImageBuffer<float>(w, h, 1);

        // Create a local defect peak at (16, 16)
        intensity.At(16, 16) = 0.88f;

        var engine = new ArtifactHeatmapEngine();
        var hotspots = engine.ExtractHotspots(intensity, ArtifactHeatmapType.Ghost, threshold: 0.45f);

        Assert.NotEmpty(hotspots);
        var peak = hotspots[0];
        Assert.Equal(16, peak.X);
        Assert.Equal(16, peak.Y);
        Assert.Equal(0.88f, peak.Severity, 2);
        Assert.Equal(ArtifactHeatmapType.Ghost, peak.DefectType);
        Assert.Contains("Ghost defect at (16, 16)", peak.Description);
    }

    [Fact]
    public void ArtifactHeatmapEngine_GenerateHeatmap_ShouldPopulateRgbAndHotspots()
    {
        int w = 24;
        int h = 24;
        var stackResult = new ProcessedStackResult(w, h)
        {
            FusedImage = new ImageBuffer<float>(w, h, 3),
            ArtifactMap = new ArtifactMap(w, h)
        };

        // Inject a localized halo artifact region
        stackResult.ArtifactMap.Regions.Add(new ArtifactRegion
        {
            Id = 1,
            Type = ArtifactType.Halo,
            X = 6,
            Y = 6,
            Width = 6,
            Height = 6,
            Severity = 0.92f,
            Description = "Halo boundary"
        });

        var engine = new ArtifactHeatmapEngine();
        using var layer = engine.GenerateHeatmap(ArtifactHeatmapType.Halo, stackResult, Array.Empty<StackFrame>());

        Assert.NotNull(layer);
        Assert.Equal(ArtifactHeatmapType.Halo, layer.Type);
        Assert.Equal(w, layer.Width);
        Assert.Equal(h, layer.Height);
        Assert.NotEmpty(layer.Hotspots);

        stackResult.Dispose();
    }
}
