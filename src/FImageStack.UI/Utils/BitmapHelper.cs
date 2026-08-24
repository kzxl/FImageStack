using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FImageStack.Core.Models;

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
                    // RGB float [0.0 - 1.0] -> BGR32 byte [0 - 255]
                    dstRow[dstIdx] = (byte)Math.Clamp((int)(srcData[srcIdx + 2] * 255f + 0.5f), 0, 255);     // B
                    dstRow[dstIdx + 1] = (byte)Math.Clamp((int)(srcData[srcIdx + 1] * 255f + 0.5f), 0, 255); // G
                    dstRow[dstIdx + 2] = (byte)Math.Clamp((int)(srcData[srcIdx] * 255f + 0.5f), 0, 255);     // R
                    dstRow[dstIdx + 3] = 255;                                                               // A
                }
                else
                {
                    // Grayscale float -> BGR32
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
