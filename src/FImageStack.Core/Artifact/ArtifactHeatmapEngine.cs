using FImageStack.Core.Models;

namespace FImageStack.Core.Artifact;

public interface IArtifactHeatmapEngine
{
    HeatmapLayerResult GenerateHeatmap(
        ArtifactHeatmapType type,
        ProcessedStackResult stackResult,
        IReadOnlyList<StackFrame> frames);

    ImageBuffer<float> ConvertScalarToTurboHeatmap(ImageBuffer<float> intensity);

    List<DefectHotspot> ExtractHotspots(
        ImageBuffer<float> intensity,
        ArtifactHeatmapType type,
        float threshold = 0.45f);
}

public sealed class ArtifactHeatmapEngine : IArtifactHeatmapEngine
{
    public unsafe HeatmapLayerResult GenerateHeatmap(
        ArtifactHeatmapType type,
        ProcessedStackResult stackResult,
        IReadOnlyList<StackFrame> frames)
    {
        if (stackResult == null) throw new ArgumentNullException(nameof(stackResult));

        int w = stackResult.FusedImage?.Width ?? (frames.Count > 0 ? frames[0].Width : 100);
        int h = stackResult.FusedImage?.Height ?? (frames.Count > 0 ? frames[0].Height : 100);

        var result = new HeatmapLayerResult(w, h, type);
        float* intPtr = result.IntensityMap.DataPointer;
        int total = w * h;

        switch (type)
        {
            case ArtifactHeatmapType.Ghost:
                PopulateFromArtifactRegions(stackResult.ArtifactMap, ArtifactType.Ghost, intPtr, w, h);
                break;

            case ArtifactHeatmapType.Halo:
                PopulateFromArtifactRegions(stackResult.ArtifactMap, ArtifactType.Halo, intPtr, w, h);
                break;

            case ArtifactHeatmapType.Blur:
                if (stackResult.DepthResult?.ConfidenceMap != null)
                {
                    float* p = stackResult.DepthResult.ConfidenceMap.DataPointer;
                    for (int i = 0; i < total; i++) intPtr[i] = Math.Clamp(1.0f - p[i], 0f, 1f);
                }
                PopulateFromArtifactRegions(stackResult.ArtifactMap, ArtifactType.FocusGap, intPtr, w, h);
                break;

            case ArtifactHeatmapType.FocusConfidence:
                if (stackResult.DepthResult?.ConfidenceMap != null)
                {
                    float* p = stackResult.DepthResult.ConfidenceMap.DataPointer;
                    for (int i = 0; i < total; i++) intPtr[i] = p[i];
                }
                break;

            case ArtifactHeatmapType.AlignmentError:
                if (frames.Count > 0)
                {
                    for (int i = 0; i < total; i++)
                    {
                        float minConf = 1.0f;
                        foreach (var f in frames)
                        {
                            if (f.AlignmentConfidence < minConf) minConf = (float)f.AlignmentConfidence;
                        }
                        intPtr[i] = Math.Clamp(1.0f - minConf, 0f, 1f);
                    }
                }
                PopulateFromArtifactRegions(stackResult.ArtifactMap, ArtifactType.Misalignment, intPtr, w, h);
                break;

            case ArtifactHeatmapType.ReconstructionRisk:
            case ArtifactHeatmapType.CompositeDefect:
            default:
                if (stackResult.ArtifactMap != null)
                {
                    foreach (var r in stackResult.ArtifactMap.Regions)
                    {
                        PaintRegionSplat(intPtr, w, h, r.X + r.Width / 2, r.Y + r.Height / 2, Math.Max(r.Width, r.Height), r.Severity);
                    }
                }
                break;
        }

        // Generate Colorized Heatmap
        using var colorized = ConvertScalarToTurboHeatmap(result.IntensityMap);
        float* colPtr = colorized.DataPointer;
        float* dstCol = result.RgbHeatmap.DataPointer;
        for (int i = 0; i < total * 3; i++)
        {
            dstCol[i] = colPtr[i];
        }

        // Extract Hotspots
        var hotspots = ExtractHotspots(result.IntensityMap, type, threshold: 0.45f);
        result.Hotspots.AddRange(hotspots);

        return result;
    }

    private static unsafe void PopulateFromArtifactRegions(
        ArtifactMap? artifactMap,
        ArtifactType targetType,
        float* intPtr,
        int w,
        int h)
    {
        if (artifactMap == null) return;

        foreach (var r in artifactMap.Regions)
        {
            if (r.Type == targetType)
            {
                PaintRegionSplat(intPtr, w, h, r.X + r.Width / 2, r.Y + r.Height / 2, Math.Max(r.Width, r.Height), r.Severity);
            }
        }
    }

    private static unsafe void PaintRegionSplat(float* intPtr, int w, int h, int cx, int cy, int size, float severity)
    {
        int r = Math.Clamp(size / 2, 2, 64);
        int minX = Math.Max(0, cx - r);
        int maxX = Math.Min(w - 1, cx + r);
        int minY = Math.Max(0, cy - r);
        int maxY = Math.Min(h - 1, cy + r);
        float rSq = r * r;

        for (int y = minY; y <= maxY; y++)
        {
            int rowOffset = y * w;
            for (int x = minX; x <= maxX; x++)
            {
                float dSq = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                if (dSq <= rSq)
                {
                    float falloff = 1f - MathF.Sqrt(dSq) / r;
                    float val = severity * falloff;
                    int idx = rowOffset + x;
                    if (val > intPtr[idx]) intPtr[idx] = val;
                }
            }
        }
    }

    public unsafe ImageBuffer<float> ConvertScalarToTurboHeatmap(ImageBuffer<float> intensity)
    {
        int w = intensity.Width;
        int h = intensity.Height;
        var output = new ImageBuffer<float>(w, h, 3);

        float* src = intensity.DataPointer;
        float* dst = output.DataPointer;

        Parallel.For(0, h, y =>
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                int idx = rowOffset + x;
                float v = Math.Clamp(src[idx], 0f, 1f);
                (float r, float g, float b) = MapTurboColor(v);

                int dstIdx = idx * 3;
                dst[dstIdx] = r;
                dst[dstIdx + 1] = g;
                dst[dstIdx + 2] = b;
            }
        });

        return output;
    }

    public unsafe List<DefectHotspot> ExtractHotspots(
        ImageBuffer<float> intensity,
        ArtifactHeatmapType type,
        float threshold = 0.45f)
    {
        int w = intensity.Width;
        int h = intensity.Height;
        float* ptr = intensity.DataPointer;
        var list = new List<DefectHotspot>();
        int idCounter = 1;

        int step = Math.Max(4, w / 64);

        for (int y = step; y < h - step; y += step)
        {
            for (int x = step; x < w - step; x += step)
            {
                float val = ptr[y * w + x];
                if (val >= threshold)
                {
                    // Check if local maximum
                    bool isMax = true;
                    for (int dy = -step; dy <= step; dy += step)
                    {
                        for (int dx = -step; dx <= step; dx += step)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (ptr[(y + dy) * w + (x + dx)] > val)
                            {
                                isMax = false;
                                break;
                            }
                        }
                        if (!isMax) break;
                    }

                    if (isMax)
                    {
                        list.Add(new DefectHotspot
                        {
                            Id = idCounter++,
                            X = x,
                            Y = y,
                            Radius = step * 2,
                            DefectType = type,
                            Severity = val,
                            Description = $"{type} defect at ({x}, {y}) - Severity: {val * 100f:F0}%"
                        });
                    }
                }
            }
        }

        list.Sort((a, b) => b.Severity.CompareTo(a.Severity));
        return list;
    }

    private static (float r, float g, float b) MapTurboColor(float v)
    {
        if (v <= 0.25f)
        {
            float t = v / 0.25f;
            return (0.1f * (1f - t) + 0.0f * t, 0.2f * (1f - t) + 0.8f * t, 0.8f * (1f - t) + 0.9f * t);
        }
        if (v <= 0.50f)
        {
            float t = (v - 0.25f) / 0.25f;
            return (0.0f * (1f - t) + 0.2f * t, 0.8f * (1f - t) + 0.9f * t, 0.9f * (1f - t) + 0.2f * t);
        }
        if (v <= 0.75f)
        {
            float t = (v - 0.50f) / 0.25f;
            return (0.2f * (1f - t) + 1.0f * t, 0.9f * (1f - t) + 0.6f * t, 0.2f * (1f - t) + 0.0f * t);
        }
        else
        {
            float t = (v - 0.75f) / 0.25f;
            return (1.0f * (1f - t) + 0.9f * t, 0.6f * (1f - t) + 0.05f * t, 0.0f * (1f - t) + 0.1f * t);
        }
    }
}
