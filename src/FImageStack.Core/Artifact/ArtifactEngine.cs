using FImageStack.Core;
using FImageStack.Core.DepthMap;
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
        float*[] grayPointers = new float*[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            colorPointers[f] = frames[f].ColorBuffer!.DataPointer;
            grayPointers[f] = frames[f].GrayBuffer != null ? frames[f].GrayBuffer!.DataPointer : frames[f].ColorBuffer!.DataPointer;
        }

        // 1. Edge Detection & Halo Gradient Analysis (White/Dark Fringes along edges)
        using var edgeBuffer = new ImageBuffer<float>(width, height);
        float* edgePtr = edgeBuffer.DataPointer;

        Parallel.For(1, height - 1, y =>
        {
            int rowOffset = y * width;
            int prevRow = (y - 1) * width;
            int nextRow = (y + 1) * width;

            for (int x = 1; x < width - 1; x++)
            {
                int frameIdx = Math.Clamp(srcMap[rowOffset + x], 0, frameCount - 1);
                float* srcG = grayPointers[frameIdx];

                // Sobel on source frame
                float gx = (srcG[nextRow + x + 1] + 2f * srcG[rowOffset + x + 1] + srcG[prevRow + x + 1])
                         - (srcG[nextRow + x - 1] + 2f * srcG[rowOffset + x - 1] + srcG[prevRow + x - 1]);
                float gy = (srcG[nextRow + x - 1] + 2f * srcG[nextRow + x] + srcG[nextRow + x + 1])
                         - (srcG[prevRow + x - 1] + 2f * srcG[prevRow + x] + srcG[prevRow + x + 1]);

                edgePtr[rowOffset + x] = MathF.Sqrt(gx * gx + gy * gy);
            }
        });

        // 2. Seam Line Analysis (Transitions between non-consecutive frames)
        using var seamBuffer = new ImageBuffer<byte>(width, height);
        byte* seamPtr = seamBuffer.DataPointer;

        Parallel.For(0, height - 1, y =>
        {
            int rowOffset = y * width;
            int nextRow = (y + 1) * width;

            for (int x = 0; x < width - 1; x++)
            {
                int fCurrent = srcMap[rowOffset + x];
                int fRight = srcMap[rowOffset + x + 1];
                int fDown = srcMap[nextRow + x];

                if (Math.Abs(fCurrent - fRight) >= 2 || Math.Abs(fCurrent - fDown) >= 2)
                {
                    seamPtr[rowOffset + x] = 255;
                }
            }
        });

        // 3. Block-Level Clustering for Artifact Regions (32x32 blocks)
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
                int seamPixels = 0;
                int dominantFrame = 0;
                int blockTotal = (y1 - y0) * (x1 - x0);

                for (int y = y0; y < y1; y++)
                {
                    int rowOffset = y * width;
                    for (int x = x0; x < x1; x++)
                    {
                        int idx = rowOffset + x;
                        int frameIdx = Math.Clamp(srcMap[idx], 0, frameCount - 1);
                        dominantFrame = frameIdx;

                        float conf = confMap[idx];
                        float edgeMag = edgePtr[idx];

                        // Compare fused color against source frame color
                        int cIdx = idx * 3;
                        float* srcColor = colorPointers[frameIdx] + cIdx;
                        float dr = MathF.Abs(fusedPtr[cIdx] - srcColor[0]);
                        float dg = MathF.Abs(fusedPtr[cIdx + 1] - srcColor[1]);
                        float db = MathF.Abs(fusedPtr[cIdx + 2] - srcColor[2]);
                        float colorDiff = (dr + dg + db) / 3f;

                        // Check Seam Lines
                        if (seamPtr[idx] > 0)
                        {
                            seamPixels++;
                            maskPtr[idx] = 120; // Seam mask value
                        }

                        // Check White/Dark Halo Fringes along edges (Overshoot / Undershoot)
                        if (edgeMag > 0.15f && colorDiff > 0.20f)
                        {
                            haloPixels++;
                            maskPtr[idx] = 180; // Halo mask value
                        }
                        // Check Ghosting (Color divergence in low/mid confidence dynamic zones)
                        else if (colorDiff > 0.30f && conf < 0.65f)
                        {
                            ghostPixels++;
                            maskPtr[idx] = 255; // Ghost mask value
                        }
                    }
                }

                if (ghostPixels > blockTotal * 0.15f)
                {
                    artifactMap.Regions.Add(new ArtifactRegion
                    {
                        Id = ++regionIdCounter,
                        Type = ArtifactType.Ghost,
                        X = x0,
                        Y = y0,
                        Width = x1 - x0,
                        Height = y1 - y0,
                        Severity = Math.Clamp((float)ghostPixels / blockTotal, 0f, 1f),
                        SuggestedSourceFrame = dominantFrame,
                        Description = $"Motion Ghosting ({ghostPixels}px, Frame #{dominantFrame + 1})"
                    });
                }
                else if (haloPixels > blockTotal * 0.15f)
                {
                    artifactMap.Regions.Add(new ArtifactRegion
                    {
                        Id = ++regionIdCounter,
                        Type = ArtifactType.Halo,
                        X = x0,
                        Y = y0,
                        Width = x1 - x0,
                        Height = y1 - y0,
                        Severity = Math.Clamp((float)haloPixels / blockTotal, 0f, 1f),
                        SuggestedSourceFrame = dominantFrame,
                        Description = $"Edge Defocus Halo ({haloPixels}px, Frame #{dominantFrame + 1})"
                    });
                }
                else if (seamPixels > blockTotal * 0.10f)
                {
                    artifactMap.Regions.Add(new ArtifactRegion
                    {
                        Id = ++regionIdCounter,
                        Type = ArtifactType.Seam,
                        X = x0,
                        Y = y0,
                        Width = x1 - x0,
                        Height = y1 - y0,
                        Severity = Math.Clamp((float)seamPixels / blockTotal, 0f, 1f),
                        SuggestedSourceFrame = dominantFrame,
                        Description = $"Depth Boundary Seam ({seamPixels}px)"
                    });
                }
            }
        }

        return artifactMap;
    }
}
