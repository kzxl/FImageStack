using FImageStack.Core;
using FImageStack.Core.DepthMap;
using FImageStack.Core.Fusion;
using FImageStack.Core.Models;

namespace FImageStack.Core.Tiling;

public sealed class TileSpecification
{
    public int TileIndex { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int OverlapMargin { get; set; }
}

public interface ITiledProcessor
{
    ImageBuffer<float> ProcessTiled(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        IFusionEngine fusionEngine,
        FusionSettings settings,
        int tileSize = 4096,
        int overlapMargin = 64,
        IProgress<StackProgress>? progress = null);
}

public sealed class StandardTiledProcessor : ITiledProcessor
{
    public unsafe ImageBuffer<float> ProcessTiled(
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        IFusionEngine fusionEngine,
        FusionSettings settings,
        int tileSize = 4096,
        int overlapMargin = 64,
        IProgress<StackProgress>? progress = null)
    {
        int fullW = depthResult.Width;
        int fullH = depthResult.Height;

        var fullOutput = new ImageBuffer<float>(fullW, fullH, 3, PixelFormatType.RgbFloat32);
        using var weightAccumulator = new ImageBuffer<float>(fullW, fullH, 1, PixelFormatType.GrayFloat32);

        float* outPtr = fullOutput.DataPointer;
        float* weightPtr = weightAccumulator.DataPointer;

        int stepX = Math.Max(1, tileSize - overlapMargin * 2);
        int stepY = Math.Max(1, tileSize - overlapMargin * 2);

        var tileSpecs = new List<TileSpecification>();
        int tileIdx = 0;

        for (int y = 0; y < fullH; y += stepY)
        {
            int tY = Math.Max(0, y);
            int tH = Math.Min(tileSize, fullH - tY);

            for (int x = 0; x < fullW; x += stepX)
            {
                int tX = Math.Max(0, x);
                int tW = Math.Min(tileSize, fullW - tX);

                tileSpecs.Add(new TileSpecification
                {
                    TileIndex = tileIdx++,
                    X = tX,
                    Y = tY,
                    Width = tW,
                    Height = tH,
                    OverlapMargin = overlapMargin
                });
            }
        }

        // Process each tile sequentially to maintain bounded RAM usage
        for (int i = 0; i < tileSpecs.Count; i++)
        {
            var tile = tileSpecs[i];
            progress?.Report(new StackProgress("Tiled Fusion", (double)(i + 1) / tileSpecs.Count * 100, $"Processing Tile {i + 1}/{tileSpecs.Count} ({tile.Width}x{tile.Height} at X:{tile.X}, Y:{tile.Y})..."));

            // Extract tile frames
            var tileFrames = new List<StackFrame>(frames.Count);
            for (int f = 0; f < frames.Count; f++)
            {
                var srcF = frames[f];
                var tColor = ExtractCrop(srcF.ColorBuffer!, tile.X, tile.Y, tile.Width, tile.Height);
                var tGray = ExtractCrop(srcF.GrayBuffer!, tile.X, tile.Y, tile.Width, tile.Height);
                var tFocus = ExtractCrop(srcF.FocusMap!, tile.X, tile.Y, tile.Width, tile.Height);

                tileFrames.Add(new StackFrame
                {
                    Index = f,
                    Width = tile.Width,
                    Height = tile.Height,
                    ColorBuffer = tColor,
                    GrayBuffer = tGray,
                    FocusMap = tFocus
                });
            }

            var tDepth = new DepthMapResult(tile.Width, tile.Height);
            CopyCrop(depthResult.SourceFrameMap, tDepth.SourceFrameMap, tile.X, tile.Y, tile.Width, tile.Height);
            CopyCrop(depthResult.DepthMap, tDepth.DepthMap, tile.X, tile.Y, tile.Width, tile.Height);
            CopyCrop(depthResult.ConfidenceMap, tDepth.ConfidenceMap, tile.X, tile.Y, tile.Width, tile.Height);

            // Fuse the tile with chosen engine
            using var fusedTile = fusionEngine.Fuse(tileFrames, tDepth, settings);

            // Accumulate tile with boundary Cosine Hanning feathering to eliminate seams
            AccumulateTile(fusedTile, fullOutput, weightAccumulator, tile);

            // Immediately dispose tile buffers to release memory
            foreach (var tf in tileFrames) tf.Dispose();
            tDepth.Dispose();
        }

        // Normalize full output by accumulated weights
        Parallel.For(0, fullH, y =>
        {
            int rowOffset = y * fullW;
            for (int x = 0; x < fullW; x++)
            {
                int idx = rowOffset + x;
                float w = weightPtr[idx];
                float invW = w > 0 ? 1f / w : 1f;

                int cIdx = idx * 3;
                outPtr[cIdx] = Math.Clamp(outPtr[cIdx] * invW, 0f, 1f);
                outPtr[cIdx + 1] = Math.Clamp(outPtr[cIdx + 1] * invW, 0f, 1f);
                outPtr[cIdx + 2] = Math.Clamp(outPtr[cIdx + 2] * invW, 0f, 1f);
            }
        });

        return fullOutput;
    }

    private static unsafe void AccumulateTile(
        ImageBuffer<float> tile,
        ImageBuffer<float> fullDst,
        ImageBuffer<float> weightDst,
        TileSpecification spec)
    {
        float* tPtr = tile.DataPointer;
        float* dPtr = fullDst.DataPointer;
        float* wPtr = weightDst.DataPointer;

        int tW = spec.Width;
        int tH = spec.Height;
        int fullW = fullDst.Width;
        int margin = spec.OverlapMargin;

        for (int ty = 0; ty < tH; ty++)
        {
            int gy = spec.Y + ty;
            float wy = 1.0f;
            if (ty < margin) wy = 0.5f * (1.0f - MathF.Cos((float)ty / margin * MathF.PI));
            else if (ty > tH - 1 - margin) wy = 0.5f * (1.0f - MathF.Cos((float)(tH - 1 - ty) / margin * MathF.PI));

            for (int tx = 0; tx < tW; tx++)
            {
                int gx = spec.X + tx;
                float wx = 1.0f;
                if (tx < margin) wx = 0.5f * (1.0f - MathF.Cos((float)tx / margin * MathF.PI));
                else if (tx > tW - 1 - margin) wx = 0.5f * (1.0f - MathF.Cos((float)(tW - 1 - tx) / margin * MathF.PI));

                float weight = MathF.Max(0.001f, wx * wy);
                int gIdx = gy * fullW + gx;
                int tIdx = ty * tW + tx;

                wPtr[gIdx] += weight;

                int gcIdx = gIdx * 3;
                int tcIdx = tIdx * 3;

                dPtr[gcIdx] += tPtr[tcIdx] * weight;
                dPtr[gcIdx + 1] += tPtr[tcIdx + 1] * weight;
                dPtr[gcIdx + 2] += tPtr[tcIdx + 2] * weight;
            }
        }
    }

    private static unsafe ImageBuffer<float> ExtractCrop(ImageBuffer<float> src, int x0, int y0, int w, int h)
    {
        var crop = new ImageBuffer<float>(w, h, src.Channels, src.Format);
        float* s = src.DataPointer;
        float* d = crop.DataPointer;
        int srcW = src.Width;
        int channels = src.Channels;

        for (int y = 0; y < h; y++)
        {
            int sy = y0 + y;
            int sRow = sy * srcW;
            int dRow = y * w;

            for (int x = 0; x < w; x++)
            {
                int sx = x0 + x;
                int sIdx = (sRow + sx) * channels;
                int dIdx = (dRow + x) * channels;

                for (int c = 0; c < channels; c++)
                {
                    d[dIdx + c] = s[sIdx + c];
                }
            }
        }
        return crop;
    }

    private static unsafe void CopyCrop<T>(ImageBuffer<T> src, ImageBuffer<T> dst, int x0, int y0, int w, int h) where T : unmanaged
    {
        T* s = src.DataPointer;
        T* d = dst.DataPointer;
        int srcW = src.Width;
        int channels = src.Channels;

        for (int y = 0; y < h; y++)
        {
            int sy = y0 + y;
            int sRow = sy * srcW;
            int dRow = y * w;

            for (int x = 0; x < w; x++)
            {
                int sx = x0 + x;
                int sIdx = (sRow + sx) * channels;
                int dIdx = (dRow + x) * channels;

                for (int c = 0; c < channels; c++)
                {
                    d[dIdx + c] = s[sIdx + c];
                }
            }
        }
    }
}
