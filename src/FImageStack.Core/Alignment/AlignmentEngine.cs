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

public sealed class LocalDisplacementMesh
{
    public int GridCols { get; }
    public int GridRows { get; }
    public float[,] Dx { get; }
    public float[,] Dy { get; }
    public float[,] Confidence { get; }

    public LocalDisplacementMesh(int cols, int rows)
    {
        GridCols = cols;
        GridRows = rows;
        Dx = new float[cols, rows];
        Dy = new float[cols, rows];
        Confidence = new float[cols, rows];
    }
}

public interface IAlignmentEngine
{
    void AlignStack(
        IList<StackFrame> frames,
        AlignmentMode mode = AlignmentMode.Similarity,
        bool correctFocusBreathing = true,
        bool enableLocalAlignment = false,
        int localGridSize = 8,
        LensDistortionParams lensDistortion = default,
        IProgress<StackProgress>? progress = null);
}

public sealed class AdvancedAlignmentEngine : IAlignmentEngine
{
    private readonly IFocusBreathingEstimator _focusBreathingEstimator;
    private readonly ILensDistortionCorrector _lensCorrector;
    private readonly IHomographyEstimator _homographyEstimator;
    private readonly IDenseOpticalFlowEstimator _opticalFlowEstimator;

    public AdvancedAlignmentEngine(
        IFocusBreathingEstimator? focusBreathingEstimator = null,
        ILensDistortionCorrector? lensCorrector = null,
        IHomographyEstimator? homographyEstimator = null,
        IDenseOpticalFlowEstimator? opticalFlowEstimator = null)
    {
        _focusBreathingEstimator = focusBreathingEstimator ?? new FocusBreathingEstimator();
        _lensCorrector = lensCorrector ?? new LensDistortionCorrector();
        _homographyEstimator = homographyEstimator ?? new HomographyEstimator();
        _opticalFlowEstimator = opticalFlowEstimator ?? new DenseOpticalFlowEstimator();
    }

    public unsafe void AlignStack(
        IList<StackFrame> frames,
        AlignmentMode mode = AlignmentMode.Similarity,
        bool correctFocusBreathing = true,
        bool enableLocalAlignment = false,
        int localGridSize = 8,
        LensDistortionParams lensDistortion = default,
        IProgress<StackProgress>? progress = null)
    {
        if (frames == null || frames.Count <= 1 || mode == AlignmentMode.None) return;

        int count = frames.Count;
        int refIndex = count / 2; // Middle frame as reference anchor
        var refFrame = frames[refIndex];
        int width = refFrame.Width;
        int height = refFrame.Height;

        // Stage 1: Lens Distortion Correction (Brown-Conrady: Radial k1/k2 + Tangential p1/p2)
        if (lensDistortion.HasDistortion)
        {
            for (int i = 0; i < count; i++)
            {
                _lensCorrector.UndistortFrame(frames[i], lensDistortion);
            }
            progress?.Report(new StackProgress("Auto Alignment", 5.0, $"Lens Distortion corrected (k1:{lensDistortion.K1:F3}, p1:{lensDistortion.P1:F3})"));
        }

        // Stage 2: Focus Breathing Scale Curve Estimation
        if (correctFocusBreathing && (mode == AlignmentMode.Similarity || mode == AlignmentMode.Affine || mode == AlignmentMode.Homography || mode == AlignmentMode.OpticalFlow))
        {
            var breathingResult = _focusBreathingEstimator.EstimateScaleCurve(frames, refIndex);
            progress?.Report(new StackProgress("Auto Alignment", 10.0, $"Focus Breathing Curve: ΔM={breathingResult.TotalMagnificationShiftPercentage:+0.00;-0.00}%, R²={breathingResult.R2:F2}"));
        }

        for (int i = 0; i < count; i++)
        {
            if (i == refIndex)
            {
                frames[i].AlignmentConfidence = 1.0;
                progress?.Report(new StackProgress("Auto Alignment", (double)(i + 1) / count * 100, $"Reference frame #{i + 1} locked"));
                continue;
            }

            var targetFrame = frames[i];

            // Stage 3: Global & Dense Vector Alignment
            if (mode == AlignmentMode.OpticalFlow)
            {
                using var flow = _opticalFlowEstimator.ComputeDenseFlow(refFrame, targetFrame, pyramidLevels: 3);
                flow.ApplyDenseWarp(targetFrame);
            }
            else if (mode == AlignmentMode.Homography)
            {
                var matches = FindPointCorrespondences(refFrame, targetFrame);
                var h = _homographyEstimator.EstimateHomography(matches);
                _homographyEstimator.ApplyHomographyWarp(targetFrame, h);
                targetFrame.AlignmentHomography = (float[])h.Clone();
            }
            else
            {
                var transform = EstimateTransform(refFrame, targetFrame, mode, correctFocusBreathing);
                if (MathF.Abs(transform.Dx) > 0.05f || MathF.Abs(transform.Dy) > 0.05f ||
                    MathF.Abs(transform.RotationAngle) > 0.001f || MathF.Abs(transform.ScaleX - 1.0f) > 0.001f)
                {
                    ApplySubpixelWarp(targetFrame, transform);
                }
            }

            // Stage 4: Local Elastic Non-Rigid Mesh Alignment (Macro 1:1 Parallax Compensation)
            if (enableLocalAlignment && localGridSize >= 4)
            {
                var localMesh = EstimateLocalElasticMesh(refFrame, targetFrame, localGridSize, localGridSize);
                ApplyLocalElasticWarp(targetFrame, localMesh);
            }

            targetFrame.AlignmentConfidence = 0.95;
            progress?.Report(new StackProgress("Auto Alignment", (double)(i + 1) / count * 100, $"Aligned frame #{i + 1} ({mode}{(correctFocusBreathing ? $", scale:{targetFrame.FocusBreathingScale * 100f:F1}%" : "")}{(enableLocalAlignment ? ", Local Mesh" : "")})"));
        }
    }

    private static unsafe List<(float srcX, float srcY, float dstX, float dstY)> FindPointCorrespondences(
        StackFrame refFrame,
        StackFrame targetFrame)
    {
        int w = refFrame.Width;
        int h = refFrame.Height;
        float* refGray = refFrame.GrayBuffer!.DataPointer;
        float* tgtGray = targetFrame.GrayBuffer!.DataPointer;

        int patchSize = 24;
        int searchRadius = 12;
        var gridPoints = new (int x, int y)[]
        {
            (w / 4, h / 4),     (w / 2, h / 4),     (w * 3 / 4, h / 4),
            (w / 4, h / 2),     (w / 2, h / 2),     (w * 3 / 4, h / 2),
            (w / 4, h * 3 / 4), (w / 2, h * 3 / 4), (w * 3 / 4, h * 3 / 4)
        };

        var matches = new List<(float srcX, float srcY, float dstX, float dstY)>();

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

                            sumDiff += MathF.Abs(refGray[ry * w + rx] - tgtGray[ty * w + tx]);
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

            matches.Add((cx, cy, cx + bestDx, cy + bestDy));
        }

        return matches;
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

        if (displacements.Count > 0)
        {
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

            if (correctFocusBreathing && (mode == AlignmentMode.Similarity || mode == AlignmentMode.Affine || mode == AlignmentMode.Homography))
            {
                float breathingScale = targetFrame.FocusBreathingScale > 0.01f ? targetFrame.FocusBreathingScale : 1.0f;
                transform.ScaleX = breathingScale;
                transform.ScaleY = breathingScale;
            }

            transform.Affine[0] = transform.ScaleX;
            transform.Affine[1] = 0;
            transform.Affine[2] = transform.Dx;
            transform.Affine[3] = 0;
            transform.Affine[4] = transform.ScaleY;
            transform.Affine[5] = transform.Dy;
        }

        return transform;
    }

    public static unsafe LocalDisplacementMesh EstimateLocalElasticMesh(
        StackFrame refFrame,
        StackFrame targetFrame,
        int gridCols = 8,
        int gridRows = 8)
    {
        var mesh = new LocalDisplacementMesh(gridCols, gridRows);
        int w = refFrame.Width;
        int h = refFrame.Height;

        float* refGray = refFrame.GrayBuffer!.DataPointer;
        float* tgtGray = targetFrame.GrayBuffer!.DataPointer;

        int patchW = Math.Max(16, w / gridCols);
        int patchH = Math.Max(16, h / gridRows);
        int searchRadius = 8;

        Parallel.For(0, gridRows, gy =>
        {
            int cy = (int)((gy + 0.5f) * h / gridRows);

            for (int gx = 0; gx < gridCols; gx++)
            {
                int cx = (int)((gx + 0.5f) * w / gridCols);

                float bestScore = float.MaxValue;
                int bestDx = 0, bestDy = 0;

                for (int dy = -searchRadius; dy <= searchRadius; dy++)
                {
                    for (int dx = -searchRadius; dx <= searchRadius; dx++)
                    {
                        float sumDiff = 0f;
                        int samples = 0;

                        for (int py = -patchH / 4; py <= patchH / 4; py += 2)
                        {
                            int ry = cy + py;
                            int ty = cy + py + dy;
                            if (ry < 0 || ry >= h || ty < 0 || ty >= h) continue;

                            for (int px = -patchW / 4; px <= patchW / 4; px += 2)
                            {
                                int rx = cx + px;
                                int tx = cx + px + dx;
                                if (rx < 0 || rx >= w || tx < 0 || tx >= w) continue;

                                sumDiff += MathF.Abs(refGray[ry * w + rx] - tgtGray[ty * w + tx]);
                                samples++;
                            }
                        }

                        if (samples > 0 && sumDiff < bestScore)
                        {
                            bestScore = sumDiff;
                            bestDx = dx;
                            bestDy = dy;
                        }
                    }
                }

                mesh.Dx[gx, gy] = bestDx;
                mesh.Dy[gx, gy] = bestDy;
                mesh.Confidence[gx, gy] = 1.0f / (bestScore + 1.0f);
            }
        });

        return mesh;
    }

    public static unsafe void ApplyLocalElasticWarp(StackFrame frame, LocalDisplacementMesh mesh)
    {
        int w = frame.Width;
        int h = frame.Height;
        int cols = mesh.GridCols;
        int rows = mesh.GridRows;

        // 1. Elastic Warp for Color Buffer
        if (frame.ColorBuffer != null)
        {
            using var tempColor = frame.ColorBuffer.Clone();
            float* src = tempColor.DataPointer;
            float* dst = frame.ColorBuffer.DataPointer;
            int ch = frame.ColorBuffer.Channels;

            Parallel.For(0, h, y =>
            {
                int rowOffset = y * w * ch;
                float normY = ((float)y / h) * rows - 0.5f;
                int gy0 = Math.Clamp((int)MathF.Floor(normY), 0, rows - 1);
                int gy1 = Math.Clamp(gy0 + 1, 0, rows - 1);
                float ty = Math.Clamp(normY - gy0, 0f, 1f);

                for (int x = 0; x < w; x++)
                {
                    float normX = ((float)x / w) * cols - 0.5f;
                    int gx0 = Math.Clamp((int)MathF.Floor(normX), 0, cols - 1);
                    int gx1 = Math.Clamp(gx0 + 1, 0, cols - 1);
                    float tx = Math.Clamp(normX - gx0, 0f, 1f);

                    // Bilinear interpolation of local displacement vector (dx, dy)
                    float dx0 = mesh.Dx[gx0, gy0] * (1f - tx) + mesh.Dx[gx1, gy0] * tx;
                    float dx1 = mesh.Dx[gx0, gy1] * (1f - tx) + mesh.Dx[gx1, gy1] * tx;
                    float localDx = dx0 * (1f - ty) + dx1 * ty;

                    float dy0 = mesh.Dy[gx0, gy0] * (1f - tx) + mesh.Dy[gx1, gy0] * tx;
                    float dy1 = mesh.Dy[gx0, gy1] * (1f - tx) + mesh.Dy[gx1, gy1] * tx;
                    float localDy = dy0 * (1f - ty) + dy1 * ty;

                    float srcX = x + localDx;
                    float srcY = y + localDy;

                    int x0 = (int)MathF.Floor(srcX);
                    int y0 = (int)MathF.Floor(srcY);
                    int x1 = x0 + 1;
                    int y1 = y0 + 1;

                    int dstIdx = rowOffset + x * ch;

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

        // 2. Elastic Warp for Gray Buffer
        if (frame.GrayBuffer != null)
        {
            using var tempGray = frame.GrayBuffer.Clone();
            float* src = tempGray.DataPointer;
            float* dst = frame.GrayBuffer.DataPointer;

            Parallel.For(0, h, y =>
            {
                int rowOffset = y * w;
                float normY = ((float)y / h) * rows - 0.5f;
                int gy0 = Math.Clamp((int)MathF.Floor(normY), 0, rows - 1);
                int gy1 = Math.Clamp(gy0 + 1, 0, rows - 1);
                float ty = Math.Clamp(normY - gy0, 0f, 1f);

                for (int x = 0; x < w; x++)
                {
                    float normX = ((float)x / w) * cols - 0.5f;
                    int gx0 = Math.Clamp((int)MathF.Floor(normX), 0, cols - 1);
                    int gx1 = Math.Clamp(gx0 + 1, 0, cols - 1);
                    float tx = Math.Clamp(normX - gx0, 0f, 1f);

                    float dx0 = mesh.Dx[gx0, gy0] * (1f - tx) + mesh.Dx[gx1, gy0] * tx;
                    float dx1 = mesh.Dx[gx0, gy1] * (1f - tx) + mesh.Dx[gx1, gy1] * tx;
                    float localDx = dx0 * (1f - ty) + dx1 * ty;

                    float dy0 = mesh.Dy[gx0, gy0] * (1f - tx) + mesh.Dy[gx1, gy0] * tx;
                    float dy1 = mesh.Dy[gx0, gy1] * (1f - tx) + mesh.Dy[gx1, gy1] * tx;
                    float localDy = dy0 * (1f - ty) + dy1 * ty;

                    float srcX = x + localDx;
                    float srcY = y + localDy;

                    int x0 = (int)MathF.Floor(srcX);
                    int y0 = (int)MathF.Floor(srcY);
                    int x1 = x0 + 1;
                    int y1 = y0 + 1;

                    int dstIdx = rowOffset + x;

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
