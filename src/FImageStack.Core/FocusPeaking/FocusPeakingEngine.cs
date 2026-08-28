using FImageStack.Core.Models;

namespace FImageStack.Core.FocusPeaking;

public interface IFocusPeakingEngine
{
    FocusPeakingResult RenderFocusPeaking(
        ImageBuffer<float> inputColor,
        ImageBuffer<float>? inputFocusMap = null,
        FocusPeakingSettings? settings = null);

    unsafe void RenderFocusPeakingRgbaDirect(
        byte* srcRgba,
        int width,
        int height,
        byte* dstRgba,
        FocusPeakingSettings settings);
}

public sealed class FocusPeakingEngine : IFocusPeakingEngine
{
    public unsafe FocusPeakingResult RenderFocusPeaking(
        ImageBuffer<float> inputColor,
        ImageBuffer<float>? inputFocusMap = null,
        FocusPeakingSettings? settings = null)
    {
        settings ??= new FocusPeakingSettings();
        int width = inputColor.Width;
        int height = inputColor.Height;
        int channels = inputColor.Channels;

        var resultImage = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32);

        // Get peaking RGB values
        var (peakR, peakG, peakB) = GetPeakingColorRgb(settings.Color);

        // Compute or use existing focus map
        bool disposeFocusMap = false;
        ImageBuffer<float> focusMap;
        if (inputFocusMap != null)
        {
            focusMap = inputFocusMap;
        }
        else
        {
            focusMap = new ImageBuffer<float>(width, height, 1);
            disposeFocusMap = true;
            ComputeSobelLaplacianFocus(inputColor, focusMap);
        }

        float* srcPtr = inputColor.DataPointer;
        float* dstPtr = resultImage.DataPointer;
        float* fPtr = focusMap.DataPointer;

        float threshold = settings.Threshold;
        float alpha = settings.OverlayAlpha;
        float maxSharpness = 0f;
        int inFocusPixels = 0;

        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int pIdx = rowOffset + x;
                float sharpness = fPtr[pIdx];

                if (sharpness > maxSharpness)
                {
                    // Non-atomic peak estimation
                    maxSharpness = sharpness;
                }

                bool isInFocus = sharpness >= threshold;
                if (isInFocus)
                {
                    Interlocked.Increment(ref inFocusPixels);
                }

                int srcBase = pIdx * channels;
                int dstBase = pIdx * 3;

                float r = srcPtr[srcBase + 0];
                float g = channels > 1 ? srcPtr[srcBase + 1] : r;
                float b = channels > 2 ? srcPtr[srcBase + 2] : r;

                switch (settings.Mode)
                {
                    case PeakingDisplayMode.MonochromeBackground:
                        float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                        if (isInFocus)
                        {
                            dstPtr[dstBase + 0] = luma * (1f - alpha) + peakR * alpha;
                            dstPtr[dstBase + 1] = luma * (1f - alpha) + peakG * alpha;
                            dstPtr[dstBase + 2] = luma * (1f - alpha) + peakB * alpha;
                        }
                        else
                        {
                            dstPtr[dstBase + 0] = luma;
                            dstPtr[dstBase + 1] = luma;
                            dstPtr[dstBase + 2] = luma;
                        }
                        break;

                    case PeakingDisplayMode.ColorOverlay:
                        if (isInFocus)
                        {
                            dstPtr[dstBase + 0] = r * (1f - alpha) + peakR * alpha;
                            dstPtr[dstBase + 1] = g * (1f - alpha) + peakG * alpha;
                            dstPtr[dstBase + 2] = b * (1f - alpha) + peakB * alpha;
                        }
                        else
                        {
                            dstPtr[dstBase + 0] = r;
                            dstPtr[dstBase + 1] = g;
                            dstPtr[dstBase + 2] = b;
                        }
                        break;

                    case PeakingDisplayMode.MaskOnly:
                        float maskVal = isInFocus ? 1.0f : 0.0f;
                        dstPtr[dstBase + 0] = maskVal * peakR;
                        dstPtr[dstBase + 1] = maskVal * peakG;
                        dstPtr[dstBase + 2] = maskVal * peakB;
                        break;

                    case PeakingDisplayMode.Heatmap:
                        float norm = Math.Clamp(sharpness / Math.Max(0.01f, threshold * 2.0f), 0f, 1f);
                        var (hR, hG, hB) = TurboColormap(norm);
                        dstPtr[dstBase + 0] = hR;
                        dstPtr[dstBase + 1] = hG;
                        dstPtr[dstBase + 2] = hB;
                        break;
                }
            }
        });

        if (disposeFocusMap)
        {
            focusMap.Dispose();
        }

        int totalPixels = width * height;
        return new FocusPeakingResult
        {
            PeakingImage = resultImage,
            InFocusPixelCount = inFocusPixels,
            InFocusPercentage = totalPixels > 0 ? (float)inFocusPixels / totalPixels * 100f : 0f,
            PeakSharpness = maxSharpness
        };
    }

    public unsafe void RenderFocusPeakingRgbaDirect(
        byte* srcRgba,
        int width,
        int height,
        byte* dstRgba,
        FocusPeakingSettings settings)
    {
        var (peakR, peakG, peakB) = GetPeakingColorRgb(settings.Color);
        byte pR = (byte)(peakR * 255f);
        byte pG = (byte)(peakG * 255f);
        byte pB = (byte)(peakB * 255f);

        float threshold = settings.Threshold * 255f;
        bool isMono = settings.Mode == PeakingDisplayMode.MonochromeBackground;

        Parallel.For(1, height - 1, y =>
        {
            int row = y * width * 4;
            int prevRow = (y - 1) * width * 4;
            int nextRow = (y + 1) * width * 4;

            for (int x = 1; x < width - 1; x++)
            {
                int curr = row + x * 4;
                int left = row + (x - 1) * 4;
                int right = row + (x + 1) * 4;
                int up = prevRow + x * 4;
                int down = nextRow + x * 4;

                // Luma calculation: (R + 2G + B) / 4 (fast bitshift approximation)
                int lCenter = (srcRgba[curr] + (srcRgba[curr + 1] << 1) + srcRgba[curr + 2]) >> 2;
                int lLeft = (srcRgba[left] + (srcRgba[left + 1] << 1) + srcRgba[left + 2]) >> 2;
                int lRight = (srcRgba[right] + (srcRgba[right + 1] << 1) + srcRgba[right + 2]) >> 2;
                int lUp = (srcRgba[up] + (srcRgba[up + 1] << 1) + srcRgba[up + 2]) >> 2;
                int lDown = (srcRgba[down] + (srcRgba[down + 1] << 1) + srcRgba[down + 2]) >> 2;

                int lx = Math.Abs((lCenter << 1) - lLeft - lRight);
                int ly = Math.Abs((lCenter << 1) - lUp - lDown);
                int edgeEnergy = lx + ly;

                bool inFocus = edgeEnergy >= threshold;

                if (inFocus)
                {
                    dstRgba[curr + 0] = pR;
                    dstRgba[curr + 1] = pG;
                    dstRgba[curr + 2] = pB;
                    dstRgba[curr + 3] = 255;
                }
                else if (isMono)
                {
                    byte gray = (byte)lCenter;
                    dstRgba[curr + 0] = gray;
                    dstRgba[curr + 1] = gray;
                    dstRgba[curr + 2] = gray;
                    dstRgba[curr + 3] = 255;
                }
                else
                {
                    dstRgba[curr + 0] = srcRgba[curr + 0];
                    dstRgba[curr + 1] = srcRgba[curr + 1];
                    dstRgba[curr + 2] = srcRgba[curr + 2];
                    dstRgba[curr + 3] = 255;
                }
            }
        });
    }

    private static unsafe void ComputeSobelLaplacianFocus(ImageBuffer<float> color, ImageBuffer<float> focusMap)
    {
        int w = color.Width;
        int h = color.Height;
        int ch = color.Channels;
        float* src = color.DataPointer;
        float* dst = focusMap.DataPointer;

        Parallel.For(1, h - 1, y =>
        {
            int row = y * w * ch;
            int prevRow = (y - 1) * w * ch;
            int nextRow = (y + 1) * w * ch;
            int mapRow = y * w;

            for (int x = 1; x < w - 1; x++)
            {
                int curr = row + x * ch;
                float centerLuma = 0.2126f * src[curr] + 0.7152f * src[curr + 1] + 0.0722f * src[curr + 2];
                float leftLuma = 0.2126f * src[curr - ch] + 0.7152f * src[curr - ch + 1] + 0.0722f * src[curr - ch + 2];
                float rightLuma = 0.2126f * src[curr + ch] + 0.7152f * src[curr + ch + 1] + 0.0722f * src[curr + ch + 2];
                float upLuma = 0.2126f * src[prevRow + x * ch] + 0.7152f * src[prevRow + x * ch + 1] + 0.0722f * src[prevRow + x * ch + 2];
                float downLuma = 0.2126f * src[nextRow + x * ch] + 0.7152f * src[nextRow + x * ch + 1] + 0.0722f * src[nextRow + x * ch + 2];

                float lx = MathF.Abs(2f * centerLuma - leftLuma - rightLuma);
                float ly = MathF.Abs(2f * centerLuma - upLuma - downLuma);
                dst[mapRow + x] = lx + ly;
            }
        });
    }

    private static (float r, float g, float b) GetPeakingColorRgb(PeakingColor color)
    {
        return color switch
        {
            PeakingColor.NeonGreen => (0.10f, 1.00f, 0.15f),
            PeakingColor.Red => (1.00f, 0.10f, 0.10f),
            PeakingColor.Yellow => (1.00f, 0.95f, 0.00f),
            PeakingColor.Cyan => (0.00f, 0.90f, 1.00f),
            PeakingColor.Magenta => (1.00f, 0.05f, 0.85f),
            _ => (1.00f, 1.00f, 1.00f)
        };
    }

    private static (float r, float g, float b) TurboColormap(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float r = Math.Clamp(1.5f - MathF.Abs(4.0f * t - 3.0f), 0f, 1f);
        float g = Math.Clamp(1.5f - MathF.Abs(4.0f * t - 2.0f), 0f, 1f);
        float b = Math.Clamp(1.5f - MathF.Abs(4.0f * t - 1.0f), 0f, 1f);
        return (r, g, b);
    }
}
