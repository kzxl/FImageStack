using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using FImageStack.Core;
using FImageStack.Core.DepthMap;
using FImageStack.Core.Models;
using FImageStack.Core.Project;
using FImageStack.Core.Retouch;

namespace FImageStack.Application.Services;

public interface IProjectService
{
    List<string> DiscoverImageFiles(string directoryPath);
    StackValidationResult ValidateStack(IReadOnlyList<string> filePaths);
    Task SaveProjectAsync(string filePath, FStackProject project, ProcessedStackResult? result, RetouchLayer? retouchLayer, CancellationToken ct = default);
    Task<LoadedProjectResult> LoadProjectAsync(string filePath, CancellationToken ct = default);
}

public sealed class ProjectService : IProjectService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp",
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf", ".rw2", ".pef"
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public List<string> DiscoverImageFiles(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var files = Directory.GetFiles(directoryPath)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => NaturalSortKey(Path.GetFileName(f)))
            .ToList();

        return files;
    }

    public StackValidationResult ValidateStack(IReadOnlyList<string> filePaths)
    {
        var result = new StackValidationResult
        {
            FrameCount = filePaths.Count
        };

        if (filePaths.Count < 2)
        {
            result.Issues.Add("Focus stacking requires at least 2 image frames.");
            return result;
        }

        return result;
    }

    public async Task SaveProjectAsync(
        string filePath,
        FStackProject project,
        ProcessedStackResult? result,
        RetouchLayer? retouchLayer,
        CancellationToken ct = default)
    {
        project.LastModifiedAt = DateTime.UtcNow;

        // Serialize retouch strokes
        project.RetouchStrokes.Clear();
        if (retouchLayer != null)
        {
            foreach (var s in retouchLayer.Strokes)
            {
                project.RetouchStrokes.Add(new RetouchStrokeData
                {
                    StrokeId = s.StrokeId,
                    Tool = s.Tool,
                    SourceFrameIndex = s.SourceFrameIndex,
                    CenterX = s.CenterX,
                    CenterY = s.CenterY,
                    Radius = s.Radius,
                    Feather = s.Feather,
                    Opacity = s.Opacity
                });
            }
        }

        if (result != null)
        {
            project.Width = result.FusedImage.Width;
            project.Height = result.FusedImage.Height;
            project.QualityReport = result.QualityReport;
            project.Benchmark = result.Benchmark;
        }

        if (File.Exists(filePath)) File.Delete(filePath);

        using var zipStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        // 1. Write project.json
        var metaEntry = archive.CreateEntry("project.json", CompressionLevel.Fastest);
        using (var entryStream = metaEntry.Open())
        {
            await JsonSerializer.SerializeAsync(entryStream, project, JsonOpts, ct);
        }

        // 2. Write Cached Binary Buffers (Zero-Recomputation Stream)
        if (result != null)
        {
            // Fused Linear Image Buffer
            var fusedEntry = archive.CreateEntry("cache/fused.bin", CompressionLevel.Fastest);
            using (var s = fusedEntry.Open())
            {
                var span = MemoryMarshal.AsBytes(result.FusedImage.AsSpan());
                await s.WriteAsync(span.ToArray(), ct);
            }

            // Depth Map Buffer
            var depthEntry = archive.CreateEntry("cache/depth.bin", CompressionLevel.Fastest);
            using (var s = depthEntry.Open())
            {
                var span = MemoryMarshal.AsBytes(result.DepthResult.DepthMap.AsSpan());
                await s.WriteAsync(span.ToArray(), ct);
            }

            // Source Map Buffer
            var srcEntry = archive.CreateEntry("cache/source.bin", CompressionLevel.Fastest);
            using (var s = srcEntry.Open())
            {
                var span = MemoryMarshal.AsBytes(result.DepthResult.SourceFrameMap.AsSpan());
                await s.WriteAsync(span.ToArray(), ct);
            }

            // Confidence Map Buffer
            var confEntry = archive.CreateEntry("cache/conf.bin", CompressionLevel.Fastest);
            using (var s = confEntry.Open())
            {
                var span = MemoryMarshal.AsBytes(result.DepthResult.ConfidenceMap.AsSpan());
                await s.WriteAsync(span.ToArray(), ct);
            }
        }
    }

    public async Task<LoadedProjectResult> LoadProjectAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Project file not found.", filePath);

        using var zipStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // 1. Read project.json
        var metaEntry = archive.GetEntry("project.json")
            ?? throw new InvalidDataException("Invalid .fstack project file: missing project.json");

        FStackProject project;
        using (var entryStream = metaEntry.Open())
        {
            project = (await JsonSerializer.DeserializeAsync<FStackProject>(entryStream, JsonOpts, ct))
                ?? throw new InvalidDataException("Failed to parse project.json");
        }

        var loadedResult = new LoadedProjectResult
        {
            Project = project
        };

        // 2. Restore Retouch Layer
        if (project.Width > 0 && project.Height > 0)
        {
            var retouchLayer = new RetouchLayer(project.Width, project.Height);
            foreach (var sd in project.RetouchStrokes)
            {
                retouchLayer.Strokes.Add(new RetouchStroke
                {
                    StrokeId = sd.StrokeId,
                    Tool = sd.Tool,
                    SourceFrameIndex = sd.SourceFrameIndex,
                    CenterX = sd.CenterX,
                    CenterY = sd.CenterY,
                    Radius = sd.Radius,
                    Feather = sd.Feather,
                    Opacity = sd.Opacity
                });
            }
            loadedResult.RestoredRetouchLayer = retouchLayer;
        }

        // 3. Restore Cached Binary Result (Zero Recomputation)
        var fusedEntry = archive.GetEntry("cache/fused.bin");
        var depthEntry = archive.GetEntry("cache/depth.bin");
        var srcEntry = archive.GetEntry("cache/source.bin");
        var confEntry = archive.GetEntry("cache/conf.bin");

        if (fusedEntry != null && depthEntry != null && srcEntry != null && confEntry != null && project.Width > 0 && project.Height > 0)
        {
            int w = project.Width;
            int h = project.Height;

            var fusedBuffer = new ImageBuffer<float>(w, h, 3, PixelFormatType.RgbFloat32);
            using (var s = fusedEntry.Open())
            {
                var bytes = new byte[w * h * 3 * sizeof(float)];
                await ReadExactAsync(s, bytes, ct);
                MemoryMarshal.Cast<byte, float>(bytes).CopyTo(fusedBuffer.AsSpan());
            }

            var depthResult = new DepthMapResult(w, h);

            using (var s = depthEntry.Open())
            {
                var bytes = new byte[w * h * sizeof(float)];
                await ReadExactAsync(s, bytes, ct);
                MemoryMarshal.Cast<byte, float>(bytes).CopyTo(depthResult.DepthMap.AsSpan());
            }

            using (var s = srcEntry.Open())
            {
                var bytes = new byte[w * h * sizeof(int)];
                await ReadExactAsync(s, bytes, ct);
                MemoryMarshal.Cast<byte, int>(bytes).CopyTo(depthResult.SourceFrameMap.AsSpan());
            }

            using (var s = confEntry.Open())
            {
                var bytes = new byte[w * h * sizeof(float)];
                await ReadExactAsync(s, bytes, ct);
                MemoryMarshal.Cast<byte, float>(bytes).CopyTo(depthResult.ConfidenceMap.AsSpan());
            }

            var cachedStackResult = new ProcessedStackResult(w, h)
            {
                FusedImage = fusedBuffer,
                DepthResult = depthResult,
                QualityReport = project.QualityReport,
                Benchmark = project.Benchmark ?? new BenchmarkReport()
            };

            loadedResult.CachedResult = cachedStackResult;
        }

        return loadedResult;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) break;
            totalRead += read;
        }
    }

    private static string NaturalSortKey(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, @"\d+", match => match.Value.PadLeft(10, '0'));
    }
}
