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

public enum AlignmentMode
{
    None,
    TranslationOnly,
    Rigid,         // Translation + Rotation
    Similarity,    // Translation + Rotation + Scale (Focus Breathing)
    Affine,        // 6 DOF (Translation + Rotation + Scale + Shear)
    Homography     // 8 DOF (Perspective Transformation)
}

public enum FocusMeasureMethod
{
    Laplacian,
    ModifiedLaplacian,
    Tenengrad,
    LocalVariance,
    Wavelet
}

public enum FusionMethod
{
    WinnerTakesAll,
    FocusWeighted,
    MultiScalePyramid,
    WaveletDWT,
    HDRFocusExposure
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

public enum ToneMappingOperator
{
    ACESFilmic,
    ReinhardExtended,
    LinearPreserve,
    AgX
}

public enum BayerPatternType
{
    RGGB,
    BGGR,
    GRBG,
    GBRG
}

public enum ResolutionMode
{
    FastPreview1280, // Fast interactive proxy stack (1280px / ~0.2s-0.5s)
    FullMaster       // 100% full sensor resolution (24MP - 100MP+)
}
