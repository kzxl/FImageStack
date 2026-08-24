namespace FImageStack.Core.FocusVolume;

/// <summary>
/// Represents the continuous Gaussian focus transition parameters fitted for a pixel profile:
/// S(z) = PeakAmplitude * exp( - (z - OptimalMu)^2 / (2 * TransitionSpread^2) ) + BaselineFloor
/// </summary>
public readonly record struct FocusTransitionModel(
    float OptimalMu,
    float PeakAmplitude,
    float TransitionSpread,
    float BaselineFloor,
    float GoodnessOfFit,
    float TransitionSlope)
{
    public bool IsReliable => GoodnessOfFit >= 0.70f && PeakAmplitude > 0.05f;

    public override string ToString() =>
        $"μ:{OptimalMu:F2} | σ:{TransitionSpread:F2} | A:{PeakAmplitude:F2} | R²:{GoodnessOfFit:F2}";
}
