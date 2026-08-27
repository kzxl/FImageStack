using System.Globalization;
using System.Numerics;
using FImageStack.Core.Models;

namespace FImageStack.Core.Depth3D;

public interface IDepthMeshExporter
{
    ImageBuffer<float> GenerateNormalMap(ImageBuffer<float> depthMap, float zScale = 1.0f);
    void ExportToPly(ImageBuffer<float> depthMap, ImageBuffer<float>? colorMap, Stream outputStream, DepthMeshOptions options);
    void ExportToObj(ImageBuffer<float> depthMap, ImageBuffer<float>? colorMap, TextWriter writer, DepthMeshOptions options);
}

public sealed class DepthMeshExporter : IDepthMeshExporter
{
    public unsafe ImageBuffer<float> GenerateNormalMap(ImageBuffer<float> depthMap, float zScale = 1.0f)
    {
        if (depthMap == null) throw new ArgumentNullException(nameof(depthMap));

        int w = depthMap.Width;
        int h = depthMap.Height;
        var normalMap = new ImageBuffer<float>(w, h, 3, PixelFormatType.RgbFloat32);

        float* dPtr = depthMap.DataPointer;
        float* nPtr = normalMap.DataPointer;

        Parallel.For(0, h, y =>
        {
            int yPrev = Math.Max(0, y - 1);
            int yNext = Math.Min(h - 1, y + 1);

            for (int x = 0; x < w; x++)
            {
                int xPrev = Math.Max(0, x - 1);
                int xNext = Math.Min(w - 1, x + 1);

                // Sobel filter for Z gradients
                float zTL = dPtr[yPrev * w + xPrev];
                float zT  = dPtr[yPrev * w + x];
                float zTR = dPtr[yPrev * w + xNext];
                float zL  = dPtr[y * w + xPrev];
                float zR  = dPtr[y * w + xNext];
                float zBL = dPtr[yNext * w + xPrev];
                float zB  = dPtr[yNext * w + x];
                float zBR = dPtr[yNext * w + xNext];

                float dzdx = ((zTR + 2f * zR + zBR) - (zTL + 2f * zL + zBL)) * (zScale / 8f);
                float dzdy = ((zBL + 2f * zB + zBR) - (zTL + 2f * zT + zTR)) * (zScale / 8f);

                // Vector N = (-dzdx, -dzdy, 1.0)
                var normal = new Vector3(-dzdx, -dzdy, 1.0f);
                if (normal.LengthSquared() > 1e-8f)
                {
                    normal = Vector3.Normalize(normal);
                }
                else
                {
                    normal = Vector3.UnitZ;
                }

                // Map [-1, 1] to RGB [0, 1]
                int dstIdx = (y * w + x) * 3;
                nPtr[dstIdx]     = normal.X * 0.5f + 0.5f;
                nPtr[dstIdx + 1] = normal.Y * 0.5f + 0.5f;
                nPtr[dstIdx + 2] = normal.Z * 0.5f + 0.5f;
            }
        });

        return normalMap;
    }

    public void ExportToPly(
        ImageBuffer<float> depthMap, 
        ImageBuffer<float>? colorMap, 
        Stream outputStream, 
        DepthMeshOptions options)
    {
        if (depthMap == null) throw new ArgumentNullException(nameof(depthMap));
        if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));

        int w = depthMap.Width;
        int h = depthMap.Height;
        int step = Math.Max(1, options.DecimationStep);

        // Count valid vertices
        int vertexCount = 0;
        for (int y = 0; y < h; y += step)
        {
            for (int x = 0; x < w; x += step)
            {
                float z = depthMap.At(x, y);
                if (z >= options.DepthMinCutoff && z <= options.DepthMaxCutoff)
                {
                    vertexCount++;
                }
            }
        }

        using var writer = new StreamWriter(outputStream, System.Text.Encoding.ASCII, leaveOpen: true);
        var inv = CultureInfo.InvariantCulture;

        // Write PLY Header
        writer.WriteLine("ply");
        writer.WriteLine("format ascii 1.0");
        writer.WriteLine("comment FImageStack Computational 3D Reconstruction Point Cloud");
        writer.WriteLine($"element vertex {vertexCount}");
        writer.WriteLine("property float x");
        writer.WriteLine("property float y");
        writer.WriteLine("property float z");
        if (colorMap != null)
        {
            writer.WriteLine("property uchar red");
            writer.WriteLine("property uchar green");
            writer.WriteLine("property uchar blue");
        }
        writer.WriteLine("end_header");

        float zScale = options.ZScale;
        if (options.InvertZ) zScale = -zScale;

        // Write Vertex Records
        for (int y = 0; y < h; y += step)
        {
            for (int x = 0; x < w; x += step)
            {
                float zVal = depthMap.At(x, y);
                if (zVal < options.DepthMinCutoff || zVal > options.DepthMaxCutoff) continue;

                float xPos = x;
                float yPos = (h - 1 - y); // Flip Y for standard Cartesian 3D coordinates
                float zPos = zVal * zScale;

                if (colorMap != null)
                {
                    int r = Math.Clamp((int)(colorMap.At(x, y, 0) * 255.0f), 0, 255);
                    int g = Math.Clamp((int)(colorMap.At(x, y, 1) * 255.0f), 0, 255);
                    int b = Math.Clamp((int)(colorMap.At(x, y, 2) * 255.0f), 0, 255);
                    writer.WriteLine(string.Format(inv, "{0:F2} {1:F2} {2:F3} {3} {4} {5}", xPos, yPos, zPos, r, g, b));
                }
                else
                {
                    writer.WriteLine(string.Format(inv, "{0:F2} {1:F2} {2:F3}", xPos, yPos, zPos));
                }
            }
        }

        writer.Flush();
    }

    public void ExportToObj(
        ImageBuffer<float> depthMap, 
        ImageBuffer<float>? colorMap, 
        TextWriter writer, 
        DepthMeshOptions options)
    {
        if (depthMap == null) throw new ArgumentNullException(nameof(depthMap));
        if (writer == null) throw new ArgumentNullException(nameof(writer));

        int w = depthMap.Width;
        int h = depthMap.Height;
        int step = Math.Max(1, options.DecimationStep);
        var inv = CultureInfo.InvariantCulture;

        writer.WriteLine("# FImageStack Computational 3D Surface Mesh");
        writer.WriteLine("# Dimensions: " + w + "x" + h + " Decimation: " + step);

        float zScale = options.ZScale;
        if (options.InvertZ) zScale = -zScale;

        int gridW = (w + step - 1) / step;
        int gridH = (h + step - 1) / step;
        int[,] vertexIndices = new int[gridW, gridH];
        int currentVertexIdx = 1;

        // 1. Write Vertices
        int gy = 0;
        for (int y = 0; y < h; y += step, gy++)
        {
            int gx = 0;
            for (int x = 0; x < w; x += step, gx++)
            {
                float zVal = depthMap.At(x, y);
                if (zVal >= options.DepthMinCutoff && zVal <= options.DepthMaxCutoff)
                {
                    float xPos = (float)x / w;
                    float yPos = (float)(h - 1 - y) / h;
                    float zPos = (zVal * zScale) / Math.Max(w, h);

                    if (colorMap != null)
                    {
                        float r = Math.Clamp(colorMap.At(x, y, 0), 0f, 1f);
                        float g = Math.Clamp(colorMap.At(x, y, 1), 0f, 1f);
                        float b = Math.Clamp(colorMap.At(x, y, 2), 0f, 1f);
                        writer.WriteLine(string.Format(inv, "v {0:F4} {1:F4} {2:F4} {3:F3} {4:F3} {5:F3}", xPos, yPos, zPos, r, g, b));
                    }
                    else
                    {
                        writer.WriteLine(string.Format(inv, "v {0:F4} {1:F4} {2:F4}", xPos, yPos, zPos));
                    }

                    vertexIndices[gx, gy] = currentVertexIdx++;
                }
                else
                {
                    vertexIndices[gx, gy] = 0; // Invalid / Out of bounds
                }
            }
        }

        // 2. Write Triangle Faces
        for (int j = 0; j < gridH - 1; j++)
        {
            for (int i = 0; i < gridW - 1; i++)
            {
                int vTL = vertexIndices[i, j];
                int vTR = vertexIndices[i + 1, j];
                int vBL = vertexIndices[i, j + 1];
                int vBR = vertexIndices[i + 1, j + 1];

                // Quad -> 2 Triangles: (TL, BL, TR) and (TR, BL, BR)
                if (vTL > 0 && vBL > 0 && vTR > 0)
                {
                    writer.WriteLine($"f {vTL} {vBL} {vTR}");
                }
                if (vTR > 0 && vBL > 0 && vBR > 0)
                {
                    writer.WriteLine($"f {vTR} {vBL} {vBR}");
                }
            }
        }

        writer.Flush();
    }
}
