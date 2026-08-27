using FImageStack.Core.Models;

namespace FImageStack.Core.Raw;

public sealed class RawBayerBuffer : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public BayerPatternType Pattern { get; set; } = BayerPatternType.RGGB;
    
    /// <summary>
    /// Sensor black level per channel [R, Gr, Gb, B] (e.g. 512 in 14-bit RAW).
    /// </summary>
    public float[] BlackLevels { get; set; } = new float[] { 512f, 512f, 512f, 512f };

    /// <summary>
    /// Sensor saturation white level (e.g. 16383 in 14-bit RAW).
    /// </summary>
    public float WhiteLevel { get; set; } = 16383f;

    /// <summary>
    /// Camera White Balance multipliers [R, G, B].
    /// </summary>
    public float[] WhiteBalanceGains { get; set; } = new float[] { 2.1f, 1.0f, 1.6f };

    /// <summary>
    /// Camera to sRGB 3x3 Color Matrix (row-major).
    /// </summary>
    public float[] ColorMatrix { get; set; } = new float[]
    {
        1.65f, -0.55f, -0.10f,
        -0.20f, 1.40f, -0.20f,
        0.00f, -0.40f, 1.40f
    };

    /// <summary>
    /// Single-channel 2D buffer storing raw sensor photosite values.
    /// </summary>
    public ImageBuffer<float> Data { get; }

    public RawBayerBuffer(int width, int height, BayerPatternType pattern = BayerPatternType.RGGB)
    {
        Width = width;
        Height = height;
        Pattern = pattern;
        Data = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
    }

    public void Dispose()
    {
        Data.Dispose();
    }
}

public sealed class RawStackSettings
{
    public NoiseStackMethod MergeMethod { get; set; } = NoiseStackMethod.KappaSigmaClipping;
    public float Kappa { get; set; } = 2.5f;
    public int Iterations { get; set; } = 3;
    
    public bool EnableHighlightRecovery { get; set; } = true;
    public bool ApplyWhiteBalance { get; set; } = true;
    public bool ApplyColorMatrix { get; set; } = true;
    
    public ToneMappingOperator ToneMapping { get; set; } = ToneMappingOperator.ACESFilmic;
    public float ExposureEV { get; set; } = 0.0f;
}

public sealed class RawStackResult : IDisposable
{
    /// <summary>
    /// Fully demosaiced, color-corrected and tone-mapped RGB image.
    /// </summary>
    public ImageBuffer<float> DemosaicedRgb { get; }

    /// <summary>
    /// The merged 2D RAW Bayer CFA sensor grid before demosaicing.
    /// </summary>
    public RawBayerBuffer MergedBayer { get; }

    public float EstimatedDynamicRangeEv { get; set; }
    public int TotalRawFramesMerged { get; set; }

    public RawStackResult(ImageBuffer<float> demosaicedRgb, RawBayerBuffer mergedBayer, int totalFrames)
    {
        DemosaicedRgb = demosaicedRgb ?? throw new ArgumentNullException(nameof(demosaicedRgb));
        MergedBayer = mergedBayer ?? throw new ArgumentNullException(nameof(mergedBayer));
        TotalRawFramesMerged = totalFrames;
    }

    public void Dispose()
    {
        DemosaicedRgb.Dispose();
        MergedBayer.Dispose();
    }
}
