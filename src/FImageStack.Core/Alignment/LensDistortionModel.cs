using System.Runtime.CompilerServices;
using FImageStack.Core.Models;

namespace FImageStack.Core.Alignment;

/// <summary>
/// Parameters for Brown-Conrady Lens Distortion Model (Radial k1, k2 and Tangential p1, p2).
/// </summary>
public readonly record struct LensDistortionParams(
    float K1 = 0f,
    float K2 = 0f,
    float P1 = 0f,
    float P2 = 0f,
    float Cx = 0.5f,
    float Cy = 0.5f,
    float FocalLength = 1.0f)
{
    public bool HasDistortion =>
        MathF.Abs(K1) > 1e-5f ||
        MathF.Abs(K2) > 1e-5f ||
        MathF.Abs(P1) > 1e-5f ||
        MathF.Abs(P2) > 1e-5f;

    public static LensDistortionParams Identity => new();
}

public interface ILensDistortionCorrector
{
    void UndistortFrame(StackFrame frame, LensDistortionParams parameters);
    void UndistortBuffer(ImageBuffer<float> buffer, LensDistortionParams parameters);
}

public sealed class LensDistortionCorrector : ILensDistortionCorrector
{
    public void UndistortFrame(StackFrame frame, LensDistortionParams parameters)
    {
        if (!parameters.HasDistortion) return;

        if (frame.GrayBuffer != null)
        {
            UndistortBuffer(frame.GrayBuffer, parameters);
        }

        if (frame.ColorBuffer != null)
        {
            UndistortBuffer(frame.ColorBuffer, parameters);
        }
    }

    public unsafe void UndistortBuffer(ImageBuffer<float> buffer, LensDistortionParams parameters)
    {
        if (!parameters.HasDistortion) return;

        int width = buffer.Width;
        int height = buffer.Height;
        int channels = buffer.Channels;

        using var srcClone = buffer.Clone();
        float* src = srcClone.DataPointer;
        float* dst = buffer.DataPointer;

        float cx = parameters.Cx * width;
        float cy = parameters.Cy * height;
        float f = parameters.FocalLength * MathF.Max(width, height);
        float invF = 1f / (f > 1e-4f ? f : 1.0f);

        float k1 = parameters.K1;
        float k2 = parameters.K2;
        float p1 = parameters.P1;
        float p2 = parameters.P2;

        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width * channels;
            float v = (y - cy) * invF;

            for (int x = 0; x < width; x++)
            {
                float u = (x - cx) * invF;
                float r2 = u * u + v * v;
                float r4 = r2 * r2;

                // 1. Radial factor
                float rad = 1.0f + k1 * r2 + k2 * r4;

                // 2. Tangential distortion
                float dxTan = 2f * p1 * u * v + p2 * (r2 + 2f * u * u);
                float dyTan = p1 * (r2 + 2f * v * v) + 2f * p2 * u * v;

                // 3. Map to distorted source coordinate
                float uSrc = u * rad + dxTan;
                float vSrc = v * rad + dyTan;

                float srcX = uSrc * f + cx;
                float srcY = vSrc * f + cy;

                int x0 = (int)MathF.Floor(srcX);
                int y0 = (int)MathF.Floor(srcY);
                int x1 = x0 + 1;
                int y1 = y0 + 1;

                int dstIdx = rowOffset + x * channels;

                if (x0 >= 0 && x1 < width && y0 >= 0 && y1 < height)
                {
                    float wx1 = srcX - x0;
                    float wx0 = 1.0f - wx1;
                    float wy1 = srcY - y0;
                    float wy0 = 1.0f - wy1;

                    int i00 = (y0 * width + x0) * channels;
                    int i01 = (y0 * width + x1) * channels;
                    int i10 = (y1 * width + x0) * channels;
                    int i11 = (y1 * width + x1) * channels;

                    for (int c = 0; c < channels; c++)
                    {
                        dst[dstIdx + c] =
                            wx0 * wy0 * src[i00 + c] +
                            wx1 * wy0 * src[i01 + c] +
                            wx0 * wy1 * src[i10 + c] +
                            wx1 * wy1 * src[i11 + c];
                    }
                }
                else
                {
                    for (int c = 0; c < channels; c++)
                    {
                        dst[dstIdx + c] = 0f;
                    }
                }
            }
        });
    }
}
