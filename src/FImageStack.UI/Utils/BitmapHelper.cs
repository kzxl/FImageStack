using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FImageStack.Core.Models;
using FImageStack.Core.Quality;

namespace FImageStack.UI.Utils;

public static class BitmapHelper
{
    public static unsafe BitmapSource? ToBitmapSource(ImageBuffer<float>? buffer)
    {
        if (buffer == null) return null;

        int width = buffer.Width;
        int height = buffer.Height;
        int channels = buffer.Channels;

        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
        wb.Lock();

        byte* backBuffer = (byte*)wb.BackBuffer;
        int backBufferStride = wb.BackBufferStride;
        float* srcData = buffer.DataPointer;

        Parallel.For(0, height, y =>
        {
            byte* dstRow = backBuffer + y * backBufferStride;
            int srcRowOffset = y * width;

            for (int x = 0; x < width; x++)
            {
                int srcIdx = (srcRowOffset + x) * channels;
                int dstIdx = x * 4;

                if (channels >= 3)
                {
                    dstRow[dstIdx] = (byte)Math.Clamp((int)(srcData[srcIdx + 2] * 255f + 0.5f), 0, 255);     // B
                    dstRow[dstIdx + 1] = (byte)Math.Clamp((int)(srcData[srcIdx + 1] * 255f + 0.5f), 0, 255); // G
                    dstRow[dstIdx + 2] = (byte)Math.Clamp((int)(srcData[srcIdx] * 255f + 0.5f), 0, 255);     // R
                    dstRow[dstIdx + 3] = 255;                                                               // A
                }
                else
                {
                    byte g = (byte)Math.Clamp((int)(srcData[srcIdx] * 255f + 0.5f), 0, 255);
                    dstRow[dstIdx] = g;
                    dstRow[dstIdx + 1] = g;
                    dstRow[dstIdx + 2] = g;
                    dstRow[dstIdx + 3] = 255;
                }
            }
        });

        wb.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
        wb.Unlock();
        wb.Freeze();

        return wb;
    }

    public static unsafe BitmapSource? ToBitmapSource(ImageBuffer<byte>? buffer)
    {
        if (buffer == null) return null;

        int width = buffer.Width;
        int height = buffer.Height;

        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
        wb.Lock();

        byte* backBuffer = (byte*)wb.BackBuffer;
        int backBufferStride = wb.BackBufferStride;
        byte* srcData = buffer.DataPointer;

        Parallel.For(0, height, y =>
        {
            byte* dstRow = backBuffer + y * backBufferStride;
            int srcRowOffset = y * width;

            for (int x = 0; x < width; x++)
            {
                byte val = srcData[srcRowOffset + x];
                int dstIdx = x * 4;

                dstRow[dstIdx] = val;
                dstRow[dstIdx + 1] = val;
                dstRow[dstIdx + 2] = val;
                dstRow[dstIdx + 3] = 255;
            }
        });

        wb.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
        wb.Unlock();
        wb.Freeze();

        return wb;
    }

    public static unsafe BitmapSource? CreateSplitWipeComposite(
        ImageBuffer<float>? primary,
        ImageBuffer<float>? secondary,
        float splitRatio = 0.5f)
    {
        if (primary == null) return ToBitmapSource(secondary);
        if (secondary == null) return ToBitmapSource(primary);

        int width = primary.Width;
        int height = primary.Height;
        int splitX = Math.Clamp((int)(splitRatio * width), 0, width - 1);

        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
        wb.Lock();

        byte* backBuffer = (byte*)wb.BackBuffer;
        int backBufferStride = wb.BackBufferStride;
        float* pData = primary.DataPointer;
        float* sData = secondary.DataPointer;
        int pCh = primary.Channels;
        int sCh = secondary.Channels;

        Parallel.For(0, height, y =>
        {
            byte* dstRow = backBuffer + y * backBufferStride;
            int rowOffset = y * width;

            for (int x = 0; x < width; x++)
            {
                int dstIdx = x * 4;

                // Draw vertical dividing line at split position
                if (Math.Abs(x - splitX) <= 1)
                {
                    dstRow[dstIdx] = 250;     // B
                    dstRow[dstIdx + 1] = 180; // G
                    dstRow[dstIdx + 2] = 59;  // R (Electric Blue line)
                    dstRow[dstIdx + 3] = 255;
                    continue;
                }

                if (x < splitX)
                {
                    // Left side: Primary (Fused)
                    int idx = (rowOffset + x) * pCh;
                    if (pCh >= 3)
                    {
                        dstRow[dstIdx] = (byte)Math.Clamp((int)(pData[idx + 2] * 255f + 0.5f), 0, 255);
                        dstRow[dstIdx + 1] = (byte)Math.Clamp((int)(pData[idx + 1] * 255f + 0.5f), 0, 255);
                        dstRow[dstIdx + 2] = (byte)Math.Clamp((int)(pData[idx] * 255f + 0.5f), 0, 255);
                    }
                    else
                    {
                        byte g = (byte)Math.Clamp((int)(pData[idx] * 255f + 0.5f), 0, 255);
                        dstRow[dstIdx] = g; dstRow[dstIdx + 1] = g; dstRow[dstIdx + 2] = g;
                    }
                }
                else
                {
                    // Right side: Secondary (Source or Depth)
                    int idx = (rowOffset + x) * sCh;
                    if (sCh >= 3)
                    {
                        dstRow[dstIdx] = (byte)Math.Clamp((int)(sData[idx + 2] * 255f + 0.5f), 0, 255);
                        dstRow[dstIdx + 1] = (byte)Math.Clamp((int)(sData[idx + 1] * 255f + 0.5f), 0, 255);
                        dstRow[dstIdx + 2] = (byte)Math.Clamp((int)(sData[idx] * 255f + 0.5f), 0, 255);
                    }
                    else
                    {
                        byte g = (byte)Math.Clamp((int)(sData[idx] * 255f + 0.5f), 0, 255);
                        dstRow[dstIdx] = g; dstRow[dstIdx + 1] = g; dstRow[dstIdx + 2] = g;
                    }
                }
                dstRow[dstIdx + 3] = 255;
            }
        });

        wb.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
        wb.Unlock();
        wb.Freeze();

        return wb;
    }

    public static unsafe BitmapSource RenderHistogramBitmap(HistogramData data, int width = 280, int height = 90)
    {
        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
        wb.Lock();

        byte* backBuffer = (byte*)wb.BackBuffer;
        int stride = wb.BackBufferStride;
        int maxFreq = data.MaxFrequency;

        // Fill background dark card (#1E2235)
        for (int y = 0; y < height; y++)
        {
            byte* row = backBuffer + y * stride;
            for (int x = 0; x < width; x++)
            {
                int idx = x * 4;
                row[idx] = 0x35;     // B
                row[idx + 1] = 0x22; // G
                row[idx + 2] = 0x1E; // R
                row[idx + 3] = 255;
            }
        }

        // Draw RGB and Luminance curves
        for (int x = 0; x < width; x++)
        {
            int bin = Math.Clamp((int)((float)x / width * 256), 0, 255);
            int barHeight = Math.Clamp((int)((float)data.Luminance[bin] / maxFreq * (height - 6)), 0, height - 6);

            for (int h = 0; h < barHeight; h++)
            {
                int y = height - 1 - h;
                byte* pixel = backBuffer + y * stride + x * 4;

                // Subtle semi-transparent white/blue glow for luminance
                pixel[0] = (byte)Math.Min(255, pixel[0] + 120); // B
                pixel[1] = (byte)Math.Min(255, pixel[1] + 130); // G
                pixel[2] = (byte)Math.Min(255, pixel[2] + 140); // R
            }
        }

        wb.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
        wb.Unlock();
        wb.Freeze();

        return wb;
    }

    public static BitmapImage LoadThumbnail(string filePath, int decodeWidth = 120)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
