using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using FImageStack.Core.Models;

namespace FImageStack.Core.FocusMeasure;

public interface IFocusMeasureEngine
{
    FocusMeasureMethod Method { get; }
    void ComputeFocusMap(ImageBuffer<float> grayImage, ImageBuffer<float> outputFocusMap, int windowRadius = 2);
}

public sealed class ModifiedLaplacianFocusMeasure : IFocusMeasureEngine
{
    public FocusMeasureMethod Method => FocusMeasureMethod.ModifiedLaplacian;

    public unsafe void ComputeFocusMap(ImageBuffer<float> grayImage, ImageBuffer<float> outputFocusMap, int windowRadius = 2)
    {
        int width = grayImage.Width;
        int height = grayImage.Height;

        // Temporary buffer for raw modified laplacian
        using var rawLaplacian = new ImageBuffer<float>(width, height);
        float* src = grayImage.DataPointer;
        float* lap = rawLaplacian.DataPointer;

        // Step 1: Compute Modified Laplacian ML(x, y) = |2I(x,y) - I(x-1,y) - I(x+1,y)| + |2I(x,y) - I(x,y-1) - I(x,y+1)|
        Parallel.For(1, height - 1, y =>
        {
            int rowOffset = y * width;
            int prevRowOffset = (y - 1) * width;
            int nextRowOffset = (y + 1) * width;

            for (int x = 1; x < width - 1; x++)
            {
                float center = src[rowOffset + x];
                float lx = MathF.Abs(2f * center - src[rowOffset + x - 1] - src[rowOffset + x + 1]);
                float ly = MathF.Abs(2f * center - src[prevRowOffset + x] - src[nextRowOffset + x]);
                lap[rowOffset + x] = lx + ly;
            }
        });

        // Step 2: Sum Modified Laplacian over a local window (SML)
        float* dst = outputFocusMap.DataPointer;
        int radius = Math.Max(1, windowRadius);

        Parallel.For(0, height, y =>
        {
            int yMin = Math.Max(0, y - radius);
            int yMax = Math.Min(height - 1, y + radius);

            for (int x = 0; x < width; x++)
            {
                int xMin = Math.Max(0, x - radius);
                int xMax = Math.Min(width - 1, x + radius);

                float sum = 0f;
                for (int wy = yMin; wy <= yMax; wy++)
                {
                    int wOffset = wy * width;
                    for (int wx = xMin; wx <= xMax; wx++)
                    {
                        sum += lap[wOffset + wx];
                    }
                }

                dst[y * width + x] = sum;
            }
        });
    }
}

public sealed class TenengradFocusMeasure : IFocusMeasureEngine
{
    public FocusMeasureMethod Method => FocusMeasureMethod.Tenengrad;

    public unsafe void ComputeFocusMap(ImageBuffer<float> grayImage, ImageBuffer<float> outputFocusMap, int windowRadius = 2)
    {
        int width = grayImage.Width;
        int height = grayImage.Height;

        using var gradBuffer = new ImageBuffer<float>(width, height);
        float* src = grayImage.DataPointer;
        float* grad = gradBuffer.DataPointer;

        // Sobel gradient magnitude squared: (gx^2 + gy^2)
        Parallel.For(1, height - 1, y =>
        {
            int rowOffset = y * width;
            int prevRow = (y - 1) * width;
            int nextRow = (y + 1) * width;

            for (int x = 1; x < width - 1; x++)
            {
                float gx = (src[nextRow + x + 1] + 2f * src[rowOffset + x + 1] + src[prevRow + x + 1])
                         - (src[nextRow + x - 1] + 2f * src[rowOffset + x - 1] + src[prevRow + x - 1]);

                float gy = (src[nextRow + x - 1] + 2f * src[nextRow + x] + src[nextRow + x + 1])
                         - (src[prevRow + x - 1] + 2f * src[prevRow + x] + src[prevRow + x + 1]);

                grad[rowOffset + x] = gx * gx + gy * gy;
            }
        });

        // Local window integration
        float* dst = outputFocusMap.DataPointer;
        int radius = Math.Max(1, windowRadius);

        Parallel.For(0, height, y =>
        {
            int yMin = Math.Max(0, y - radius);
            int yMax = Math.Min(height - 1, y + radius);

            for (int x = 0; x < width; x++)
            {
                int xMin = Math.Max(0, x - radius);
                int xMax = Math.Min(width - 1, x + radius);

                float sum = 0f;
                for (int wy = yMin; wy <= yMax; wy++)
                {
                    int wOffset = wy * width;
                    for (int wx = xMin; wx <= xMax; wx++)
                    {
                        sum += grad[wOffset + wx];
                    }
                }
                dst[y * width + x] = sum;
            }
        });
    }
}
