using FImageStack.Core.Models;

namespace FImageStack.Core.Lab;

public sealed class StackLabSlot : IDisposable
{
    public string SlotId { get; set; } = string.Empty;
    public string AlgorithmTitle { get; set; } = string.Empty;
    public FusionMethod FusionMethod { get; set; }
    public ImageBuffer<float>? RenderedImage { get; set; }
    public object? PreviewBitmap { get; set; }
    public float SharpnessScore { get; set; }
    public float SmoothnessSnrScore { get; set; }
    public float ArtifactFreeScore { get; set; }
    public float CompositeScore { get; set; }
    public bool IsWinnerBest { get; set; }
    public TimeSpan RenderDuration { get; set; }
    public string RenderDurationText => $"{RenderDuration.TotalMilliseconds:F0} ms";

    public void Dispose()
    {
        RenderedImage?.Dispose();
        RenderedImage = null;
    }
}

public sealed class SynchronizedCropViewport
{
    public int CenterX { get; set; }
    public int CenterY { get; set; }
    public int CropWidth { get; set; } = 256;
    public int CropHeight { get; set; } = 256;
    public float ZoomFactor { get; set; } = 1.0f;
}

public sealed class StackLabReport : IDisposable
{
    public int TotalSlots { get; set; }
    public string WinnerSlotId { get; set; } = string.Empty;
    public string WinnerAlgorithmTitle { get; set; } = string.Empty;
    public float WinnerScore { get; set; }
    public List<StackLabSlot> Slots { get; } = new();
    public string AsciiComparisonMatrix { get; set; } = string.Empty;

    public void Dispose()
    {
        foreach (var slot in Slots)
        {
            slot.Dispose();
        }
        Slots.Clear();
    }
}
