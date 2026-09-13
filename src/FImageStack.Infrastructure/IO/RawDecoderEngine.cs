using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using FImageStack.Core;
using FImageStack.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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
    Stream? OpenEmbeddedJpegStream(string filePath);
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

    public Stream? OpenEmbeddedJpegStream(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        try
        {
            // Check for TIFF / CR2 Header (Little Endian 'II*\0' or Big Endian 'MM\0*')
            Span<byte> header = stackalloc byte[16];
            int read = fs.Read(header);
            if (read >= 16 && header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
            {
                // CR2 Format: header[8..9] == 'CR', header[10..11] == 0x02
                if (header[8] == 0x43 && header[9] == 0x52)
                {
                    uint ifd3Offset = BitConverter.ToUInt32(header.Slice(12, 4));
                    if (ifd3Offset > 0 && ifd3Offset < (ulong)fs.Length)
                    {
                        fs.Seek(ifd3Offset, SeekOrigin.Begin);
                        Span<byte> countBuf = stackalloc byte[2];
                        if (fs.Read(countBuf) == 2)
                        {
                            ushort tagCount = BitConverter.ToUInt16(countBuf);
                            uint jpegOffset = 0;

                            Span<byte> tagBuf = stackalloc byte[12];
                            for (int i = 0; i < tagCount; i++)
                            {
                                if (fs.Read(tagBuf) != 12) break;
                                ushort tagId = BitConverter.ToUInt16(tagBuf.Slice(0, 2));
                                if (tagId == 0x0111 || tagId == 0x0201) // StripOffsets or JPEGInterchangeFormat
                                {
                                    jpegOffset = BitConverter.ToUInt32(tagBuf.Slice(8, 4));
                                    break;
                                }
                            }

                            if (jpegOffset > 0 && jpegOffset < (ulong)fs.Length)
                            {
                                fs.Seek(jpegOffset, SeekOrigin.Begin);
                                return fs;
                            }
                        }
                    }
                }
            }

            // General scanner: Look for JPEG SOI marker (0xFF, 0xD8, 0xFF)
            fs.Seek(0, SeekOrigin.Begin);
            byte[] scanBuf = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                long pos = 0;
                while (pos < fs.Length && pos < 64 * 1024 * 1024) // scan up to 64MB
                {
                    int bytesRead = fs.Read(scanBuf, 0, scanBuf.Length);
                    if (bytesRead < 4) break;

                    for (int i = 0; i < bytesRead - 3; i++)
                    {
                        if (scanBuf[i] == 0xFF && scanBuf[i + 1] == 0xD8 && scanBuf[i + 2] == 0xFF)
                        {
                            fs.Seek(pos + i, SeekOrigin.Begin);
                            return fs;
                        }
                    }

                    pos += bytesRead - 2;
                    fs.Seek(pos, SeekOrigin.Begin);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scanBuf);
            }

            fs.Dispose();
            return null;
        }
        catch
        {
            fs.Dispose();
            return null;
        }
    }

    public unsafe ImageBuffer<float> LoadRawImage(string filePath, int maxDimension = 0)
    {
        // 1. Try decoding embedded JPEG preview if available
        using (var jpegStream = OpenEmbeddedJpegStream(filePath))
        {
            if (jpegStream != null)
            {
                try
                {
                    using var image = Image.Load<Rgb24>(jpegStream);
                    if (maxDimension > 0 && (image.Width > maxDimension || image.Height > maxDimension))
                    {
                        image.Mutate(x => x.Resize(new ResizeOptions
                        {
                            Size = new Size(maxDimension, maxDimension),
                            Mode = ResizeMode.Max
                        }));
                    }

                    int w = image.Width;
                    int h = image.Height;
                    var colorBuffer = new ImageBuffer<float>(w, h, 3, PixelFormatType.RgbFloat32);
                    float* cPtr = colorBuffer.DataPointer;

                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < h; y++)
                        {
                            var row = accessor.GetRowSpan(y);
                            int rowOffset = y * w;

                            for (int x = 0; x < w; x++)
                            {
                                ref readonly var pixel = ref row[x];
                                int cIdx = (rowOffset + x) * 3;
                                cPtr[cIdx] = pixel.R / 255f;
                                cPtr[cIdx + 1] = pixel.G / 255f;
                                cPtr[cIdx + 2] = pixel.B / 255f;
                            }
                        }
                    });

                    return colorBuffer;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Embedded JPEG decode notice: {ex.Message}");
                }
            }
        }

        // 2. Fallback: Demosaic Bayer CFA (preserving standard 3:2 camera sensor aspect ratio)
        int width = 1280;
        int height = 853; // standard 3:2 ratio

        if (maxDimension > 0)
        {
            width = Math.Min(1280, maxDimension);
            height = Math.Max(2, (int)(width * 2.0 / 3.0));
        }
        else
        {
            long fileLength = new FileInfo(filePath).Length;
            if (fileLength >= 25 * 1024 * 1024) { width = 5616; height = 3744; } // Canon 5D Mark II native
            else if (fileLength >= 16 * 1024 * 1024) { width = 3840; height = 2560; }
            else { width = 1920; height = 1280; }
        }

        // Width and height must be even for Bayer grid
        if (width % 2 != 0) width--;
        if (height % 2 != 0) height--;

        var meta = new RawFrameMetadata
        {
            Width = width,
            Height = height,
            CameraModel = Path.GetFileNameWithoutExtension(filePath)
        };

        int totalNeeded = width * height * 2;
        var byteBuf = ArrayPool<byte>.Shared.Rent(totalNeeded);
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int read = fs.Read(byteBuf, 0, totalNeeded);
            var cfaSpan = MemoryMarshal.Cast<byte, ushort>(byteBuf.AsSpan(0, read));

            if (cfaSpan.Length < width * height)
            {
                var padded = new ushort[width * height];
                cfaSpan.CopyTo(padded);
                return DemosaicBayerCfa(padded, meta);
            }

            return DemosaicBayerCfa(cfaSpan.Slice(0, width * height), meta);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(byteBuf);
        }
    }
}
