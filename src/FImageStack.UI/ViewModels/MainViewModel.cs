using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FImageStack.Application.Services;
using FImageStack.Core;
using FImageStack.Core.Acceleration;
using FImageStack.Core.Lab;
using FImageStack.Core.Models;
using FImageStack.Core.PostProcessing;
using FImageStack.Core.Presets;
using FImageStack.Core.Project;
using FImageStack.Core.Quality;
using FImageStack.Core.Refocus;
using FImageStack.Core.Retouch;
using FImageStack.Core.Selection;
using FImageStack.Infrastructure.IO;
using FImageStack.UI.Common;
using FImageStack.UI.Utils;
using Microsoft.Win32;
using StackFrame = FImageStack.Core.Models.StackFrame;

namespace FImageStack.UI.ViewModels;

public sealed class PixelInspectorInfo : ViewModelBase
{
    private int _pixelX;
    private int _pixelY;
    private int _sourceFrameIndex;
    private string _sourceFrameFileName = string.Empty;
    private float _subFrameIndex;
    private float _depthZ;
    private float _dofThickness;
    private float _confidencePercentage;
    private bool _isValidFocus;
    private string _statusBadge = string.Empty;
    private string _focusCurveSummary = string.Empty;

    public int PixelX { get => _pixelX; set => SetProperty(ref _pixelX, value); }
    public int PixelY { get => _pixelY; set => SetProperty(ref _pixelY, value); }
    public int SourceFrameIndex { get => _sourceFrameIndex; set => SetProperty(ref _sourceFrameIndex, value); }
    public string SourceFrameFileName { get => _sourceFrameFileName; set => SetProperty(ref _sourceFrameFileName, value); }
    public float SubFrameIndex { get => _subFrameIndex; set => SetProperty(ref _subFrameIndex, value); }
    public float DepthZ { get => _depthZ; set => SetProperty(ref _depthZ, value); }
    public float DofThickness { get => _dofThickness; set => SetProperty(ref _dofThickness, value); }
    public float ConfidencePercentage { get => _confidencePercentage; set => SetProperty(ref _confidencePercentage, value); }
    public bool IsValidFocus { get => _isValidFocus; set => SetProperty(ref _isValidFocus, value); }
    public string StatusBadge { get => _statusBadge; set => SetProperty(ref _statusBadge, value); }
    public string FocusCurveSummary
    {
        get => _focusCurveSummary;
        set
        {
            if (SetProperty(ref _focusCurveSummary, value))
            {
                OnPropertyChanged(nameof(HasFocusCurve));
            }
        }
    }
    public bool HasFocusCurve => !string.IsNullOrEmpty(_focusCurveSummary);

    private string _confidenceBreakdownText = string.Empty;
    public string ConfidenceBreakdownText
    {
        get => _confidenceBreakdownText;
        set
        {
            if (SetProperty(ref _confidenceBreakdownText, value))
            {
                OnPropertyChanged(nameof(HasConfidenceBreakdown));
            }
        }
    }
    public bool HasConfidenceBreakdown => !string.IsNullOrEmpty(_confidenceBreakdownText);

    private string _transitionModelText = string.Empty;
    public string TransitionModelText
    {
        get => _transitionModelText;
        set
        {
            if (SetProperty(ref _transitionModelText, value))
            {
                OnPropertyChanged(nameof(HasTransitionModel));
            }
        }
    }
    public bool HasTransitionModel => !string.IsNullOrEmpty(_transitionModelText);
}

public sealed class ArtifactRegionViewModel : ViewModelBase
{
    public int Id { get; init; }
    public string TypeName { get; init; } = string.Empty;
    public int CenterX { get; init; }
    public int CenterY { get; init; }
    public float Severity { get; init; }
    public string Description { get; init; } = string.Empty;
    public string BadgeColorHex => TypeName switch
    {
        "HALO" => "#F59E0B",
        "GHOST" => "#EF4444",
        "SEAM" => "#38BDF8",
        _ => "#A855F7"
    };
}

public sealed class MainViewModel : ViewModelBase
{
    private readonly IStackService _stackService;
    private readonly IProjectService _projectService;
    private readonly IImageIO _imageIO;
    private readonly IPostProcessEngine _postProcessEngine;
    private readonly ISmartFrameSelector _frameSelector;
    private readonly IGpuAccelerationEngine _gpuEngine;

    private CancellationTokenSource? _cts;
    private ProcessedStackResult? _lastResult;
    private ImageBuffer<float>? _postProcessedBuffer;
    private RetouchLayer? _retouchLayer;

    private bool _isProcessing;
    private double _progressPercentage;
    private string _currentStage = "Ready";
    private string _statusMessage = "Select a folder or click a quick sample stack to begin.";

    // Active Display Tab: 0=Fused, 1=Split, 2=Turbo Depth, 3=Confidence, 4=Invalid/Bokeh, 5=Motion, 6=Artifacts, 7=Source
    private int _selectedViewTab = 0;
    private BitmapSource? _displayBitmap;
    private FrameItemViewModel? _selectedFrame;

    // Presets
    public ObservableCollection<StackingPreset> AvailablePresets { get; } = new();
    private StackingPreset? _selectedPreset;

    // Fusion Settings
    private FusionMethod _selectedMethod = FusionMethod.MultiScalePyramid;
    private FocusMeasureMethod _selectedFocusMethod = FocusMeasureMethod.ModifiedLaplacian;
    private AlignmentMode _selectedAlignmentMode = AlignmentMode.Similarity;
    private int _pyramidLevels = 5;
    private int _smoothingRadius = 2;
    private bool _enableQualityAnalysis = true;
    private bool _enableMotionSuppression = true;
    private bool _enableArtifactDetection = true;
    private bool _enableAutoRepair = true;
    private bool _enableLocalAlignment = true;
    private bool _enableEdgeReconstruction = true;
    private bool _enableTiledProcessing = false;
    private int _tileSize = 512;

    // Split Comparison
    private double _splitRatio = 0.5;

    // Retouch & Manual Focus Override
    private bool _isRetouchModeActive;
    private RetouchToolType _activeRetouchTool = RetouchToolType.SourceBrush;
    private int _retouchSourceFrameIndex = 0;
    private float _brushRadius = 35.0f;
    private float _brushFeather = 0.5f;
    private float _brushOpacity = 1.0f;
    private string _strokesCountText = "0 strokes";

    // Pixel Inspector
    private PixelInspectorInfo? _inspectorInfo;

    // Post-Processing Settings
    private float _exposureCompensation = 0.0f;
    private float _contrastAdjustment = 1.0f;
    private float _clarityAdjustment = 0.0f;
    private float _sharpeningAdjustment = 0.3f;
    private float _saturationAdjustment = 1.0f;
    private ToneMappingOperator _selectedToneMapping = ToneMappingOperator.ACESFilmic;

    // Metrics & Histogram
    private string _qualityScoreText = "--";
    private string _focusCoverageText = "--";
    private string _timingText = "--";
    private string _artifactsReportText = "--";
    private string _memoryUsageText = "--";
    private BitmapSource? _histogramBitmap;
    private string _shadowClippingText = "0.0%";
    private string _highlightClippingText = "0.0%";

    // Resolution & Preview Mode
    private ResolutionMode _selectedRenderMode = ResolutionMode.FastPreview1280;
    private string _resolutionBadgeText = "⚡ FAST PREVIEW (1280px)";

    public ResolutionMode SelectedRenderMode { get => _selectedRenderMode; set => SetProperty(ref _selectedRenderMode, value); }
    public string ResolutionBadgeText { get => _resolutionBadgeText; set => SetProperty(ref _resolutionBadgeText, value); }

    // Hardware & GPU Acceleration
    public ObservableCollection<GpuDeviceInfo> AvailableGpuDevices { get; } = new();
    private GpuDeviceInfo? _selectedGpuDevice;
    private GpuBackendType _selectedGpuBackend = GpuBackendType.Auto;
    private string _gpuStatusBadgeText = "⚡ DIRECTCOMPUTE GPU ACTIVE";

    public GpuDeviceInfo? SelectedGpuDevice
    {
        get => _selectedGpuDevice;
        set
        {
            if (SetProperty(ref _selectedGpuDevice, value) && value != null)
            {
                _gpuEngine.SetActiveBackend(value.Backend);
                GpuStatusBadgeText = value.IsHardwareAccelerated
                    ? $"⚡ {value.Backend.ToString().ToUpper()} GPU ACCELERATED"
                    : "💻 CPU AVX2 SIMD ACTIVE";
            }
        }
    }

    public GpuBackendType SelectedGpuBackend
    {
        get => _selectedGpuBackend;
        set
        {
            if (SetProperty(ref _selectedGpuBackend, value))
            {
                _gpuEngine.SetActiveBackend(value);
                GpuStatusBadgeText = value != GpuBackendType.CpuSimd
                    ? $"⚡ {value.ToString().ToUpper()} ACCELERATED"
                    : "💻 CPU AVX2 SIMD ACTIVE";
            }
        }
    }

    public string GpuStatusBadgeText { get => _gpuStatusBadgeText; set => SetProperty(ref _gpuStatusBadgeText, value); }

    // Advanced 6-Metric Quality Analyzer
    private double _metricAlignmentScore = 98.0;
    private double _metricFocusCoverageScore = 95.0;
    private double _metricGhostingPercent = 2.0;
    private double _metricHaloPercent = 3.0;
    private double _metricNoisePercent = 2.5;
    private double _metricEdgeQualityScore = 96.0;

    public double MetricAlignmentScore { get => _metricAlignmentScore; set => SetProperty(ref _metricAlignmentScore, value); }
    public double MetricFocusCoverageScore { get => _metricFocusCoverageScore; set => SetProperty(ref _metricFocusCoverageScore, value); }
    public double MetricGhostingPercent { get => _metricGhostingPercent; set => SetProperty(ref _metricGhostingPercent, value); }
    public double MetricHaloPercent { get => _metricHaloPercent; set => SetProperty(ref _metricHaloPercent, value); }
    public double MetricNoisePercent { get => _metricNoisePercent; set => SetProperty(ref _metricNoisePercent, value); }
    public double MetricEdgeQualityScore { get => _metricEdgeQualityScore; set => SetProperty(ref _metricEdgeQualityScore, value); }

    public ObservableCollection<ArtifactRegionViewModel> DetectedArtifactRegions { get; } = new();

    public ObservableCollection<FrameItemViewModel> Frames { get; } = new();

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            if (SetProperty(ref _isProcessing, value))
            {
                (StartStackingCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (CancelStackingCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ExportResultCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public double ProgressPercentage
    {
        get => _progressPercentage;
        set => SetProperty(ref _progressPercentage, value);
    }

    public string CurrentStage
    {
        get => _currentStage;
        set => SetProperty(ref _currentStage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public int SelectedViewTab
    {
        get => _selectedViewTab;
        set
        {
            if (SetProperty(ref _selectedViewTab, value))
            {
                OnPropertyChanged(nameof(IsFusedTabSelected));
                OnPropertyChanged(nameof(IsSplitTabSelected));
                OnPropertyChanged(nameof(IsDepthTabSelected));
                OnPropertyChanged(nameof(IsConfidenceTabSelected));
                OnPropertyChanged(nameof(IsInvalidTabSelected));
                OnPropertyChanged(nameof(IsMotionTabSelected));
                OnPropertyChanged(nameof(IsArtifactTabSelected));
                OnPropertyChanged(nameof(IsSourceTabSelected));
                OnPropertyChanged(nameof(IsDofVolumeTabSelected));
                OnPropertyChanged(nameof(IsFocusWaveTabSelected));
                OnPropertyChanged(nameof(IsVirtualDofTabSelected));
                OnPropertyChanged(nameof(IsStackLabTabSelected));
                UpdateDisplayBitmap();
            }
        }
    }

    public bool IsFusedTabSelected
    {
        get => SelectedViewTab == 0;
        set { if (value) SelectedViewTab = 0; }
    }

    public bool IsSplitTabSelected
    {
        get => SelectedViewTab == 1;
        set { if (value) SelectedViewTab = 1; }
    }

    public bool IsDepthTabSelected
    {
        get => SelectedViewTab == 2;
        set { if (value) SelectedViewTab = 2; }
    }

    public bool IsConfidenceTabSelected
    {
        get => SelectedViewTab == 3;
        set { if (value) SelectedViewTab = 3; }
    }

    public bool IsInvalidTabSelected
    {
        get => SelectedViewTab == 4;
        set { if (value) SelectedViewTab = 4; }
    }

    public bool IsMotionTabSelected
    {
        get => SelectedViewTab == 5;
        set { if (value) SelectedViewTab = 5; }
    }

    public bool IsArtifactTabSelected
    {
        get => SelectedViewTab == 6;
        set { if (value) SelectedViewTab = 6; }
    }

    public bool IsSourceTabSelected
    {
        get => SelectedViewTab == 7;
        set { if (value) SelectedViewTab = 7; }
    }

    public bool IsDofVolumeTabSelected
    {
        get => SelectedViewTab == 8;
        set { if (value) SelectedViewTab = 8; }
    }

    public bool IsFocusWaveTabSelected
    {
        get => SelectedViewTab == 9;
        set { if (value) SelectedViewTab = 9; }
    }

    public bool IsVirtualDofTabSelected
    {
        get => SelectedViewTab == 10;
        set { if (value) SelectedViewTab = 10; }
    }

    public bool IsStackLabTabSelected
    {
        get => SelectedViewTab == 11;
        set { if (value) SelectedViewTab = 11; }
    }

    public BitmapSource? DisplayBitmap
    {
        get => _displayBitmap;
        set => SetProperty(ref _displayBitmap, value);
    }

    public FrameItemViewModel? SelectedFrame
    {
        get => _selectedFrame;
        set
        {
            if (SetProperty(ref _selectedFrame, value))
            {
                if (value != null) RetouchSourceFrameIndex = value.Index;
                if (SelectedViewTab == 7 || SelectedViewTab == 1)
                {
                    UpdateDisplayBitmap();
                }
            }
        }
    }

    public StackingPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value) && value != null)
            {
                ApplyPreset(value);
            }
        }
    }

    public double SplitRatio
    {
        get => _splitRatio;
        set
        {
            if (SetProperty(ref _splitRatio, value))
            {
                if (SelectedViewTab == 1) UpdateDisplayBitmap();
            }
        }
    }

    // Retouch Properties
    public bool IsRetouchModeActive
    {
        get => _isRetouchModeActive;
        set => SetProperty(ref _isRetouchModeActive, value);
    }

    public RetouchToolType ActiveRetouchTool
    {
        get => _activeRetouchTool;
        set => SetProperty(ref _activeRetouchTool, value);
    }

    public int RetouchSourceFrameIndex
    {
        get => _retouchSourceFrameIndex;
        set => SetProperty(ref _retouchSourceFrameIndex, value);
    }

    public float BrushRadius
    {
        get => _brushRadius;
        set => SetProperty(ref _brushRadius, value);
    }

    public float BrushFeather
    {
        get => _brushFeather;
        set => SetProperty(ref _brushFeather, value);
    }

    public float BrushOpacity
    {
        get => _brushOpacity;
        set => SetProperty(ref _brushOpacity, value);
    }

    public string StrokesCountText
    {
        get => _strokesCountText;
        set => SetProperty(ref _strokesCountText, value);
    }

    public PixelInspectorInfo? InspectorInfo
    {
        get => _inspectorInfo;
        set => SetProperty(ref _inspectorInfo, value);
    }

    // Post-Processing Settings Properties
    public float ExposureCompensation
    {
        get => _exposureCompensation;
        set
        {
            if (SetProperty(ref _exposureCompensation, value)) ApplyLivePostProcessing();
        }
    }

    public float ContrastAdjustment
    {
        get => _contrastAdjustment;
        set
        {
            if (SetProperty(ref _contrastAdjustment, value)) ApplyLivePostProcessing();
        }
    }

    public float ClarityAdjustment
    {
        get => _clarityAdjustment;
        set
        {
            if (SetProperty(ref _clarityAdjustment, value)) ApplyLivePostProcessing();
        }
    }

    public float SharpeningAdjustment
    {
        get => _sharpeningAdjustment;
        set
        {
            if (SetProperty(ref _sharpeningAdjustment, value)) ApplyLivePostProcessing();
        }
    }

    public float SaturationAdjustment
    {
        get => _saturationAdjustment;
        set
        {
            if (SetProperty(ref _saturationAdjustment, value)) ApplyLivePostProcessing();
        }
    }

    public ToneMappingOperator SelectedToneMapping
    {
        get => _selectedToneMapping;
        set
        {
            if (SetProperty(ref _selectedToneMapping, value)) ApplyLivePostProcessing();
        }
    }

    // Fusion Settings Properties
    public FusionMethod SelectedMethod
    {
        get => _selectedMethod;
        set => SetProperty(ref _selectedMethod, value);
    }

    public FocusMeasureMethod SelectedFocusMethod
    {
        get => _selectedFocusMethod;
        set => SetProperty(ref _selectedFocusMethod, value);
    }

    public AlignmentMode SelectedAlignmentMode
    {
        get => _selectedAlignmentMode;
        set => SetProperty(ref _selectedAlignmentMode, value);
    }

    public int PyramidLevels
    {
        get => _pyramidLevels;
        set => SetProperty(ref _pyramidLevels, value);
    }

    public int SmoothingRadius
    {
        get => _smoothingRadius;
        set => SetProperty(ref _smoothingRadius, value);
    }

    public bool EnableQualityAnalysis
    {
        get => _enableQualityAnalysis;
        set => SetProperty(ref _enableQualityAnalysis, value);
    }

    public bool EnableMotionSuppression
    {
        get => _enableMotionSuppression;
        set => SetProperty(ref _enableMotionSuppression, value);
    }

    public bool EnableArtifactDetection
    {
        get => _enableArtifactDetection;
        set => SetProperty(ref _enableArtifactDetection, value);
    }

    public bool EnableAutoRepair
    {
        get => _enableAutoRepair;
        set => SetProperty(ref _enableAutoRepair, value);
    }

    public bool EnableLocalAlignment
    {
        get => _enableLocalAlignment;
        set => SetProperty(ref _enableLocalAlignment, value);
    }

    public bool EnableEdgeReconstruction
    {
        get => _enableEdgeReconstruction;
        set => SetProperty(ref _enableEdgeReconstruction, value);
    }

    public bool EnableTiledProcessing { get => _enableTiledProcessing; set => SetProperty(ref _enableTiledProcessing, value); }
    public object TileSize
    {
        get => _tileSize;
        set
        {
            if (value is int i) SetProperty(ref _tileSize, i);
            else if (value is string s && int.TryParse(s, out int parsed)) SetProperty(ref _tileSize, parsed);
        }
    }

    // Metrics properties
    public string QualityScoreText
    {
        get => _qualityScoreText;
        set => SetProperty(ref _qualityScoreText, value);
    }

    public string FocusCoverageText
    {
        get => _focusCoverageText;
        set => SetProperty(ref _focusCoverageText, value);
    }

    public string TimingText
    {
        get => _timingText;
        set => SetProperty(ref _timingText, value);
    }

    public string ArtifactsReportText
    {
        get => _artifactsReportText;
        set => SetProperty(ref _artifactsReportText, value);
    }

    public string MemoryUsageText
    {
        get => _memoryUsageText;
        set => SetProperty(ref _memoryUsageText, value);
    }

    public BitmapSource? HistogramBitmap
    {
        get => _histogramBitmap;
        set => SetProperty(ref _histogramBitmap, value);
    }

    public string ShadowClippingText
    {
        get => _shadowClippingText;
        set => SetProperty(ref _shadowClippingText, value);
    }

    public string HighlightClippingText
    {
        get => _highlightClippingText;
        set => SetProperty(ref _highlightClippingText, value);
    }

    // Cached Bitmap Sources
    public BitmapSource? FusedBitmap { get; private set; }
    public BitmapSource? TurboDepthBitmap { get; private set; }
    public BitmapSource? ConfidenceMapBitmap { get; private set; }
    public BitmapSource? InvalidRegionBitmap { get; private set; }
    public BitmapSource? DofThicknessBitmap { get; private set; }
    public BitmapSource? MotionMapBitmap { get; private set; }
    public BitmapSource? ArtifactMapBitmap { get; private set; }
    public BitmapSource? VirtualDofBitmap { get; private set; }

    // Advanced Quality & Lab Engines
    private readonly IArtifactHunterEngine _hunterEngine = new ArtifactHunterEngine();
    private readonly IShotQualityPredictor _qualityPredictor = new ShotQualityPredictor();
    private readonly IFocusWaveEngine _focusWaveEngine = new FocusWaveEngine();
    private readonly IRefocusEngine _refocusEngine = new RefocusEngine();
    private readonly IABStackLabEngine _stackLabEngine = new ABStackLabEngine();

    // Artifact Hunter & Quality Scorecard Properties
    private ArtifactHunterReport? _hunterReport;
    public ArtifactHunterReport? HunterReport { get => _hunterReport; set => SetProperty(ref _hunterReport, value); }

    private bool _isHunterPopupOpen;
    public bool IsHunterPopupOpen { get => _isHunterPopupOpen; set => SetProperty(ref _isHunterPopupOpen, value); }

    private ShotQualityScorecard? _scorecard;
    public ShotQualityScorecard? Scorecard { get => _scorecard; set => SetProperty(ref _scorecard, value); }

    private string _scorecardBadgeText = "Grade A+ (93%)";
    public string ScorecardBadgeText { get => _scorecardBadgeText; set => SetProperty(ref _scorecardBadgeText, value); }

    private string _scorecardSummaryText = "Ready to Render (Optimal Condition)";
    public string ScorecardSummaryText { get => _scorecardSummaryText; set => SetProperty(ref _scorecardSummaryText, value); }

    // Focus Wave Properties
    private FocusWaveAnalysisResult? _focusWaveResult;
    public FocusWaveAnalysisResult? FocusWaveResult { get => _focusWaveResult; set => SetProperty(ref _focusWaveResult, value); }

    private string _focusWaveAsciiGraph = string.Empty;
    public string FocusWaveAsciiGraph { get => _focusWaveAsciiGraph; set => SetProperty(ref _focusWaveAsciiGraph, value); }

    private string _stepUniformityScoreText = "94% Uniform";
    public string StepUniformityScoreText { get => _stepUniformityScoreText; set => SetProperty(ref _stepUniformityScoreText, value); }

    private string _focusWaveSummaryText = string.Empty;
    public string FocusWaveSummaryText { get => _focusWaveSummaryText; set => SetProperty(ref _focusWaveSummaryText, value); }

    // Virtual Focus & DOF Sliders
    private float _virtualAperture = 0.5f;
    public float VirtualAperture { get => _virtualAperture; set => SetProperty(ref _virtualAperture, value); }

    private float _virtualDofMin = 0f;
    public float VirtualDofMin { get => _virtualDofMin; set => SetProperty(ref _virtualDofMin, value); }

    private float _virtualDofMax = 10f;
    public float VirtualDofMax { get => _virtualDofMax; set => SetProperty(ref _virtualDofMax, value); }

    // A/B Stack Lab Properties
    private StackLabReport? _labReport;
    public StackLabReport? LabReport { get => _labReport; set => SetProperty(ref _labReport, value); }
    public ObservableCollection<StackLabSlot> LabSlots { get; } = new();

    private string _labSummaryText = "Click 'Run Lab' to benchmark all 5 stacking algorithms simultaneously.";
    public string LabSummaryText { get => _labSummaryText; set => SetProperty(ref _labSummaryText, value); }

    // Right Sidebar Active Tab: 0=Stacking Engine, 1=Color/Post, 2=Retouch, 3=Quality/Metrics
    private int _selectedSidebarTab = 0;
    public int SelectedSidebarTab
    {
        get => _selectedSidebarTab;
        set
        {
            if (SetProperty(ref _selectedSidebarTab, value))
            {
                OnPropertyChanged(nameof(IsSidebarStackTabSelected));
                OnPropertyChanged(nameof(IsSidebarColorTabSelected));
                OnPropertyChanged(nameof(IsSidebarRetouchTabSelected));
                OnPropertyChanged(nameof(IsSidebarMetricsTabSelected));
            }
        }
    }

    public bool IsSidebarStackTabSelected
    {
        get => _selectedSidebarTab == 0;
        set { if (value) SelectedSidebarTab = 0; }
    }

    public bool IsSidebarColorTabSelected
    {
        get => _selectedSidebarTab == 1;
        set { if (value) SelectedSidebarTab = 1; }
    }

    public bool IsSidebarRetouchTabSelected
    {
        get => _selectedSidebarTab == 2;
        set { if (value) SelectedSidebarTab = 2; }
    }

    public bool IsSidebarMetricsTabSelected
    {
        get => _selectedSidebarTab == 3;
        set { if (value) SelectedSidebarTab = 3; }
    }

    // Zoom & Pan System
    private double _zoomScale = 1.0;
    public double ZoomScale
    {
        get => _zoomScale;
        set
        {
            double clamped = Math.Clamp(value, 0.1, 10.0);
            if (SetProperty(ref _zoomScale, clamped))
            {
                OnPropertyChanged(nameof(ZoomScalePercentText));
            }
        }
    }

    public string ZoomScalePercentText => $"{(int)Math.Round(ZoomScale * 100)}%";

    // Commands
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomActualSizeCommand { get; }
    public ICommand ZoomFitCommand { get; }
    public ICommand Zoom200Command { get; }

    public ICommand LoadFolderCommand { get; }
    public ICommand LoadSampleStackCommand { get; }
    public ICommand SaveProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand StartStackingCommand { get; }
    public ICommand StartPreviewStackCommand { get; }
    public ICommand StartFullMasterRenderCommand { get; }
    public ICommand CancelStackingCommand { get; }
    public ICommand ExportResultCommand { get; }
    public ICommand SelectAllFramesCommand { get; }
    public ICommand AutoCullBadFramesCommand { get; }
    public ICommand AnalyzeFramesCommand { get; }
    public ICommand ResetPostProcessingCommand { get; }
    public ICommand JumpToInspectedFrameCommand { get; }
    public ICommand JumpToArtifactRegionCommand { get; }
    public ICommand UndoRetouchCommand { get; }
    public ICommand RedoRetouchCommand { get; }
    public ICommand ClearRetouchCommand { get; }
    public ICommand HuntArtifactsCommand { get; }
    public ICommand CloseHunterPopupCommand { get; }
    public ICommand ApplyVirtualDofCommand { get; }
    public ICommand RunStackLabCommand { get; }
    public ICommand SelectLabWinnerCommand { get; }

    public MainViewModel()
    {
        _imageIO = new ImageSharpIO();
        _projectService = new ProjectService();
        _stackService = new StackService(_imageIO);
        _postProcessEngine = new StandardPostProcessEngine();
        _frameSelector = new SmartFrameSelector();
        _gpuEngine = new StandardGpuAccelerationEngine();

        foreach (var dev in _gpuEngine.GetAvailableDevices())
        {
            AvailableGpuDevices.Add(dev);
        }
        _selectedGpuDevice = AvailableGpuDevices.FirstOrDefault(d => d.IsHardwareAccelerated) ?? AvailableGpuDevices[0];

        foreach (var p in StackingPreset.GetBuiltinPresets())
        {
            AvailablePresets.Add(p);
        }
        _selectedPreset = AvailablePresets[0];

        LoadFolderCommand = new RelayCommand(ExecuteLoadFolder);
        LoadSampleStackCommand = new RelayCommand(ExecuteLoadSampleStack);
        SaveProjectCommand = new AsyncRelayCommand(ExecuteSaveProjectAsync, () => !IsProcessing);
        OpenProjectCommand = new AsyncRelayCommand(ExecuteOpenProjectAsync, () => !IsProcessing);
        StartStackingCommand = new AsyncRelayCommand(() => ExecuteStartStackingAsync(), () => !IsProcessing && Frames.Count >= 2);
        StartPreviewStackCommand = new AsyncRelayCommand(() => ExecuteStartStackingAsync(ResolutionMode.FastPreview1280), () => !IsProcessing && Frames.Count >= 2);
        StartFullMasterRenderCommand = new AsyncRelayCommand(() => ExecuteStartStackingAsync(ResolutionMode.FullMaster), () => !IsProcessing && Frames.Count >= 2);
        CancelStackingCommand = new RelayCommand(ExecuteCancelStacking, () => IsProcessing);
        ExportResultCommand = new RelayCommand(ExecuteExportResult, () => FusedBitmap != null && !IsProcessing);
        SelectAllFramesCommand = new RelayCommand(_ =>
        {
            bool anyUnchecked = Frames.Any(f => !f.IsSelected);
            foreach (var f in Frames) f.IsSelected = anyUnchecked;
        });

        AutoCullBadFramesCommand = new RelayCommand(_ =>
        {
            int culledCount = 0;
            foreach (var f in Frames)
            {
                if (f.IsBadFrame || f.IsDuplicate)
                {
                    f.IsSelected = false;
                    culledCount++;
                }
            }
            StatusMessage = $"Smart Filter: Excluded {culledCount} bad/duplicate frames from stack.";
        });

        AnalyzeFramesCommand = new AsyncRelayCommand(AnalyzeStackQualityAsync);

        ResetPostProcessingCommand = new RelayCommand(_ =>
        {
            ExposureCompensation = 0.0f;
            ContrastAdjustment = 1.0f;
            ClarityAdjustment = 0.0f;
            SharpeningAdjustment = 0.3f;
            SaturationAdjustment = 1.0f;
        });

        JumpToInspectedFrameCommand = new RelayCommand(_ =>
        {
            if (InspectorInfo != null && InspectorInfo.SourceFrameIndex >= 0 && InspectorInfo.SourceFrameIndex < Frames.Count)
            {
                SelectedFrame = Frames[InspectorInfo.SourceFrameIndex];
                SelectedViewTab = 7; // Switch to Source Frame tab
            }
        });

        JumpToArtifactRegionCommand = new RelayCommand(param =>
        {
            if (param is ArtifactRegionViewModel r)
            {
                InspectPixel(r.CenterX, r.CenterY);
                SelectedViewTab = 0; // Fused view with HUD
                StatusMessage = $"Navigated to {r.TypeName} at ({r.CenterX}, {r.CenterY}) [Severity: {r.Severity * 100:F0}%]. Retouch Brush is ready.";
            }
        });

        UndoRetouchCommand = new RelayCommand(_ =>
        {
            if (_retouchLayer != null && _retouchLayer.Undo())
            {
                ApplyLivePostProcessing();
                UpdateRetouchStrokesCount();
            }
        });

        RedoRetouchCommand = new RelayCommand(_ =>
        {
            if (_retouchLayer != null && _retouchLayer.Redo())
            {
                ApplyLivePostProcessing();
                UpdateRetouchStrokesCount();
            }
        });

        ClearRetouchCommand = new RelayCommand(_ =>
        {
            if (_retouchLayer != null && _retouchLayer.Strokes.Count > 0)
            {
                _retouchLayer.Strokes.Clear();
                ApplyLivePostProcessing();
                UpdateRetouchStrokesCount();
            }
        });

        ZoomInCommand = new RelayCommand(_ => ZoomScale = Math.Min(10.0, ZoomScale * 1.25));
        ZoomOutCommand = new RelayCommand(_ => ZoomScale = Math.Max(0.1, ZoomScale / 1.25));
        ZoomActualSizeCommand = new RelayCommand(_ => ZoomScale = 1.0);
        ZoomFitCommand = new RelayCommand(_ => ZoomScale = 1.0);
        Zoom200Command = new RelayCommand(_ => ZoomScale = 2.0);

        HuntArtifactsCommand = new AsyncRelayCommand(ExecuteHuntArtifactsAsync, () => !IsProcessing && Frames.Count >= 2);
        CloseHunterPopupCommand = new RelayCommand(_ => IsHunterPopupOpen = false);
        ApplyVirtualDofCommand = new AsyncRelayCommand(ExecuteApplyVirtualDofAsync, () => _lastResult != null && !IsProcessing);
        RunStackLabCommand = new AsyncRelayCommand(ExecuteRunStackLabAsync, () => !IsProcessing && Frames.Count >= 2);
        SelectLabWinnerCommand = new RelayCommand(param => ExecuteSelectLabWinner(param as StackLabSlot));

        // Try pre-loading test dataset if available
        string defaultSample = @"data\test_stack_50";
        if (Directory.Exists(defaultSample))
        {
            LoadFolder(defaultSample);
        }
    }

    public async Task AnalyzeStackQualityAsync()
    {
        if (Frames.Count == 0) return;

        StatusMessage = "Analyzing stack frames for blur, duplicates, and exposure...";

        await Task.Run(() =>
        {
            var loadedFrames = new List<StackFrame>(Frames.Count);
            try
            {
                for (int i = 0; i < Frames.Count; i++)
                {
                    var f = _imageIO.LoadFrame(Frames[i].FilePath, i);
                    loadedFrames.Add(f);
                }

                var diags = _frameSelector.AnalyzeStack(loadedFrames);
                var scorecard = _qualityPredictor.PredictQuality(loadedFrames);
                var waveResult = _focusWaveEngine.AnalyzeFocusWave(loadedFrames);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    int badCount = 0;
                    for (int i = 0; i < diags.Count && i < Frames.Count; i++)
                    {
                        var d = diags[i];
                        var item = Frames[i];

                        item.SharpnessScore = d.SharpnessScore;
                        item.IsBadFrame = d.IsBadFrame && !d.IsDuplicate;
                        item.IsDuplicate = d.IsDuplicate;
                        item.QualityBadge = d.BadgeText;
                        item.QualityTooltip = string.IsNullOrEmpty(d.Reason) ? $"Sharpness: {d.SharpnessScore:F0}% | Exposure: {d.ExposureMean * 100:F0}%" : d.Reason;

                        if (d.IsBadFrame || d.IsDuplicate) badCount++;
                    }

                    Scorecard = scorecard;
                    ScorecardBadgeText = $"{scorecard.GradeTitle} ({scorecard.FinalExpectedQualityScore:F0}%)";
                    ScorecardSummaryText = scorecard.SummaryMessage;

                    FocusWaveResult = waveResult;
                    FocusWaveAsciiGraph = waveResult.AsciiWaveGraph;
                    StepUniformityScoreText = $"{waveResult.StepUniformityScore:F0}% Uniform";
                    FocusWaveSummaryText = waveResult.EvaluationSummary;

                    StatusMessage = $"Analysis complete: Flagged {badCount} problematic frame(s). Predicted Quality: {scorecard.GradeTitle}.";
                });
            }
            finally
            {
                foreach (var lf in loadedFrames) lf.Dispose();
            }
        });
    }

    private async Task ExecuteHuntArtifactsAsync()
    {
        if (Frames.Count < 2) return;
        StatusMessage = "Artifact Hunter: Scanning entire stack for ghosting, halos, blur and alignment risks...";

        await Task.Run(() =>
        {
            var loadedFrames = new List<StackFrame>(Frames.Count);
            try
            {
                int sampleCount = Math.Min(Frames.Count, 20);
                for (int i = 0; i < sampleCount; i++)
                {
                    if (File.Exists(Frames[i].FilePath))
                    {
                        loadedFrames.Add(_imageIO.LoadFrame(Frames[i].FilePath, i));
                    }
                }

                if (loadedFrames.Count >= 2)
                {
                    var report = _hunterEngine.HuntArtifacts(loadedFrames);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        HunterReport = report;
                        IsHunterPopupOpen = true;
                        StatusMessage = $"Artifact Hunter Scan Complete! Health Score: {report.HealthScore}% with {report.Hotspots.Count} hotspots.";
                    });
                }
            }
            finally
            {
                foreach (var lf in loadedFrames) lf.Dispose();
            }
        });
    }

    private async Task ExecuteApplyVirtualDofAsync()
    {
        if (_lastResult == null || _lastResult.DepthResult == null)
        {
            StatusMessage = "Virtual DOF requires a completed stack render first.";
            return;
        }

        StatusMessage = $"Rendering Virtual DOF (Aperture: f/{VirtualAperture * 2.8f:F1}, Range: [{VirtualDofMin:F1}..{VirtualDofMax:F1}])...";

        await Task.Run(() =>
        {
            var loadedFrames = new List<StackFrame>(Frames.Count);
            try
            {
                for (int i = 0; i < Frames.Count; i++)
                {
                    if (File.Exists(Frames[i].FilePath))
                    {
                        loadedFrames.Add(_imageIO.LoadFrame(Frames[i].FilePath, i));
                    }
                }

                var rendered = _refocusEngine.RenderSelectiveDofRange(
                    _lastResult.DepthResult,
                    loadedFrames,
                    VirtualDofMin,
                    VirtualDofMax);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    VirtualDofBitmap = BitmapHelper.ToBitmapSource(rendered);
                    SelectedViewTab = 10;
                    UpdateDisplayBitmap();
                    StatusMessage = $"Virtual DOF Render Complete (Aperture: {VirtualAperture:F2}x, Range: [{VirtualDofMin:F1}..{VirtualDofMax:F1}]).";
                });
            }
            finally
            {
                foreach (var lf in loadedFrames) lf.Dispose();
            }
        });
    }

    private async Task ExecuteRunStackLabAsync()
    {
        if (Frames.Count < 2) return;
        StatusMessage = "A/B Stack Lab: Running multi-algorithm benchmark matrix in parallel...";

        await Task.Run(() =>
        {
            var loadedFrames = new List<StackFrame>(Frames.Count);
            try
            {
                int sampleCount = Math.Min(Frames.Count, 15);
                for (int i = 0; i < sampleCount; i++)
                {
                    if (File.Exists(Frames[i].FilePath))
                    {
                        loadedFrames.Add(_imageIO.LoadFrame(Frames[i].FilePath, i));
                    }
                }

                if (loadedFrames.Count >= 2)
                {
                    var report = _stackLabEngine.RunMultiStackLab(loadedFrames);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        LabReport = report;
                        LabSlots.Clear();
                        foreach (var slot in report.Slots)
                        {
                            LabSlots.Add(slot);
                        }
                        LabSummaryText = $"Lab Winner: {report.WinnerAlgorithmTitle} with Score {report.WinnerScore:F1} pts!";
                        SelectedViewTab = 11;
                        StatusMessage = $"A/B Stack Lab Finished! Best performer: {report.WinnerAlgorithmTitle} ({report.WinnerScore:F1} pts).";
                    });
                }
            }
            finally
            {
                foreach (var lf in loadedFrames) lf.Dispose();
            }
        });
    }

    private void ExecuteSelectLabWinner(StackLabSlot? slot)
    {
        if (slot == null || slot.RenderedImage == null) return;
        _postProcessedBuffer?.Dispose();
        _postProcessedBuffer = slot.RenderedImage.Clone();
        FusedBitmap = BitmapHelper.ToBitmapSource(_postProcessedBuffer);
        SelectedViewTab = 0;
        UpdateDisplayBitmap();
        StatusMessage = $"Adopted {slot.AlgorithmTitle} as Primary Fused Master Image!";
    }

    public unsafe void ApplyBrushStroke(float x, float y)
    {
        if (_lastResult == null || _retouchLayer == null) return;
        if (RetouchSourceFrameIndex < 0 || RetouchSourceFrameIndex >= Frames.Count) return;

        var stroke = new RetouchStroke
        {
            StrokeId = _retouchLayer.Strokes.Count + 1,
            Tool = ActiveRetouchTool,
            SourceFrameIndex = RetouchSourceFrameIndex,
            CenterX = x,
            CenterY = y,
            Radius = BrushRadius,
            Feather = BrushFeather,
            Opacity = BrushOpacity
        };

        _retouchLayer.AddStroke(stroke);
        UpdateRetouchStrokesCount();

        // Real-time incremental patch on current post-processed buffer
        if (_postProcessedBuffer != null && File.Exists(Frames[RetouchSourceFrameIndex].FilePath))
        {
            using var srcFrame = _imageIO.LoadFrame(Frames[RetouchSourceFrameIndex].FilePath, RetouchSourceFrameIndex);
            int w = _postProcessedBuffer.Width;
            int h = _postProcessedBuffer.Height;

            int x0 = Math.Max(0, (int)(x - BrushRadius));
            int y0 = Math.Max(0, (int)(y - BrushRadius));
            int x1 = Math.Min(w, (int)(x + BrushRadius + 1));
            int y1 = Math.Min(h, (int)(y + BrushRadius + 1));

            float rSq = BrushRadius * BrushRadius;
            float innerRadius = BrushRadius * (1f - BrushFeather);
            float innerSq = innerRadius * innerRadius;

            float* dstPtr = _postProcessedBuffer.DataPointer;
            float* srcPtr = srcFrame.ColorBuffer!.DataPointer;

            Parallel.For(y0, y1, py =>
            {
                int rowOffset = py * w;
                float dy = py - y;
                float dySq = dy * dy;

                for (int px = x0; px < x1; px++)
                {
                    float dx = px - x;
                    float distSq = dx * dx + dySq;
                    if (distSq > rSq) continue;

                    float weight = BrushOpacity;
                    if (distSq > innerSq && BrushFeather > 0)
                    {
                        float dist = MathF.Sqrt(distSq);
                        float featherT = (dist - innerRadius) / (BrushRadius - innerRadius + 1e-5f);
                        weight *= 0.5f * (1.0f + MathF.Cos(featherT * MathF.PI));
                    }

                    int idx = (rowOffset + px) * 3;
                    dstPtr[idx] = dstPtr[idx] * (1f - weight) + srcPtr[idx] * weight;
                    dstPtr[idx + 1] = dstPtr[idx + 1] * (1f - weight) + srcPtr[idx + 1] * weight;
                    dstPtr[idx + 2] = dstPtr[idx + 2] * (1f - weight) + srcPtr[idx + 2] * weight;
                }
            });

            FusedBitmap = BitmapHelper.ToBitmapSource(_postProcessedBuffer);
            if (SelectedViewTab == 0) DisplayBitmap = FusedBitmap;
        }
    }

    private void UpdateRetouchStrokesCount()
    {
        int count = _retouchLayer?.Strokes.Count ?? 0;
        StrokesCountText = $"{count} stroke{(count != 1 ? "s" : "")}";
    }

    public unsafe void InspectPixel(int x, int y)
    {
        if (_lastResult == null || _lastResult.DepthResult == null) return;

        var depthRes = _lastResult.DepthResult;
        int w = depthRes.Width;
        int h = depthRes.Height;

        if (x < 0 || x >= w || y < 0 || y >= h) return;

        int idx = y * w + x;
        int frameIdx = depthRes.SourceFrameMap.DataPointer[idx];
        float depthZ = depthRes.DepthMap.DataPointer[idx];
        float conf = depthRes.ConfidenceMap.DataPointer[idx];
        float dofVal = depthRes.DofMap != null ? depthRes.DofMap.DataPointer[idx] * Math.Max(1, Frames.Count - 1) : 1.0f;
        bool isGap = depthRes.FocusGapMask != null && depthRes.FocusGapMask.DataPointer[idx] > 0.5f;

        float subFrame = depthZ * Math.Max(1, Frames.Count - 1);
        string fileName = (frameIdx >= 0 && frameIdx < Frames.Count) ? Frames[frameIdx].FileName : $"Frame #{frameIdx + 1}";
        bool isValid = conf >= 0.15f && !isGap;

        string curveSummary = string.Empty;
        if (depthRes.FocusVolume != null && depthRes.FocusVolume.Width == w && depthRes.FocusVolume.Height == h)
        {
            var profileSpan = depthRes.FocusVolume.GetProfile(x, y);
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int f = 0; f < profileSpan.Length; f++)
            {
                sb.Append($"F{f + 1}: {profileSpan[f]:F2}");
                if (f == frameIdx) sb.Append("★");
                if (f < profileSpan.Length - 1) sb.Append(" | ");
            }
            sb.Append("]");
            curveSummary = sb.ToString();
        }

        string breakdownText = $"Sharpness: {conf:F2} | Conf: {conf * 100f:F0}%";
        string transitionText = string.Empty;
        if (depthRes.FocusVolume != null)
        {
            var profileSpan = depthRes.FocusVolume.GetProfile(x, y);
            float s = conf;
            float a = Math.Clamp(1.0f - MathF.Abs(depthZ - (float)frameIdx / Math.Max(1, Frames.Count - 1)), 0.1f, 1.0f);
            float m = _lastResult.MotionResult?.MotionMap != null ? _lastResult.MotionResult.MotionMap.DataPointer[idx] : 0f;
            float e = Math.Clamp(s * 1.1f, 0.2f, 1.0f);

            float neighborMean = 0f;
            if (profileSpan.Length >= 3 && frameIdx > 0 && frameIdx < profileSpan.Length - 1)
            {
                neighborMean = (profileSpan[frameIdx - 1] + profileSpan[frameIdx + 1]) * 0.5f;
            }
            float cons = profileSpan.Length >= 3 && profileSpan[frameIdx] > neighborMean * 1.8f && neighborMean > 0
                ? Math.Clamp((2f * neighborMean) / (profileSpan[frameIdx] + neighborMean + 1e-5f), 0.05f, 1.0f)
                : 1.0f;

            float total = s * (0.35f + 0.65f * a) * (0.2f + 0.8f * (1f - Math.Clamp(m * 1.5f, 0f, 0.95f))) * (0.4f + 0.6f * e) * cons;
            breakdownText = $"S:{s:F2} | A:{a:F2} | M:{m:F2} | E:{e:F2} | Cons:{cons:F2} => Conf:{total:F2}";

            var fitter = new FImageStack.Core.FocusVolume.FocusTransitionFitter();
            var model = fitter.FitTransition(profileSpan);
            transitionText = $"Gaussian Model: μ: {model.OptimalMu:F2} | σ: {model.TransitionSpread:F2} slices | A: {model.PeakAmplitude:F2} | R²: {model.GoodnessOfFit * 100f:F0}%";
        }

        InspectorInfo = new PixelInspectorInfo
        {
            PixelX = x,
            PixelY = y,
            SourceFrameIndex = frameIdx,
            SourceFrameFileName = fileName,
            SubFrameIndex = subFrame,
            DepthZ = depthZ,
            DofThickness = dofVal,
            ConfidencePercentage = conf * 100f,
            IsValidFocus = isValid,
            StatusBadge = isValid ? "✅ In-Focus" : (isGap ? "⚠️ Focus Gap" : "⚠️ Bokeh / Low Texture"),
            FocusCurveSummary = curveSummary,
            ConfidenceBreakdownText = breakdownText,
            TransitionModelText = transitionText
        };
    }

    private void ApplyPreset(StackingPreset preset)
    {
        SelectedMethod = preset.Settings.Method;
        SelectedFocusMethod = preset.Settings.FocusMethod;
        SelectedAlignmentMode = preset.Settings.AlignmentMode;
        PyramidLevels = preset.Settings.PyramidLevels;
        SmoothingRadius = preset.Settings.SmoothingRadius;
        EnableQualityAnalysis = preset.Settings.EnableQualityAnalysis;
        EnableMotionSuppression = preset.Settings.EnableMotionSuppression;
        EnableArtifactDetection = preset.Settings.EnableArtifactDetection;
        EnableAutoRepair = preset.Settings.EnableAutoRepair;
        EnableTiledProcessing = preset.Settings.EnableTiledProcessing;
        StatusMessage = $"Applied preset: {preset.Name}";
    }

    private void ExecuteLoadFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Image Stack Folder"
        };
        if (dialog.ShowDialog() == true)
        {
            LoadFolder(dialog.FolderName);
        }
    }

    private async Task ExecuteSaveProjectAsync()
    {
        var activeFiles = Frames.Select(f => f.FilePath).ToList();
        if (activeFiles.Count == 0 && _lastResult == null)
        {
            MessageBox.Show("No active project to save. Please load frames or run a stack first.", "FImageStack", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Save FImageStack Project",
            Filter = "FImageStack Project (*.fstack)|*.fstack",
            DefaultExt = ".fstack",
            FileName = "Macro_Stack_Project.fstack"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                StatusMessage = "Packaging and saving project...";
                var project = new FStackProject
                {
                    SourceFilePaths = activeFiles,
                    Settings = new FusionSettings
                    {
                        Method = SelectedMethod,
                        FocusMethod = SelectedFocusMethod,
                        AlignmentMode = SelectedAlignmentMode,
                        PyramidLevels = PyramidLevels,
                        SmoothingRadius = SmoothingRadius,
                        EnableQualityAnalysis = EnableQualityAnalysis,
                        EnableMotionSuppression = EnableMotionSuppression,
                        EnableArtifactDetection = EnableArtifactDetection,
                        EnableAutoRepair = EnableAutoRepair,
                        EnableLocalAlignment = EnableLocalAlignment,
                        EnableEdgeReconstruction = EnableEdgeReconstruction,
                        EnableTiledProcessing = EnableTiledProcessing,
                        TileSize = _tileSize
                    },
                    PostProcess = new PostProcessSettings
                    {
                        Exposure = ExposureCompensation,
                        Contrast = ContrastAdjustment,
                        Clarity = ClarityAdjustment,
                        SharpenAmount = SharpeningAdjustment,
                        Saturation = SaturationAdjustment,
                        ToneMapping = SelectedToneMapping
                    }
                };

                await _projectService.SaveProjectAsync(dlg.FileName, project, _lastResult, _retouchLayer);
                StatusMessage = $"Project successfully saved to {Path.GetFileName(dlg.FileName)}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save project:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Save error: {ex.Message}";
            }
        }
    }

    private async Task ExecuteOpenProjectAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open FImageStack Project",
            Filter = "FImageStack Project (*.fstack)|*.fstack",
            DefaultExt = ".fstack"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                StatusMessage = "Loading project cache (Zero-Recomputation)...";
                var loaded = await _projectService.LoadProjectAsync(dlg.FileName);

                // Restore Frames
                Frames.Clear();
                int idx = 0;
                foreach (var path in loaded.Project.SourceFilePaths)
                {
                    if (File.Exists(path))
                    {
                        Frames.Add(new FrameItemViewModel(path, idx++));
                    }
                }

                // Restore Settings
                SelectedMethod = loaded.Project.Settings.Method;
                SelectedFocusMethod = loaded.Project.Settings.FocusMethod;
                SelectedAlignmentMode = loaded.Project.Settings.AlignmentMode;
                PyramidLevels = loaded.Project.Settings.PyramidLevels;
                SmoothingRadius = loaded.Project.Settings.SmoothingRadius;
                EnableQualityAnalysis = loaded.Project.Settings.EnableQualityAnalysis;
                EnableMotionSuppression = loaded.Project.Settings.EnableMotionSuppression;
                EnableArtifactDetection = loaded.Project.Settings.EnableArtifactDetection;
                EnableAutoRepair = loaded.Project.Settings.EnableAutoRepair;
                EnableLocalAlignment = loaded.Project.Settings.EnableLocalAlignment;
                EnableEdgeReconstruction = loaded.Project.Settings.EnableEdgeReconstruction;
                EnableTiledProcessing = loaded.Project.Settings.EnableTiledProcessing;
                TileSize = loaded.Project.Settings.TileSize;

                // Restore Post-Processing
                ExposureCompensation = loaded.Project.PostProcess.Exposure;
                ContrastAdjustment = loaded.Project.PostProcess.Contrast;
                ClarityAdjustment = loaded.Project.PostProcess.Clarity;
                SharpeningAdjustment = loaded.Project.PostProcess.SharpenAmount;
                SaturationAdjustment = loaded.Project.PostProcess.Saturation;
                SelectedToneMapping = loaded.Project.PostProcess.ToneMapping;

                // Restore Cached Results & Retouch
                if (loaded.CachedResult != null)
                {
                    _lastResult?.Dispose();
                    _lastResult = loaded.CachedResult;

                    _retouchLayer?.Dispose();
                    _retouchLayer = loaded.RestoredRetouchLayer ?? new RetouchLayer(loaded.CachedResult.FusedImage.Width, loaded.CachedResult.FusedImage.Height);
                    UpdateRetouchStrokesCount();

                    ApplyLivePostProcessing();

                    TurboDepthBitmap = BitmapHelper.ToTurboColormapBitmap(loaded.CachedResult.DepthResult.DepthMap, loaded.CachedResult.DepthResult.ConfidenceMap);
                    ConfidenceMapBitmap = BitmapHelper.ToBitmapSource(loaded.CachedResult.DepthResult.ConfidenceMap);
                    InvalidRegionBitmap = BitmapHelper.ToInvalidRegionBitmap(loaded.CachedResult.DepthResult.ConfidenceMap);

                    if (loaded.Project.QualityReport != null)
                    {
                        QualityScoreText = $"{loaded.Project.QualityReport.OverallScore:F1}% ({loaded.Project.QualityReport.FocusCoverageRating})";
                        FocusCoverageText = $"{loaded.Project.QualityReport.FocusCoveragePercentage:F1}%";
                        MetricAlignmentScore = loaded.Project.QualityReport.AlignmentScore;
                        MetricFocusCoverageScore = loaded.Project.QualityReport.FocusCoverageScore;
                        MetricGhostingPercent = loaded.Project.QualityReport.GhostingPercent;
                        MetricHaloPercent = loaded.Project.QualityReport.HaloPercent;
                        MetricNoisePercent = loaded.Project.QualityReport.NoisePercent;
                        MetricEdgeQualityScore = loaded.Project.QualityReport.EdgeQualityScore;
                    }

                    SelectedViewTab = 0;
                    UpdateDisplayBitmap();
                }

                StatusMessage = $"Project loaded from {Path.GetFileName(dlg.FileName)} with Zero Recomputation!";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open project:\n{ex.Message}", "Open Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Open error: {ex.Message}";
            }
        }
    }

    private void ExecuteLoadSampleStack(object? param)
    {
        string count = param?.ToString() ?? "50";
        string path = Path.Combine("data", $"test_stack_{count}");
        if (Directory.Exists(path))
        {
            LoadFolder(path);
        }
        else
        {
            MessageBox.Show($"Sample stack '{path}' not found.", "FImageStack", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void LoadFolder(string folderPath)
    {
        Frames.Clear();
        var files = _projectService.DiscoverImageFiles(folderPath);
        for (int i = 0; i < files.Count; i++)
        {
            Frames.Add(new FrameItemViewModel(files[i], i));
        }

        if (Frames.Count > 0)
        {
            SelectedFrame = Frames[0];
            StatusMessage = $"Loaded {Frames.Count} frames from {Path.GetFileName(folderPath)}";
            _ = AnalyzeStackQualityAsync();
        }
        else
        {
            StatusMessage = "No valid image frames found in selected folder.";
        }

        (StartStackingCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task ExecuteStartStackingAsync(ResolutionMode? overrideMode = null)
    {
        if (overrideMode.HasValue)
        {
            SelectedRenderMode = overrideMode.Value;
        }

        ResolutionBadgeText = SelectedRenderMode == ResolutionMode.FastPreview1280
            ? "⚡ FAST PREVIEW (1280px)"
            : "💎 FULL MASTER (100% SENSOR)";

        var activeFiles = Frames.Where(f => f.IsSelected).Select(f => f.FilePath).ToList();
        if (activeFiles.Count < 2)
        {
            MessageBox.Show("Please select at least 2 frames to stack.", "FImageStack", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsProcessing = true;
        _cts = new CancellationTokenSource();

        var settings = new FusionSettings
        {
            Method = SelectedMethod,
            FocusMethod = SelectedFocusMethod,
            AlignmentMode = SelectedAlignmentMode,
            PyramidLevels = PyramidLevels,
            SmoothingRadius = SmoothingRadius,
            EnableDepthSmoothing = true,
            EnableQualityAnalysis = EnableQualityAnalysis,
            EnableMotionSuppression = EnableMotionSuppression,
            EnableArtifactDetection = EnableArtifactDetection,
            EnableAutoRepair = EnableAutoRepair,
            EnableLocalAlignment = EnableLocalAlignment,
            EnableEdgeReconstruction = EnableEdgeReconstruction,
            EnableTiledProcessing = EnableTiledProcessing,
            TileSize = _tileSize,
            RenderMode = SelectedRenderMode,
            PreviewMaxDimension = 1280
        };

        var progress = new Progress<StackProgress>(p =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentStage = p.Stage;
                ProgressPercentage = p.Percentage;
                StatusMessage = p.Details;
            });
        });

        try
        {
            _lastResult?.Dispose();
            _postProcessedBuffer?.Dispose();
            _postProcessedBuffer = null;
            _retouchLayer?.Dispose();

            var result = await _stackService.ProcessStackAsync(activeFiles, settings, progress, _cts.Token);
            _lastResult = result;
            _retouchLayer = new RetouchLayer(result.FusedImage.Width, result.FusedImage.Height);
            UpdateRetouchStrokesCount();

            // Generate Bitmaps
            ApplyLivePostProcessing();

            TurboDepthBitmap = BitmapHelper.ToTurboColormapBitmap(result.DepthResult.DepthMap, result.DepthResult.ConfidenceMap);
            ConfidenceMapBitmap = BitmapHelper.ToBitmapSource(result.DepthResult.ConfidenceMap);
            InvalidRegionBitmap = BitmapHelper.ToInvalidRegionBitmap(result.DepthResult.ConfidenceMap);
            DofThicknessBitmap = BitmapHelper.ToDofThicknessBitmap(result.DepthResult.DofMap, result.DepthResult.ConfidenceMap);
            MotionMapBitmap = BitmapHelper.ToBitmapSource(result.MotionResult?.MotionMap);
            ArtifactMapBitmap = BitmapHelper.ToBitmapSource(result.ArtifactMap?.ArtifactMask);

            // Update Metrics
            var b = result.Benchmark;
            TimingText = $"{b.TotalTimeMs / 1000.0:F2}s (Fusion: {b.FusionTimeMs:F0}ms)";
            MemoryUsageText = $"{b.PeakWorkingSetMb} MB";

            if (result.QualityReport != null)
            {
                QualityScoreText = $"{result.QualityReport.OverallScore:F1}% ({result.QualityReport.FocusCoverageRating})";
                FocusCoverageText = $"{result.QualityReport.FocusCoveragePercentage:F1}% (Gaps: {result.QualityReport.DetectedGaps.Count})";

                MetricAlignmentScore = result.QualityReport.AlignmentScore;
                MetricFocusCoverageScore = result.QualityReport.FocusCoverageScore;
                MetricGhostingPercent = result.QualityReport.GhostingPercent;
                MetricHaloPercent = result.QualityReport.HaloPercent;
                MetricNoisePercent = result.QualityReport.NoisePercent;
                MetricEdgeQualityScore = result.QualityReport.EdgeQualityScore;

                DetectedArtifactRegions.Clear();
                foreach (var a in result.QualityReport.TopArtifacts)
                {
                    DetectedArtifactRegions.Add(new ArtifactRegionViewModel
                    {
                        Id = a.Id,
                        TypeName = a.TypeName,
                        CenterX = a.CenterX,
                        CenterY = a.CenterY,
                        Severity = a.Severity,
                        Description = a.Description
                    });
                }
            }

            if (result.ArtifactMap != null)
            {
                int repaired = result.RepairReport?.RepairedRegionsCount ?? 0;
                ArtifactsReportText = $"Found: {result.ArtifactMap.Regions.Count}, Repaired: {repaired}";
            }

            SelectedViewTab = 0;
            UpdateDisplayBitmap();
            StatusMessage = $"Completed successfully in {b.TotalTimeMs / 1000.0:F2}s!";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Stacking operation was cancelled by user.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show($"Stacking failed:\n{ex.Message}", "FImageStack Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void ApplyLivePostProcessing()
    {
        if (_lastResult == null) return;

        var baseImg = _lastResult.RepairedImage ?? _lastResult.FusedImage;
        _postProcessedBuffer?.Dispose();

        var ppSettings = new PostProcessSettings
        {
            Exposure = ExposureCompensation,
            Contrast = ContrastAdjustment,
            Clarity = ClarityAdjustment,
            SharpenAmount = SharpeningAdjustment,
            Saturation = SaturationAdjustment,
            ToneMapping = SelectedToneMapping
        };

        _postProcessedBuffer = _postProcessEngine.ApplyPostProcessing(baseImg, ppSettings);
        FusedBitmap = BitmapHelper.ToBitmapSource(_postProcessedBuffer);

        // Update live histogram
        var hist = HistogramEngine.Compute(_postProcessedBuffer);
        HistogramBitmap = BitmapHelper.RenderHistogramBitmap(hist);
        ShadowClippingText = $"{hist.ShadowClippingPercent:F1}%";
        HighlightClippingText = $"{hist.HighlightClippingPercent:F1}%";

        if (SelectedViewTab == 0 || SelectedViewTab == 1)
        {
            UpdateDisplayBitmap();
        }
    }

    private void ExecuteCancelStacking()
    {
        _cts?.Cancel();
    }

    private void ExecuteExportResult()
    {
        if (_lastResult == null) return;

        var saveDialog = new SaveFileDialog
        {
            Title = "Export Master Image",
            Filter = "TIFF 16-bit Master (*.tif)|*.tif|TIFF 32-bit Float HDR (*.tif)|*.tif|PNG 16-bit Lossless (*.png)|*.png|JPEG High-Q (*.jpg)|*.jpg",
            FileName = "fused_master.tif"
        };

        if (saveDialog.ShowDialog() == true)
        {
            var img = _postProcessedBuffer ?? _lastResult.RepairedImage ?? _lastResult.FusedImage;
            int bitDepth = saveDialog.FilterIndex switch
            {
                1 => 16,
                2 => 32,
                3 => 16,
                _ => 8
            };
            _imageIO.SaveImage(img, saveDialog.FileName, bitDepth);
            MessageBox.Show($"Exported successfully ({bitDepth}-bit) to:\n{saveDialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void UpdateDisplayBitmap()
    {
        if (SelectedViewTab == 1)
        {
            // Split A/B Comparison: Fused (Left) vs Selected Source Frame (Right)
            if (_postProcessedBuffer != null && SelectedFrame != null && File.Exists(SelectedFrame.FilePath))
            {
                using var srcFrame = _imageIO.LoadFrame(SelectedFrame.FilePath, SelectedFrame.Index);
                DisplayBitmap = BitmapHelper.CreateSplitWipeComposite(_postProcessedBuffer, srcFrame.ColorBuffer, (float)SplitRatio);
            }
            else
            {
                DisplayBitmap = FusedBitmap;
            }
            return;
        }

        DisplayBitmap = SelectedViewTab switch
        {
            0 => FusedBitmap,
            1 => FusedBitmap,
            2 => TurboDepthBitmap,
            3 => ConfidenceMapBitmap,
            4 => InvalidRegionBitmap,
            5 => MotionMapBitmap,
            6 => ArtifactMapBitmap,
            7 => SelectedFrame != null && File.Exists(SelectedFrame.FilePath) ? new BitmapImage(new Uri(SelectedFrame.FilePath, UriKind.Absolute)) : null,
            8 => DofThicknessBitmap,
            10 => VirtualDofBitmap ?? FusedBitmap,
            _ => FusedBitmap
        };
    }
}
