using FImageStack.Core.Fusion;
using FImageStack.Core.Models;
using FImageStack.Core.Occlusion;
using Xunit;

namespace FImageStack.Core.Tests;

public class OcclusionAwareTests
{
    [Fact]
    public void OcclusionAwareStacker_AnalyzeOcclusion_ShouldClassifyVisibleOccludedRevealed()
    {
        int w = 10;
        int h = 10;
        int frameCount = 3;

        var frames = new List<StackFrame>();
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h),
                GrayBuffer = new ImageBuffer<float>(w, h)
            };
            frame.GrayBuffer.AsSpan().Fill(0.5f);
            frames.Add(frame);
        }

        // Frame 0: Foreground object on left half (x < 5) has sharpness 0.90
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                frames[0].FocusMap!.At(x, y) = x < 5 ? 0.90f : 0.10f;
                frames[1].FocusMap!.At(x, y) = 0.20f;
                // Frame 2: Background object on right half (x >= 5) has sharpness 0.85; left half is blurry background (0.15)
                frames[2].FocusMap!.At(x, y) = x >= 5 ? 0.85f : 0.15f;
            }
        }

        var stacker = new OcclusionAwareStacker();
        using var result = stacker.AnalyzeOcclusion(frames);

        Assert.Equal(frameCount, result.FrameCount);

        // Frame 0 left half (x=2, y=5) should be Visible
        byte f0State = result.StateMaps[0].At(2, 5);
        Assert.Equal((byte)OcclusionState.Visible, f0State);

        // Frame 2 left half (x=2, y=5) is obscured behind the sharp leaf -> Occluded
        byte f2LeftState = result.StateMaps[2].At(2, 5);
        Assert.Equal((byte)OcclusionState.Occluded, f2LeftState);

        // Frame 2 right half (x=8, y=5) is sharp insect -> Revealed
        byte f2RightState = result.StateMaps[2].At(8, 5);
        Assert.True(f2RightState == (byte)OcclusionState.Revealed || f2RightState == (byte)OcclusionState.Visible);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void OcclusionAwareFusionEngine_ShouldBlendCleanlyWithoutBleedingHalo()
    {
        int w = 8;
        int h = 8;
        int frameCount = 2;

        var frames = new List<StackFrame>();
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h),
                GrayBuffer = new ImageBuffer<float>(w, h),
                ColorBuffer = new ImageBuffer<float>(w, h, 3)
            };
            frame.GrayBuffer.AsSpan().Fill(0.5f);
            frames.Add(frame);
        }

        // Frame 0: Red foreground on left half
        // Frame 1: Blue background on right half
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                frames[0].ColorBuffer!.At(x, y, 0) = 1.0f; // Red
                frames[1].ColorBuffer!.At(x, y, 2) = 1.0f; // Blue

                frames[0].FocusMap!.At(x, y) = x < 4 ? 0.95f : 0.05f;
                frames[1].FocusMap!.At(x, y) = x >= 4 ? 0.95f : 0.10f; // Left side is blurry halo (0.10)
            }
        }

        var fusionEngine = new OcclusionAwareFusionEngine();
        using var depthResult = new DepthMapResult(w, h);
        var settings = new FusionSettings { Method = FusionMethod.OcclusionAware };

        using var fused = fusionEngine.Fuse(frames, depthResult, settings);

        // Left side (0, 0) should be purely Red
        Assert.True(fused.At(0, 0, 0) > 0.85f);
        Assert.True(fused.At(0, 0, 2) < 0.15f);

        // Right side (7, 7) should be purely Blue
        Assert.True(fused.At(7, 7, 2) > 0.85f);
        Assert.True(fused.At(7, 7, 0) < 0.15f);

        foreach (var f in frames) f.Dispose();
    }
}
