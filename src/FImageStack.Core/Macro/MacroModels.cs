using FImageStack.Core.Models;

namespace FImageStack.Core.Macro;

/// <summary>
/// Represents a single frame captured during a macro focus bracketing burst.
/// Holds image buffers and physical capture telemetry (lens distance, gyro motion).
/// </summary>
public sealed class MacroFrame : IDisposable
{
    public int Index { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>
    /// Lens position / focus distance (e.g. normalized [0.0 - 1.0] from camera hardware).
    /// </summary>
    public float LensFocusDistance { get; set; }

    /// <summary>
    /// Gyroscopic / sensor motion magnitude (hand-shake telemetry) during capture.
    /// </summary>
    public float GyroShakeMagnitude { get; set; }

    /// <summary>
    /// Global contrast / sharpness metric across the frame.
    /// </summary>
    public double SharpnessScore { get; set; }

    /// <summary>
    /// Indicates whether the frame is culled (rejected) due to blur, shake, or redundancy.
    /// </summary>
    public bool IsCulled { get; set; }

    /// <summary>
    /// Reason explaining why the frame was culled from the fusion stack.
    /// </summary>
    public string CullReason { get; set; } = string.Empty;

    /// <summary>
    /// Focus breathing magnification compensation factor.
    /// </summary>
    public float FocusBreathingScale { get; set; } = 1.0f;

    /// <summary>
    /// Estimated alignment homography matrix (3x3 row-major) relative to anchor frame.
    /// </summary>
    public float[]? AlignmentHomography { get; set; }

    /// <summary>
    /// Normalized linear RGB float buffer [0.0 - 1.0], 3 channels.
    /// </summary>
    public ImageBuffer<float>? ColorBuffer { get; set; }

    /// <summary>
    /// Normalized grayscale float buffer [0.0 - 1.0], 1 channel.
    /// </summary>
    public ImageBuffer<float>? GrayBuffer { get; set; }

    /// <summary>
    /// High-frequency sharpness response map for this frame.
    /// </summary>
    public ImageBuffer<float>? FocusMap { get; set; }

    /// <summary>
    /// Converts this MacroFrame to an internal StackFrame representation.
    /// </summary>
    public StackFrame ToStackFrame()
    {
        return new StackFrame
        {
            Index = Index,
            FilePath = Label,
            Width = Width,
            Height = Height,
            ColorBuffer = ColorBuffer,
            GrayBuffer = GrayBuffer,
            FocusMap = FocusMap,
            SharpnessScore = SharpnessScore,
            FocusBreathingScale = FocusBreathingScale,
            AlignmentHomography = AlignmentHomography
        };
    }

    public void Dispose()
    {
        ColorBuffer?.Dispose();
        ColorBuffer = null;
        GrayBuffer?.Dispose();
        GrayBuffer = null;
        FocusMap?.Dispose();
        FocusMap = null;
    }
}

/// <summary>
/// Encapsulates a complete burst sequence of macro frames with lifecycle management.
/// </summary>
public sealed class MacroFrameSet : IDisposable
{
    private readonly List<MacroFrame> _frames = new();

    public IReadOnlyList<MacroFrame> Frames => _frames;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int TotalFrames => _frames.Count;
    public int ActiveFramesCount => _frames.Count(f => !f.IsCulled);

    public IReadOnlyList<MacroFrame> ActiveFrames => _frames.Where(f => !f.IsCulled).ToList();

    public void AddFrame(MacroFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (_frames.Count == 0)
        {
            Width = frame.Width;
            Height = frame.Height;
        }
        else if (frame.Width != Width || frame.Height != Height)
        {
            throw new ArgumentException($"Frame dimension {frame.Width}x{frame.Height} does not match frame set {Width}x{Height}.");
        }

        frame.Index = _frames.Count;
        _frames.Add(frame);
    }

    public void Dispose()
    {
        foreach (var frame in _frames)
        {
            frame.Dispose();
        }
        _frames.Clear();
    }
}

/// <summary>
/// Computational photography configuration optimized for macro imaging.
/// </summary>
public sealed class MacroPipelineConfig
{
    /// <summary>
    /// Automatically detects and rejects blurry / hand-shaken frames before stacking.
    /// </summary>
    public bool AutoCullBlurFrames { get; set; } = true;

    /// <summary>
    /// Minimum relative sharpness threshold compared to the peak frame (0.0 to 1.0).
    /// </summary>
    public float MinSharpnessRatio { get; set; } = 0.12f;

    /// <summary>
    /// Automatically compensates for focal breathing magnification differences.
    /// </summary>
    public bool EnableFocusBreathingCorrection { get; set; } = true;

    /// <summary>
    /// Alignment transformation mode (Rigid, Similarity, Affine, Homography).
    /// </summary>
    public AlignmentMode AlignmentMode { get; set; } = AlignmentMode.Similarity;

    /// <summary>
    /// Advanced focus fusion algorithm. Recommended: MultiScalePyramid for seamless macro DOF.
    /// </summary>
    public FusionMethod FusionMethod { get; set; } = FusionMethod.MultiScalePyramid;

    /// <summary>
    /// Method used to extract high-frequency detail for sharpness maps.
    /// </summary>
    public FocusMeasureMethod FocusMeasureMethod { get; set; } = FocusMeasureMethod.ModifiedLaplacian;

    /// <summary>
    /// Preserves sub-part DOF (e.g. facet detail of insect eyes) while blending seams.
    /// </summary>
    public bool EnableSubPartDofPreservation { get; set; } = true;

    /// <summary>
    /// Restores micro-contrast and edge sharpness using non-blind deconvolution.
    /// </summary>
    public bool EnableMicroDetailRecovery { get; set; } = true;

    /// <summary>
    /// Micro-detail recovery boost strength [0.0 - 1.0].
    /// </summary>
    public float MicroDetailStrength { get; set; } = 0.35f;

    /// <summary>
    /// Deconvolves optical diffraction blur (Richardson-Lucy iterations).
    /// </summary>
    public bool EnableDeconvolution { get; set; } = false;
    public int DeconvolutionIterations { get; set; } = 5;

    /// <summary>
    /// Applies temporal denoise on static aligned regions.
    /// </summary>
    public bool EnableTemporalDenoise { get; set; } = true;
    public float DenoiseStrength { get; set; } = 0.8f;

    /// <summary>
    /// Memory tiling for processing high-resolution images on constrained mobile hardware.
    /// </summary>
    public bool EnableTiling { get; set; } = false;
    public int TileSize { get; set; } = 1024;
}

/// <summary>
/// Telemetry and quality analytics for the executed macro stack.
/// </summary>
public sealed class MacroQualityReport
{
    public int TotalFrames { get; set; }
    public int ActiveFrames { get; set; }
    public int CulledFrames { get; set; }
    public float EstimatedDofCoverage { get; set; }
    public int DetectedFocusGaps { get; set; }
    public double AverageSharpness { get; set; }
    public List<string> DiagnosticNotes { get; } = new();
}

/// <summary>
/// Final result of the Macro Computational Photography pipeline.
/// </summary>
public sealed class MacroStackResult : IDisposable
{
    public ImageBuffer<float> FusedImage { get; set; } = null!;
    public DepthMapResult? DepthMap { get; set; }
    public MacroQualityReport QualityReport { get; set; } = new();
    public BenchmarkReport Benchmark { get; set; } = new();

    public void Dispose()
    {
        FusedImage?.Dispose();
        DepthMap?.Dispose();
    }
}
