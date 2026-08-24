using FImageStack.Core.Lab;
using FImageStack.Core.Models;
using Xunit;

namespace FImageStack.Core.Tests;

public class ABStackLabTests
{
    [Fact]
    public void ABStackLabEngine_RunMultiStackLab_ShouldRenderAllSlotsAndIdentifyWinner()
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
                ColorBuffer = new ImageBuffer<float>(w, h, 3),
                FocusMap = new ImageBuffer<float>(w, h)
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool inFocus = (y >= i * 8 && y < (i + 1) * 8);
                    float val = inFocus ? 0.9f : 0.1f;
                    frame.GrayBuffer.At(x, y) = val;
                    frame.FocusMap.At(x, y) = val;
                    frame.ColorBuffer.At(x, y, 0) = val;
                    frame.ColorBuffer.At(x, y, 1) = val;
                    frame.ColorBuffer.At(x, y, 2) = val;
                }
            }
            frames.Add(frame);
        }

        var labEngine = new ABStackLabEngine();
        using var report = labEngine.RunMultiStackLab(frames);

        Assert.NotNull(report);
        Assert.Equal(5, report.TotalSlots);
        Assert.Equal(5, report.Slots.Count);

        foreach (var slot in report.Slots)
        {
            Assert.NotNull(slot.RenderedImage);
            Assert.True(slot.CompositeScore >= 60.0f);
        }

        Assert.True(report.WinnerScore >= 75.0f);
        Assert.NotEmpty(report.WinnerSlotId);
        Assert.Contains("WINNER", report.AsciiComparisonMatrix);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void ABStackLabEngine_ExtractSynchronized100PercentCrop_ShouldExtractExactPatch()
    {
        int w = 32;
        int h = 32;
        using var image = new ImageBuffer<float>(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                image.At(x, y) = x * 100f + y;
            }
        }

        var labEngine = new ABStackLabEngine();
        var viewport = new SynchronizedCropViewport
        {
            CenterX = 16,
            CenterY = 16,
            CropWidth = 8,
            CropHeight = 8,
            ZoomFactor = 1.0f
        };

        using var crop = labEngine.ExtractSynchronized100PercentCrop(image, viewport);

        Assert.Equal(8, crop.Width);
        Assert.Equal(8, crop.Height);
        Assert.Equal(image.At(12, 12), crop.At(0, 0));
    }
}
