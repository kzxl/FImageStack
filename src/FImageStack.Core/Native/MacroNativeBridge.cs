using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FImageStack.Core.Macro;
using FImageStack.Core.Models;

namespace FImageStack.Core.Native;

/// <summary>
/// High-performance C-ABI Native Bridge for iOS (Swift), Android (Kotlin/NDK), and C++ integration.
/// Functions use UnmanagedCallersOnly to avoid any runtime marshaling overhead.
/// </summary>
public static unsafe class MacroNativeBridge
{
    private static readonly MacroPipelineEngine s_engine = new();

    /// <summary>
    /// Processes a set of unmanaged RGB float buffers through the Macro Computational Photography pipeline.
    /// </summary>
    /// <param name="frameBuffers">Array of pointers to unmanaged linear RGB float buffers (size: width * height * 3).</param>
    /// <param name="frameCount">Number of frames.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="outRgbBuffer">Destination pointer for the fused RGB result buffer.</param>
    /// <param name="outDepthMapBuffer">Optional destination pointer for normalized continuous depth map (can be null).</param>
    /// <param name="autoCullBlur">1 to cull blurry frames, 0 to keep all frames.</param>
    /// <param name="minSharpnessRatio">Minimum sharpness ratio [0.0 - 1.0] for culling (default 0.12f).</param>
    /// <param name="alignmentMode">0=None, 1=Rigid, 2=Similarity, 3=Affine, 4=Homography.</param>
    /// <param name="fusionMethod">0=WTA, 1=Weighted, 2=Pyramid, 3=Wavelet, 4=RegionAdaptive, 5=Confidence, 6=Occlusion.</param>
    /// <param name="microDetailBoost">High-frequency detail enhancement strength [0.0 - 1.0].</param>
    /// <returns>0 on success, or a negative error code.</returns>
    [UnmanagedCallersOnly(EntryPoint = "fstack_macro_process_raw_rgb")]
    public static int ProcessMacroRawRgb(
        float** frameBuffers,
        int frameCount,
        int width,
        int height,
        float* outRgbBuffer,
        float* outDepthMapBuffer,
        int autoCullBlur,
        float minSharpnessRatio,
        int alignmentMode,
        int fusionMethod,
        float microDetailBoost)
    {
        if (frameBuffers == null || outRgbBuffer == null || frameCount < 1 || width <= 0 || height <= 0)
        {
            return -1; // Invalid argument
        }

        try
        {
            using var frameSet = new MacroFrameSet();
            int totalRgbElements = width * height * 3;

            for (int i = 0; i < frameCount; i++)
            {
                float* srcPtr = frameBuffers[i];
                if (srcPtr == null) return -2; // Null frame buffer pointer

                var frame = new MacroFrame
                {
                    Index = i,
                    Label = $"NativeFrame_{i}",
                    Width = width,
                    Height = height,
                    ColorBuffer = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32)
                };

                // Copy input unmanaged buffer into internal safe ImageBuffer
                Buffer.MemoryCopy(srcPtr, frame.ColorBuffer.DataPointer, (long)totalRgbElements * sizeof(float), (long)totalRgbElements * sizeof(float));
                frameSet.AddFrame(frame);
            }

            var config = new MacroPipelineConfig
            {
                AutoCullBlurFrames = autoCullBlur != 0,
                MinSharpnessRatio = minSharpnessRatio > 0 ? minSharpnessRatio : 0.12f,
                AlignmentMode = (AlignmentMode)Math.Clamp(alignmentMode, 0, 4),
                FusionMethod = (FusionMethod)Math.Clamp(fusionMethod, 0, 6),
                EnableMicroDetailRecovery = microDetailBoost > 0,
                MicroDetailStrength = microDetailBoost
            };

            using var result = s_engine.Process(frameSet, config);

            // Copy fused RGB result to output buffer
            if (result.FusedImage != null)
            {
                Buffer.MemoryCopy(
                    result.FusedImage.DataPointer,
                    outRgbBuffer,
                    (long)totalRgbElements * sizeof(float),
                    (long)totalRgbElements * sizeof(float));
            }

            // Copy depth map if requested
            if (outDepthMapBuffer != null && result.DepthMap != null)
            {
                int totalDepthElements = width * height;
                Buffer.MemoryCopy(
                    result.DepthMap.DepthMap.DataPointer,
                    outDepthMapBuffer,
                    (long)totalDepthElements * sizeof(float),
                    (long)totalDepthElements * sizeof(float));
            }

            return 0; // Success
        }
        catch (Exception)
        {
            return -99; // Internal processing exception
        }
    }

    /// <summary>
    /// Returns the engine semantic version string.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "fstack_get_version")]
    public static IntPtr GetEngineVersion()
    {
        return Marshal.StringToHGlobalAnsi("1.2.0-macro");
    }
}
