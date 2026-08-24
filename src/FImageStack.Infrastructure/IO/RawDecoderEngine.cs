using FImageStack.Core;
using FImageStack.Core.Models;

namespace FImageStack.Infrastructure.IO;

public sealed class RawFrameMetadata
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int BlackLevel { get; set; } = 512;
    public int WhiteLevel { get; set; } = 16383; // 14-bit sensor dynamic range
    public float RedGain { get; set; } = 2.1f;
    public float GreenGain { get; set; } = 1.0f;
    public float BlueGain { get; set; } = 1.6f;
    public BayerPatternType Pattern { get; set; } = BayerPatternType.RGGB;
    public string CameraModel { get; set; } = "Generic Camera RAW";
}

public interface IRawDecoderEngine
{
    bool IsRawFile(string filePath);
    ImageBuffer<float> DemosaicBayerCfa(ReadOnlySpan<ushort> cfaData, RawFrameMetadata metadata);
    ImageBuffer<float> LoadRawImage(string filePath, int maxDimension = 0);
}

public sealed class RawDecoderEngine : IRawDecoderEngine
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf", ".rw2", ".pef"
    };

    public bool IsRawFile(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        return RawExtensions.Contains(ext);
    }

    public unsafe ImageBuffer<float> DemosaicBayerCfa(ReadOnlySpan<ushort> cfaData, RawFrameMetadata metadata)
    {
        int w = metadata.Width;
        int h = metadata.Height;
        float invRange = 1.0f / MathF.Max(1.0f, metadata.WhiteLevel - metadata.BlackLevel);

        var output = new ImageBuffer<float>(w, h, 3, PixelFormatType.RgbFloat32);
        float* dst = output.DataPointer;

        float rGain = metadata.RedGain;
        float gGain = metadata.GreenGain;
        float bGain = metadata.BlueGain;
        int bl = metadata.BlackLevel;

        fixed (ushort* cfaPtr = cfaData)
        {
            ushort* pCfa = cfaPtr;

            // Edge-Directed Adaptive Bilinear Bayer CFA Demosaicing (RGGB layout)
            Parallel.For(1, h - 1, y =>
            {
                int rowOffset = y * w;
                int prevRow = (y - 1) * w;
                int nextRow = (y + 1) * w;

                for (int x = 1; x < w - 1; x++)
                {
                    int dstIdx = (rowOffset + x) * 3;
                    bool isEvenRow = (y % 2 == 0);
                    bool isEvenCol = (x % 2 == 0);

                    float r = 0f, g = 0f, b = 0f;

                    if (isEvenRow && isEvenCol) // [R] pixel at (even, even)
                    {
                        r = MathF.Max(0f, pCfa[rowOffset + x] - bl) * invRange * rGain;

                        // Green interpolation: directional gradient
                        float gh = MathF.Abs(pCfa[rowOffset + x - 1] - pCfa[rowOffset + x + 1]);
                        float gv = MathF.Abs(pCfa[prevRow + x] - pCfa[nextRow + x]);

                        if (gh < gv)
                        {
                            g = (MathF.Max(0f, pCfa[rowOffset + x - 1] - bl) +
                                 MathF.Max(0f, pCfa[rowOffset + x + 1] - bl)) * 0.5f * invRange * gGain;
                        }
                        else
                        {
                            g = (MathF.Max(0f, pCfa[prevRow + x] - bl) +
                                 MathF.Max(0f, pCfa[nextRow + x] - bl)) * 0.5f * invRange * gGain;
                        }

                        // Blue interpolation from 4 diagonals
                        b = (MathF.Max(0f, pCfa[prevRow + x - 1] - bl) +
                             MathF.Max(0f, pCfa[prevRow + x + 1] - bl) +
                             MathF.Max(0f, pCfa[nextRow + x - 1] - bl) +
                             MathF.Max(0f, pCfa[nextRow + x + 1] - bl)) * 0.25f * invRange * bGain;
                    }
                    else if (isEvenRow && !isEvenCol) // [Gr] Green on Red row
                    {
                        g = MathF.Max(0f, pCfa[rowOffset + x] - bl) * invRange * gGain;
                        r = (MathF.Max(0f, pCfa[rowOffset + x - 1] - bl) +
                             MathF.Max(0f, pCfa[rowOffset + x + 1] - bl)) * 0.5f * invRange * rGain;
                        b = (MathF.Max(0f, pCfa[prevRow + x] - bl) +
                             MathF.Max(0f, pCfa[nextRow + x] - bl)) * 0.5f * invRange * bGain;
                    }
                    else if (!isEvenRow && isEvenCol) // [Gb] Green on Blue row
                    {
                        g = MathF.Max(0f, pCfa[rowOffset + x] - bl) * invRange * gGain;
                        b = (MathF.Max(0f, pCfa[rowOffset + x - 1] - bl) +
                             MathF.Max(0f, pCfa[rowOffset + x + 1] - bl)) * 0.5f * invRange * bGain;
                        r = (MathF.Max(0f, pCfa[prevRow + x] - bl) +
                             MathF.Max(0f, pCfa[nextRow + x] - bl)) * 0.5f * invRange * rGain;
                    }
                    else // [B] Blue pixel
                    {
                        b = MathF.Max(0f, pCfa[rowOffset + x] - bl) * invRange * bGain;

                        float gh = MathF.Abs(pCfa[rowOffset + x - 1] - pCfa[rowOffset + x + 1]);
                        float gv = MathF.Abs(pCfa[prevRow + x] - pCfa[nextRow + x]);

                        if (gh < gv)
                        {
                            g = (MathF.Max(0f, pCfa[rowOffset + x - 1] - bl) +
                                 MathF.Max(0f, pCfa[rowOffset + x + 1] - bl)) * 0.5f * invRange * gGain;
                        }
                        else
                        {
                            g = (MathF.Max(0f, pCfa[prevRow + x] - bl) +
                                 MathF.Max(0f, pCfa[nextRow + x] - bl)) * 0.5f * invRange * gGain;
                        }

                        r = (MathF.Max(0f, pCfa[prevRow + x - 1] - bl) +
                             MathF.Max(0f, pCfa[prevRow + x + 1] - bl) +
                             MathF.Max(0f, pCfa[nextRow + x - 1] - bl) +
                             MathF.Max(0f, pCfa[nextRow + x + 1] - bl)) * 0.25f * invRange * rGain;
                    }

                    // Store in 32-bit Linear Float RGB
                    dst[dstIdx] = r;
                    dst[dstIdx + 1] = g;
                    dst[dstIdx + 2] = b;
                }
            });
        }

        return output;
    }

    public ImageBuffer<float> LoadRawImage(string filePath, int maxDimension = 0)
    {
        var bytes = File.ReadAllBytes(filePath);
        int totalUshorts = bytes.Length / 2;

        int width = 1024;
        int height = 1024;

        if (maxDimension > 0)
        {
            width = Math.Min(1280, maxDimension);
            height = Math.Min(1280, maxDimension);
        }
        else if (totalUshorts >= 4096 * 3072) { width = 4096; height = 3072; }
        else if (totalUshorts >= 3840 * 2160) { width = 3840; height = 2160; }
        else if (totalUshorts >= 1920 * 1080) { width = 1920; height = 1080; }

        var meta = new RawFrameMetadata
        {
            Width = width,
            Height = height,
            CameraModel = Path.GetFileNameWithoutExtension(filePath)
        };

        var cfaSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(bytes.AsSpan(0, Math.Min(bytes.Length, width * height * 2)));
        if (cfaSpan.Length < width * height)
        {
            var padded = new ushort[width * height];
            cfaSpan.CopyTo(padded);
            return DemosaicBayerCfa(padded, meta);
        }

        return DemosaicBayerCfa(cfaSpan, meta);
    }
}
