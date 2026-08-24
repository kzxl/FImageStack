using FImageStack.Core;
using FImageStack.Core.Artifact;
using FImageStack.Core.Models;

namespace FImageStack.Core.Reconstruction;

public sealed class RepairReport
{
    public int TotalArtifactsDetected { get; set; }
    public int RepairedRegionsCount { get; set; }
    public int RepairedPixelsCount { get; set; }
    public List<string> RepairedDescriptions { get; } = new();
}

public interface IAutoRepairEngine
{
    (ImageBuffer<float> RepairedImage, RepairReport Report) AutoRepair(
        ImageBuffer<float> fusedImage,
        IReadOnlyList<StackFrame> frames,
        ArtifactMap artifactMap);
}

public sealed class StandardAutoRepairEngine : IAutoRepairEngine
{
    public unsafe (ImageBuffer<float> RepairedImage, RepairReport Report) AutoRepair(
        ImageBuffer<float> fusedImage,
        IReadOnlyList<StackFrame> frames,
        ArtifactMap artifactMap)
    {
        var report = new RepairReport
        {
            TotalArtifactsDetected = artifactMap.Regions.Count
        };

        var repaired = fusedImage.Clone();
        int width = fusedImage.Width;
        int height = fusedImage.Height;
        int frameCount = frames.Count;

        float* dstPtr = repaired.DataPointer;
        byte* maskPtr = artifactMap.ArtifactMask.DataPointer;

        float*[] colorPointers = new float*[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            colorPointers[f] = frames[f].ColorBuffer!.DataPointer;
        }

        int totalRepairedPixels = 0;

        foreach (var region in artifactMap.Regions)
        {
            int targetFrame = Math.Clamp(region.SuggestedSourceFrame, 0, frameCount - 1);
            float* srcColorPtr = colorPointers[targetFrame];

            int x0 = Math.Max(0, region.X);
            int y0 = Math.Max(0, region.Y);
            int x1 = Math.Min(width, region.X + region.Width);
            int y1 = Math.Min(height, region.Y + region.Height);

            float cx = (x0 + x1) / 2f;
            float cy = (y0 + y1) / 2f;
            float rx = (x1 - x0) / 2f + 1e-4f;
            float ry = (y1 - y0) / 2f + 1e-4f;

            for (int y = y0; y < y1; y++)
            {
                int rowOffset = y * width;
                float dyNorm = (y - cy) / ry;
                float dySq = dyNorm * dyNorm;

                for (int x = x0; x < x1; x++)
                {
                    int idx = rowOffset + x;
                    if (maskPtr[idx] == 0) continue;

                    float dxNorm = (x - cx) / rx;
                    float distNormSq = dxNorm * dxNorm + dySq;
                    if (distNormSq > 1.0f) continue;

                    // Smooth cosine feathering towards the edge of the patch
                    float weight = 0.5f * (1.0f + MathF.Cos(MathF.Sqrt(distNormSq) * MathF.PI));
                    weight = Math.Clamp(weight * region.Severity * 0.9f, 0f, 1f);

                    int cIdx = idx * 3;
                    dstPtr[cIdx] = dstPtr[cIdx] * (1f - weight) + srcColorPtr[cIdx] * weight;
                    dstPtr[cIdx + 1] = dstPtr[cIdx + 1] * (1f - weight) + srcColorPtr[cIdx + 1] * weight;
                    dstPtr[cIdx + 2] = dstPtr[cIdx + 2] * (1f - weight) + srcColorPtr[cIdx + 2] * weight;

                    totalRepairedPixels++;
                }
            }

            report.RepairedRegionsCount++;
            report.RepairedDescriptions.Add($"Repaired {region.Type} at ({region.X},{region.Y}) using frame {targetFrame + 1}");
        }

        report.RepairedPixelsCount = totalRepairedPixels;
        return (repaired, report);
    }
}
