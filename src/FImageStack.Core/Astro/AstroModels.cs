using System.Numerics;
using FImageStack.Core.Models;

namespace FImageStack.Core.Astro;

public sealed class StarCandidate
{
    public float X { get; set; }
    public float Y { get; set; }
    public float PeakIntensity { get; set; }
    public float TotalFlux { get; set; }
    public float Fwhm { get; set; }
    public float Roundness { get; set; }
    public float Snr { get; set; }
}

public sealed class StarTriangle
{
    public int StarIdx1 { get; set; }
    public int StarIdx2 { get; set; }
    public int StarIdx3 { get; set; }
    
    // Invariant geometric ratios: ratio of sides sorted by length: L1 <= L2 <= L3
    public float Ratio1 { get; set; } // L1 / L3
    public float Ratio2 { get; set; } // L2 / L3
}

public sealed class AstroCalibrationFrames : IDisposable
{
    public List<StackFrame> DarkFrames { get; } = new();
    public List<StackFrame> FlatFrames { get; } = new();
    public List<StackFrame> BiasFrames { get; } = new();

    public void Dispose()
    {
        foreach (var f in DarkFrames) f.Dispose();
        foreach (var f in FlatFrames) f.Dispose();
        foreach (var f in BiasFrames) f.Dispose();
        DarkFrames.Clear();
        FlatFrames.Clear();
        BiasFrames.Clear();
    }
}

public sealed class AstroStackSettings
{
    public float StarDetectionSigma { get; set; } = 3.5f;
    public int MaxStarsPerFrame { get; set; } = 100;
    public float MinStarRoundness { get; set; } = 0.60f;
    
    public bool EnableCalibration { get; set; } = true;
    public bool EnableStarAlignment { get; set; } = true;
    public bool EnableKappaSigmaClipping { get; set; } = true;
    public float Kappa { get; set; } = 2.5f;
    public int StackingIterations { get; set; } = 3;
    
    public bool EnableBackgroundNeutralization { get; set; } = true;
    public bool EnableAutoStretch { get; set; } = true;
    public float TargetBackgroundLevel { get; set; } = 0.12f;
}

public sealed class AstroStackResult : IDisposable
{
    public ImageBuffer<float> StackedImage { get; }
    public ImageBuffer<float>? CalibratedMasterDark { get; set; }
    public ImageBuffer<float>? CalibratedMasterFlat { get; set; }
    public int TotalLightsMerged { get; set; }
    public float SkyBackgroundLuminance { get; set; }
    public List<int> DetectedStarCounts { get; } = new();

    public AstroStackResult(ImageBuffer<float> stackedImage, int totalLightsMerged)
    {
        StackedImage = stackedImage ?? throw new ArgumentNullException(nameof(stackedImage));
        TotalLightsMerged = totalLightsMerged;
    }

    public void Dispose()
    {
        StackedImage.Dispose();
        CalibratedMasterDark?.Dispose();
        CalibratedMasterFlat?.Dispose();
    }
}
