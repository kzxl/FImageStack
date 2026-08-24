using FImageStack.Core;
using FImageStack.Core.Artifact;
using FImageStack.Core.Models;
using FImageStack.Core.Quality;
using Xunit;

namespace FImageStack.Core.Tests;

public class QualityAnalyzerTests
{
    [Fact]
    public void StandardStackQualityAnalyzer_ShouldComputeMultiDimensionalMetricsAndExtractArtifacts()
    {
        int size = 32;
        var frames = new List<StackFrame>();

        for (int i = 0; i < 4; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                FilePath = $"frame_{i}.jpg",
                Width = size,
                Height = size,
                AlignmentConfidence = 0.98f,
                ColorBuffer = new ImageBuffer<float>(size, size, 3),
                GrayBuffer = new ImageBuffer<float>(size, size, 1),
                FocusMap = new ImageBuffer<float>(size, size, 1)
            };

            frame.FocusMap.AsSpan().Fill(0.8f);
            frame.GrayBuffer.AsSpan().Fill(0.5f);
            frames.Add(frame);
        }

        var depthResult = new DepthMapResult(size, size);
        depthResult.ConfidenceMap.AsSpan().Fill(0.9f);

        // Synthesize an artifact map with 2 detected regions
        var artifactMap = new ArtifactMap(size, size);
        artifactMap.Regions.Add(new ArtifactRegion
        {
            Type = ArtifactType.Halo,
            X = 5,
            Y = 5,
            Width = 8,
            Height = 8,
            Severity = 0.85f
        });
        artifactMap.Regions.Add(new ArtifactRegion
        {
            Type = ArtifactType.Ghost,
            X = 20,
            Y = 20,
            Width = 6,
            Height = 6,
            Severity = 0.72f
        });

        var analyzer = new StandardStackQualityAnalyzer();
        var report = analyzer.AnalyzeQuality(frames, depthResult, artifactMap);

        Assert.NotNull(report);
        Assert.True(report.OverallScore >= 80.0, $"Overall score was {report.OverallScore:F1}, expected >= 80");
        Assert.True(report.AlignmentScore >= 95.0, $"Alignment score was {report.AlignmentScore:F1}");
        Assert.True(report.FocusCoverageScore >= 90.0, $"Focus coverage was {report.FocusCoverageScore:F1}");
        Assert.True(report.EdgeQualityScore >= 85.0, $"Edge quality was {report.EdgeQualityScore:F1}");

        // Top artifacts list verification
        Assert.Equal(2, report.TopArtifacts.Count);
        Assert.Equal("HALO", report.TopArtifacts[0].TypeName);
        Assert.Equal(9, report.TopArtifacts[0].CenterX);
        Assert.Equal(9, report.TopArtifacts[0].CenterY);
        Assert.Equal(0.85f, report.TopArtifacts[0].Severity);

        depthResult.Dispose();
        artifactMap.Dispose();
        foreach (var f in frames) f.Dispose();
    }
}
