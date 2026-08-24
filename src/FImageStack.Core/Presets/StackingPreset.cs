using FImageStack.Core.Models;

namespace FImageStack.Core.Presets;

public enum StackingPresetType
{
    MacroCloseUp,
    LandscapeDeepFocus,
    MicroscopyHighPower,
    HandheldBurst,
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
            }
        };
    }
}
