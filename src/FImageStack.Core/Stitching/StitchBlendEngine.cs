using System.Runtime.CompilerServices;
using FImageStack.Core.Alignment;
using FImageStack.Core.Models;

namespace FImageStack.Core.Stitching;

public interface IStitchBlendEngine
{
    StitchResult BlendTilesIntoCanvas(
        IReadOnlyList<StackFrame> frames,
        IReadOnlyList<float[]> globalHomographies,
        StitchSettings settings,
        int referenceIndex,
        IProgress<StackProgress>? progress = null);
}

public sealed class StitchBlendEngine : IStitchBlendEngine
{
    private readonly IHomographyEstimator _homographyEstimator;

    public StitchBlendEngine(IHomographyEstimator? homographyEstimator = null)
    {
        _homographyEstimator = homographyEstimator ?? new HomographyEstimator();
    }

    public unsafe StitchResult BlendTilesIntoCanvas(
        IReadOnlyList<StackFrame> frames,
        IReadOnlyList<float[]> globalHomographies,
        StitchSettings settings,
        int referenceIndex,
        IProgress<StackProgress>? progress = null)
    {
        int count = frames.Count;
        int channels = frames[0].ColorBuffer?.Channels ?? 3;

        // 1. Calculate Global Canvas Bounds across all frames
        float globalMinX = float.MaxValue;
        float globalMinY = float.MaxValue;
        float globalMaxX = float.MinValue;
        float globalMaxY = float.MinValue;

        var tileInfos = new List<StitchTileInfo>(count);

        for (int i = 0; i < count; i++)
        {
            var f = frames[i];
            float[] h = globalHomographies[i];

            var (tMinX, tMinY, tMaxX, tMaxY) = ComputeTileBounds(f.Width, f.Height, h);

            globalMinX = MathF.Min(globalMinX, tMinX);
            globalMinY = MathF.Min(globalMinY, tMinY);
            globalMaxX = MathF.Max(globalMaxX, tMaxX);
            globalMaxY = MathF.Max(globalMaxY, tMaxY);

            tileInfos.Add(new StitchTileInfo
            {
                FrameIndex = i,
                SourceWidth = f.Width,
                SourceHeight = f.Height,
                MinX = tMinX,
                MinY = tMinY,
                MaxX = tMaxX,
                MaxY = tMaxY
            });
        }

        // Add 2px border padding
        int canvasW = (int)MathF.Ceiling(globalMaxX - globalMinX) + 4;
        int canvasH = (int)MathF.Ceiling(globalMaxY - globalMinY) + 4;

        // Origin offset shifts coordinates so globalMin maps to (2, 2)
        float offsetX = -globalMinX + 2f;
        float offsetY = -globalMinY + 2f;

        // Memory safety clamp
        int maxDim = Math.Max(2048, settings.MaxOutputDimension);
        if (canvasW > maxDim || canvasH > maxDim)
        {
            float downscale = MathF.Min((float)maxDim / canvasW, (float)maxDim / canvasH);
            canvasW = (int)(canvasW * downscale);
            canvasH = (int)(canvasH * downscale);
            offsetX *= downscale;
            offsetY *= downscale;
        }

        canvasW = Math.Max(canvasW, frames[0].Width);
        canvasH = Math.Max(canvasH, frames[0].Height);

        // 2. Setup Canvas Homographies (Mapping to/from expanded canvas space)
        float[] tCanvas = new float[9] { 1, 0, offsetX, 0, 1, offsetY, 0, 0, 1 };

        for (int i = 0; i < count; i++)
        {
            var tile = tileInfos[i];
            float[] hToCanvas = StitchRegistrationEngine.MultiplyHomographies(tCanvas, globalHomographies[i]);
            float[] invHFromCanvas = _homographyEstimator.InvertHomography(hToCanvas);

            tile.HomographyToCanvas = hToCanvas;
            tile.InvHomographyFromCanvas = invHFromCanvas;

            // Recalculate tile bounds in canvas coordinate space
            var (cMinX, cMinY, cMaxX, cMaxY) = ComputeTileBounds(frames[i].Width, frames[i].Height, hToCanvas);
            tile.MinX = Math.Clamp(cMinX, 0, canvasW - 1);
            tile.MinY = Math.Clamp(cMinY, 0, canvasH - 1);
            tile.MaxX = Math.Clamp(cMaxX, 0, canvasW - 1);
            tile.MaxY = Math.Clamp(cMaxY, 0, canvasH - 1);
        }

        // 3. Optional Exposure Gain Balancing relative to Reference Frame
        if (settings.EnableGainCompensation && referenceIndex >= 0 && referenceIndex < count)
        {
            float refMean = ComputeMeanLuminance(frames[referenceIndex]);
            for (int i = 0; i < count; i++)
            {
                if (i == referenceIndex)
                {
                    tileInfos[i].GainFactor = 1.0f;
                }
                else
                {
                    float frameMean = ComputeMeanLuminance(frames[i]);
                    if (frameMean > 0.01f && refMean > 0.01f)
                    {
                        tileInfos[i].GainFactor = Math.Clamp(refMean / frameMean, 0.80f, 1.25f);
                    }
                }
            }
        }

        // 4. Allocate Canvas Buffers (Accumulator + Weight Map)
        var accumBuffer = new ImageBuffer<float>(canvasW, canvasH, channels, PixelFormatType.RgbFloat32);
        var weightBuffer = new ImageBuffer<float>(canvasW, canvasH, 1, PixelFormatType.GrayFloat32);

        float* accPtr = accumBuffer.DataPointer;
        float* weightPtr = weightBuffer.DataPointer;

        var rowLocks = new object[canvasH];
        for (int y = 0; y < canvasH; y++) rowLocks[y] = new object();

        // 5. Splat & Blend Tiles into Global Canvas
        for (int i = 0; i < count; i++)
        {
            var frame = frames[i];
            var tile = tileInfos[i];
            var colorBuf = frame.ColorBuffer ?? throw new InvalidOperationException($"Frame {i} ColorBuffer is null");

            float* srcPtr = colorBuf.DataPointer;
            int srcW = frame.Width;
            int srcH = frame.Height;
            float gain = tile.GainFactor;

            float[] invH = tile.InvHomographyFromCanvas;
            float h00 = invH[0], h01 = invH[1], h02 = invH[2];
            float h10 = invH[3], h11 = invH[4], h12 = invH[5];
            float h20 = invH[6], h21 = invH[7], h22 = invH[8];

            int startY = (int)MathF.Floor(tile.MinY);
            int endY = (int)MathF.Ceiling(tile.MaxY);
            int startX = (int)MathF.Floor(tile.MinX);
            int endX = (int)MathF.Ceiling(tile.MaxX);

            float featherDist = Math.Max(16f, Math.Min(srcW, srcH) * settings.FeatherRadiusPercentage);
            float invFeather = 1.0f / featherDist;

            Parallel.For(startY, endY + 1, cy =>
            {
                if (cy < 0 || cy >= canvasH) return;

                int canvasRow = cy * canvasW * channels;
                int weightRow = cy * canvasW;

                for (int cx = startX; cx <= endX; cx++)
                {
                    if (cx < 0 || cx >= canvasW) continue;

                    // Inverse project from canvas (cx, cy) to frame source space (fx, fy)
                    float w = h20 * cx + h21 * cy + h22;
                    if (MathF.Abs(w) < 1e-6f) continue;
                    float invW = 1.0f / w;

                    float fx = (h00 * cx + h01 * cy + h02) * invW;
                    float fy = (h10 * cx + h11 * cy + h12) * invW;

                    if (fx < 0 || fx > srcW - 1 || fy < 0 || fy > srcH - 1)
                        continue;

                    // Bilinear sample from source image
                    SampleBilinear(srcPtr, srcW, srcH, channels, fx, fy, out float r, out float g, out float b);

                    // Compute seamless boundary feather weight
                    float distBorderX = MathF.Min(fx, srcW - 1 - fx);
                    float distBorderY = MathF.Min(fy, srcH - 1 - fy);
                    float distBorder = MathF.Min(distBorderX, distBorderY);

                    float weight = 1.0f;
                    if (settings.BlendingMode == SeamBlendingMode.LinearFeathering)
                    {
                        weight = Math.Clamp(distBorder * invFeather, 0.001f, 1.0f);
                        // Smoothstep curve for soft seam boundary transition
                        weight = weight * weight * (3f - 2f * weight);
                    }

                    int canvasIdx = canvasRow + cx * channels;
                    int weightIdx = weightRow + cx;

                    lock (rowLocks[cy])
                    {
                        weightPtr[weightIdx] += weight;
                        accPtr[canvasIdx + 0] += r * gain * weight;
                        accPtr[canvasIdx + 1] += g * gain * weight;
                        accPtr[canvasIdx + 2] += b * gain * weight;
                    }
                }
            });

            progress?.Report(new StackProgress(
                "Canvas Blending",
                50.0 + ((double)(i + 1) / count * 45.0),
                $"Splatting tile #{i + 1}/{count} onto canvas ({canvasW}x{canvasH})"));
        }

        // 6. Final Canvas Normalization
        var finalCanvas = new ImageBuffer<float>(canvasW, canvasH, channels, PixelFormatType.RgbFloat32);
        float* dstPtr = finalCanvas.DataPointer;

        Parallel.For(0, canvasH, cy =>
        {
            int rowOffset = cy * canvasW * channels;
            int weightOffset = cy * canvasW;

            for (int cx = 0; cx < canvasW; cx++)
            {
                int wIdx = weightOffset + cx;
                float totalW = weightPtr[wIdx];
                int dstIdx = rowOffset + cx * channels;

                if (totalW > 1e-5f)
                {
                    float invTotalW = 1.0f / totalW;
                    dstPtr[dstIdx + 0] = Math.Clamp(accPtr[dstIdx + 0] * invTotalW, 0f, 1f);
                    dstPtr[dstIdx + 1] = Math.Clamp(accPtr[dstIdx + 1] * invTotalW, 0f, 1f);
                    dstPtr[dstIdx + 2] = Math.Clamp(accPtr[dstIdx + 2] * invTotalW, 0f, 1f);
                }
                else
                {
                    dstPtr[dstIdx + 0] = 0f;
                    dstPtr[dstIdx + 1] = 0f;
                    dstPtr[dstIdx + 2] = 0f;
                }
            }
        });

        accumBuffer.Dispose();
        weightBuffer.Dispose();

        progress?.Report(new StackProgress("Canvas Blending", 100.0, $"Mosaic canvas generated ({canvasW}x{canvasH})"));

        return new StitchResult(
            finalCanvas,
            canvasW,
            canvasH,
            offsetX,
            offsetY,
            count,
            tileInfos);
    }

    private static (float minX, float minY, float maxX, float maxY) ComputeTileBounds(int w, int h, float[] H)
    {
        var corners = new (float x, float y)[]
        {
            (0, 0),
            (w, 0),
            (w, h),
            (0, h)
        };

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var (x, y) in corners)
        {
            float denom = H[6] * x + H[7] * y + H[8];
            float invDenom = MathF.Abs(denom) > 1e-6f ? 1.0f / denom : 1.0f;

            float px = (H[0] * x + H[1] * y + H[2]) * invDenom;
            float py = (H[3] * x + H[4] * y + H[5]) * invDenom;

            minX = MathF.Min(minX, px);
            minY = MathF.Min(minY, py);
            maxX = MathF.Max(maxX, px);
            maxY = MathF.Max(maxY, py);
        }

        return (minX, minY, maxX, maxY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void SampleBilinear(
        float* src, int w, int h, int ch, float x, float y,
        out float r, out float g, out float b)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int x1 = Math.Min(x0 + 1, w - 1);
        int y1 = Math.Min(y0 + 1, h - 1);

        float wx1 = x - x0;
        float wx0 = 1.0f - wx1;
        float wy1 = y - y0;
        float wy0 = 1.0f - wy1;

        int i00 = (y0 * w + x0) * ch;
        int i01 = (y0 * w + x1) * ch;
        int i10 = (y1 * w + x0) * ch;
        int i11 = (y1 * w + x1) * ch;

        r = wx0 * wy0 * src[i00 + 0] + wx1 * wy0 * src[i01 + 0] + wx0 * wy1 * src[i10 + 0] + wx1 * wy1 * src[i11 + 0];
        g = wx0 * wy0 * src[i00 + 1] + wx1 * wy0 * src[i01 + 1] + wx0 * wy1 * src[i10 + 1] + wx1 * wy1 * src[i11 + 1];
        b = wx0 * wy0 * src[i00 + 2] + wx1 * wy0 * src[i01 + 2] + wx0 * wy1 * src[i10 + 2] + wx1 * wy1 * src[i11 + 2];
    }

    private static unsafe float ComputeMeanLuminance(StackFrame frame)
    {
        if (frame.GrayBuffer == null) return 0.5f;
        int w = frame.Width;
        int h = frame.Height;
        float* ptr = frame.GrayBuffer.DataPointer;
        double sum = 0.0;
        int step = Math.Max(1, (w * h) / 10000); // 10k samples
        int samples = 0;

        for (int i = 0; i < w * h; i += step)
        {
            sum += ptr[i];
            samples++;
        }

        return samples > 0 ? (float)(sum / samples) : 0.5f;
    }
}
