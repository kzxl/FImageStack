using FImageStack.Core;
using FImageStack.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FImageStack.Infrastructure.IO;

public interface IImageIO
{
    StackFrame LoadFrame(string filePath, int index, int maxDimension = 0);
    void SaveImage(ImageBuffer<float> buffer, string outputPath, int bitDepth = 8);
}

public sealed class ImageSharpIO : IImageIO
{
    private readonly IRawDecoderEngine _rawDecoder;

    public ImageSharpIO(IRawDecoderEngine? rawDecoder = null)
    {
        _rawDecoder = rawDecoder ?? new RawDecoderEngine();
    }

    public unsafe StackFrame LoadFrame(string filePath, int index, int maxDimension = 0)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Image file not found.", filePath);

        // Native RAW Decoder Flow
        if (_rawDecoder.IsRawFile(filePath))
        {
            var rawColor = _rawDecoder.LoadRawImage(filePath, maxDimension);
            int rw = rawColor.Width;
            int rh = rawColor.Height;

            var rawGray = new ImageBuffer<float>(rw, rh, 1, PixelFormatType.GrayFloat32);
            float* rcPtr = rawColor.DataPointer;
            float* rgPtr = rawGray.DataPointer;

            Parallel.For(0, rh, y =>
            {
                int rowOffset = y * rw;
                for (int x = 0; x < rw; x++)
                {
                    int cIdx = (rowOffset + x) * 3;
                    rgPtr[rowOffset + x] = 0.2126f * rcPtr[cIdx] + 0.7152f * rcPtr[cIdx + 1] + 0.0722f * rcPtr[cIdx + 2];
                }
            });

            return new StackFrame
            {
                Index = index,
                FilePath = filePath,
                Width = rw,
                Height = rh,
                BitDepth = 16,
                Format = PixelFormatType.RgbFloat32,
                ColorBuffer = rawColor,
                GrayBuffer = rawGray
            };
        }

        // Standard Image Loading (JPEG, PNG, TIFF)
        using var image = Image.Load<Rgb24>(filePath);

        if (maxDimension > 0 && (image.Width > maxDimension || image.Height > maxDimension))
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxDimension, maxDimension),
                Mode = ResizeMode.Max
            }));
        }

        int width = image.Width;
        int height = image.Height;

        var colorBuffer = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32);
        var grayBuffer = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);

        float* cPtr = colorBuffer.DataPointer;
        float* gPtr = grayBuffer.DataPointer;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                int rowOffset = y * width;

                for (int x = 0; x < width; x++)
                {
                    ref readonly var pixel = ref row[x];
                    float r = pixel.R / 255f;
                    float g = pixel.G / 255f;
                    float b = pixel.B / 255f;

                    int cIdx = (rowOffset + x) * 3;
                    cPtr[cIdx] = r;
                    cPtr[cIdx + 1] = g;
                    cPtr[cIdx + 2] = b;

                    // Standard Rec. 709 luminance weights
                    gPtr[rowOffset + x] = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                }
            }
        });

        return new StackFrame
        {
            Index = index,
            FilePath = filePath,
            Width = width,
            Height = height,
            BitDepth = 8,
            Format = PixelFormatType.RgbFloat32,
            ColorBuffer = colorBuffer,
            GrayBuffer = grayBuffer
        };
    }

    public unsafe void SaveImage(ImageBuffer<float> buffer, string outputPath, int bitDepth = 8)
    {
        int width = buffer.Width;
        int height = buffer.Height;
        int channels = buffer.Channels;
        float* src = buffer.DataPointer;

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (bitDepth >= 16)
        {
            // 16-bit Pro / 32-bit Float Pipeline
            using var image = new Image<Rgb48>(width, height);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    int rowOffset = y * width;

                    for (int x = 0; x < width; x++)
                    {
                        int idx = (rowOffset + x) * channels;
                        ushort r = (ushort)Math.Clamp(src[idx] * 65535f, 0, 65535);
                        ushort g = channels >= 2 ? (ushort)Math.Clamp(src[idx + 1] * 65535f, 0, 65535) : r;
                        ushort b = channels >= 3 ? (ushort)Math.Clamp(src[idx + 2] * 65535f, 0, 65535) : r;

                        row[x] = new Rgb48(r, g, b);
                    }
                }
            });

            if (outputPath.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || outputPath.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
            {
                var encoder = new TiffEncoder
                {
                    BitsPerPixel = TiffBitsPerPixel.Bit48
                };
                image.SaveAsTiff(outputPath, encoder);
            }
            else
            {
                image.Save(outputPath);
            }
        }
        else
        {
            using var image = new Image<Rgb24>(width, height);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    int rowOffset = y * width;

                    for (int x = 0; x < width; x++)
                    {
                        int idx = (rowOffset + x) * channels;
                        byte r = (byte)Math.Clamp(src[idx] * 255f, 0, 255);
                        byte g = channels >= 2 ? (byte)Math.Clamp(src[idx + 1] * 255f, 0, 255) : r;
                        byte b = channels >= 3 ? (byte)Math.Clamp(src[idx + 2] * 255f, 0, 255) : r;

                        row[x] = new Rgb24(r, g, b);
                    }
                }
            });
            image.Save(outputPath);
        }
    }
}
