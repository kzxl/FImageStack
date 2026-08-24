using FImageStack.Core.FocusVolume;
using FImageStack.Core.Models;
using FImageStack.Core.Quality;
using Xunit;

namespace FImageStack.Core.Tests;

public class MultiFrameConsensusTests
{
    [Fact]
    public void MultiFrameConsensusEngine_ShouldDetectAndAttenuateSpikeOutlier()
    {
        // Outlier scenario: Frame 2 = 0.98, while neighbor frames are ~ 0.41
        float[] profile = [0.35f, 0.40f, 0.98f, 0.42f, 0.38f];

        var engine = new MultiFrameConsensusEngine();
        float consensusScore = engine.ComputeConsensusScore(profile, 2);

        // Frame 2 consensus score should be heavily penalized (outlier)
        Assert.True(consensusScore < 0.6f, $"Expected consensus score < 0.6, got {consensusScore}");

        // Frame 1 and 3 should have high consensus scores
        float score1 = engine.ComputeConsensusScore(profile, 1);
        Assert.True(score1 > 0.8f);

        // Test in 3D Focus Volume
        using var volume = new FocusVolume.FocusVolume(2, 2, profile.Length);
        for (int z = 0; z < profile.Length; z++)
        {
            using var slice = new ImageBuffer<float>(2, 2);
            slice.AsSpan().Fill(profile[z]);
            volume.SetSlice(z, slice);
        }

        engine.ApplyConsensusFilter(volume);

        // Frame 2 in volume should be attenuated down from 0.98
        float filteredValue = volume.At(0, 0, 2);
        Assert.True(filteredValue < 0.7f, $"Expected filtered value < 0.7, got {filteredValue}");
    }

    [Fact]
    public void MultiFrameConsensusEngine_ShouldPreserveTrueSmoothGaussianPeak()
    {
        // Smooth optical PSF: 0.30 -> 0.65 -> 0.95 -> 0.94 -> 0.68 -> 0.32
        float[] smoothProfile = [0.30f, 0.65f, 0.95f, 0.94f, 0.68f, 0.32f];

        var engine = new MultiFrameConsensusEngine();
        float peakConsensus = engine.ComputeConsensusScore(smoothProfile, 2);

        // Peak is natural with strong neighbor support -> Consensus ~ 1.0
        Assert.True(peakConsensus > 0.95f, $"Expected smooth peak consensus > 0.95, got {peakConsensus}");

        using var volume = new FocusVolume.FocusVolume(2, 2, smoothProfile.Length);
        for (int z = 0; z < smoothProfile.Length; z++)
        {
            using var slice = new ImageBuffer<float>(2, 2);
            slice.AsSpan().Fill(smoothProfile[z]);
            volume.SetSlice(z, slice);
        }

        engine.ApplyConsensusFilter(volume);

        // Should remain untouched
        Assert.Equal(0.95f, volume.At(0, 0, 2), 2);
    }

    [Fact]
    public void FocusVolumeEngine_WithConsensusFilter_ShouldAvoidSpikeInSubFrameFitting()
    {
        int w = 4;
        int h = 4;
        int frameCount = 6;

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

        // True focus is at Frame 1 (z=1.0) with curve [0.6, 0.85, 0.6, 0.2, 0.1, 0.05]
        float[] trueCurve = [0.6f, 0.85f, 0.6f, 0.2f, 0.1f, 0.05f];
        for (int z = 0; z < frameCount; z++)
        {
            frames[z].FocusMap!.AsSpan().Fill(trueCurve[z]);
        }

        // Introduce a rogue single-frame flare spike at Frame 4 (z=4) with sharpness 0.95
        frames[4].FocusMap!.AsSpan().Fill(0.95f);

        var engine = new FocusVolumeEngine();
        using var volume = engine.BuildVolume(frames);
        using var result = engine.ProcessVolume(volume, frames, enable3DSmoothing: false);

        // Discrete peak should NOT be hijacked by Frame 4 spike; it should stay around Frame 1
        int detectedPeak = result.SourceFrameMap.At(0, 0);
        Assert.Equal(1, detectedPeak);

        foreach (var f in frames) f.Dispose();
    }
}
