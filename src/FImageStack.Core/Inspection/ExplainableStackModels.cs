namespace FImageStack.Core.Inspection;

public sealed class FrameWeightContribution
{
    public int FrameIndex { get; set; }
    public int FrameNumber => FrameIndex + 1;
    public float WeightPercentage { get; set; }
    public float RawConfidence { get; set; }
    public bool IsPrimaryWinner { get; set; }
}

public sealed class PixelFactorBreakdown
{
    public float Sharpness { get; set; }
    public float AlignmentConfidence { get; set; }
    public float MotionPenalty { get; set; }
    public float EdgeConfidence { get; set; }
    public float ExposureConsistency { get; set; }
    public float CompositeConfidence { get; set; }
}

public sealed class PixelInspectionReport
{
    public int X { get; set; }
    public int Y { get; set; }
    public int PrimaryFrameIndex { get; set; }
    public int PrimaryFrameNumber => PrimaryFrameIndex + 1;
    public float EstimatedDepth { get; set; }
    public PixelFactorBreakdown PrimaryFactors { get; set; } = new();
    public List<FrameWeightContribution> WeightDistribution { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
}
