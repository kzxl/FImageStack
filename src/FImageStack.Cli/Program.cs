using System.Diagnostics;
using FImageStack.Application.Services;
using FImageStack.Core;
using FImageStack.Core.Astro;
using FImageStack.Core.Depth3D;
using FImageStack.Core.Hdr;
using FImageStack.Core.Macro;
using FImageStack.Core.Models;
using FImageStack.Core.Noise;
using FImageStack.Core.Restoration;
using FImageStack.Core.SuperResolution.Drizzle;
using FImageStack.Infrastructure.IO;

namespace FImageStack.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine(" FImageStack — Computational Imaging Engine (CLI)");
        Console.WriteLine("=================================================");

        string mode = "focus"; // focus, macro, hdr, noise, astro, drizzle, restore
        string inputDir = @"data\test_stack_50";
        string outputPath = @"data\output_result.png";
        string export3dPath = string.Empty;
        
        // Macro Stacking parameters
        bool macroAutoCull = true;
        float macroMinSharpness = 0.12f;
        float macroDetailBoost = 0.35f;
        bool macroBreathing = true;
        FusionMethod macroFusionMethod = FusionMethod.RegionAdaptive;
        bool macroDeconvolve = false;
        
        // Focus Stack parameters
        FusionMethod fusionMethod = FusionMethod.MultiScalePyramid;
        FocusMeasureMethod focusMethod = FocusMeasureMethod.ModifiedLaplacian;
        int pyramidLevels = 5;
        bool qualityAnalysis = false;
        bool motionAware = false;
        bool detectArtifacts = false;
        bool autoRepair = false;
        bool tiled = false;
        int tileSize = 512;

        // Noise Stack parameters
        NoiseStackMethod noiseMethod = NoiseStackMethod.KappaSigmaClipping;
        float kappa = 2.5f;

        // HDR parameters
        HdrMergeMethod hdrMethod = HdrMergeMethod.MertensFusion;
        bool deghost = true;

        // Astro parameters
        string darkDir = string.Empty;
        string flatDir = string.Empty;
        string biasDir = string.Empty;

        // Drizzle parameters
        float drizzleScale = 2.0f;
        float drizzlePixFrac = 0.70f;

        // Restoration parameters
        bool dehaze = false;
        bool deconvolve = false;
        float psfRadius = 2.0f;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--mode" && i + 1 < args.Length) mode = args[++i].ToLowerInvariant();
            if (args[i] == "--input" && i + 1 < args.Length) inputDir = args[++i];
            if (args[i] == "--output" && i + 1 < args.Length) outputPath = args[++i];
            if (args[i] == "--export-3d" && i + 1 < args.Length) export3dPath = args[++i];
            if (args[i] == "--method" && i + 1 < args.Length)
            {
                string m = args[++i].ToLowerInvariant();
                fusionMethod = m switch
                {
                    "wta" or "winnertakesall" => FusionMethod.WinnerTakesAll,
                    "weighted" => FusionMethod.FocusWeighted,
                    "hdr" or "exposure" => FusionMethod.HDRFocusExposure,
                    _ => FusionMethod.MultiScalePyramid
                };
            }
            if (args[i] == "--noise-method" && i + 1 < args.Length)
            {
                string nm = args[++i].ToLowerInvariant();
                noiseMethod = nm switch
                {
                    "mean" => NoiseStackMethod.Mean,
                    "median" => NoiseStackMethod.Median,
                    "winsor" or "winsorized" => NoiseStackMethod.WinsorizedMean,
                    _ => NoiseStackMethod.KappaSigmaClipping
                };
            }
            if (args[i] == "--kappa" && i + 1 < args.Length) float.TryParse(args[++i], out kappa);
            if (args[i] == "--hdr-method" && i + 1 < args.Length)
            {
                string hm = args[++i].ToLowerInvariant();
                hdrMethod = hm switch
                {
                    "radiance" or "debevec" => HdrMergeMethod.DebevecRadiance,
                    _ => HdrMergeMethod.MertensFusion
                };
            }
            if (args[i] == "--astro-dark" && i + 1 < args.Length) darkDir = args[++i];
            if (args[i] == "--astro-flat" && i + 1 < args.Length) flatDir = args[++i];
            if (args[i] == "--astro-bias" && i + 1 < args.Length) biasDir = args[++i];
            if (args[i] == "--drizzle-scale" && i + 1 < args.Length) float.TryParse(args[++i], out drizzleScale);
            if (args[i] == "--drizzle-pixfrac" && i + 1 < args.Length) float.TryParse(args[++i], out drizzlePixFrac);
            if (args[i] == "--dehaze") dehaze = true;
            if (args[i] == "--deconvolve") deconvolve = true;
            if (args[i] == "--psf-radius" && i + 1 < args.Length) float.TryParse(args[++i], out psfRadius);
            if (args[i] == "--levels" && i + 1 < args.Length) int.TryParse(args[++i], out pyramidLevels);
            if (args[i] == "--analyze-quality") qualityAnalysis = true;
            if (args[i] == "--motion-aware") motionAware = true;
            if (args[i] == "--detect-artifacts") detectArtifacts = true;
            if (args[i] == "--repair") { detectArtifacts = true; autoRepair = true; }
            if (args[i] == "--macro-cull" && i + 1 < args.Length) bool.TryParse(args[++i], out macroAutoCull);
            if (args[i] == "--macro-min-sharpness" && i + 1 < args.Length) float.TryParse(args[++i], out macroMinSharpness);
            if (args[i] == "--macro-detail" && i + 1 < args.Length) float.TryParse(args[++i], out macroDetailBoost);
            if (args[i] == "--macro-breathing" && i + 1 < args.Length) bool.TryParse(args[++i], out macroBreathing);
            if (args[i] == "--macro-deconv") macroDeconvolve = true;
            if (args[i] == "--tiled") tiled = true;
            if (args[i] == "--tile-size" && i + 1 < args.Length) int.TryParse(args[++i], out tileSize);
        }

        var imageIO = new ImageSharpIO();
        var projectService = new ProjectService();
        var stackService = new StackService(imageIO);
        var macroService = new MacroService(imageIO);

        if (!Directory.Exists(inputDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Input directory not found: {Path.GetFullPath(inputDir)}");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine($"Mode              : {mode.ToUpperInvariant()}");
        Console.WriteLine($"Scanning folder   : {Path.GetFullPath(inputDir)}");
        var imageFiles = projectService.DiscoverImageFiles(inputDir);
        Console.WriteLine($"Found             : {imageFiles.Count} frames");

        if (imageFiles.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No supported image files found in directory.");
            Console.ResetColor();
            return 1;
        }

        var progress = new Progress<StackProgress>(p =>
        {
            Console.Write($"\r[{p.Stage,-25}] {p.Percentage,5:F1}% | {p.Details,-35}");
        });

        try
        {
            var sw = Stopwatch.StartNew();

            if (mode == "macro")
            {
                var macroConfig = new MacroPipelineConfig
                {
                    AutoCullBlurFrames = macroAutoCull,
                    MinSharpnessRatio = macroMinSharpness,
                    MicroDetailStrength = macroDetailBoost,
                    EnableMicroDetailRecovery = macroDetailBoost > 0,
                    EnableFocusBreathingCorrection = macroBreathing,
                    EnableDeconvolution = macroDeconvolve,
                    FusionMethod = macroFusionMethod
                };

                Console.WriteLine("Running Macro Computational Photography Engine...");
                using var macroRes = await macroService.ProcessMacroStackAsync(imageFiles, macroConfig, 0, progress);
                Console.WriteLine();
                Console.WriteLine("-------------------------------------------------");
                Console.WriteLine($"Active Frames     : {macroRes.QualityReport.ActiveFrames}/{macroRes.QualityReport.TotalFrames} (Culled: {macroRes.QualityReport.CulledFrames})");
                Console.WriteLine($"Estimated DOF     : {macroRes.QualityReport.EstimatedDofCoverage * 100:F1}% Depth Covered");
                Console.WriteLine($"Avg Sharpness     : {macroRes.QualityReport.AverageSharpness:F3}");

                Console.WriteLine($"Saving Fused Output to: {Path.GetFullPath(outputPath)}");
                string outDir = Path.GetDirectoryName(outputPath) ?? ".";
                string depthOut = Path.Combine(outDir, "macro_depth_map.png");
                await macroService.SaveResultAsync(macroRes, outputPath, depthOut, bitDepth: 8);
            }
            else if (mode == "noise")
            {
                var settings = new NoiseStackSettings { Method = noiseMethod, Kappa = kappa };
                using var noiseRes = await stackService.ProcessNoiseStackAsync(imageFiles, settings, AlignmentMode.Similarity, progress);
                Console.WriteLine($"\nSaving Noise-Reduced Output to: {Path.GetFullPath(outputPath)}");
                imageIO.SaveImage(noiseRes.DenoisedImage, outputPath, bitDepth: 8);
                Console.WriteLine($"SNR Boost         : +{noiseRes.EstimatedSnrImprovementDb:F1} dB");
            }
            else if (mode == "hdr")
            {
                var settings = new HdrStackSettings { Method = hdrMethod, EnableDeghosting = deghost };
                using var hdrRes = await stackService.ProcessHdrStackAsync(imageFiles, settings, AlignmentMode.Similarity, progress);
                Console.WriteLine($"\nSaving HDR Tone-Mapped Output to: {Path.GetFullPath(outputPath)}");
                imageIO.SaveImage(hdrRes.ToneMappedImage, outputPath, bitDepth: 8);
                Console.WriteLine($"Dynamic Range     : {hdrRes.EstimatedDynamicRangeEv:F1} EV");
            }
            else if (mode == "astro")
            {
                var settings = new AstroStackSettings { Kappa = kappa };
                AstroCalibrationPathSets? calSets = null;
                if (!string.IsNullOrEmpty(darkDir) || !string.IsNullOrEmpty(flatDir) || !string.IsNullOrEmpty(biasDir))
                {
                    calSets = new AstroCalibrationPathSets
                    {
                        DarkPaths = Directory.Exists(darkDir) ? projectService.DiscoverImageFiles(darkDir) : null,
                        FlatPaths = Directory.Exists(flatDir) ? projectService.DiscoverImageFiles(flatDir) : null,
                        BiasPaths = Directory.Exists(biasDir) ? projectService.DiscoverImageFiles(biasDir) : null
                    };
                }

                using var astroRes = await stackService.ProcessAstroStackAsync(imageFiles, settings, calSets, progress);
                Console.WriteLine($"\nSaving Astro Output to: {Path.GetFullPath(outputPath)}");
                imageIO.SaveImage(astroRes.StackedImage, outputPath, bitDepth: 8);
            }
            else if (mode == "drizzle")
            {
                var settings = new DrizzleSettings { ScaleFactor = drizzleScale, PixFrac = drizzlePixFrac };
                using var drizzleRes = await stackService.ProcessDrizzleSuperResAsync(imageFiles, settings, AlignmentMode.Similarity, progress);
                Console.WriteLine($"\nSaving Drizzle Super-Resolution Output to: {Path.GetFullPath(outputPath)}");
                imageIO.SaveImage(drizzleRes.SuperResolvedImage, outputPath, bitDepth: 8);
            }
            else if (mode == "restore")
            {
                using var inputFrame = imageIO.LoadFrame(imageFiles[0], 0);
                var inputImg = inputFrame.ColorBuffer ?? throw new InvalidOperationException("Could not load color buffer.");
                ImageBuffer<float> restoredImg = inputImg;

                if (dehaze)
                {
                    Console.WriteLine("\nApplying Dark Channel Prior Dehazing...");
                    using var dehazeRes = await stackService.DehazeImageAsync(restoredImg, new DehazeOptions());
                    restoredImg = dehazeRes.DehazedImage.Clone();
                }
                if (deconvolve)
                {
                    Console.WriteLine("\nApplying Richardson-Lucy Deconvolution...");
                    var deconvRes = await stackService.DeconvolveImageAsync(restoredImg, new DeconvolutionOptions { PsfRadius = psfRadius });
                    if (restoredImg != inputImg) restoredImg.Dispose();
                    restoredImg = deconvRes;
                }

                Console.WriteLine($"Saving Restored Output to: {Path.GetFullPath(outputPath)}");
                imageIO.SaveImage(restoredImg, outputPath, bitDepth: 8);
                if (restoredImg != inputImg) restoredImg.Dispose();
            }

            else // Default: Focus Stacking
            {
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

                using var result = await stackService.ProcessStackAsync(imageFiles, settings, progress);
                Console.WriteLine();
                Console.WriteLine("-------------------------------------------------");

                var finalImg = result.RepairedImage ?? result.FusedImage;
                Console.WriteLine($"Saving Fused Output to  : {Path.GetFullPath(outputPath)}");
                imageIO.SaveImage(finalImg, outputPath, bitDepth: 8);

                string outDir = Path.GetDirectoryName(outputPath) ?? ".";
                string depthOutPath = Path.Combine(outDir, "output_depth_map.png");
                imageIO.SaveImage(result.DepthResult.DepthMap, depthOutPath, bitDepth: 8);

                // 3D Point Cloud or Mesh Export
                if (!string.IsNullOrEmpty(export3dPath))
                {
                    Console.WriteLine($"Exporting 3D Geometry to: {Path.GetFullPath(export3dPath)}");
                    await stackService.ExportDepthMeshAsync(result.DepthResult.DepthMap, finalImg, export3dPath, new DepthMeshOptions());
                }
            }

            sw.Stop();
            Console.WriteLine($"\nPipeline execution finished in {sw.Elapsed.TotalSeconds:F2} seconds.");
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
