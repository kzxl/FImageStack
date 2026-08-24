using FImageStack.Core.DepthMap;
using FImageStack.Core.Models;
using FImageStack.Core.Recovery;
using Xunit;

namespace FImageStack.Core.Tests;

public class MicroDetailRecoveryTests
{
    [Fact]
    public void MicroDetailRecoveryEngine_AnalyzeRecoveryPotential_ShouldDetectRecoverableTexture()
    {
        int w = 24;
        int h = 24;
        int frameCount = 5;
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
                    // Mid sharpness
                    frame.FocusMap.At(x, y) = 0.55f;
                    // High frequency texture in frame 2
                    frame.GrayBuffer.At(x, y) = (i == 2 && (x + y) % 2 == 0) ? 0.9f : 0.4f;
                }
            }
            frames.Add(frame);
        }

        var depthResult = new DepthMapResult(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                depthResult.DepthMap.At(x, y) = 1.0f; // Primary frame is 1, neighbor frame 2 has complementary detail
            }
        }

        var fused = new ImageBuffer<float>(w, h, 1);
        var engine = new MicroDetailRecoveryEngine();
        var rec = engine.AnalyzeRecoveryPotential(fused, frames, depthResult);

        Assert.True(rec.IsRecommended);
        Assert.True(rec.RecoverableAreaPercentage > 10.0f, $"Recoverable pct was {rec.RecoverableAreaPercentage}%");
        Assert.Contains("Detected", rec.Message);
        Assert.Contains("recoverable micro-details", rec.Message);

        foreach (var f in frames) f.Dispose();
        depthResult.Dispose();
        fused.Dispose();
    }

    [Fact]
    public void MicroDetailRecoveryEngine_RecoverMicroDetails_ShouldBoostTextureSharpness()
    {
        int w = 20;
        int h = 20;
        int frameCount = 5;
        var frames = new List<StackFrame>();

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
                    float val = (i == 3 && (x % 2 == 0)) ? 0.95f : 0.20f;
                    frame.GrayBuffer.At(x, y) = val;
                    frame.ColorBuffer.At(x, y, 0) = val;
                    frame.ColorBuffer.At(x, y, 1) = val;
                    frame.ColorBuffer.At(x, y, 2) = val;
                    frame.FocusMap.At(x, y) = (i == 3) ? 0.75f : 0.3f;
                }
            }
            frames.Add(frame);
        }

        var depthResult = new DepthMapResult(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                depthResult.DepthMap.At(x, y) = 2.0f; // Fused from frame 2, neighbor frame 3 has micro-details
            }
        }

        // Fused image is slightly soft/flat (0.45 vs 0.55)
        var fused = new ImageBuffer<float>(w, h, 3);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float val = (x % 2 == 0) ? 0.55f : 0.45f;
                fused.At(x, y, 0) = val;
                fused.At(x, y, 1) = val;
                fused.At(x, y, 2) = val;
            }
        }

        float initialContrast = MathF.Abs(fused.At(4, 4, 0) - fused.At(5, 4, 0)); // 0.10f

        var engine = new MicroDetailRecoveryEngine();
        var config = new MicroDetailRecoveryConfig { BoostStrength = 1.5f, NeighborRadius = 2 };
        using var result = engine.RecoverMicroDetails(fused, frames, depthResult, config);

        float recoveredContrast = MathF.Abs(result.EnhancedImage.At(4, 4, 0) - result.EnhancedImage.At(5, 4, 0));

        Assert.True(recoveredContrast > initialContrast, $"Recovered contrast {recoveredContrast} should be > initial contrast {initialContrast}");
        Assert.NotNull(result.RecoveredDetailMap);

        foreach (var f in frames) f.Dispose();
        depthResult.Dispose();
        fused.Dispose();
    }
}
