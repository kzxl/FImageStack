using FImageStack.Core.FocusMeasure;
using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class RegionAdaptiveFusionTests
{
    [Fact]
    public void RegionAdaptiveFusionEngine_ClassifyRegions_ShouldIdentifySemanticZones()
    {
        int w = 24;
        int h = 24;
        int frameCount = 3;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                GrayBuffer = new ImageBuffer<float>(w, h),
                FocusMap = new ImageBuffer<float>(w, h)
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (y < 8)
                    {
                        // Background: smooth low contrast
                        frame.GrayBuffer.At(x, y) = 0.5f;
                        frame.FocusMap.At(x, y) = 0.05f;
                    }
                    else if (y < 16)
                    {
                        // Solid Subject: medium texture
                        frame.GrayBuffer.At(x, y) = ((x + y) % 2 == 0) ? 0.7f : 0.3f;
                        frame.FocusMap.At(x, y) = (i == 1) ? 0.85f : 0.2f;
                    }
                    else
                    {
                        // Fine Edge/Hair: extreme step edge
                        frame.GrayBuffer.At(x, y) = (x % 2 == 0) ? 0.95f : 0.05f;
                        frame.FocusMap.At(x, y) = (i == 2) ? 0.95f : 0.3f;
                    }
                }
            }
            frames.Add(frame);
        }

        var engine = new RegionAdaptiveFusionEngine();
        using var refComposite = new ImageBuffer<float>(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                refComposite.At(x, y) = frames[1].GrayBuffer!.At(x, y);
            }
        }

        using var map = engine.ClassifyRegions(frames, refComposite);

        Assert.True(map.BackgroundRatio > 0.10f, $"Background ratio was {map.BackgroundRatio}");
        Assert.True(map.EdgeRatio > 0.10f, $"Edge ratio was {map.EdgeRatio}");

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void RegionAdaptiveFusionEngine_FuseStack_ShouldBlendSeamlessly()
    {
        int w = 24;
        int h = 24;
        int frameCount = 3;
        var frames = new List<StackFrame>();
        var focusEngine = new ModifiedLaplacianFocusMeasure();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                GrayBuffer = new ImageBuffer<float>(w, h),
                ColorBuffer = new ImageBuffer<float>(w, h, 3),
                FocusMap = new ImageBuffer<float>(w, h)
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool inFocus = (y >= i * 8 && y < (i + 1) * 8);
                    float val = inFocus ? (((x + y) % 2 == 0) ? 0.9f : 0.1f) : 0.5f;
                    frame.GrayBuffer.At(x, y) = val;
                    frame.ColorBuffer.At(x, y, 0) = val;
                    frame.ColorBuffer.At(x, y, 1) = val;
                    frame.ColorBuffer.At(x, y, 2) = val;
                }
            }

            focusEngine.ComputeFocusMap(frame.GrayBuffer, frame.FocusMap, 1);
            frames.Add(frame);
        }

        var engine = new RegionAdaptiveFusionEngine();
        var settings = new FusionSettings
        {
            Method = FusionMethod.RegionAdaptive,
            PyramidLevels = 3
        };

        using var fused = engine.FuseStack(frames, settings);

        Assert.NotNull(fused);
        Assert.Equal(w, fused.Width);
        Assert.Equal(h, fused.Height);

        // Ensure all pixels are valid normalized float values
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float val = fused.At(x, y);
                Assert.False(float.IsNaN(val));
                Assert.True(val >= 0.0f && val <= 1.0f);
            }
        }

        foreach (var f in frames) f.Dispose();
    }
}
