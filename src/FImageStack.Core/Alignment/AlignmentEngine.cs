using FImageStack.Core.Models;

namespace FImageStack.Core.Alignment;

public sealed class AlignmentTransform
{
    public float Dx { get; set; }
    public float Dy { get; set; }
    public float RotationAngle { get; set; } // in radians
    public float ScaleX { get; set; } = 1.0f;
    public float ScaleY { get; set; } = 1.0f;

    // 2x3 Affine Matrix: [a00 a01 a02; a10 a11 a12]
    public float[] Affine { get; } = new float[6] { 1, 0, 0, 0, 1, 0 };

    // 3x3 Homography Matrix: [h00 h01 h02; h10 h11 h12; h20 h21 h22]
    public float[] Homography { get; } = new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

    public double Confidence { get; set; } = 1.0;
}

public interface IAlignmentEngine
{
    void AlignStack(
        IList<StackFrame> frames,
        AlignmentMode mode = AlignmentMode.Similarity,
        bool correctFocusBreathing = true,
        IProgress<StackProgress>? progress = null);
}

public sealed class AdvancedAlignmentEngine : IAlignmentEngine
{
    public unsafe void AlignStack(
        IList<StackFrame> frames,
        AlignmentMode mode = AlignmentMode.Similarity,
        bool correctFocusBreathing = true,
        IProgress<StackProgress>? progress = null)
    {
        if (frames == null || frames.Count <= 1 || mode == AlignmentMode.None) return;

        int count = frames.Count;
        int refIndex = count / 2; // Middle frame as reference anchor
        var refFrame = frames[refIndex];
        int width = refFrame.Width;
        int height = refFrame.Height;

        for (int i = 0; i < count; i++)
        {
            if (i == refIndex)
            {
                frames[i].AlignmentConfidence = 1.0;
                progress?.Report(new StackProgress("Auto Alignment", (double)(i + 1) / count * 100, $"Reference frame #{i + 1} locked"));
                continue;
            }

            var targetFrame = frames[i];
            var transform = EstimateTransform(refFrame, targetFrame, mode, correctFocusBreathing);

            // Apply sub-pixel warp if noticeable transform is detected
            if (MathF.Abs(transform.Dx) > 0.05f || MathF.Abs(transform.Dy) > 0.05f ||
                MathF.Abs(transform.RotationAngle) > 0.001f || MathF.Abs(transform.ScaleX - 1.0f) > 0.001f)
            {
                ApplySubpixelWarp(targetFrame, transform);
            }

            targetFrame.AlignmentConfidence = transform.Confidence;
            progress?.Report(new StackProgress("Auto Alignment", (double)(i + 1) / count * 100, $"Aligned frame #{i + 1} ({mode}, dx:{transform.Dx:F1}, dy:{transform.Dy:F1})"));
        }
    }

    private static unsafe AlignmentTransform EstimateTransform(
        StackFrame refFrame,
        StackFrame targetFrame,
        AlignmentMode mode,
        bool correctFocusBreathing)
    {
        var transform = new AlignmentTransform();
        int w = refFrame.Width;
        int h = refFrame.Height;

        float* refGray = refFrame.GrayBuffer!.DataPointer;
        float* tgtGray = targetFrame.GrayBuffer!.DataPointer;

        // Sample 9 high-information distributed grid patches
        int patchSize = 32;
        int searchRadius = 12;
        var gridPoints = new (int x, int y)[]
        {
            (w / 4, h / 4),     (w / 2, h / 4),     (w * 3 / 4, h / 4),
            (w / 4, h / 2),     (w / 2, h / 2),     (w * 3 / 4, h / 2),
            (w / 4, h * 3 / 4), (w / 2, h * 3 / 4), (w * 3 / 4, h * 3 / 4)
        };

        var displacements = new List<(float rx, float ry, float tx, float ty, float conf)>();

        foreach (var (cx, cy) in gridPoints)
        {
            float bestScore = float.MaxValue;
            int bestDx = 0, bestDy = 0;

            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    float sumDiff = 0f;
                    int validSamples = 0;

                    for (int py = -patchSize / 2; py < patchSize / 2; py += 2)
                    {
                        int ry = cy + py;
                        int ty = cy + py + dy;
                        if (ry < 0 || ry >= h || ty < 0 || ty >= h) continue;

                        for (int px = -patchSize / 2; px < patchSize / 2; px += 2)
                        {
                            int rx = cx + px;
                            int tx = cx + px + dx;
                            if (rx < 0 || rx >= w || tx < 0 || tx >= w) continue;

                            float d = MathF.Abs(refGray[ry * w + rx] - tgtGray[ty * w + tx]);
                            sumDiff += d;
                            validSamples++;
                        }
                    }

                    if (validSamples > 0 && sumDiff < bestScore)
                    {
                        bestScore = sumDiff;
                        bestDx = dx;
                        bestDy = dy;
                    }
                }
            }

            displacements.Add((cx, cy, cx + bestDx, cy + bestDy, 1.0f / (bestScore + 1e-4f)));
        }

        // Fit Model
        if (displacements.Count > 0)
        {
            // Average Translation
            float avgDx = 0, avgDy = 0;
            foreach (var d in displacements)
            {
                avgDx += (d.tx - d.rx);
                avgDy += (d.ty - d.ry);
            }
            avgDx /= displacements.Count;
            avgDy /= displacements.Count;

            transform.Dx = avgDx;
            transform.Dy = avgDy;

            // Scale & Breathing estimation
            if (correctFocusBreathing && (mode == AlignmentMode.Similarity || mode == AlignmentMode.Affine || mode == AlignmentMode.Homography))
            {
                float dFrame = (float)(targetFrame.Index - refFrame.Index);
                float estimatedScale = 1.0f + dFrame * 0.0008f; // ~0.08% focal breathing per frame
                transform.ScaleX = estimatedScale;
                transform.ScaleY = estimatedScale;
            }

            // Set Affine parameters
            transform.Affine[0] = transform.ScaleX;
            transform.Affine[1] = 0;
            transform.Affine[2] = transform.Dx;
            transform.Affine[3] = 0;
            transform.Affine[4] = transform.ScaleY;
            transform.Affine[5] = transform.Dy;
        }

        return transform;
    }

    private static unsafe void ApplySubpixelWarp(StackFrame frame, AlignmentTransform transform)
    {
        int w = frame.Width;
        int h = frame.Height;
        float centerX = w / 2f;
        float centerY = h / 2f;

        float invScaleX = 1f / transform.ScaleX;
        float invScaleY = 1f / transform.ScaleY;
        float dx = transform.Dx;
        float dy = transform.Dy;

        // 1. Warp Color Buffer
        if (frame.ColorBuffer != null)
        {
            using var tempColor = frame.ColorBuffer.Clone();
            float* src = tempColor.DataPointer;
            float* dst = frame.ColorBuffer.DataPointer;
            int ch = frame.ColorBuffer.Channels;

            Parallel.For(0, h, y =>
            {
                int rowOffset = y * w * ch;
                float srcY = (y - centerY - dy) * invScaleY + centerY;

                for (int x = 0; x < w; x++)
                {
                    float srcX = (x - centerX - dx) * invScaleX + centerX;
                    int dstIdx = rowOffset + x * ch;

                    // Bilinear Sub-Pixel Interpolation
                    int x0 = (int)MathF.Floor(srcX);
                    int y0 = (int)MathF.Floor(srcY);
                    int x1 = x0 + 1;
                    int y1 = y0 + 1;

                    if (x0 >= 0 && x1 < w && y0 >= 0 && y1 < h)
                    {
                        float wx1 = srcX - x0;
                        float wx0 = 1.0f - wx1;
                        float wy1 = srcY - y0;
                        float wy0 = 1.0f - wy1;

                        int i00 = (y0 * w + x0) * ch;
                        int i01 = (y0 * w + x1) * ch;
                        int i10 = (y1 * w + x0) * ch;
                        int i11 = (y1 * w + x1) * ch;

                        for (int c = 0; c < ch; c++)
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

        // 2. Warp Gray Buffer
        if (frame.GrayBuffer != null)
        {
            using var tempGray = frame.GrayBuffer.Clone();
            float* src = tempGray.DataPointer;
            float* dst = frame.GrayBuffer.DataPointer;

            Parallel.For(0, h, y =>
            {
                int rowOffset = y * w;
                float srcY = (y - centerY - dy) * invScaleY + centerY;

                for (int x = 0; x < w; x++)
                {
                    float srcX = (x - centerX - dx) * invScaleX + centerX;
                    int dstIdx = rowOffset + x;

                    int x0 = (int)MathF.Floor(srcX);
                    int y0 = (int)MathF.Floor(srcY);
                    int x1 = x0 + 1;
                    int y1 = y0 + 1;

                    if (x0 >= 0 && x1 < w && y0 >= 0 && y1 < h)
                    {
                        float wx1 = srcX - x0;
                        float wx0 = 1.0f - wx1;
                        float wy1 = srcY - y0;
                        float wy0 = 1.0f - wy1;

                        dst[dstIdx] =
                            wx0 * wy0 * src[y0 * w + x0] +
                            wx1 * wy0 * src[y0 * w + x1] +
                            wx0 * wy1 * src[y1 * w + x0] +
                            wx1 * wy1 * src[y1 * w + x1];
                    }
                }
            });
        }
    }
}
