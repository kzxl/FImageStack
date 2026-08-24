using System.Runtime.CompilerServices;
using FImageStack.Core.Models;

namespace FImageStack.Core.Alignment;

public interface IHomographyEstimator
{
    float[] EstimateHomography(IReadOnlyList<(float srcX, float srcY, float dstX, float dstY)> points);
    float[] InvertHomography(float[] h);
    void ApplyHomographyWarp(StackFrame frame, float[] homographyMatrix);
    void ApplyHomographyWarpToBuffer(ImageBuffer<float> buffer, float[] homographyMatrix);
}

public sealed class HomographyEstimator : IHomographyEstimator
{
    public float[] EstimateHomography(IReadOnlyList<(float srcX, float srcY, float dstX, float dstY)> points)
    {
        // 3x3 identity default
        float[] h = new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        if (points == null || points.Count < 4)
            return h;

        int n = Math.Min(points.Count, 16);
        // Direct Linear Transform (DLT) using 8-variable normal equations: A^T A h = A^T b
        // where h = [h00, h01, h02, h10, h11, h12, h20, h21]^T and h22 = 1.0
        double[,] ata = new double[8, 8];
        double[] atb = new double[8];

        for (int i = 0; i < n; i++)
        {
            var (x, y, u, v) = points[i];

            // Row 1: [-x, -y, -1,  0,  0,  0,  u*x,  u*y] * h = -u
            double[] r1 = new double[8] { -x, -y, -1, 0, 0, 0, u * x, u * y };
            double b1 = -u;

            // Row 2: [ 0,  0,  0, -x, -y, -1,  v*x,  v*y] * h = -v
            double[] r2 = new double[8] { 0, 0, 0, -x, -y, -1, v * x, v * y };
            double b2 = -v;

            for (int r = 0; r < 8; r++)
            {
                atb[r] += r1[r] * b1 + r2[r] * b2;
                for (int c = 0; c < 8; c++)
                {
                    ata[r, c] += r1[r] * r1[c] + r2[r] * r2[c];
                }
            }
        }

        // Regularize diagonal for numerical stability
        for (int r = 0; r < 8; r++) ata[r, r] += 1e-4;

        // Gaussian elimination with partial pivoting to solve 8x8 system
        double[]? solution = SolveLinearSystem8(ata, atb);
        if (solution != null)
        {
            h[0] = (float)solution[0];
            h[1] = (float)solution[1];
            h[2] = (float)solution[2];
            h[3] = (float)solution[3];
            h[4] = (float)solution[4];
            h[5] = (float)solution[5];
            h[6] = (float)solution[6];
            h[7] = (float)solution[7];
            h[8] = 1.0f;
        }

        return h;
    }

    public float[] InvertHomography(float[] h)
    {
        float[] inv = new float[9];
        // Adjugate matrix
        inv[0] = h[4] * h[8] - h[5] * h[7];
        inv[1] = h[2] * h[7] - h[1] * h[8];
        inv[2] = h[1] * h[5] - h[2] * h[4];

        inv[3] = h[5] * h[6] - h[3] * h[8];
        inv[4] = h[0] * h[8] - h[2] * h[6];
        inv[5] = h[2] * h[3] - h[0] * h[5];

        inv[6] = h[3] * h[7] - h[4] * h[6];
        inv[7] = h[1] * h[6] - h[0] * h[7];
        inv[8] = h[0] * h[4] - h[1] * h[3];

        float det = h[0] * inv[0] + h[1] * inv[3] + h[2] * inv[6];
        if (MathF.Abs(det) > 1e-7f)
        {
            float invDet = 1.0f / det;
            for (int i = 0; i < 9; i++) inv[i] *= invDet;
        }
        else
        {
            // Identity fallback
            return new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        }

        return inv;
    }

    public void ApplyHomographyWarp(StackFrame frame, float[] homographyMatrix)
    {
        if (homographyMatrix == null || homographyMatrix.Length != 9) return;

        if (frame.GrayBuffer != null)
        {
            ApplyHomographyWarpToBuffer(frame.GrayBuffer, homographyMatrix);
        }

        if (frame.ColorBuffer != null)
        {
            ApplyHomographyWarpToBuffer(frame.ColorBuffer, homographyMatrix);
        }
    }

    public unsafe void ApplyHomographyWarpToBuffer(ImageBuffer<float> buffer, float[] homographyMatrix)
    {
        int width = buffer.Width;
        int height = buffer.Height;
        int channels = buffer.Channels;

        float[] invH = InvertHomography(homographyMatrix);
        float h00 = invH[0], h01 = invH[1], h02 = invH[2];
        float h10 = invH[3], h11 = invH[4], h12 = invH[5];
        float h20 = invH[6], h21 = invH[7], h22 = invH[8];

        using var srcClone = buffer.Clone();
        float* src = srcClone.DataPointer;
        float* dst = buffer.DataPointer;

        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width * channels;

            for (int x = 0; x < width; x++)
            {
                float w = h20 * x + h21 * y + h22;
                float invW = MathF.Abs(w) > 1e-6f ? 1.0f / w : 0f;

                float srcX = (h00 * x + h01 * y + h02) * invW;
                float srcY = (h10 * x + h11 * y + h12) * invW;

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

    private static double[]? SolveLinearSystem8(double[,] a, double[] b)
    {
        int n = 8;
        double[,] m = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = a[i, j];
            m[i, n] = b[i];
        }

        for (int p = 0; p < n; p++)
        {
            int maxRow = p;
            for (int i = p + 1; i < n; i++)
            {
                if (Math.Abs(m[i, p]) > Math.Abs(m[maxRow, p])) maxRow = i;
            }

            for (int k = p; k <= n; k++)
            {
                (m[p, k], m[maxRow, k]) = (m[maxRow, k], m[p, k]);
            }

            if (Math.Abs(m[p, p]) < 1e-12) return null;

            for (int i = p + 1; i < n; i++)
            {
                double factor = m[i, p] / m[p, p];
                for (int j = p; j <= n; j++) m[i, j] -= factor * m[p, j];
            }
        }

        double[] x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0;
            for (int j = i + 1; j < n; j++) sum += m[i, j] * x[j];
            x[i] = (m[i, n] - sum) / m[i, i];
        }

        return x;
    }
}
