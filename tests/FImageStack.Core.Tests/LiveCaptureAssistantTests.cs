using FImageStack.Core.LiveCapture;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class LiveCaptureAssistantTests
{
    [Fact]
    public void LiveCaptureAssistant_SequentialFeed_ShouldTrackDepthAndCoverage()
    {
        int w = 20;
        int h = 20;
        var config = new LiveCaptureConfig
        {
            NominalDofMm = 0.20f,
            StepOverlapRatio = 0.75f,
            CompletionCoverageThreshold = 95.0f
        };

        var assistant = new LiveCaptureAssistant(config);
        LiveFrameAnalysis lastAnalysis = null!;

        for (int i = 0; i < 5; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                FocusMap = new ImageBuffer<float>(w, h)
            };

            // Shift focus region rightward by 3 pixels per frame
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool inFocus = (x >= i * 3 && x < (i + 1) * 3 + 4);
                    frame.FocusMap.At(x, y) = inFocus ? 0.85f : 0.05f;
                }
            }

            lastAnalysis = assistant.FeedNextFrame(frame);

            if (i == 0)
            {
                Assert.Equal(0.0f, lastAnalysis.CurrentFocusDepthMm);
                Assert.Equal(0.15f, lastAnalysis.SuggestedNextStepMm, 2);
                Assert.Contains("Current focus depth", lastAnalysis.GuidanceMessage);
            }
            else if (i == 1)
            {
                Assert.True(lastAnalysis.CurrentFocusDepthMm > 0.0f);
                Assert.Equal(0.0f, lastAnalysis.PreviousFocusDepthMm);
                Assert.Contains("Previous", lastAnalysis.GuidanceMessage);
            }
        }

        Assert.True(lastAnalysis.IsStackComplete);
        Assert.Equal(StepQualityStatus.TargetCompleted, lastAnalysis.Status);
        Assert.True(lastAnalysis.CumulativeCoveragePercentage >= 95.0f);
        Assert.Contains("Target depth fully covered", lastAnalysis.GuidanceMessage);
    }

    [Fact]
    public void LiveCaptureAssistant_ReversedOrLargeStep_ShouldWarn()
    {
        int w = 16;
        int h = 16;
        var config = new LiveCaptureConfig
        {
            NominalDofMm = 0.20f,
            StepOverlapRatio = 0.75f
        };

        var assistant = new LiveCaptureAssistant(config);

        // Frame 0: Baseline (Scale 1.00)
        var f0 = new StackFrame { Index = 0, Width = w, Height = h, FocusMap = new ImageBuffer<float>(w, h), FocusBreathingScale = 1.00f };
        f0.FocusMap.AsSpan().Fill(0.70f);
        assistant.FeedNextFrame(f0);

        // Frame 1: Optimal forward step (Scale 1.06 -> movement = 0.12mm ~= 0.15mm)
        var f1 = new StackFrame { Index = 1, Width = w, Height = h, FocusMap = new ImageBuffer<float>(w, h), FocusBreathingScale = 1.06f };
        f1.FocusMap.AsSpan().Fill(0.70f);
        var a1 = assistant.FeedNextFrame(f1);
        Assert.Equal(StepQualityStatus.Optimal, a1.Status);

        // Frame 2: Reversed step (Scale drops to 1.02 -> movement = -0.08mm)
        var f2 = new StackFrame { Index = 2, Width = w, Height = h, FocusMap = new ImageBuffer<float>(w, h), FocusBreathingScale = 1.02f };
        f2.FocusMap.AsSpan().Fill(0.70f);
        var a2 = assistant.FeedNextFrame(f2);
        Assert.Equal(StepQualityStatus.Reversed, a2.Status);
        Assert.Contains("backward", a2.GuidanceMessage);

        // Frame 3: Huge forward step (Scale jumps to 1.25 -> movement = +0.46mm > 2 * 0.15mm)
        var f3 = new StackFrame { Index = 3, Width = w, Height = h, FocusMap = new ImageBuffer<float>(w, h), FocusBreathingScale = 1.25f };
        f3.FocusMap.AsSpan().Fill(0.70f);
        var a3 = assistant.FeedNextFrame(f3);
        Assert.Equal(StepQualityStatus.TooLarge, a3.Status);
        Assert.Contains("Large focus step", a3.GuidanceMessage);

        f0.Dispose();
        f1.Dispose();
        f2.Dispose();
        f3.Dispose();
    }
}
