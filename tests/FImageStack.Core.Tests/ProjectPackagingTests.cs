using System.Drawing;
using FImageStack.Application.Services;
using FImageStack.Core;
using FImageStack.Core.DepthMap;
using FImageStack.Core.Models;
using FImageStack.Core.Project;
using FImageStack.Core.Retouch;
using Xunit;

namespace FImageStack.Core.Tests;

public class ProjectPackagingTests
{
    [Fact]
    public async Task ProjectService_ShouldSaveAndRestoreProjectPackageWithZeroLoss()
    {
        string tempProjectFile = Path.Combine(Path.GetTempPath(), $"test_proj_{Guid.NewGuid():N}.fstack");

        try
        {
            int w = 32;
            int h = 32;

            var project = new FStackProject
            {
                Version = "1.0",
                Width = w,
                Height = h,
                SourceFilePaths = new List<string> { "frame_001.jpg", "frame_002.jpg", "frame_003.jpg" },
                Settings = new FusionSettings
                {
                    Method = FusionMethod.MultiScalePyramid,
                    FocusMethod = FocusMeasureMethod.ModifiedLaplacian,
                    PyramidLevels = 4,
                    SmoothingRadius = 3
                },
                PostProcess = new PostProcessSettings
                {
                    Exposure = 0.5f,
                    Contrast = 1.2f,
                    Clarity = 0.4f,
                    Sharpening = 0.8f,
                    ToneMapping = ToneMappingOperator.ACESFilmic
                }
            };

            // Synthetic Processed Result
            var result = new ProcessedStackResult(w, h)
            {
                FusedImage = new ImageBuffer<float>(w, h, 3),
                DepthResult = new DepthMapResult(w, h)
            };
            result.FusedImage.AsSpan().Fill(0.75f);
            result.DepthResult.DepthMap.AsSpan().Fill(1.5f);
            result.DepthResult.SourceFrameMap.AsSpan().Fill(2);
            result.DepthResult.ConfidenceMap.AsSpan().Fill(0.92f);

            // Synthetic Retouch Layer
            var retouch = new RetouchLayer(w, h);
            var stroke = new RetouchStroke
            {
                StrokeId = 1,
                Tool = RetouchToolType.SourceBrush,
                SourceFrameIndex = 1,
                CenterX = 10f,
                CenterY = 12f,
                Radius = 25f,
                Feather = 0.4f,
                Opacity = 0.85f
            };
            retouch.Strokes.Add(stroke);

            var projectService = new ProjectService();

            // 1. Save Project
            await projectService.SaveProjectAsync(tempProjectFile, project, result, retouch);
            Assert.True(File.Exists(tempProjectFile));

            // 2. Load Project
            using var loaded = await projectService.LoadProjectAsync(tempProjectFile);

            Assert.NotNull(loaded);
            Assert.NotNull(loaded.Project);
            Assert.Equal(3, loaded.Project.SourceFilePaths.Count);
            Assert.Equal(FusionMethod.MultiScalePyramid, loaded.Project.Settings.Method);
            Assert.Equal(0.5f, loaded.Project.PostProcess.Exposure);
            Assert.Equal(ToneMappingOperator.ACESFilmic, loaded.Project.PostProcess.ToneMapping);

            // Verify Retouch Strokes
            Assert.NotNull(loaded.RestoredRetouchLayer);
            Assert.Single(loaded.RestoredRetouchLayer.Strokes);
            Assert.Equal(1, loaded.RestoredRetouchLayer.Strokes[0].SourceFrameIndex);
            Assert.Equal(10f, loaded.RestoredRetouchLayer.Strokes[0].CenterX);
            Assert.Equal(12f, loaded.RestoredRetouchLayer.Strokes[0].CenterY);
            Assert.Equal(25f, loaded.RestoredRetouchLayer.Strokes[0].Radius);

            // Verify Cached Binary Buffers (Zero-Recomputation)
            Assert.NotNull(loaded.CachedResult);
            Assert.Equal(w, loaded.CachedResult.FusedImage.Width);
            Assert.Equal(h, loaded.CachedResult.FusedImage.Height);
            Assert.Equal(0.75f, loaded.CachedResult.FusedImage.At(16, 16, 0));
            Assert.Equal(1.5f, loaded.CachedResult.DepthResult.DepthMap.At(16, 16));
            Assert.Equal(2, loaded.CachedResult.DepthResult.SourceFrameMap.At(16, 16));
            Assert.Equal(0.92f, loaded.CachedResult.DepthResult.ConfidenceMap.At(16, 16));

            result.Dispose();
            retouch.Dispose();
        }
        finally
        {
            if (File.Exists(tempProjectFile)) File.Delete(tempProjectFile);
        }
    }
}
