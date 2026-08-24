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
        bool qualityAnalysis = false;
        bool motionAware = false;
        bool detectArtifacts = false;
        bool autoRepair = false;
        bool tiled = false;
        int tileSize = 512;

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
            if (args[i] == "--analyze-quality") qualityAnalysis = true;
            if (args[i] == "--motion-aware") motionAware = true;
            if (args[i] == "--detect-artifacts") detectArtifacts = true;
            if (args[i] == "--repair") { detectArtifacts = true; autoRepair = true; }
            if (args[i] == "--tiled") tiled = true;
            if (args[i] == "--tile-size" && i + 1 < args.Length) int.TryParse(args[++i], out tileSize);
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

        Console.WriteLine($"Scanning folder   : {Path.GetFullPath(inputDir)}");
        var imageFiles = projectService.DiscoverImageFiles(inputDir);
        Console.WriteLine($"Found             : {imageFiles.Count} frames");

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
            SmoothingRadius = 2,
            EnableQualityAnalysis = qualityAnalysis,
            EnableMotionSuppression = motionAware,
            EnableArtifactDetection = detectArtifacts,
            EnableAutoRepair = autoRepair,
            EnableTiledProcessing = tiled,
            TileSize = tileSize
        };

        Console.WriteLine($"Fusion Strategy   : {settings.Method} (Levels: {settings.PyramidLevels})");
        Console.WriteLine($"Focus Measure     : {settings.FocusMethod}");
        Console.WriteLine($"Diagnostics Flags : Quality={settings.EnableQualityAnalysis}, Motion={settings.EnableMotionSuppression}, Artifacts={settings.EnableArtifactDetection}, Repair={settings.EnableAutoRepair}, Tiled={settings.EnableTiledProcessing}");
        Console.WriteLine("-------------------------------------------------");

        var progress = new Progress<StackProgress>(p =>
        {
            Console.Write($"\r[{p.Stage,-25}] {p.Percentage,5:F1}% | {p.Details,-35}");
        });

        try
        {
            using var result = await stackService.ProcessStackAsync(imageFiles, settings, progress);
            Console.WriteLine();
            Console.WriteLine("-------------------------------------------------");

            string outDir = Path.GetDirectoryName(outputPath) ?? ".";

            // Save final output image (repaired if available, otherwise fused)
            var finalImg = result.RepairedImage ?? result.FusedImage;
            Console.WriteLine($"Saving Fused Output to  : {Path.GetFullPath(outputPath)}");
            imageIO.SaveImage(finalImg, outputPath, bitDepth: 8);

            // Save Depth Map visualization
            string depthOutPath = Path.Combine(outDir, "output_depth_map.png");
            imageIO.SaveImage(result.DepthResult.DepthMap, depthOutPath, bitDepth: 8);
            Console.WriteLine($"Saving Depth Map to     : {Path.GetFullPath(depthOutPath)}");

            // Save Confidence Map visualization
            string confOutPath = Path.Combine(outDir, "output_confidence_map.png");
            imageIO.SaveImage(result.DepthResult.ConfidenceMap, confOutPath, bitDepth: 8);
            Console.WriteLine($"Saving Confidence Map   : {Path.GetFullPath(confOutPath)}");

            // Save Motion Map if computed
            if (result.MotionResult != null)
            {
                string motionOutPath = Path.Combine(outDir, "output_motion_map.png");
                imageIO.SaveImage(result.MotionResult.MotionMap, motionOutPath, bitDepth: 8);
                Console.WriteLine($"Saving Motion Map to    : {Path.GetFullPath(motionOutPath)}");
            }

            // Print Quality Report if enabled
            if (result.QualityReport != null)
            {
                Console.WriteLine("=================================================");
                Console.WriteLine(" STACK QUALITY & COVERAGE REPORT");
                Console.WriteLine("=================================================");
                Console.WriteLine($" Overall Quality Score  : {result.QualityReport.OverallScore:F1}% ({result.QualityReport.FocusCoverageRating})");
                Console.WriteLine($" Focus Coverage         : {result.QualityReport.FocusCoveragePercentage:F1}%");
                Console.WriteLine($" Detected Focus Gaps    : {result.QualityReport.DetectedGaps.Count}");
                foreach (var gap in result.QualityReport.DetectedGaps)
                {
                    Console.WriteLine($"   * {gap.Description}");
                }
                foreach (var warn in result.QualityReport.Warnings)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"   [WARN] {warn}");
                    Console.ResetColor();
                }
            }

            // Print Artifact Report if enabled
            if (result.ArtifactMap != null)
            {
                Console.WriteLine("=================================================");
                Console.WriteLine(" ARTIFACT DETECTION & RECONSTRUCTION");
                Console.WriteLine("=================================================");
                Console.WriteLine($" Total Regions Detected : {result.ArtifactMap.Regions.Count}");
                Console.WriteLine($"   - Halos              : {result.ArtifactMap.HaloCount}");
                Console.WriteLine($"   - Ghosts             : {result.ArtifactMap.GhostCount}");
                Console.WriteLine($"   - Low Confidence     : {result.ArtifactMap.LowConfidenceCount}");
                if (result.RepairReport != null)
                {
                    Console.WriteLine($" Auto-Repaired Regions  : {result.RepairReport.RepairedRegionsCount}/{result.ArtifactMap.Regions.Count}");
                    Console.WriteLine($" Auto-Repaired Pixels   : {result.RepairReport.RepairedPixelsCount:N0} pixels");
                }
            }

            // Print detailed Benchmark
            var benchmark = result.Benchmark;
            Console.WriteLine("=================================================");
            Console.WriteLine(" PERFORMANCE BENCHMARK REPORT");
            Console.WriteLine("=================================================");
            Console.WriteLine($" Stack Dimensions       : {benchmark.Width} x {benchmark.Height} ({benchmark.FrameCount} frames)");
            Console.WriteLine($" Load Time              : {benchmark.LoadTimeMs:F1} ms");
            Console.WriteLine($" Alignment Time         : {benchmark.AlignmentTimeMs:F1} ms");
            if (benchmark.MotionTimeMs > 0) Console.WriteLine($" Motion Detection Time  : {benchmark.MotionTimeMs:F1} ms");
            Console.WriteLine($" Focus Measure Time     : {benchmark.FocusMeasureTimeMs:F1} ms");
            Console.WriteLine($" Depth Map Time         : {benchmark.DepthMapTimeMs:F1} ms");
            if (benchmark.QualityAnalysisTimeMs > 0) Console.WriteLine($" Quality Analysis Time  : {benchmark.QualityAnalysisTimeMs:F1} ms");
            Console.WriteLine($" Fusion ({benchmark.FusionMethod,-13}) : {benchmark.FusionTimeMs:F1} ms");
            if (benchmark.ArtifactDetectionTimeMs > 0) Console.WriteLine($" Artifact Detection Time: {benchmark.ArtifactDetectionTimeMs:F1} ms");
            if (benchmark.AutoRepairTimeMs > 0) Console.WriteLine($" Auto-Repair Time       : {benchmark.AutoRepairTimeMs:F1} ms");
            Console.WriteLine($" TOTAL PIPELINE TIME    : {benchmark.TotalTimeMs:F1} ms ({benchmark.TotalTimeMs / 1000.0:F2} s)");
            Console.WriteLine($" Peak Working Set       : {benchmark.PeakWorkingSetMb} MB");
            Console.WriteLine("=================================================");

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
