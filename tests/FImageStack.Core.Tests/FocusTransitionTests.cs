using FImageStack.Core.FocusVolume;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class FocusTransitionTests
{
    [Fact]
    public void FocusTransitionFitter_FitTransition_ShouldRecoverContinuousSubFramePeakAndR2()
    {
        // 20-frame sequence with true continuous focus peak at mu = 10.73
        int frameCount = 20;
        float muTrue = 10.73f;
        float sigmaTrue = 1.25f;
        float ampTrue = 0.92f;
        float baseTrue = 0.08f;

        float[] profile = new float[frameCount];
        for (int z = 0; z < frameCount; z++)
        {
            float zDiff = z - muTrue;
            profile[z] = ampTrue * MathF.Exp(-(zDiff * zDiff) / (2f * sigmaTrue * sigmaTrue)) + baseTrue;
        }

        var fitter = new FocusTransitionFitter();
        var model = fitter.FitTransition(profile);

        // OptimalMu should recover 10.73 with high sub-frame precision
        Assert.InRange(model.OptimalMu, 10.65f, 10.80f);

        // Goodness of Fit R^2 should be near perfect (> 0.98)
        Assert.True(model.GoodnessOfFit > 0.98f, $"Expected R^2 > 0.98, got {model.GoodnessOfFit}");

        // Transition spread should be close to 1.25
        Assert.InRange(model.TransitionSpread, 1.0f, 1.5f);

        // Model is reliable
        Assert.True(model.IsReliable);
    }

    [Fact]
    public void FocusTransitionFitter_SynthesizeSubFrameColor_ShouldBlendProportionally()
    {
        int w = 2;
        int h = 2;
        var frames = new List<StackFrame>();

        for (int i = 0; i < 20; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                ColorBuffer = new ImageBuffer<float>(w, h, 3)
            };
            frames.Add(frame);
        }

        // Frame 10: Red (1, 0, 0)
        frames[10].ColorBuffer!.AsSpan().Fill(0f);
        for (int i = 0; i < w * h; i++) frames[10].ColorBuffer!.At(i % w, i / w, 0) = 1.0f;

        // Frame 11: Green (0, 1, 0)
        frames[11].ColorBuffer!.AsSpan().Fill(0f);
        for (int i = 0; i < w * h; i++) frames[11].ColorBuffer!.At(i % w, i / w, 1) = 1.0f;

        var fitter = new FocusTransitionFitter();
        Span<float> synthRgb = stackalloc float[3];

        // Virtual position mu = 10.70 -> should be 30% Red + 70% Green
        fitter.SynthesizeSubFrameColor(10.70f, w, h, frames, 0, synthRgb);

        Assert.Equal(0.30f, synthRgb[0], 2);
        Assert.Equal(0.70f, synthRgb[1], 2);
        Assert.Equal(0.00f, synthRgb[2], 2);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void FocusVolumeEngine_WithTransitionModeling_ShouldPopulateR2Map()
    {
        int w = 4;
        int h = 4;
        int frameCount = 10;

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

        // Gaussian curve peak around frame 5.3
        for (int z = 0; z < frameCount; z++)
        {
            float s = MathF.Exp(-MathF.Pow(z - 5.3f, 2) / (2f * 1.2f));
            frames[z].FocusMap!.AsSpan().Fill(s);
        }

        var engine = new FocusVolumeEngine();
        using var volume = engine.BuildVolume(frames);
        using var result = engine.ProcessVolume(volume, frames, enable3DSmoothing: false);

        Assert.NotNull(result.R2FitMap);
        float r2Val = result.R2FitMap!.At(0, 0);
        Assert.True(r2Val > 0.90f, $"Expected high R^2 > 0.90 for clean Gaussian curve, got {r2Val}");

        // Sub-frame depth should be around 5.3 / 9 = 0.588
        float normalizedDepth = result.DepthMap.At(0, 0);
        float subFrame = normalizedDepth * (frameCount - 1);
        Assert.InRange(subFrame, 5.1f, 5.5f);

        foreach (var f in frames) f.Dispose();
    }
}
