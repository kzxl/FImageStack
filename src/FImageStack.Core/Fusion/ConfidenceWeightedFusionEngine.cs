using FImageStack.Core.Models;
using FImageStack.Core.Quality;

namespace FImageStack.Core.Fusion;

public sealed class ConfidenceWeightedFusionEngine : IFusionEngine
{
    private readonly IMultiFactorConfidenceEngine _confidenceEngine;

    public FusionMethod Method => FusionMethod.ConfidenceWeighted;

    public ConfidenceWeightedFusionEngine(IMultiFactorConfidenceEngine? confidenceEngine = null)
    {
        _confidenceEngine = confidenceEngine ?? new MultiFactorConfidenceEngine();
    }

    public unsafe ImageBuffer<float> Fuse(IReadOnlyList<StackFrame> frames, DepthMapResult depthResult, FusionSettings settings)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("No frames to fuse.", nameof(frames));

        int width = depthResult.Width;
        int height = depthResult.Height;
        int frameCount = frames.Count;

        var output = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32);
        float* dst = output.DataPointer;

        float*[] colorPointers = new float*[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            if (frames[i].ColorBuffer == null)
                throw new InvalidOperationException($"Frame {i} has no ColorBuffer.");
            colorPointers[i] = frames[i].ColorBuffer!.DataPointer;
        }

        // Compute multi-factor confidence maps for each frame
        var confidenceMaps = _confidenceEngine.ComputeConfidenceMaps(frames);
        float*[] confPointers = new float*[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            confPointers[i] = confidenceMaps[i].DataPointer;
        }

        try
        {
            // Soft fusion with power sharpening p = 4.0
            const float power = 4.0f;

            Parallel.For(0, height, y =>
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int pixelIdx = rowOffset + x;
                    float totalWeight = 0f;
                    float r = 0f, g = 0f, b = 0f;

                    for (int f = 0; f < frameCount; f++)
                    {
                        float conf = confPointers[f][pixelIdx];
                        float weight = MathF.Pow(conf + 1e-4f, power);
                        totalWeight += weight;

                        float* srcColor = colorPointers[f] + pixelIdx * 3;
                        r += srcColor[0] * weight;
                        g += srcColor[1] * weight;
                        b += srcColor[2] * weight;
                    }

                    float invWeight = totalWeight > 0f ? 1f / totalWeight : 1f / frameCount;
                    float* dstColor = dst + pixelIdx * 3;
                    dstColor[0] = Math.Clamp(r * invWeight, 0f, 1f);
                    dstColor[1] = Math.Clamp(g * invWeight, 0f, 1f);
                    dstColor[2] = Math.Clamp(b * invWeight, 0f, 1f);
                }
            });

            return output;
        }
        finally
        {
            foreach (var map in confidenceMaps)
            {
                map.Dispose();
            }
        }
    }
}
