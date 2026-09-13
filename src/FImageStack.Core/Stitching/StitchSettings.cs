namespace FImageStack.Core.Stitching;

public sealed class StitchSettings
{
    public StitchProjectionMode ProjectionMode { get; set; } = StitchProjectionMode.Planar;
    public SeamBlendingMode BlendingMode { get; set; } = SeamBlendingMode.LinearFeathering;
    public float SearchOverlapRatio { get; set; } = 0.30f;
    public bool EnableGainCompensation { get; set; } = true;
    public float FeatherRadiusPercentage { get; set; } = 0.10f;
    public int MaxOutputDimension { get; set; } = 16384;
    public int ReferenceFrameIndex { get; set; } = -1; // -1 = Auto middle frame
}
