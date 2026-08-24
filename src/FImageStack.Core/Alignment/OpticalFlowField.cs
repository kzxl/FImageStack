using FImageStack.Core.Models;

namespace FImageStack.Core.Alignment;

public sealed class OpticalFlowField : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public ImageBuffer<float> Vx { get; }
    public ImageBuffer<float> Vy { get; }
    public ImageBuffer<float> Confidence { get; }

    public OpticalFlowField(int width, int height)
    {
        Width = width;
        Height = height;
        Vx = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
        Vy = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
        Confidence = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
    }

    public void ApplyDenseWarp(StackFrame frame)
    {
        if (frame.GrayBuffer != null)
        {
            ApplyDenseWarpToBuffer(frame.GrayBuffer);
        }

        if (frame.ColorBuffer != null)
        {
            ApplyDenseWarpToBuffer(frame.ColorBuffer);
        }
    }

    public unsafe void ApplyDenseWarpToBuffer(ImageBuffer<float> buffer)
    {
        int w = buffer.Width;
        int h = buffer.Height;
        int channels = buffer.Channels;

        using var srcClone = buffer.Clone();
        float* src = srcClone.DataPointer;
        float* dst = buffer.DataPointer;

        float* vxPtr = Vx.DataPointer;
        float* vyPtr = Vy.DataPointer;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w;
            int pixelOffset = rowOffset * channels;

            for (int x = 0; x < w; x++)
            {
                int idx = rowOffset + x;
                float u = vxPtr[idx];
                float v = vyPtr[idx];

                float srcX = x + u;
                float srcY = y + v;

                int x0 = (int)MathF.Floor(srcX);
                int y0 = (int)MathF.Floor(srcY);
                int x1 = x0 + 1;
                int y1 = y0 + 1;

                int dstIdx = pixelOffset + x * channels;

                if (x0 >= 0 && x1 < w && y0 >= 0 && y1 < h)
                {
                    float wx1 = srcX - x0;
                    float wx0 = 1.0f - wx1;
                    float wy1 = srcY - y0;
                    float wy0 = 1.0f - wy1;

                    int i00 = (y0 * w + x0) * channels;
                    int i01 = (y0 * w + x1) * channels;
                    int i10 = (y1 * w + x0) * channels;
                    int i11 = (y1 * w + x1) * channels;

                    for (int c = 0; c < channels; c++)
                    {
                        dst[dstIdx + c] =
                            wx0 * wy0 * src[i00 + c] +
                            wx1 * wy0 * src[i01 + c] +
                            wx0 * wy1 * src[i10 + c] +
                            wx1 * wy1 * src[i11 + c];
                    }
                }
            }
        });
    }

    public void Dispose()
    {
        Vx.Dispose();
        Vy.Dispose();
        Confidence.Dispose();
    }
}
