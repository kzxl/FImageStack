using System.Diagnostics;
using FImageStack.Core;
using FImageStack.Core.Models;
using FImageStack.Infrastructure.IO;

namespace FImageStack.DatasetGenerator;

internal static class Program
{
    private static unsafe void Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine(" FImageStack Synthetic Focus-Bracket Dataset Generator");
        Console.WriteLine("=================================================");

        string outputDir = @"data\test_stack_50";
        int frameCount = 50;
        int width = 1920;
        int height = 1080;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--output" && i + 1 < args.Length) outputDir = args[++i];
            if (args[i] == "--frames" && i + 1 < args.Length) int.TryParse(args[++i], out frameCount);
            if (args[i] == "--width" && i + 1 < args.Length) int.TryParse(args[++i], out width);
            if (args[i] == "--height" && i + 1 < args.Length) int.TryParse(args[++i], out height);
        }

        Directory.CreateDirectory(outputDir);
        var imageIO = new ImageSharpIO();

        Console.WriteLine($"Target Directory : {Path.GetFullPath(outputDir)}");
        Console.WriteLine($"Resolution       : {width} x {height}");
        Console.WriteLine($"Frame Count      : {frameCount} frames");
        Console.WriteLine("-------------------------------------------------");

        var sw = Stopwatch.StartNew();

        // 1. Synthesize Ground Truth Scene & Ground Truth Depth Map
        using var gtImage = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32);
        using var gtDepth = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);

        GenerateScene(gtImage, gtDepth);

        // 2. Generate Focus-Bracket Frames with Depth of Field (DoF) Blur Simulation
        float* gtPtr = gtImage.DataPointer;
        float* depthPtr = gtDepth.DataPointer;

        for (int f = 0; f < frameCount; f++)
        {
            float focusDepth = (float)f / (frameCount - 1); // 0.0 (near) to 1.0 (far)
            using var frameBuffer = new ImageBuffer<float>(width, height, 3, PixelFormatType.RgbFloat32);
            float* framePtr = frameBuffer.DataPointer;

            Parallel.For(0, height, y =>
            {
                int rowOffset = y * width;
                var rand = new Random(f * 1000 + y);

                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    float pixelDepth = depthPtr[idx];
                    float depthDiff = MathF.Abs(pixelDepth - focusDepth);

                    // Circle of Confusion (CoC) radius
                    float blurSigma = depthDiff * 14.0f;
                    float r = 0f, g = 0f, b = 0f;

                    if (blurSigma < 0.35f)
                    {
                        int cIdx = idx * 3;
                        r = gtPtr[cIdx];
                        g = gtPtr[cIdx + 1];
                        b = gtPtr[cIdx + 2];
                    }
                    else
                    {
                        int radius = Math.Min(12, (int)MathF.Ceiling(blurSigma * 1.8f));
                        float totalWeight = 0f;
                        int step = radius > 6 ? 2 : 1;

                        int y0 = Math.Max(0, y - radius);
                        int y1 = Math.Min(height - 1, y + radius);
                        int x0 = Math.Max(0, x - radius);
                        int x1 = Math.Min(width - 1, x + radius);

                        float invTwoSigmaSq = 1f / (2f * blurSigma * blurSigma + 1e-4f);

                        for (int sy = y0; sy <= y1; sy += step)
                        {
                            int sRow = sy * width;
                            int dy = sy - y;
                            int dySq = dy * dy;

                            for (int sx = x0; sx <= x1; sx += step)
                            {
                                int dx = sx - x;
                                float distSq = dx * dx + dySq;
                                float weight = MathF.Exp(-distSq * invTwoSigmaSq);
                                totalWeight += weight;

                                int sIdx = (sRow + sx) * 3;
                                r += gtPtr[sIdx] * weight;
                                g += gtPtr[sIdx + 1] * weight;
                                b += gtPtr[sIdx + 2] * weight;
                            }
                        }

                        if (totalWeight > 0)
                        {
                            float invWeight = 1f / totalWeight;
                            r *= invWeight;
                            g *= invWeight;
                            b *= invWeight;
                        }
                    }

                    // Add subtle sensor noise (ISO simulation)
                    float noise = (float)(rand.NextDouble() - 0.5) * 0.005f;
                    int dstIdx = idx * 3;
                    framePtr[dstIdx] = Math.Clamp(r + noise, 0f, 1f);
                    framePtr[dstIdx + 1] = Math.Clamp(g + noise, 0f, 1f);
                    framePtr[dstIdx + 2] = Math.Clamp(b + noise, 0f, 1f);
                }
            });

            string frameFileName = $"frame_{f + 1:D3}.png";
            string framePath = Path.Combine(outputDir, frameFileName);
            imageIO.SaveImage(frameBuffer, framePath);

            if ((f + 1) % 10 == 0 || f == frameCount - 1)
            {
                Console.WriteLine($"   [{f + 1:D3}/{frameCount:D3}] Generated {frameFileName} (Z={focusDepth:F2})");
            }
        }

        sw.Stop();
        Console.WriteLine($"==> Completed {frameCount} frames in {sw.Elapsed.TotalSeconds:F2}s ({outputDir})");
    }

    private static unsafe void GenerateScene(ImageBuffer<float> image, ImageBuffer<float> depth)
    {
        int w = image.Width;
        int h = image.Height;
        float* img = image.DataPointer;
        float* dep = depth.DataPointer;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                int idx = rowOffset + x;
                int cIdx = idx * 3;

                // Base 3D ramp depth from top-left (z=0.9) to bottom-right (z=0.1)
                float baseDepth = 0.95f - 0.85f * ((float)x / w * 0.5f + (float)y / h * 0.5f);

                // Background pattern
                float r = 0.15f + 0.1f * MathF.Sin(x * 0.05f) * MathF.Cos(y * 0.05f);
                float g = 0.18f + 0.08f * MathF.Sin(x * 0.03f);
                float b = 0.28f + 0.12f * MathF.Cos(y * 0.04f);
                float pixelDepth = baseDepth;

                // Object 1 (Foreground Left): Checkerboard (Z = 0.12)
                if (x >= 150 && x <= 650 && y >= 550 && y <= 950)
                {
                    pixelDepth = 0.12f;
                    int cx = (x - 150) / 10;
                    int cy = (y - 550) / 10;
                    bool checker = ((cx + cy) % 2) == 0;
                    r = checker ? 0.95f : 0.08f;
                    g = checker ? 0.85f : 0.08f;
                    b = checker ? 0.20f : 0.08f;
                }

                // Object 2 (Midground Center): Siemens Star & Rings (Z = 0.50)
                float dxCenter = x - 960;
                float dyCenter = y - 540;
                float distCenter = MathF.Sqrt(dxCenter * dxCenter + dyCenter * dyCenter);
                if (distCenter <= 280)
                {
                    pixelDepth = 0.50f;
                    float angle = MathF.Atan2(dyCenter, dxCenter);
                    float spoke = MathF.Sin(angle * 24f);
                    float ring = MathF.Sin(distCenter * 0.3f);
                    float pattern = (spoke > 0 ? 0.8f : 0.2f) * (ring > 0 ? 1.0f : 0.6f);

                    r = pattern * 0.1f + 0.8f * (distCenter / 280f);
                    g = pattern * 0.9f;
                    b = pattern * 0.7f + 0.2f;
                }

                // Object 3 (Background Right): Micro-Grid (Z = 0.88)
                if (x >= 1250 && x <= 1780 && y >= 150 && y <= 550)
                {
                    pixelDepth = 0.88f;
                    bool gridX = (x % 8) < 2;
                    bool gridY = (y % 8) < 2;
                    bool isGrid = gridX || gridY;

                    r = isGrid ? 0.95f : 0.25f;
                    g = isGrid ? 0.35f : 0.15f;
                    b = isGrid ? 0.85f : 0.30f;
                }

                // Object 4 (Mid-Foreground Leaf detail) (Z = 0.28)
                if (x >= 1200 && x <= 1800 && y >= 650 && y <= 1000)
                {
                    pixelDepth = 0.28f;
                    float stripe = MathF.Sin((x + y) * 0.4f);
                    float val = stripe > 0 ? 0.92f : 0.12f;
                    r = val * 0.2f;
                    g = val * 0.9f;
                    b = val * 0.4f;
                }

                img[cIdx] = Math.Clamp(r, 0f, 1f);
                img[cIdx + 1] = Math.Clamp(g, 0f, 1f);
                img[cIdx + 2] = Math.Clamp(b, 0f, 1f);
                dep[idx] = Math.Clamp(pixelDepth, 0f, 1f);
            }
        });
    }
}
