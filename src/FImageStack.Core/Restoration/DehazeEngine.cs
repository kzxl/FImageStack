using FImageStack.Core.Models;

namespace FImageStack.Core.Restoration;

public interface IDehazeEngine
{
    DehazeResult Dehaze(ImageBuffer<float> hazyImage, DehazeOptions options);
}

public sealed class DehazeEngine : IDehazeEngine
{
    public unsafe DehazeResult Dehaze(ImageBuffer<float> hazyImage, DehazeOptions options)
    {
        if (hazyImage == null) throw new ArgumentNullException(nameof(hazyImage));

        int w = hazyImage.Width;
        int h = hazyImage.Height;
        int ch = hazyImage.Channels;
        int r = Math.Max(1, options.PatchRadius);

        float* srcPtr = hazyImage.DataPointer;

        // 1. Compute Dark Channel Map
        using var minChannel = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);
        float* minPtr = minChannel.DataPointer;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w * ch;
            for (int x = 0; x < w; x++)
            {
                int baseIdx = rowOffset + x * ch;
                float minVal = srcPtr[baseIdx];
                if (ch > 1 && srcPtr[baseIdx + 1] < minVal) minVal = srcPtr[baseIdx + 1];
                if (ch > 2 && srcPtr[baseIdx + 2] < minVal) minVal = srcPtr[baseIdx + 2];
                minPtr[y * w + x] = minVal;
            }
        });

        using var darkChannel = BoxMin2D(minChannel, r);
        float* darkPtr = darkChannel.DataPointer;

        // 2. Estimate Atmospheric Light A (Top 0.1% brightest in dark channel)
        int totalPixels = w * h;
        int topK = Math.Max(1, (int)(totalPixels * 0.001f));
        var pixelIndices = new int[totalPixels];
        for (int i = 0; i < totalPixels; i++) pixelIndices[i] = i;
        Array.Sort(pixelIndices, (a, b) => darkPtr[b].CompareTo(darkPtr[a]));

        float[] atmosphericLight = new float[ch];
        float maxBrightness = -1f;
        int bestPixel = pixelIndices[0];

        for (int i = 0; i < topK; i++)
        {
            int idx = pixelIndices[i];
            float b = 0f;
            for (int c = 0; c < Math.Min(ch, 3); c++)
            {
                b += srcPtr[idx * ch + c];
            }
            if (b > maxBrightness)
            {
                maxBrightness = b;
                bestPixel = idx;
            }
        }

        for (int c = 0; c < ch; c++)
        {
            atmosphericLight[c] = Math.Clamp(srcPtr[bestPixel * ch + c], 0.1f, 1.0f);
        }

        // 3. Estimate Raw Transmission Map
        using var normalizedMin = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);
        float* normPtr = normalizedMin.DataPointer;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w * ch;
            for (int x = 0; x < w; x++)
            {
                int baseIdx = rowOffset + x * ch;
                float minVal = srcPtr[baseIdx] / atmosphericLight[0];
                if (ch > 1) minVal = MathF.Min(minVal, srcPtr[baseIdx + 1] / atmosphericLight[1]);
                if (ch > 2) minVal = MathF.Min(minVal, srcPtr[baseIdx + 2] / atmosphericLight[2]);
                normPtr[y * w + x] = minVal;
            }
        });

        using var normDark = BoxMin2D(normalizedMin, r);
        var transmissionMap = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);
        float* rawTransPtr = normDark.DataPointer;
        float* transPtr = transmissionMap.DataPointer;
        float omega = options.Omega;

        for (int i = 0; i < totalPixels; i++)
        {
            transPtr[i] = 1.0f - omega * rawTransPtr[i];
        }

        // 4. Refine Transmission with Box Smoothing (Fast Guided approximation)
        using var refinedTrans = BoxMean2D(transmissionMap, Math.Max(1, options.GuidedFilterRadius));
        float* refTransPtr = refinedTrans.DataPointer;

        // 5. Recover Scene Radiance
        var dehazedImage = new ImageBuffer<float>(w, h, ch, hazyImage.Format);
        float* dstPtr = dehazedImage.DataPointer;
        float t0 = options.MinTransmission;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w * ch;
            for (int x = 0; x < w; x++)
            {
                int pixelIdx = y * w + x;
                float t = MathF.Max(t0, refTransPtr[pixelIdx]);
                int baseIdx = rowOffset + x * ch;

                for (int c = 0; c < ch; c++)
                {
                    float j = (srcPtr[baseIdx + c] - atmosphericLight[c]) / t + atmosphericLight[c];
                    dstPtr[baseIdx + c] = Math.Clamp(j, 0f, 1f);
                }
            }
        });

        refinedTrans.CopyTo(transmissionMap);

        return new DehazeResult(dehazedImage, transmissionMap, atmosphericLight);
    }

    private static unsafe ImageBuffer<float> BoxMin2D(ImageBuffer<float> src, int r)
    {
        int w = src.Width;
        int h = src.Height;
        var temp = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);
        var dst = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);

        float* s = src.DataPointer;
        float* t = temp.DataPointer;
        float* d = dst.DataPointer;

        // Horizontal min
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float minVal = s[row + x];
                for (int dx = -r; dx <= r; dx++)
                {
                    int sx = Math.Clamp(x + dx, 0, w - 1);
                    float v = s[row + sx];
                    if (v < minVal) minVal = v;
                }
                t[row + x] = minVal;
            }
        });

        // Vertical min
        Parallel.For(0, w, x =>
        {
            for (int y = 0; y < h; y++)
            {
                float minVal = t[y * w + x];
                for (int dy = -r; dy <= r; dy++)
                {
                    int sy = Math.Clamp(y + dy, 0, h - 1);
                    float v = t[sy * w + x];
                    if (v < minVal) minVal = v;
                }
                d[y * w + x] = minVal;
            }
        });

        temp.Dispose();
        return dst;
    }

    private static unsafe ImageBuffer<float> BoxMean2D(ImageBuffer<float> src, int r)
    {
        int w = src.Width;
        int h = src.Height;
        var temp = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);
        var dst = new ImageBuffer<float>(w, h, 1, PixelFormatType.GrayFloat32);

        float* s = src.DataPointer;
        float* t = temp.DataPointer;
        float* d = dst.DataPointer;

        // Horizontal box sum
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                int count = 0;
                for (int dx = -r; dx <= r; dx++)
                {
                    int sx = Math.Clamp(x + dx, 0, w - 1);
                    sum += s[row + sx];
                    count++;
                }
                t[row + x] = sum / count;
            }
        });

        // Vertical box sum
        Parallel.For(0, w, x =>
        {
            for (int y = 0; y < h; y++)
            {
                float sum = 0f;
                int count = 0;
                for (int dy = -r; dy <= r; dy++)
                {
                    int sy = Math.Clamp(y + dy, 0, h - 1);
                    sum += t[sy * w + x];
                    count++;
                }
                d[y * w + x] = sum / count;
            }
        });

        temp.Dispose();
        return dst;
    }
}
