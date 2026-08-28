namespace FImageStack.Core.FocusPeaking;

public enum PeakingColor
{
    NeonGreen = 0,
    Red = 1,
    Yellow = 2,
    Cyan = 3,
    Magenta = 4,
    White = 5
}

public enum PeakingDisplayMode
{
    MonochromeBackground = 0, // B&W Background + Neon edges (highest visibility)
    ColorOverlay = 1,          // Full color + Neon edges
    MaskOnly = 2,              // Binary White on Black mask
    Heatmap = 3                // Gradient heatmap based on sharpness intensity
}

public sealed class FocusPeakingSettings
{
    public PeakingColor Color { get; set; } = PeakingColor.NeonGreen;
    public PeakingDisplayMode Mode { get; set; } = PeakingDisplayMode.MonochromeBackground;
    public float Threshold { get; set; } = 0.045f;
    public int LineWidth { get; set; } = 1;
    public float OverlayAlpha { get; set; } = 0.95f;
}

public sealed class FocusPeakingResult : IDisposable
{
    public Models.ImageBuffer<float> PeakingImage { get; set; } = null!;
    public int InFocusPixelCount { get; set; }
    public float InFocusPercentage { get; set; }
    public float PeakSharpness { get; set; }

    public void Dispose()
    {
        PeakingImage?.Dispose();
    }
}
