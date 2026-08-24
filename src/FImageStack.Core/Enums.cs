namespace FImageStack.Core;

public enum PixelFormatType
{
    Gray8,
    Rgb24,
    Rgba32,
    Gray16,
    Rgb48,
    Rgba64,
    GrayFloat32,
    RgbFloat32,
    RgbaFloat32
}

public enum FocusMeasureMethod
{
    Laplacian,
    ModifiedLaplacian,
    Tenengrad,
    Variance
}

public enum FusionMethod
{
    WinnerTakesAll,
    FocusWeighted,
    MultiScalePyramid
}

public enum ArtifactType
{
    Halo,
    Ghost,
    Seam,
    Misalignment,
    LowConfidence,
    FocusGap
}
