# FImageStack Developer Recipes & Cookbook

A collection of ready-to-use C# code recipes for integrating and automating computational imaging workflows using **FImageStack** and **ZeroGraphics**.

---

## 🍳 Recipe 1: Basic Headless Focus Stacking

```csharp
using FImageStack.Application.Services;
using FImageStack.Core.Models;
using FImageStack.Infrastructure.IO;

var imageIO = new ImageSharpIO();
var stackService = new StackService(imageIO);

var inputFiles = Directory.GetFiles(@"C:\MacroStack\PCB", "*.tif");
var settings = new FusionSettings
{
    Method = FusionMethod.MultiScalePyramid,
    FocusMethod = FocusMeasureMethod.ModifiedLaplacian,
    PyramidLevels = 5,
    EnableAlignment = true,
    EnableDepthSmoothing = true
};

var progress = new Progress<StackProgress>(p =>
{
    Console.WriteLine($"[{p.ProgressPercentage:F0}%] {p.Stage}: {p.Message}");
});

using var result = await stackService.ProcessStackAsync(inputFiles, settings, progress);

// Save master fused image
imageIO.SaveImage(result.FusedImage!, @"C:\MacroStack\PCB\Master_Fused.tif");
Console.WriteLine($"Fused in {result.Benchmark.TotalExecutionTimeMs:F1} ms");
```

---

## 🍳 Recipe 2: Statistical Noise Stacking (Kappa-Sigma Burst)

```csharp
using FImageStack.Application.Services;
using FImageStack.Core.Noise;
using FImageStack.Infrastructure.IO;

var stackService = new StackService(new ImageSharpIO());
var burstFiles = Directory.GetFiles(@"C:\NightBurst", "*.jpg");

var noiseSettings = new NoiseStackSettings
{
    Method = NoiseMethod.KappaSigmaClip,
    Kappa = 2.5f,
    MaxIterations = 3
};

using var noiseResult = await stackService.ProcessNoiseStackAsync(burstFiles, noiseSettings);
new ImageSharpIO().SaveImage(noiseResult.CleanedImage, @"C:\NightBurst\Denoised.png");
```

---

## 🍳 Recipe 3: Pure HDR Exposure Fusion with ACES Filmic Curve

```csharp
using FImageStack.Application.Services;
using FImageStack.Core.Hdr;
using FImageStack.Infrastructure.IO;

var stackService = new StackService(new ImageSharpIO());
var bracketFiles = new[] { "exp_minus2.tif", "exp_zero.tif", "exp_plus2.tif" };

var hdrSettings = new HdrStackSettings
{
    Method = HdrMethod.MertensFusion,
    ContrastWeight = 1.0f,
    SaturationWeight = 1.0f,
    WellExposednessWeight = 1.0f,
    ToneMapping = ToneMappingOperator.ACESFilmic
};

using var hdrResult = await stackService.ProcessHdrStackAsync(bracketFiles, hdrSettings);
new ImageSharpIO().SaveImage(hdrResult.HdrImage, @"C:\HDR\ToneMapped_ACES.tif");
```

---

## 🍳 Recipe 4: Astrophotography Deep-Sky Alignment & Calibration

```csharp
using FImageStack.Application.Services;
using FImageStack.Core.Astro;
using FImageStack.Infrastructure.IO;

var stackService = new StackService(new ImageSharpIO());
var lightFiles = Directory.GetFiles(@"C:\Astro\Lights", "*.fits");

var calibrationPaths = new AstroCalibrationPathSets
{
    DarkPaths = Directory.GetFiles(@"C:\Astro\Darks", "*.fits"),
    FlatPaths = Directory.GetFiles(@"C:\Astro\Flats", "*.fits"),
    BiasPaths = Directory.GetFiles(@"C:\Astro\Biases", "*.fits")
};

var astroSettings = new AstroStackSettings
{
    MinStarCount = 20,
    MinRoundness = 0.65f,
    EnableAutoStretch = true,
    RejectionMethod = AstroRejectionMethod.WinsorizedSigmaClip
};

using var deepSky = await stackService.ProcessAstroStackAsync(lightFiles, astroSettings, calibrationPaths);
new ImageSharpIO().SaveImage(deepSky.MasterLight, @"C:\Astro\Master_DeepSky.tif");
```

---

## 🍳 Recipe 5: HST Subpixel Drizzle Super-Resolution (2x)

```csharp
using FImageStack.Application.Services;
using FImageStack.Core.SuperResolution.Drizzle;
using FImageStack.Infrastructure.IO;

var stackService = new StackService(new ImageSharpIO());
var ditheredFrames = Directory.GetFiles(@"C:\DitheredBurst", "*.png");

var drizzleSettings = new DrizzleSettings
{
    ScaleFactor = 2.0f,
    PixelFraction = 0.75f,
    Kernel = DrizzleKernel.Square
};

using var superRes = await stackService.ProcessDrizzleSuperResAsync(ditheredFrames, drizzleSettings);
new ImageSharpIO().SaveImage(superRes.HighResImage, @"C:\DitheredBurst\SuperRes_2x.png");
```

---

## 🍳 Recipe 6: Exporting 3D Mesh (.obj) from Stacking Depth Maps

```csharp
using FImageStack.Core.Depth3D;

// Given a completed ProcessedStackResult from Recipe 1:
var exporter = new DepthMeshExporter();

var options = new DepthMeshExportOptions
{
    ZScale = 1.5f,
    DecimationStep = 2, // 1 = full resolution, 2 = 1/4 polygons
    ExportNormals = true,
    ExportTextureCoordinates = true
};

exporter.ExportToObj(result.FusedImage!, result.DepthResult.DepthMap, @"C:\Macro\Specimen_3D.obj", options);
exporter.ExportToPly(result.FusedImage!, result.DepthResult.DepthMap, @"C:\Macro\Specimen_PointCloud.ply");
```

---

## 🍳 Recipe 7: Direct DMA Texture Upload to ZeroGraphics Direct3D 11

```csharp
using FImageStack.Core.Models;
using ZeroGraphics.DirectX.Core;
using ZeroGraphics.Imaging.Gpu;

// Upload an ImageBuffer<float> directly to GPU VRAM without GC heap allocations:
public unsafe void UploadBufferToGpu(ImageBuffer<float> buffer, GpuImageContext context, PooledGpuTexture targetTexture)
{
    float* pScan0 = buffer.DataPointer;
    int width = buffer.Width;
    int height = buffer.Height;
    int strideBytes = buffer.Stride * sizeof(float);

    context.Transfer.UploadRaw((IntPtr)pScan0, width, height, strideBytes, targetTexture.Texture);
}
```

---

## 🍳 Recipe 8: Hosting ZeroCameraCanvas in WPF Studio

```xaml
<!-- In MainWindow.xaml -->
<WindowsFormsHost Grid.Row="1" x:Name="CameraHost" HorizontalAlignment="Stretch" VerticalAlignment="Stretch">
    <!-- ZeroCameraCanvas will be assigned in code-behind -->
</WindowsFormsHost>
```

```csharp
// In MainWindow.xaml.cs
public partial class MainWindow : Window
{
    private readonly ZeroCameraCanvas _canvas;

    public MainWindow()
    {
        InitializeComponent();
        _canvas = new ZeroCameraCanvas
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            ZoomMode = CameraZoomMode.Fit
        };
        CameraHost.Child = _canvas;
    }

    public void DisplayBuffer(ImageBuffer<float> buffer)
    {
        // Zero-copy direct presentation to DirectX 11 SwapChain
        _canvas.SetFrame((IntPtr)buffer.DataPointer, buffer.Width, buffer.Height, ImageFormatMode.RgbFloat32);
    }
}
```

---

## 🍳 Recipe 9: Optical Deconvolution & Dehazing

```csharp
using FImageStack.Application.Services;
using FImageStack.Core.Restoration;
using FImageStack.Infrastructure.IO;

var io = new ImageSharpIO();
var stackService = new StackService(io);
using var rawInput = io.LoadImage(@"C:\HazyMacro\Specimen.tif");

// Step 1: Optical Dehazing
using var dehazed = await stackService.DehazeImageAsync(rawInput, new DehazeOptions
{
    Omega = 0.95f,
    GuidedFilterRadius = 15,
    GuidedFilterEpsilon = 0.001f
});

// Step 2: Richardson-Lucy Deconvolution
using var deblurred = await stackService.DeconvolveImageAsync(dehazed.DehazedImage, new DeconvolutionOptions
{
    PsfType = PsfType.Gaussian,
    PsfRadius = 2.5f,
    Iterations = 15,
    TvDamping = 0.002f
});

io.SaveImage(deblurred, @"C:\HazyMacro\Restored_Specimen.tif");
```

---

## 🍳 Recipe 10: Headless CLI Batch Automation

```bash
# 1. Standard Focus Stacking with Laplacian Pyramid
FImageStack.Cli.exe --mode focus --input "C:\Data\PCB" --output "C:\Out\fused.tif" --method pyramid

# 2. Focus Stacking with 3D Mesh Output
FImageStack.Cli.exe --mode focus --input "C:\Data\Insect" --output "C:\Out\insect.tif" --export-3d "C:\Out\insect.obj"

# 3. Burst Noise Stacking with Kappa-Sigma
FImageStack.Cli.exe --mode noise --input "C:\Data\NightBurst" --output "C:\Out\clean.png" --kappa 2.5

# 4. Pure HDR Exposure Fusion
FImageStack.Cli.exe --mode hdr --input "C:\Data\Bracket" --output "C:\Out\hdr_aces.tif" --hdr-method mertens
```
