using FImageStack.Core.Models;

namespace FImageStack.Core.Noise;

public sealed class NoiseStackSettings
{
    public NoiseStackMethod Method { get; set; } = NoiseStackMethod.KappaSigmaClipping;
    
    /// <summary>
    /// Multiplier for standard deviation in Kappa-Sigma clipping (typical 2.0 - 3.0).
    /// </summary>
    public float Kappa { get; set; } = 2.5f;

    /// <summary>
    /// Number of iterations for Kappa-Sigma clipping refinement.
    /// </summary>
    public int Iterations { get; set; } = 3;

    /// <summary>
    /// Number of highest and lowest values to reject per pixel in MinMaxRejection mode.
    /// </summary>
    public int MinMaxTrimCount { get; set; } = 1;

    /// <summary>
    /// Lower quantile bound for Winsorized mean [0.0 - 0.5].
    /// </summary>
    public float WinsorLowerQuantile { get; set; } = 0.10f;

    /// <summary>
    /// Upper quantile bound for Winsorized mean [0.5 - 1.0].
    /// </summary>
    public float WinsorUpperQuantile { get; set; } = 0.90f;

    /// <summary>
    /// Whether to generate an outlier rejection heatmap map.
    /// </summary>
    public bool GenerateRejectionMap { get; set; } = true;
}

public sealed class NoiseStackResult : IDisposable
{
    public ImageBuffer<float> DenoisedImage { get; }
    public ImageBuffer<float>? RejectionMap { get; set; }
    public float EstimatedSnrImprovementDb { get; set; }
    public int TotalFramesMerged { get; set; }
    public NoiseStackMethod MethodUsed { get; set; }

    public NoiseStackResult(ImageBuffer<float> denoisedImage, int totalFramesMerged, NoiseStackMethod method)
    {
        DenoisedImage = denoisedImage ?? throw new ArgumentNullException(nameof(denoisedImage));
        TotalFramesMerged = totalFramesMerged;
        MethodUsed = method;
    }

    public void Dispose()
    {
        DenoisedImage.Dispose();
        RejectionMap?.Dispose();
        RejectionMap = null;
    }
}
