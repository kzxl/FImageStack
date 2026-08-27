using FImageStack.Core.Models;

namespace FImageStack.Core.Hdr;

public sealed class HdrStackSettings
{
    public HdrMergeMethod Method { get; set; } = HdrMergeMethod.MertensFusion;
    
    public float ContrastWeight { get; set; } = 1.0f;
    public float SaturationWeight { get; set; } = 1.0f;
    public float WellExposednessWeight { get; set; } = 1.0f;
    public float WellExposednessSigma { get; set; } = 0.20f;
    
    public int PyramidLevels { get; set; } = 5;
    
    public bool EnableDeghosting { get; set; } = true;
    public float DeghostingThreshold { get; set; } = 0.18f;
    
    public ToneMappingOperator ToneMapping { get; set; } = ToneMappingOperator.ACESFilmic;
    public float ExposureCompensation { get; set; } = 0.0f;
    public float Gamma { get; set; } = 2.2f;
}

public sealed class HdrStackResult : IDisposable
{
    /// <summary>
    /// Linear HDR radiance map (32-bit floating point, unbounded > 1.0).
    /// </summary>
    public ImageBuffer<float> RadianceMap { get; }

    /// <summary>
    /// Tone-mapped 8/16-bit display ready buffer [0.0 - 1.0].
    /// </summary>
    public ImageBuffer<float> ToneMappedImage { get; }

    /// <summary>
    /// Motion de-ghosting mask (1.0 = motion ghost suppressed).
    /// </summary>
    public ImageBuffer<float>? DeghostMask { get; set; }

    public float EstimatedDynamicRangeEv { get; set; }
    public HdrMergeMethod MethodUsed { get; set; }
    public ToneMappingOperator ToneMapperUsed { get; set; }

    public HdrStackResult(ImageBuffer<float> radianceMap, ImageBuffer<float> toneMappedImage, HdrMergeMethod method)
    {
        RadianceMap = radianceMap ?? throw new ArgumentNullException(nameof(radianceMap));
        ToneMappedImage = toneMappedImage ?? throw new ArgumentNullException(nameof(toneMappedImage));
        MethodUsed = method;
    }

    public void Dispose()
    {
        RadianceMap.Dispose();
        ToneMappedImage.Dispose();
        DeghostMask?.Dispose();
        DeghostMask = null;
    }
}
