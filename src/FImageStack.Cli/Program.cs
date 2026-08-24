using System.Diagnostics;
using FImageStack.Application.Services;
using FImageStack.Core;
using FImageStack.Core.Models;
using FImageStack.Infrastructure.IO;

namespace FImageStack.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine(" Intelligent Focus Fusion Engine (FImageStack CLI)");
        Console.WriteLine("=================================================");

        string inputDir = @"data\test_stack_50";
        string outputPath = @"data\output_fused.png";
        FusionMethod fusionMethod = FusionMethod.MultiScalePyramid;
        FocusMeasureMethod focusMethod = FocusMeasureMethod.ModifiedLaplacian;
        int pyramidLevels = 5;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--input" && i + 1 < args.Length) inputDir = args[++i];
            if (args[i] == "--output" && i + 1 < args.Length) outputPath = args[++i];
            if (args[i] == "--method" && i + 1 < args.Length)
            {
                string m = args[++i].ToLowerInvariant();
                fusionMethod = m switch
                {
                    "wta" or "winnertakesall" => FusionMethod.WinnerTakesAll,
                    "weighted" => FusionMethod.FocusWeighted,
                    _ => FusionMethod.MultiScalePyramid
                };
            }
            if (args[i] == "--levels" && i + 1 < args.Length) int.TryParse(args[++i], out pyramidLevels);
        }

        var imageIO = new ImageSharpIO();
        var projectService = new ProjectService();
        var stackService = new StackService(imageIO);

        if (!Directory.Exists(inputDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Input directory not found: {Path.GetFullPath(inputDir)}");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine($"Scanning folder : {Path.GetFullPath(inputDir)}");
        var imageFiles = projectService.DiscoverImageFiles(inputDir);
        Console.WriteLine($"Found           : {imageFiles.Count} frames");

        var validation = projectService.ValidateStack(imageFiles);
        if (!validation.IsValid)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            foreach (var issue in validation.Issues) Console.WriteLine($"Validation Error: {issue}");
            Console.ResetColor();
            return 1;
        }

        var settings = new FusionSettings
        {
            Method = fusionMethod,
            FocusMethod = focusMethod,
            PyramidLevels = pyramidLevels,
            EnableDepthSmoothing = true,
            SmoothingRadius = 2
        };

        Console.WriteLine($"Fusion Strategy : {settings.Method} (Levels: {settings.PyramidLevels})");
        Console.WriteLine($"Focus Measure   : {settings.FocusMethod}");
        Console.WriteLine("-------------------------------------------------");

        var progress = new Progress<StackProgress>(p =>
        {
            Console.Write($"\r[{p.Stage,-25}] {p.Percentage,5:F1}% | {p.Details,-35}");
        });

        try
        {
            var (fusedImage, depthResult, benchmark) = await stackService.ProcessStackAsync(imageFiles, settings, progress);
            Console.WriteLine();
            Console.WriteLine("-------------------------------------------------");

            // Save final output image
            Console.WriteLine($"Saving Fused Output to: {Path.GetFullPath(outputPath)}");
            imageIO.SaveImage(fusedImage, outputPath, bitDepth: 8);

            // Save Depth Map visualization
            string depthOutPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", "output_depth_map.png");
            imageIO.SaveImage(depthResult.DepthMap, depthOutPath, bitDepth: 8);
            Console.WriteLine($"Saving Depth Map to   : {Path.GetFullPath(depthOutPath)}");

            // Save Confidence Map visualization
            string confOutPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", "output_confidence_map.png");
            imageIO.SaveImage(depthResult.ConfidenceMap, confOutPath, bitDepth: 8);
            Console.WriteLine($"Saving Confidence Map : {Path.GetFullPath(confOutPath)}");

            // Print detailed Benchmark
            Console.WriteLine("=================================================");
            Console.WriteLine(" PERFORMANCE BENCHMARK REPORT");
            Console.WriteLine("=================================================");
            Console.WriteLine($" Stack Dimensions     : {benchmark.Width} x {benchmark.Height} ({benchmark.FrameCount} frames)");
            Console.WriteLine($" Load Time            : {benchmark.LoadTimeMs:F1} ms");
            Console.WriteLine($" Alignment Time       : {benchmark.AlignmentTimeMs:F1} ms");
            Console.WriteLine($" Focus Measure Time   : {benchmark.FocusMeasureTimeMs:F1} ms");
            Console.WriteLine($" Depth Map Time       : {benchmark.DepthMapTimeMs:F1} ms");
            Console.WriteLine($" Fusion ({benchmark.FusionMethod,-13}): {benchmark.FusionTimeMs:F1} ms");
            Console.WriteLine($" TOTAL PIPELINE TIME  : {benchmark.TotalTimeMs:F1} ms ({benchmark.TotalTimeMs / 1000.0:F2} s)");
            Console.WriteLine($" Peak Working Set     : {benchmark.PeakWorkingSetMb} MB");
            Console.WriteLine("=================================================");

            fusedImage.Dispose();
            depthResult.Dispose();

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nProcessing failed: {ex.Message}\n{ex.StackTrace}");
            Console.ResetColor();
            return 1;
        }
    }
}
