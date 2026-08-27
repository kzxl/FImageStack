using FImageStack.Core.Models;

namespace FImageStack.Core.Presets;

public enum StackingPresetType
{
    MacroCloseUp,
    LandscapeDeepFocus,
    MicroscopyHighPower,
    HandheldBurst,
    AstroDeepSky,
    NoiseReductionClean,
    SubpixelDrizzle2x,
    Custom
}

public sealed class StackingPreset
{
    public StackingPresetType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "🔍";
    public FusionSettings Settings { get; set; } = new();

    public static IReadOnlyList<StackingPreset> GetBuiltinPresets()
    {
        return new List<StackingPreset>
        {
            new()
            {
                Type = StackingPresetType.MacroCloseUp,
                Name = "Macro & Close-Up",
                Description = "Optimized for insect, botanical, and jewelry close-ups with fine edge preservation and halo suppression.",
                Icon = "🌸",
                Settings = new FusionSettings
                {
                    Method = FusionMethod.MultiScalePyramid,
                    FocusMethod = FocusMeasureMethod.ModifiedLaplacian,
                    PyramidLevels = 6,
                    SmoothingRadius = 3,
                    EnableDepthSmoothing = true,
                    EnableMotionSuppression = true,
                    EnableQualityAnalysis = true,
                    EnableArtifactDetection = true,
                    EnableAutoRepair = true,
                    EnableFocusBreathingCorrection = true
                }
            },
            new()
            {
                Type = StackingPresetType.LandscapeDeepFocus,
                Name = "Landscape & Deep DoF",
                Description = "Infinite depth of field for expansive vistas with atmospheric haze handling and fast gradient fusion.",
                Icon = "🏔️",
                Settings = new FusionSettings
                {
                    Method = FusionMethod.MultiScalePyramid,
                    FocusMethod = FocusMeasureMethod.Tenengrad,
                    PyramidLevels = 5,
                    SmoothingRadius = 2,
                    EnableDepthSmoothing = true,
                    EnableMotionSuppression = true,
                    EnableQualityAnalysis = true,
                    EnableArtifactDetection = false,
                    EnableAutoRepair = false,
                    EnableFocusBreathingCorrection = true
                }
            },
            new()
            {
                Type = StackingPresetType.MicroscopyHighPower,
                Name = "Microscopy & Specimen",
                Description = "Ultra-dense sub-micron focal plane stacking with 7-level pyramid frequency separation and active noise suppression.",
                Icon = "🔬",
                Settings = new FusionSettings
                {
                    Method = FusionMethod.MultiScalePyramid,
                    FocusMethod = FocusMeasureMethod.ModifiedLaplacian,
                    PyramidLevels = 7,
                    SmoothingRadius = 2,
                    EnableDepthSmoothing = true,
                    EnableMotionSuppression = false,
                    EnableQualityAnalysis = true,
                    EnableArtifactDetection = true,
                    EnableAutoRepair = true,
                    EnableFocusBreathingCorrection = false
                }
            },
            new()
            {
                Type = StackingPresetType.HandheldBurst,
                Name = "Handheld & Fast Burst",
                Description = "Fast alignment correction for camera sway, breathing compensation and rapid pixel selection.",
                Icon = "📸",
                Settings = new FusionSettings
                {
                    Method = FusionMethod.MultiScalePyramid,
                    FocusMethod = FocusMeasureMethod.ModifiedLaplacian,
                    PyramidLevels = 4,
                    SmoothingRadius = 2,
                    EnableDepthSmoothing = true,
                    EnableMotionSuppression = true,
                    EnableQualityAnalysis = true,
                    EnableArtifactDetection = true,
                    EnableAutoRepair = true,
                    EnableFocusBreathingCorrection = true
                }
            },
            new()
            {
                Type = StackingPresetType.AstroDeepSky,
                Name = "Astro & Deep Sky",
                Description = "Star point centroid registration, dark/flat calibration and statistical kappa-sigma clipping.",
                Icon = "🌌",
                Settings = new FusionSettings
                {
                    Method = FusionMethod.ConfidenceWeighted,
                    FocusMethod = FocusMeasureMethod.Tenengrad,
                    PyramidLevels = 5,
                    EnableTemporalDenoising = true,
                    DenoiseStrength = 1.5f
                }
            },
            new()
            {
                Type = StackingPresetType.NoiseReductionClean,
                Name = "Ultra-Clean Noise Stacking",
                Description = "Multi-frame temporal integration to boost signal-to-noise ratio by up to +15dB.",
                Icon = "✨",
                Settings = new FusionSettings
                {
                    Method = FusionMethod.FocusWeighted,
                    EnableTemporalDenoising = true,
                    DenoiseStrength = 2.0f
                }
            },
            new()
            {
                Type = StackingPresetType.SubpixelDrizzle2x,
                Name = "HST Subpixel Drizzle 2x",
                Description = "Linear variable pixel reconstruction for 2x super-resolution optical detail recovery.",
                Icon = "🔭",
                Settings = new FusionSettings
                {
                    Method = FusionMethod.MultiScalePyramid,
                    EnableSuperResolution = true
                }
            },
            new()
            {
                Type = StackingPresetType.Custom,
                Name = "HDR Focus & Exposure Studio",
                Description = "Mertens-Focus hybrid multi-scale stacking for sequences with varying focus AND exposure brackets (Macro specular highlights & Landscape HDR).",
                Icon = "🌈",
                Settings = new FusionSettings
                {
                    Method = FusionMethod.HDRFocusExposure,
                    FocusMethod = FocusMeasureMethod.ModifiedLaplacian,
                    PyramidLevels = 6,
                    SmoothingRadius = 3,
                    EnableDepthSmoothing = true,
                    EnableMotionSuppression = true,
                    EnableQualityAnalysis = true,
                    EnableArtifactDetection = true,
                    EnableAutoRepair = true,
                    EnableLocalAlignment = true,
                    EnableEdgeReconstruction = true,
                    EnableFocusBreathingCorrection = true
                }
            }
        };
    }
}

