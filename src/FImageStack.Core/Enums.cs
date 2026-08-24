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
    Homography,    // 8 DOF (Perspective Transformation)
    OpticalFlow    // Dense Pyramidal Motion Vector Field (Per-Pixel Vector Alignment)
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
    ConfidenceWeighted,
    OcclusionAware,
    MultiScalePyramid,
    WaveletDWT,
    HDRFocusExposure,
    RegionAdaptive
}

public enum SemanticRegionType : byte
{
    SmoothBackground = 0,
    SolidSubject = 1,
    FineEdgeHair = 2
}

public enum OcclusionState : byte
{
    Visible = 0,    // Unoccluded, in-focus sharp
    Occluded = 1,   // Obscured by foreground defocus blur / foreground object
    Revealed = 2    // Background revealed / newly disoccluded
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

public enum GpuBackendType
{
    Auto,           // Optimal hardware selection (DirectCompute / DirectML / CPU)
    DirectCompute,  // DirectX 11/12 HLSL Compute Shader (NVIDIA, AMD, Intel)
    DirectML,       // DirectX Machine Learning Accelerator
    Cuda,           // NVIDIA CUDA Engine
    CpuSimd         // CPU Multi-threaded AVX2/AVX-512 SIMD Fallback
}
