using FImageStack.Core.Models;

namespace FImageStack.Core.Fusion;

public sealed class RegionClassificationMap : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public ImageBuffer<float> BackgroundWeight { get; }
    public ImageBuffer<float> SubjectWeight { get; }
    public ImageBuffer<float> EdgeWeight { get; }
    public ImageBuffer<byte> PrimaryRegionMap { get; }
    public float BackgroundRatio { get; set; }
    public float SubjectRatio { get; set; }
    public float EdgeRatio { get; set; }

    public RegionClassificationMap(int width, int height)
    {
        Width = width;
        Height = height;
        BackgroundWeight = new ImageBuffer<float>(width, height, 1);
        SubjectWeight = new ImageBuffer<float>(width, height, 1);
        EdgeWeight = new ImageBuffer<float>(width, height, 1);
        PrimaryRegionMap = new ImageBuffer<byte>(width, height, 1);
    }

    public void Dispose()
    {
        BackgroundWeight?.Dispose();
        SubjectWeight?.Dispose();
        EdgeWeight?.Dispose();
        PrimaryRegionMap?.Dispose();
    }
}
