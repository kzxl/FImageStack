namespace FImageStack.Core.SuperResolution;

public sealed class SuperResolutionParams
{
    public int ScaleFactor { get; set; } = 2;
    public float KernelSigma { get; set; } = 0.65f;
    public int IbpIterations { get; set; } = 2;
    public float SharpnessBoost { get; set; } = 1.25f;

    public SuperResolutionParams(int scaleFactor = 2, float kernelSigma = 0.65f, int ibpIterations = 2, float sharpnessBoost = 1.25f)
    {
        ScaleFactor = Math.Clamp(scaleFactor, 2, 4);
        KernelSigma = Math.Clamp(kernelSigma, 0.3f, 1.5f);
        IbpIterations = Math.Clamp(ibpIterations, 0, 5);
        SharpnessBoost = Math.Clamp(sharpnessBoost, 1.0f, 2.0f);
    }
}
