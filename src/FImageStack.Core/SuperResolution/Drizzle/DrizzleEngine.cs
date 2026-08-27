using FImageStack.Core.Models;

namespace FImageStack.Core.SuperResolution.Drizzle;

public interface IDrizzleEngine
{
    DrizzleResult DrizzleStack(
        IReadOnlyList<StackFrame> frames, 
        DrizzleSettings settings, 
        IProgress<StackProgress>? progress = null);
}

public sealed class DrizzleEngine : IDrizzleEngine
{
    public unsafe DrizzleResult DrizzleStack(
        IReadOnlyList<StackFrame> frames,
        DrizzleSettings settings,
        IProgress<StackProgress>? progress = null)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("Frames list cannot be empty.", nameof(frames));

        int inW = frames[0].Width;
        int inH = frames[0].Height;
        int ch = frames[0].ColorBuffer?.Channels ?? 3;
        float scale = MathF.Max(1.0f, settings.ScaleFactor);
        float pixFrac = Math.Clamp(settings.PixFrac, 0.1f, 1.0f);

        int outW = (int)MathF.Round(inW * scale);
        int outH = (int)MathF.Round(inH * scale);

        var accum = new ImageBuffer<float>(outW, outH, ch, PixelFormatType.RgbFloat32);
        var weightMap = new ImageBuffer<float>(outW, outH, 1, PixelFormatType.GrayFloat32);

        float* accPtr = accum.DataPointer;
        float* wPtr = weightMap.DataPointer;

        float halfDropSize = (scale * pixFrac) * 0.5f;
        float invBoxArea = 1.0f / (4.0f * halfDropSize * halfDropSize);

        // Object lock per row to prevent multi-threaded race conditions on accumulating weights
        var rowLocks = new object[outH];
        for (int i = 0; i < outH; i++) rowLocks[i] = new object();

        for (int f = 0; f < frames.Count; f++)
        {
            var frame = frames[f];
            var colorBuf = frame.ColorBuffer ?? throw new InvalidOperationException($"Frame {f} ColorBuffer is null.");
            float* srcPtr = colorBuf.DataPointer;

            // Extract frame subpixel translation (dx, dy)
            float dx = 0f;
            float dy = 0f;
            if (frame.AlignmentHomography != null && frame.AlignmentHomography.Length >= 6)
            {
                dx = frame.AlignmentHomography[2];
                dy = frame.AlignmentHomography[5];
            }

            Parallel.For(0, inH, y =>
            {
                int srcRow = y * inW * ch;

                for (int x = 0; x < inW; x++)
                {
                    // Target center position in output space
                    float targetX = (x + dx) * scale;
                    float targetY = (y + dy) * scale;

                    float boxLeft = targetX - halfDropSize;
                    float boxRight = targetX + halfDropSize;
                    float boxTop = targetY - halfDropSize;
                    float boxBottom = targetY + halfDropSize;

                    int minOutX = Math.Max(0, (int)MathF.Floor(boxLeft));
                    int maxOutX = Math.Min(outW - 1, (int)MathF.Ceiling(boxRight));
                    int minOutY = Math.Max(0, (int)MathF.Floor(boxTop));
                    int maxOutY = Math.Min(outH - 1, (int)MathF.Ceiling(boxBottom));

                    int srcBase = srcRow + x * ch;

                    for (int oy = minOutY; oy <= maxOutY; oy++)
                    {
                        float cellTop = oy - 0.5f;
                        float cellBottom = oy + 0.5f;

                        float overlapY = MathF.Max(0f, MathF.Min(boxBottom, cellBottom) - MathF.Max(boxTop, cellTop));
                        if (overlapY <= 0f) continue;

                        lock (rowLocks[oy])
                        {
                            int dstRow = oy * outW * ch;

                            for (int ox = minOutX; ox <= maxOutX; ox++)
                            {
                                float cellLeft = ox - 0.5f;
                                float cellRight = ox + 0.5f;

                                float overlapX = MathF.Max(0f, MathF.Min(boxRight, cellRight) - MathF.Max(boxLeft, cellLeft));
                                if (overlapX <= 0f) continue;

                                float areaWeight = (overlapX * overlapY) * invBoxArea;

                                int dstBase = dstRow + ox * ch;
                                int wIdx = oy * outW + ox;

                                wPtr[wIdx] += areaWeight;
                                for (int c = 0; c < ch; c++)
                                {
                                    accPtr[dstBase + c] += srcPtr[srcBase + c] * areaWeight;
                                }
                            }
                        }
                    }
                }
            });

            progress?.Report(new StackProgress("Drizzle Super-Resolution", (double)(f + 1) / frames.Count * 100, $"Drizzling frame {f + 1}/{frames.Count}"));
        }

        // Final normalization and hole filling
        var finalImage = new ImageBuffer<float>(outW, outH, ch, PixelFormatType.RgbFloat32);
        float* dstPtr = finalImage.DataPointer;
        float floor = settings.WeightFloorThreshold;

        Parallel.For(0, outH, y =>
        {
            int rowOffset = y * outW * ch;

            for (int x = 0; x < outW; x++)
            {
                int wIdx = y * outW + x;
                float w = wPtr[wIdx];
                int baseIdx = rowOffset + x * ch;

                if (w > floor)
                {
                    float invW = 1.0f / w;
                    for (int c = 0; c < ch; c++)
                    {
                        dstPtr[baseIdx + c] = Math.Clamp(accPtr[baseIdx + c] * invW, 0f, 1f);
                    }
                }
                else
                {
                    // Fallback to nearest source frame pixel for empty grid holes
                    int srcX = Math.Clamp((int)(x / scale), 0, inW - 1);
                    int srcY = Math.Clamp((int)(y / scale), 0, inH - 1);
                    for (int c = 0; c < ch; c++)
                    {
                        dstPtr[baseIdx + c] = frames[0].ColorBuffer!.At(srcX, srcY, c);
                    }
                }
            }
        });

        accum.Dispose();

        return new DrizzleResult(finalImage, weightMap, scale, frames.Count);
    }
}
