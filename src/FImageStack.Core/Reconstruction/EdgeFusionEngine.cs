using FImageStack.Core.Models;

namespace FImageStack.Core.Reconstruction;

public sealed class EdgeFusionResult : IDisposable
{
    public ImageBuffer<float> ReconstructedImage { get; }
    public ImageBuffer<float> EdgeDiscontinuityMask { get; }
    public int ReconstructedEdgeCount { get; set; }

    public EdgeFusionResult(ImageBuffer<float> reconstructed, ImageBuffer<float> mask, int count)
    {
        ReconstructedImage = reconstructed;
        EdgeDiscontinuityMask = mask;
        ReconstructedEdgeCount = count;
    }

    public void Dispose()
    {
        ReconstructedImage.Dispose();
        EdgeDiscontinuityMask.Dispose();
    }
}

public interface IEdgeFusionEngine
{
    EdgeFusionResult ReconstructEdges(
        ImageBuffer<float> fusedImage,
        IReadOnlyList<StackFrame> sourceFrames,
        ImageBuffer<int> sourceFrameMap,
        float edgeThreshold = 0.04f);
}

public sealed class EdgeFusionEngine : IEdgeFusionEngine
{
    public unsafe EdgeFusionResult ReconstructEdges(
        ImageBuffer<float> fusedImage,
        IReadOnlyList<StackFrame> sourceFrames,
        ImageBuffer<int> sourceFrameMap,
        float edgeThreshold = 0.04f)
    {
        int w = fusedImage.Width;
        int h = fusedImage.Height;
        int ch = fusedImage.Channels;
        int count = sourceFrames.Count;

        var resultBuffer = fusedImage.Clone();
        var discontinuityMask = new ImageBuffer<float>(w, h, 1);

        float* fusedPtr = fusedImage.DataPointer;
        float* resPtr = resultBuffer.DataPointer;
        float* maskPtr = discontinuityMask.DataPointer;
        int* mapPtr = sourceFrameMap.DataPointer;

        int reconstructedCount = 0;
        int patchRadius = 3;

        // Step 1: Detect edge discontinuity regions
        // An edge discontinuity occurs where there is both a strong visual gradient
        // AND a step jump in the source frame index map (boundary seam between mismatched frames)
        Parallel.For(2, h - 2, y =>
        {
            int row = y * w;
            int prevRow = (y - 1) * w;
            int nextRow = (y + 1) * w;

            for (int x = 2; x < w - 2; x++)
            {
                int idx = row + x;

                // Visual gradient in fused image
                int p00 = ((row + x) * ch);
                int p01 = ((row + x + 1) * ch);
                int p0m1 = ((row + x - 1) * ch);
                int p10 = ((nextRow + x) * ch);
                int pm10 = ((prevRow + x) * ch);

                float gx = MathF.Abs(fusedPtr[p01] - fusedPtr[p0m1]);
                float gy = MathF.Abs(fusedPtr[p10] - fusedPtr[pm10]);
                float grad = MathF.Sqrt(gx * gx + gy * gy);

                if (grad < edgeThreshold) continue;

                // Source map step jump
                int curFrame = mapPtr[idx];
                int leftFrame = mapPtr[idx - 1];
                int rightFrame = mapPtr[idx + 1];
                int upFrame = mapPtr[prevRow + x];
                int downFrame = mapPtr[nextRow + x];

                int maxFrameDiff = Math.Max(
                    Math.Max(Math.Abs(curFrame - leftFrame), Math.Abs(curFrame - rightFrame)),
                    Math.Max(Math.Abs(curFrame - upFrame), Math.Abs(curFrame - downFrame))
                );

                // If frame indices jump by 2 or more frames across an active edge, it is a discontinuity
                if (maxFrameDiff >= 2)
                {
                    maskPtr[idx] = 1.0f;
                    Interlocked.Increment(ref reconstructedCount);
                }
            }
        });

        if (reconstructedCount == 0 || count == 0)
        {
            return new EdgeFusionResult(resultBuffer, discontinuityMask, 0);
        }

        // Step 2 & 3: For each discontinuity, find the best single continuous edge frame and re-fuse
        Parallel.For(patchRadius, h - patchRadius, y =>
        {
            int row = y * w;

            for (int x = patchRadius; x < w - patchRadius; x++)
            {
                int idx = row + x;
                if (maskPtr[idx] < 0.5f) continue;

                // Find candidate frame with strongest consistent edge inside the local patch
                int bestFrameIdx = mapPtr[idx];
                float maxEdgeEnergy = 0f;

                for (int f = 0; f < count; f++)
                {
                    var sf = sourceFrames[f];
                    if (sf.GrayBuffer == null) continue;
                    float* gPtr = sf.GrayBuffer.DataPointer;

                    float localEnergy = 0f;
                    for (int py = -patchRadius; py <= patchRadius; py++)
                    {
                        int sRow = (y + py) * w;
                        for (int px = -patchRadius; px <= patchRadius; px++)
                        {
                            float g1 = gPtr[sRow + x + px + 1];
                            float g0 = gPtr[sRow + x + px - 1];
                            float eg = MathF.Abs(g1 - g0);
                            localEnergy += eg;
                        }
                    }

                    if (localEnergy > maxEdgeEnergy)
                    {
                        maxEdgeEnergy = localEnergy;
                        bestFrameIdx = f;
                    }
                }

                // Reconstruct pixel by blending source frame with Gaussian edge feathering
                if (bestFrameIdx >= 0 && bestFrameIdx < count && sourceFrames[bestFrameIdx].ColorBuffer != null)
                {
                    float* bestColorPtr = sourceFrames[bestFrameIdx].ColorBuffer!.DataPointer;
                    int baseIdx = idx * ch;

                    float blendWeight = 0.85f; // Dominance of continuous reconstructed edge
                    for (int c = 0; c < ch; c++)
                    {
                        resPtr[baseIdx + c] = resPtr[baseIdx + c] * (1f - blendWeight) + bestColorPtr[baseIdx + c] * blendWeight;
                    }
                }
            }
        });

        return new EdgeFusionResult(resultBuffer, discontinuityMask, reconstructedCount);
    }
}
