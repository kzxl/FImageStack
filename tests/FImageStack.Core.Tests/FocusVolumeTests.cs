using FImageStack.Core.FocusVolume;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class FocusVolumeTests
{
    [Fact]
    public void FocusVolume_CreationAndProfileExtraction_ShouldMatchInput()
    {
        int w = 10;
        int h = 10;
        int slices = 5;

        using var volume = new FocusVolume.FocusVolume(w, h, slices);
        Assert.Equal(w, volume.Width);
        Assert.Equal(h, volume.Height);
        Assert.Equal(slices, volume.Slices);
        Assert.Equal(w * h * slices, volume.TotalVoxels);

        // Fill test values
        for (int z = 0; z < slices; z++)
        {
            using var sliceMap = new ImageBuffer<float>(w, h);
            sliceMap.AsSpan().Fill((z + 1) * 0.2f);
            volume.SetSlice(z, sliceMap);
        }

        // Test GetProfile for a pixel (x=4, y=6)
        var profile = volume.GetProfile(4, 6);
        Assert.Equal(slices, profile.Length);
        for (int z = 0; z < slices; z++)
        {
            Assert.Equal((z + 1) * 0.2f, profile[z], 4);
        }

        // Test ExtractSlice
        using var extractedSlice = new ImageBuffer<float>(w, h);
        volume.ExtractSlice(2, extractedSlice);
        Assert.Equal(0.6f, extractedSlice.At(4, 6), 4);
    }

    [Fact]
    public void FocusVolumeEngine_SubFramePeakFitting_ShouldInterpolateAccurateContinuousDepth()
    {
        int w = 8;
        int h = 8;
        int frameCount = 5;

        var frames = new List<StackFrame>();
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h)
            };
            frames.Add(frame);
        }

        // Target ground truth fractional peak at sub-frame z_true = 2.3
        // Gaussian curve: S(z) = exp( - (z - 2.3)^2 / (2 * 0.8^2) )
        float zTrue = 2.3f;
        float sigma = 0.8f;

        for (int z = 0; z < frameCount; z++)
        {
            float s = MathF.Exp(-MathF.Pow(z - zTrue, 2) / (2f * sigma * sigma));
            frames[z].FocusMap!.AsSpan().Fill(s);
        }

        var engine = new FocusVolumeEngine();
        using var volume = engine.BuildVolume(frames);
        using var depthResult = engine.ProcessVolume(volume, frames, enable3DSmoothing: false);

        // At pixel (3, 3)
        int discretePeak = depthResult.SourceFrameMap.At(3, 3);
        float normalizedDepth = depthResult.DepthMap.At(3, 3);
        float continuousSubFrame = normalizedDepth * (frameCount - 1);

        // Discrete peak is frame 2
        Assert.Equal(2, discretePeak);

        // Sub-frame estimate should be close to 2.3 (tolerance 0.1)
        Assert.InRange(continuousSubFrame, 2.2f, 2.4f);
        Assert.True(depthResult.ConfidenceMap.At(3, 3) > 0.4f);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void FocusVolumeEngine_DofThickness_ShouldCalculateFWHMCorrectly()
    {
        int w = 4;
        int h = 4;
        int frameCount = 7;

        var frames = new List<StackFrame>();
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h)
            };
            frames.Add(frame);
        }

        // Peak at frame 3 with FWHM ~ 2 frames
        for (int z = 0; z < frameCount; z++)
        {
            float s = MathF.Exp(-MathF.Pow(z - 3f, 2) / (2f * 1.0f));
            frames[z].FocusMap!.AsSpan().Fill(s);
        }

        var engine = new FocusVolumeEngine();
        using var volume = engine.BuildVolume(frames);
        using var depthResult = engine.ProcessVolume(volume, frames, enable3DSmoothing: false);

        Assert.NotNull(depthResult.DofMap);
        float normalizedDof = depthResult.DofMap!.At(0, 0);
        float dofSlices = normalizedDof * (frameCount - 1);

        // FWHM for Gaussian with sigma=1.0 is ~ 2.355 * sigma = 2.35 frames
        Assert.InRange(dofSlices, 1.5f, 3.2f);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void FocusVolumeEngine_FocusGapDetection_ShouldFlagLowSharpnessRegions()
    {
        int w = 6;
        int h = 6;
        int frameCount = 4;

        var frames = new List<StackFrame>();
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h)
            };
            // Low background noise sharpness (flat out-of-focus background)
            frame.FocusMap.AsSpan().Fill(0.0001f);
            frames.Add(frame);
        }

        // Put a sharp feature only at pixel (2, 2) on frame 1
        frames[1].FocusMap!.At(2, 2) = 0.85f;

        var engine = new FocusVolumeEngine();
        using var volume = engine.BuildVolume(frames);
        using var depthResult = engine.ProcessVolume(volume, frames, enable3DSmoothing: false);

        Assert.NotNull(depthResult.FocusGapMask);
        // Background pixel (0, 0) should be flagged as focus gap
        Assert.Equal(1.0f, depthResult.FocusGapMask!.At(0, 0));

        // In-focus pixel (2, 2) should NOT be flagged as gap
        Assert.Equal(0.0f, depthResult.FocusGapMask.At(2, 2));

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void FocusVolume_Dispose_ShouldFreeUnmanagedMemoryWithoutLeak()
    {
        var volume = new FocusVolume.FocusVolume(100, 100, 10);
        Assert.True(volume.TotalVoxels == 100000);
        volume.Dispose();

        // Calling At() after dispose should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => volume.At(0, 0, 0));
    }
}
