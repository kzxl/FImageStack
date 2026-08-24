namespace FImageStack.Core.Models;

public sealed class StackFrame : IDisposable
{
    public int Index { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public int Width { get; set; }
    public int Height { get; set; }
    public int BitDepth { get; set; } = 8;
    public PixelFormatType Format { get; set; } = PixelFormatType.RgbFloat32;
    
    /// <summary>
    /// Linear RGB normalized float buffer [0.0 - 1.0], 3 channels
    /// </summary>
    public ImageBuffer<float>? ColorBuffer { get; set; }

    /// <summary>
    /// Grayscale normalized float buffer [0.0 - 1.0], 1 channel for sharpness analysis
    /// </summary>
    public ImageBuffer<float>? GrayBuffer { get; set; }

    /// <summary>
    /// Calculated focus sharpness map for this frame
    /// </summary>
    public ImageBuffer<float>? FocusMap { get; set; }

    public double SharpnessScore { get; set; }
    public double AlignmentConfidence { get; set; } = 1.0;

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

public sealed class DepthMapResult : IDisposable
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Selected source frame index for each pixel (0 to N-1)
    /// </summary>
    public ImageBuffer<int> SourceFrameMap { get; }

    /// <summary>
    /// Normalized continuous depth (0.0 = closest / first frame, 1.0 = farthest / last frame)
    /// </summary>
    public ImageBuffer<float> DepthMap { get; }

    /// <summary>
    /// Confidence score [0.0 - 1.0] indicating sharpness certainty
    /// </summary>
    public ImageBuffer<float> ConfidenceMap { get; }

    public DepthMapResult(int width, int height)
    {
        Width = width;
        Height = height;
        SourceFrameMap = new ImageBuffer<int>(width, height, 1, PixelFormatType.GrayFloat32);
        DepthMap = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
        ConfidenceMap = new ImageBuffer<float>(width, height, 1, PixelFormatType.GrayFloat32);
    }

    public void Dispose()
    {
        SourceFrameMap.Dispose();
        DepthMap.Dispose();
        ConfidenceMap.Dispose();
    }
}

public sealed class FusionSettings
{
    public FusionMethod Method { get; set; } = FusionMethod.MultiScalePyramid;
    public FocusMeasureMethod FocusMethod { get; set; } = FocusMeasureMethod.ModifiedLaplacian;
    public int PyramidLevels { get; set; } = 5;
    public int SmoothingRadius { get; set; } = 2;
    public float ContrastThreshold { get; set; } = 0.001f;
    public bool EnableDepthSmoothing { get; set; } = true;
    public bool EnableNoiseAwareness { get; set; } = true;
    public bool EnableFocusBreathingCorrection { get; set; } = true;
    public bool EnableQualityAnalysis { get; set; } = false;
    public bool EnableMotionSuppression { get; set; } = false;
    public bool EnableArtifactDetection { get; set; } = false;
    public bool EnableAutoRepair { get; set; } = false;
    public bool EnableTiledProcessing { get; set; } = false;
    public int TileSize { get; set; } = 512;
}

public sealed class StackValidationResult
{
    public bool IsValid => Issues.Count == 0;
    public int FrameCount { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public List<string> Issues { get; } = new();
    public List<string> Warnings { get; } = new();
}

public sealed class StackProgress
{
    public string Stage { get; set; } = string.Empty;
    public double Percentage { get; set; }
    public string Details { get; set; } = string.Empty;

    public StackProgress(string stage, double percentage, string details = "")
    {
        Stage = stage;
        Percentage = percentage;
        Details = details;
    }
}
