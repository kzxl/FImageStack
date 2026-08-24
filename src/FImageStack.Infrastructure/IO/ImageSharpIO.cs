using FImageStack.Core;
using FImageStack.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FImageStack.Infrastructure.IO;

public interface IImageIO
{
    StackFrame LoadFrame(string filePath, int index);
    void SaveImage(ImageBuffer<float> buffer, string outputPath, int bitDepth = 8);
}

public sealed class ImageSharpIO : IImageIO
{
    public unsafe StackFrame LoadFrame(string filePath, int index)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Image file not found.", filePath);

        using var image = Image.Load<Rgb24>(filePath);
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

        if (bitDepth == 16)
        {
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
            image.Save(outputPath);
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
