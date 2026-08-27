using FImageStack.Core.Models;

namespace FImageStack.Core.Restoration;

public enum PsfKernelType
{
    Gaussian,
    DefocusDisc,
    AiryDisk,
    MotionBlur
}

public sealed class DeconvolutionOptions
{
    public PsfKernelType PsfType { get; set; } = PsfKernelType.Gaussian;
    public float PsfRadius { get; set; } = 2.5f;
    public float MotionAngleDegrees { get; set; } = 0.0f;
    
    public int Iterations { get; set; } = 20;
    
    /// <summary>
    /// Total Variation (TV) gradient damping weight to suppress ringing artifacts.
    /// </summary>
    public float TvDampingWeight { get; set; } = 0.002f;
}

public sealed class DehazeOptions
{
    public int PatchRadius { get; set; } = 7;
    
    /// <summary>
    /// Atmospheric haze retention factor (0.95 keeps small natural atmospheric perspective).
    /// </summary>
    public float Omega { get; set; } = 0.95f;

    /// <summary>
    /// Minimum transmission floor to avoid noise division in deep haze.
    /// </summary>
    public float MinTransmission { get; set; } = 0.10f;

    /// <summary>
    /// Guided filter refinement radius for smooth edge-preserving transmission map.
    /// </summary>
    public int GuidedFilterRadius { get; set; } = 15;
}

public sealed class DehazeResult : IDisposable
{
    public ImageBuffer<float> DehazedImage { get; }
    public ImageBuffer<float> TransmissionMap { get; }
    public float[] AtmosphericLight { get; }

    public DehazeResult(ImageBuffer<float> dehazedImage, ImageBuffer<float> transmissionMap, float[] atmosphericLight)
    {
        DehazedImage = dehazedImage ?? throw new ArgumentNullException(nameof(dehazedImage));
        TransmissionMap = transmissionMap ?? throw new ArgumentNullException(nameof(transmissionMap));
        AtmosphericLight = atmosphericLight ?? throw new ArgumentNullException(nameof(atmosphericLight));
    }

    public void Dispose()
    {
        DehazedImage.Dispose();
        TransmissionMap.Dispose();
    }
}
