using System.Runtime.InteropServices;
using FImageStack.Core.Macro;
using FImageStack.Core.Models;
using FImageStack.Core.Native;
using Xunit;

namespace FImageStack.Core.Tests;

public unsafe class MacroPipelineTests
{
    [Fact]
    public void MacroFrameSet_Lifecycle_ManagesMemoryCleanly()
    {
        using var frameSet = new MacroFrameSet();

        for (int i = 0; i < 3; i++)
        {
            var frame = new MacroFrame
            {
                Label = $"Frame_{i}",
                Width = 64,
                Height = 64,
                ColorBuffer = new ImageBuffer<float>(64, 64, 3, PixelFormatType.RgbFloat32),
                LensFocusDistance = i * 0.5f
            };
            frameSet.AddFrame(frame);
        }

        Assert.Equal(3, frameSet.TotalFrames);
        Assert.Equal(64, frameSet.Width);
        Assert.Equal(64, frameSet.Height);
        Assert.Equal(3, frameSet.ActiveFramesCount);

        // Mark 1 as culled
        frameSet.Frames[0].IsCulled = true;
        Assert.Equal(2, frameSet.ActiveFramesCount);
    }

    [Fact]
    public void MacroPipeline_AutoCullBlurFrames_RejectsOutliers()
    {
        using var frameSet = new MacroFrameSet();
        int width = 32;
        int height = 32;

        // Frame 0: Very sharp high frequency pattern (checkerboard)
        var sharpFrame = CreatePatternFrame(width, height, isSharp: true);
        frameSet.AddFrame(sharpFrame);

        // Frame 1: Completely flat/blurry frame (almost zero high frequencies)
        var blurFrame = CreatePatternFrame(width, height, isSharp: false);
        frameSet.AddFrame(blurFrame);

        // Frame 2: Moderately sharp frame
        var midFrame = CreatePatternFrame(width, height, isSharp: true);
        frameSet.AddFrame(midFrame);

        var engine = new MacroPipelineEngine();
        var config = new MacroPipelineConfig
        {
            AutoCullBlurFrames = true,
            MinSharpnessRatio = 0.15f,
            AlignmentMode = AlignmentMode.None,
            EnableMicroDetailRecovery = false
        };

        using var result = engine.Process(frameSet, config);

        Assert.NotNull(result.FusedImage);
        Assert.True(result.QualityReport.CulledFrames >= 1, "Expected at least 1 blurry frame to be culled.");
        Assert.True(blurFrame.IsCulled, "Blurry frame must be flagged as culled.");
    }

    [Fact]
    public void MacroPipeline_EndToEnd_GeneratesCrispResultAndDepth()
    {
        using var frameSet = new MacroFrameSet();
        int width = 48;
        int height = 48;

        // Frame 0: Sharp on Left half
        var frameLeft = CreateSplitFocusFrame(width, height, sharpOnLeft: true);
        frameSet.AddFrame(frameLeft);

        // Frame 1: Sharp on Right half
        var frameRight = CreateSplitFocusFrame(width, height, sharpOnLeft: false);
        frameSet.AddFrame(frameRight);

        var engine = new MacroPipelineEngine();
        var config = new MacroPipelineConfig
        {
            AutoCullBlurFrames = false,
            AlignmentMode = AlignmentMode.None,
            FusionMethod = FusionMethod.RegionAdaptive,
            EnableMicroDetailRecovery = true,
            MicroDetailStrength = 0.4f
        };

        using var result = engine.Process(frameSet, config);

        Assert.NotNull(result.FusedImage);
        Assert.Equal(width, result.FusedImage.Width);
        Assert.Equal(height, result.FusedImage.Height);
        Assert.NotNull(result.DepthMap);
        Assert.True(result.Benchmark.TotalTimeMs >= 0);
    }

    [Fact]
    public void MacroNativeBridge_ProcessMacroRawRgb_ExecutesSuccessfully()
    {
        int width = 32;
        int height = 32;
        int rgbSize = width * height * 3;

        // Allocate 2 unmanaged buffers
        float* buf1 = (float*)NativeMemory.AllocZeroed((nuint)rgbSize * sizeof(float));
        float* buf2 = (float*)NativeMemory.AllocZeroed((nuint)rgbSize * sizeof(float));
        float* outRgb = (float*)NativeMemory.AllocZeroed((nuint)rgbSize * sizeof(float));
        float* outDepth = (float*)NativeMemory.AllocZeroed((nuint)(width * height) * sizeof(float));

        try
        {
            // Populate test patterns
            for (int i = 0; i < rgbSize; i++)
            {
                buf1[i] = (i % 2 == 0) ? 1.0f : 0.0f;
                buf2[i] = (i % 3 == 0) ? 0.8f : 0.2f;
            }

            float*[] framePtrs = new float*[] { buf1, buf2 };
            fixed (float** frameArrayPtr = framePtrs)
            {
                delegate* unmanaged<float**, int, int, int, float*, float*, int, float, int, int, float, int> fnPtr = &MacroNativeBridge.ProcessMacroRawRgb;
                int status = fnPtr(
                    frameArrayPtr,
                    2,
                    width,
                    height,
                    outRgb,
                    outDepth,
                    0,
                    0.1f,
                    0, // None
                    2, // Pyramid
                    0.2f);

                Assert.Equal(0, status);
                Assert.True(outRgb[0] >= 0.0f);
            }
        }
        finally
        {
            NativeMemory.Free(buf1);
            NativeMemory.Free(buf2);
            NativeMemory.Free(outRgb);
            NativeMemory.Free(outDepth);
        }
    }

    private static MacroFrame CreatePatternFrame(int width, int height, bool isSharp)
    {
        var frame = new MacroFrame
        {
            Width = width,
            Height = height,
            ColorBuffer = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32)
        };

        float* ptr = frame.ColorBuffer.DataPointer;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 3;
                float val = isSharp ? (((x / 2 + y / 2) % 2 == 0) ? 1.0f : 0.0f) : 0.5f;
                ptr[idx] = val;
                ptr[idx + 1] = val;
                ptr[idx + 2] = val;
            }
        }
        return frame;
    }

    private static MacroFrame CreateSplitFocusFrame(int width, int height, bool sharpOnLeft)
    {
        var frame = new MacroFrame
        {
            Width = width,
            Height = height,
            ColorBuffer = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32)
        };

        float* ptr = frame.ColorBuffer.DataPointer;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 3;
                bool inSharpZone = sharpOnLeft ? (x < width / 2) : (x >= width / 2);
                float val = inSharpZone ? (((x + y) % 2 == 0) ? 1.0f : 0.0f) : 0.4f;
                ptr[idx] = val;
                ptr[idx + 1] = val;
                ptr[idx + 2] = val;
            }
        }
        return frame;
    }
}
