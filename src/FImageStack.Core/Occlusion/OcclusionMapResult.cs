using FImageStack.Core.Models;

namespace FImageStack.Core.Occlusion;

public sealed class OcclusionMapResult : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public int FrameCount { get; }

    /// <summary>
    /// Per-frame visibility classification: Visible (0), Occluded (1), Revealed (2).
    /// </summary>
    public ImageBuffer<byte>[] StateMaps { get; }

    /// <summary>
    /// Soft alpha matte of foreground boundaries for each frame [0.0 - 1.0].
    /// </summary>
    public ImageBuffer<float>[] ForegroundAlphaMaps { get; }

    /// <summary>
    /// Aggregate spatial risk map indicating boundary areas susceptible to defocus halo bleeding.
    /// </summary>
    public ImageBuffer<float> OcclusionRiskMap { get; }

    public OcclusionMapResult(int width, int height, int frameCount)
    {
        Width = width;
        Height = height;
        FrameCount = frameCount;

        StateMaps = new ImageBuffer<byte>[frameCount];
        ForegroundAlphaMaps = new ImageBuffer<float>[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            StateMaps[i] = new ImageBuffer<byte>(width, height, 1, PixelFormatType.Gray8);
            ForegroundAlphaMaps[i] = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
        }

        OcclusionRiskMap = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
    }

    public void Dispose()
    {
        for (int i = 0; i < FrameCount; i++)
        {
            StateMaps[i].Dispose();
            ForegroundAlphaMaps[i].Dispose();
        }
        OcclusionRiskMap.Dispose();
    }
}
