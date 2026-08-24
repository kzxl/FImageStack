using FImageStack.Core.Models;

namespace FImageStack.Core.Recovery;

public sealed class MicroDetailRecoveryConfig
{
    public float BoostStrength { get; set; } = 1.0f;
    public int NeighborRadius { get; set; } = 2;
    public float SharpnessFloorThreshold { get; set; } = 0.20f;
    public float SharpnessCeilingThreshold { get; set; } = 0.85f;
    public float MinRecoverableAreaPercent { get; set; } = 3.0f;
}

public sealed class RecoveryRecommendation
{
    public bool IsRecommended { get; set; }
    public float RecoverableAreaPercentage { get; set; }
    public float EstimatedDetailGainPercentage { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class MicroDetailRecoveryResult : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public ImageBuffer<float> EnhancedImage { get; }
    public ImageBuffer<float> RecoveredDetailMap { get; }
    public float MeanSharpnessGainPercentage { get; set; }

    public MicroDetailRecoveryResult(int width, int height, int channels)
    {
        Width = width;
        Height = height;
        EnhancedImage = new ImageBuffer<float>(width, height, channels);
        RecoveredDetailMap = new ImageBuffer<float>(width, height, 1);
    }

    public void Dispose()
    {
        EnhancedImage?.Dispose();
        RecoveredDetailMap?.Dispose();
    }
}
