using FImageStack.Core;
using FImageStack.Core.Artifact;
using FImageStack.Core.DepthMap;
using FImageStack.Core.FocusMeasure;
using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using FImageStack.Core.Motion;
using FImageStack.Core.Quality;
using FImageStack.Core.Reconstruction;
using FImageStack.Core.Retouch;
using FImageStack.Core.Tiling;
using Xunit;

namespace FImageStack.Core.Tests;

public class AdvancedFeaturesTests
{
    [Fact]
    public void MotionDetector_ShouldDistinguishMovingAndStaticRegions()
    {
        int size = 32;
        var frame0 = new StackFrame
        {
            Index = 0,
            Width = size,
            Height = size,
            GrayBuffer = new ImageBuffer<float>(size, size, 1)
        };
        var frame1 = new StackFrame
        {
            Index = 1,
            Width = size,
            Height = size,
            GrayBuffer = new ImageBuffer<float>(size, size, 1)
        };

        // Static left half (both 0.5), Moving right half (frame0: 0.1, frame1: 0.9)
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (x < size / 2)
                {
                    frame0.GrayBuffer.At(x, y) = 0.5f;
                    frame1.GrayBuffer.At(x, y) = 0.5f;
                }
                else
                {
                    frame0.GrayBuffer.At(x, y) = 0.1f;
                    frame1.GrayBuffer.At(x, y) = 0.9f;
                }
            }
        }

        var detector = new FrameDifferenceMotionDetector();
        using var result = detector.DetectMotion(new List<StackFrame> { frame0, frame1 });

        float leftMotion = result.MotionMap.At(size / 4, size / 2);
        float rightMotion = result.MotionMap.At(size * 3 / 4, size / 2);

        Assert.True(leftMotion < 0.01f, $"Left motion should be ~0, got {leftMotion}");
        Assert.True(rightMotion > 0.5f, $"Right motion should be high, got {rightMotion}");

        frame0.Dispose();
        frame1.Dispose();
    }

    [Fact]
    public void StackQualityAnalyzer_ShouldDetectFocusGapsWhenFramesAreSkipped()
    {
        int size = 32;
        int frameCount = 10;
        var frames = new List<StackFrame>();

        for (int f = 0; f < frameCount; f++)
        {
            var frame = new StackFrame
            {
                Index = f,
                Width = size,
                Height = size,
                GrayBuffer = new ImageBuffer<float>(size, size, 1),
                FocusMap = new ImageBuffer<float>(size, size, 1)
            };
            // Simulate only frame 0 and frame 9 being sharp (giant gap between 1 and 8)
            float sharpness = (f == 0 || f == 9) ? 1.0f : 0.00001f;
            frame.FocusMap.AsSpan().Fill(sharpness);
            frame.GrayBuffer.AsSpan().Fill(0.5f);
            frames.Add(frame);
        }

        var depthEstimator = new StandardDepthMapEstimator();
        using var depthResult = depthEstimator.EstimateDepthMap(frames, enableSmoothing: false);

        var analyzer = new StandardStackQualityAnalyzer();
        var report = analyzer.AnalyzeQuality(frames, depthResult);

        Assert.True(report.DetectedGaps.Count > 0, "Expected focus gap detection when middle frames have no sharp details.");

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void RetouchEngine_ShouldSupportUndoRedoAndCompositeRendering()
    {
        int size = 64;
        using var baseImage = new ImageBuffer<float>(size, size, 3);
        baseImage.AsSpan().Fill(0.2f); // Dark background

        var frame = new StackFrame
        {
            Index = 0,
            Width = size,
            Height = size,
            ColorBuffer = new ImageBuffer<float>(size, size, 3)
        };
        frame.ColorBuffer.AsSpan().Fill(0.9f); // Bright source frame

        using var retouchLayer = new RetouchLayer(size, size);

        // Add a brush stroke at center
        retouchLayer.AddStroke(new RetouchStroke
        {
            StrokeId = 1,
            Tool = RetouchToolType.SourceBrush,
            SourceFrameIndex = 0,
            CenterX = 32,
            CenterY = 32,
            Radius = 10,
            Opacity = 1.0f
        });

        using var rendered = retouchLayer.RenderComposite(baseImage, new List<StackFrame> { frame });
        float centerPixel = rendered.At(32, 32, 0);
        Assert.True(centerPixel > 0.8f, $"Expected painted center pixel > 0.8, got {centerPixel}");

        // Undo
        bool undoSuccess = retouchLayer.Undo();
        Assert.True(undoSuccess);

        using var renderedAfterUndo = retouchLayer.RenderComposite(baseImage, new List<StackFrame> { frame });
        float centerPixelAfterUndo = renderedAfterUndo.At(32, 32, 0);
        Assert.Equal(0.2f, centerPixelAfterUndo, 4);

        // Redo
        bool redoSuccess = retouchLayer.Redo();
        Assert.True(redoSuccess);

        frame.Dispose();
    }

    [Fact]
    public void TiledProcessor_ShouldMatchGlobalProcessingDimensions()
    {
        int size = 128;
        var frame = new StackFrame
        {
            Index = 0,
            Width = size,
            Height = size,
            ColorBuffer = new ImageBuffer<float>(size, size, 3),
            GrayBuffer = new ImageBuffer<float>(size, size, 1),
            FocusMap = new ImageBuffer<float>(size, size, 1)
        };
        frame.ColorBuffer.AsSpan().Fill(0.5f);
        frame.GrayBuffer.AsSpan().Fill(0.5f);
        frame.FocusMap.AsSpan().Fill(1.0f);

        var frames = new List<StackFrame> { frame };
        var depthEstimator = new StandardDepthMapEstimator();
        using var depthResult = depthEstimator.EstimateDepthMap(frames, enableSmoothing: false);

        var tiledProcessor = new StandardTiledProcessor();
        var fusionEngine = new WinnerTakesAllFusionEngine();
        using var tiledOutput = tiledProcessor.ProcessTiled(
            frames,
            depthResult,
            fusionEngine,
            new FusionSettings(),
            tileSize: 64,
            overlapMargin: 8);

        Assert.Equal(size, tiledOutput.Width);
        Assert.Equal(size, tiledOutput.Height);
        Assert.Equal(0.5f, tiledOutput.At(size / 2, size / 2, 0), 2);

        frame.Dispose();
    }
}
