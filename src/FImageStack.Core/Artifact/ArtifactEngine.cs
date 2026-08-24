using FImageStack.Core;
using FImageStack.Core.Models;

namespace FImageStack.Core.Artifact;

public sealed class ArtifactRegion
{
    public int Id { get; set; }
    public ArtifactType Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float Severity { get; set; } // 0.0 to 1.0
    public int SuggestedSourceFrame { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class ArtifactMap : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public ImageBuffer<byte> ArtifactMask { get; }
    public List<ArtifactRegion> Regions { get; } = new();

    public int HaloCount => Regions.Count(r => r.Type == ArtifactType.Halo);
    public int GhostCount => Regions.Count(r => r.Type == ArtifactType.Ghost);
    public int SeamCount => Regions.Count(r => r.Type == ArtifactType.Seam);
    public int LowConfidenceCount => Regions.Count(r => r.Type == ArtifactType.LowConfidence);

    public ArtifactMap(int width, int height)
    {
        Width = width;
        Height = height;
        ArtifactMask = new ImageBuffer<byte>(width, height, 1, PixelFormatType.Gray8);
    }

    public void Dispose()
    {
        ArtifactMask.Dispose();
    }
}

public interface IArtifactDetector
{
    ArtifactMap DetectArtifacts(
        ImageBuffer<float> fusedImage,
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        float sensitivity = 0.5f);
}

public sealed class StandardArtifactDetector : IArtifactDetector
{
    public unsafe ArtifactMap DetectArtifacts(
        ImageBuffer<float> fusedImage,
        IReadOnlyList<StackFrame> frames,
        DepthMapResult depthResult,
        float sensitivity = 0.5f)
    {
        int width = fusedImage.Width;
        int height = fusedImage.Height;
        int frameCount = frames.Count;

        var artifactMap = new ArtifactMap(width, height);
        byte* maskPtr = artifactMap.ArtifactMask.DataPointer;
        float* fusedPtr = fusedImage.DataPointer;
        int* srcMap = depthResult.SourceFrameMap.DataPointer;
        float* confMap = depthResult.ConfidenceMap.DataPointer;

        float*[] colorPointers = new float*[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            colorPointers[f] = frames[f].ColorBuffer!.DataPointer;
        }

        // Scan image in blocks of 32x32 to cluster artifacts
        int blockSize = 32;
        int blocksX = (width + blockSize - 1) / blockSize;
        int blocksY = (height + blockSize - 1) / blockSize;
        int regionIdCounter = 0;

        for (int by = 0; by < blocksY; by++)
        {
            int y0 = by * blockSize;
            int y1 = Math.Min(height, y0 + blockSize);

            for (int bx = 0; bx < blocksX; bx++)
            {
                int x0 = bx * blockSize;
                int x1 = Math.Min(width, x0 + blockSize);

                int haloPixels = 0;
                int ghostPixels = 0;
                int lowConfPixels = 0;
                float maxDeviation = 0f;
                int dominantFrame = 0;

                for (int y = y0; y < y1; y++)
                {
                    int rowOffset = y * width;
                    for (int x = x0; x < x1; x++)
                    {
                        int idx = rowOffset + x;
                        int frameIdx = Math.Clamp(srcMap[idx], 0, frameCount - 1);
                        dominantFrame = frameIdx;

                        float conf = confMap[idx];
                        if (conf < 0.15f)
                        {
                            lowConfPixels++;
                        }

                        // Compare fused color against source frame color
                        int cIdx = idx * 3;
                        float* srcColor = colorPointers[frameIdx] + cIdx;
                        float dr = MathF.Abs(fusedPtr[cIdx] - srcColor[0]);
                        float dg = MathF.Abs(fusedPtr[cIdx + 1] - srcColor[1]);
                        float db = MathF.Abs(fusedPtr[cIdx + 2] - srcColor[2]);
                        float colorDiff = (dr + dg + db) / 3f;

                        if (colorDiff > maxDeviation) maxDeviation = colorDiff;

                        // Thresholds modulated by sensitivity
                        float haloThreshold = 0.25f - (sensitivity - 0.5f) * 0.15f;
                        float ghostThreshold = 0.35f - (sensitivity - 0.5f) * 0.20f;

                        if (colorDiff > ghostThreshold && conf < 0.6f)
                        {
                            ghostPixels++;
                            maskPtr[idx] = 255;
                        }
                        else if (colorDiff > haloThreshold)
                        {
                            haloPixels++;
                            maskPtr[idx] = 180;
                        }
                    }
                }

                int blockPixels = (y1 - y0) * (x1 - x0);
                if (ghostPixels > blockPixels * 0.20f)
                {
                    artifactMap.Regions.Add(new ArtifactRegion
                    {
                        Id = ++regionIdCounter,
                        Type = ArtifactType.Ghost,
                        X = x0,
                        Y = y0,
                        Width = x1 - x0,
                        Height = y1 - y0,
                        Severity = Math.Clamp((float)ghostPixels / blockPixels, 0f, 1f),
                        SuggestedSourceFrame = dominantFrame,
                        Description = $"Ghosting artifact detected in {x0},{y0}"
                    });
                }
                else if (haloPixels > blockPixels * 0.25f)
                {
                    artifactMap.Regions.Add(new ArtifactRegion
                    {
                        Id = ++regionIdCounter,
                        Type = ArtifactType.Halo,
                        X = x0,
                        Y = y0,
                        Width = x1 - x0,
                        Height = y1 - y0,
                        Severity = Math.Clamp((float)haloPixels / blockPixels, 0f, 1f),
                        SuggestedSourceFrame = dominantFrame,
                        Description = $"Halo boundary artifact detected in {x0},{y0}"
                    });
                }
            }
        }

        return artifactMap;
    }
}
