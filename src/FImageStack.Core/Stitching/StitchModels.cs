using FImageStack.Core.Models;

namespace FImageStack.Core.Stitching;

public sealed class StitchTileInfo
{
    public int FrameIndex { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public float MinX { get; set; }
    public float MinY { get; set; }
    public float MaxX { get; set; }
    public float MaxY { get; set; }
    public float[] HomographyToCanvas { get; set; } = new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
    public float[] InvHomographyFromCanvas { get; set; } = new float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
    public float GainFactor { get; set; } = 1.0f;
}

public sealed class StitchResult : IDisposable
{
    public ImageBuffer<float> StitchedImage { get; }
    public int CanvasWidth { get; }
    public int CanvasHeight { get; }
    public float OriginOffsetX { get; }
    public float OriginOffsetY { get; }
    public int TilesMerged { get; }
    public IReadOnlyList<StitchTileInfo> TilesInfo { get; }

    public StitchResult(
        ImageBuffer<float> stitchedImage,
        int canvasWidth,
        int canvasHeight,
        float originOffsetX,
        float originOffsetY,
        int tilesMerged,
        IReadOnlyList<StitchTileInfo> tilesInfo)
    {
        StitchedImage = stitchedImage ?? throw new ArgumentNullException(nameof(stitchedImage));
        CanvasWidth = canvasWidth;
        CanvasHeight = canvasHeight;
        OriginOffsetX = originOffsetX;
        OriginOffsetY = originOffsetY;
        TilesMerged = tilesMerged;
        TilesInfo = tilesInfo ?? Array.Empty<StitchTileInfo>();
    }

    public void Dispose()
    {
        StitchedImage.Dispose();
    }
}
