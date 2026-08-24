using FImageStack.Core.Alignment;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class LensDistortionTests
{
    [Fact]
    public void LensDistortionCorrector_ShouldCorrectRadialDistortion()
    {
        int w = 32;
        int h = 32;

        using var frame = new StackFrame
        {
            Index = 0,
            Width = w,
            Height = h,
            GrayBuffer = new ImageBuffer<float>(w, h),
            ColorBuffer = new ImageBuffer<float>(w, h, 3)
        };
        frame.GrayBuffer.AsSpan().Fill(0.5f);
        frame.ColorBuffer.AsSpan().Fill(0.5f);

        // Center pixel high contrast dot
        frame.GrayBuffer.At(w / 2, h / 2) = 1.0f;
        frame.ColorBuffer.At(w / 2, h / 2, 0) = 1.0f;

        var lensParams = new LensDistortionParams(K1: -0.15f, K2: 0.02f);
        Assert.True(lensParams.HasDistortion);

        var corrector = new LensDistortionCorrector();
        corrector.UndistortFrame(frame, lensParams);

        // Center pixel should remain undisturbed at optical center
        Assert.True(frame.GrayBuffer.At(w / 2, h / 2) > 0.8f);
        Assert.True(frame.ColorBuffer.At(w / 2, h / 2, 0) > 0.8f);
    }

    [Fact]
    public void HomographyEstimator_ShouldSolveDirectLinearTransformAndInvert()
    {
        // 4 point correspondences with perspective tilt
        var points = new List<(float srcX, float srcY, float dstX, float dstY)>
        {
            (0f, 0f, 2f, 3f),
            (100f, 0f, 97f, 2f),
            (100f, 100f, 95f, 96f),
            (0f, 100f, 4f, 97f)
        };

        var estimator = new HomographyEstimator();
        float[] h = estimator.EstimateHomography(points);

        Assert.Equal(9, h.Length);
        Assert.Equal(1.0f, h[8], 2);

        // Test inversion
        float[] invH = estimator.InvertHomography(h);
        Assert.Equal(9, invH.Length);

        // Matrix multiply H * invH should produce identity
        float i00 = h[0] * invH[0] + h[1] * invH[3] + h[2] * invH[6];
        float i11 = h[3] * invH[1] + h[4] * invH[4] + h[5] * invH[7];
        float i22 = h[6] * invH[2] + h[7] * invH[5] + h[8] * invH[8];

        Assert.Equal(1.0f, i00, 1);
        Assert.Equal(1.0f, i11, 1);
        Assert.Equal(1.0f, i22, 1);
    }

    [Fact]
    public void AlignmentEngine_WithHomographyAndLensCorrection_ShouldExecuteCleanly()
    {
        int w = 32;
        int h = 32;
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
                ColorBuffer = new ImageBuffer<float>(w, h, 3)
            };
            frame.GrayBuffer.AsSpan().Fill(0.4f);
            frame.ColorBuffer.AsSpan().Fill(0.4f);
            frames.Add(frame);
        }

        var alignmentEngine = new AdvancedAlignmentEngine();
        var lensParams = new LensDistortionParams(K1: -0.05f, P1: 0.01f);

        alignmentEngine.AlignStack(
            frames,
            mode: AlignmentMode.Homography,
            correctFocusBreathing: true,
            enableLocalAlignment: true,
            localGridSize: 4,
            lensDistortion: lensParams);

        for (int i = 0; i < frameCount; i++)
        {
            Assert.True(frames[i].AlignmentConfidence > 0.8);
        }

        foreach (var f in frames) f.Dispose();
    }
}
