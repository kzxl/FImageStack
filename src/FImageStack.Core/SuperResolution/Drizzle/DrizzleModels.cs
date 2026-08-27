using FImageStack.Core.Models;

namespace FImageStack.Core.SuperResolution.Drizzle;

public enum DrizzleKernelType
{
    Square,
    Gaussian,
    TopHat
}

public sealed class DrizzleSettings
{
    /// <summary>
    /// Super-resolution upsampling scale factor (e.g. 2.0x, 3.0x).
    /// </summary>
    public float ScaleFactor { get; set; } = 2.0f;

    /// <summary>
    /// Linear footprint shrinkage factor of dropped pixel (pixfrac in (0.0, 1.0]).
    /// Typical optimal value is 0.6 - 0.8.
    /// </summary>
    public float PixFrac { get; set; } = 0.70f;

    public DrizzleKernelType Kernel { get; set; } = DrizzleKernelType.Square;

    public float WeightFloorThreshold { get; set; } = 1e-4f;
}

public sealed class DrizzleResult : IDisposable
{
    public ImageBuffer<float> SuperResolvedImage { get; }
    public ImageBuffer<float> WeightMap { get; }
    public float EffectiveScale { get; set; }
    public int TotalFramesDrizzled { get; set; }

    public DrizzleResult(ImageBuffer<float> superResolvedImage, ImageBuffer<float> weightMap, float scale, int frameCount)
    {
        SuperResolvedImage = superResolvedImage ?? throw new ArgumentNullException(nameof(superResolvedImage));
        WeightMap = weightMap ?? throw new ArgumentNullException(nameof(weightMap));
        EffectiveScale = scale;
        TotalFramesDrizzled = frameCount;
    }

    public void Dispose()
    {
        SuperResolvedImage.Dispose();
        WeightMap.Dispose();
    }
}
