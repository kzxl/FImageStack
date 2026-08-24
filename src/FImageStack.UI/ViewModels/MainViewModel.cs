using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FImageStack.Application.Services;
using FImageStack.Core;
using FImageStack.Core.Models;
using FImageStack.Core.PostProcessing;
using FImageStack.Core.Presets;
using FImageStack.Core.Quality;
using FImageStack.Infrastructure.IO;
using FImageStack.UI.Common;
using FImageStack.UI.Utils;
using Microsoft.Win32;

namespace FImageStack.UI.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IStackService _stackService;
    private readonly IProjectService _projectService;
    private readonly IImageIO _imageIO;
    private readonly IPostProcessEngine _postProcessEngine;

    private CancellationTokenSource? _cts;
    private ProcessedStackResult? _lastResult;
    private ImageBuffer<float>? _postProcessedBuffer;

    private bool _isProcessing;
    private double _progressPercentage;
    private string _currentStage = "Ready";
    private string _statusMessage = "Select a folder or click a quick sample stack to begin.";

    // Active Display Tab: 0=Fused, 1=Depth, 2=Confidence, 3=Motion, 4=Artifacts, 5=Source, 6=Split Comparison
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
    private bool _enableTiledProcessing = false;
    private int _tileSize = 512;

    // Split Comparison
    private double _splitRatio = 0.5;

    // Post-Processing Settings
    private float _exposureCompensation = 0.0f;
    private float _contrastAdjustment = 1.0f;
    private float _clarityAdjustment = 0.0f;
    private float _sharpeningAdjustment = 0.3f;
    private float _saturationAdjustment = 1.0f;

    // Metrics & Histogram
    private string _qualityScoreText = "--";
    private string _focusCoverageText = "--";
    private string _timingText = "--";
    private string _artifactsReportText = "--";
    private string _memoryUsageText = "--";
    private BitmapSource? _histogramBitmap;
    private string _shadowClippingText = "0.0%";
    private string _highlightClippingText = "0.0%";

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
                OnPropertyChanged(nameof(IsDepthTabSelected));
                OnPropertyChanged(nameof(IsConfidenceTabSelected));
                OnPropertyChanged(nameof(IsMotionTabSelected));
                OnPropertyChanged(nameof(IsArtifactTabSelected));
                OnPropertyChanged(nameof(IsSourceTabSelected));
                OnPropertyChanged(nameof(IsSplitTabSelected));
                UpdateDisplayBitmap();
            }
        }
    }

    public bool IsFusedTabSelected
    {
        get => SelectedViewTab == 0;
        set { if (value) SelectedViewTab = 0; }
    }

    public bool IsDepthTabSelected
    {
        get => SelectedViewTab == 1;
        set { if (value) SelectedViewTab = 1; }
    }

    public bool IsConfidenceTabSelected
    {
        get => SelectedViewTab == 2;
        set { if (value) SelectedViewTab = 2; }
    }

    public bool IsMotionTabSelected
    {
        get => SelectedViewTab == 3;
        set { if (value) SelectedViewTab = 3; }
    }

    public bool IsArtifactTabSelected
    {
        get => SelectedViewTab == 4;
        set { if (value) SelectedViewTab = 4; }
    }

    public bool IsSourceTabSelected
    {
        get => SelectedViewTab == 5;
        set { if (value) SelectedViewTab = 5; }
    }

    public bool IsSplitTabSelected
    {
        get => SelectedViewTab == 6;
        set { if (value) SelectedViewTab = 6; }
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
                if (SelectedViewTab == 5 || SelectedViewTab == 6)
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
                if (SelectedViewTab == 6) UpdateDisplayBitmap();
            }
        }
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

    public bool EnableTiledProcessing
    {
        get => _enableTiledProcessing;
        set => SetProperty(ref _enableTiledProcessing, value);
    }

    public int TileSize
    {
        get => _tileSize;
        set => SetProperty(ref _tileSize, value);
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
    public BitmapSource? DepthMapBitmap { get; private set; }
    public BitmapSource? ConfidenceMapBitmap { get; private set; }
    public BitmapSource? MotionMapBitmap { get; private set; }
    public BitmapSource? ArtifactMapBitmap { get; private set; }

    // Commands
    public ICommand LoadFolderCommand { get; }
    public ICommand LoadSampleStackCommand { get; }
    public ICommand StartStackingCommand { get; }
    public ICommand CancelStackingCommand { get; }
    public ICommand ExportResultCommand { get; }
    public ICommand SelectAllFramesCommand { get; }
    public ICommand ResetPostProcessingCommand { get; }

    public MainViewModel()
    {
        _imageIO = new ImageSharpIO();
        _projectService = new ProjectService();
        _stackService = new StackService(_imageIO);
        _postProcessEngine = new StandardPostProcessEngine();

        foreach (var p in StackingPreset.GetBuiltinPresets())
        {
            AvailablePresets.Add(p);
        }
        _selectedPreset = AvailablePresets[0];

        LoadFolderCommand = new RelayCommand(ExecuteLoadFolder);
        LoadSampleStackCommand = new RelayCommand(ExecuteLoadSampleStack);
        StartStackingCommand = new AsyncRelayCommand(ExecuteStartStackingAsync, () => !IsProcessing && Frames.Count >= 2);
        CancelStackingCommand = new RelayCommand(ExecuteCancelStacking, () => IsProcessing);
        ExportResultCommand = new RelayCommand(ExecuteExportResult, () => FusedBitmap != null && !IsProcessing);
        SelectAllFramesCommand = new RelayCommand(_ =>
        {
            bool anyUnchecked = Frames.Any(f => !f.IsSelected);
            foreach (var f in Frames) f.IsSelected = anyUnchecked;
        });

        ResetPostProcessingCommand = new RelayCommand(_ =>
        {
            ExposureCompensation = 0.0f;
            ContrastAdjustment = 1.0f;
            ClarityAdjustment = 0.0f;
            SharpeningAdjustment = 0.3f;
            SaturationAdjustment = 1.0f;
        });

        // Try pre-loading test dataset if available
        string defaultSample = @"data\test_stack_50";
        if (Directory.Exists(defaultSample))
        {
            LoadFolder(defaultSample);
        }
    }

    private void ApplyPreset(StackingPreset preset)
    {
        SelectedMethod = preset.Settings.Method;
        SelectedFocusMethod = preset.Settings.FocusMethod;
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
        }
        else
        {
            StatusMessage = "No valid image frames found in selected folder.";
        }

        (StartStackingCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task ExecuteStartStackingAsync()
    {
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
            EnableTiledProcessing = EnableTiledProcessing,
            TileSize = TileSize
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

            var result = await _stackService.ProcessStackAsync(activeFiles, settings, progress, _cts.Token);
            _lastResult = result;

            // Generate Bitmaps
            var baseImg = result.RepairedImage ?? result.FusedImage;
            ApplyLivePostProcessing();

            DepthMapBitmap = BitmapHelper.ToBitmapSource(result.DepthResult.DepthMap);
            ConfidenceMapBitmap = BitmapHelper.ToBitmapSource(result.DepthResult.ConfidenceMap);
            MotionMapBitmap = BitmapHelper.ToBitmapSource(result.MotionResult?.MotionMap);
            ArtifactMapBitmap = BitmapHelper.ToBitmapSource(result.ArtifactMap?.ArtifactMask);

            // Update Metrics
            var b = result.Benchmark;
            TimingText = $"{b.TotalTimeMs / 1000.0:F2}s (Fusion: {b.FusionTimeMs:F0}ms)";
            MemoryUsageText = $"{b.PeakWorkingSetMb} MB";

            if (result.QualityReport != null)
            {
                QualityScoreText = $"{result.QualityReport.OverallScore:F0}% ({result.QualityReport.FocusCoverageRating})";
                FocusCoverageText = $"{result.QualityReport.FocusCoveragePercentage:F1}% (Gaps: {result.QualityReport.DetectedGaps.Count})";
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
            Saturation = SaturationAdjustment
        };

        _postProcessedBuffer = _postProcessEngine.ApplyPostProcessing(baseImg, ppSettings);
        FusedBitmap = BitmapHelper.ToBitmapSource(_postProcessedBuffer);

        // Update live histogram
        var hist = HistogramEngine.Compute(_postProcessedBuffer);
        HistogramBitmap = BitmapHelper.RenderHistogramBitmap(hist);
        ShadowClippingText = $"{hist.ShadowClippingPercent:F1}%";
        HighlightClippingText = $"{hist.HighlightClippingPercent:F1}%";

        if (SelectedViewTab == 0 || SelectedViewTab == 6)
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
            Title = "Export Fused Image",
            Filter = "PNG Image (*.png)|*.png|TIFF 16-bit (*.tif;*.tiff)|*.tif|JPEG Image (*.jpg)|*.jpg",
            FileName = "fused_output.png"
        };

        if (saveDialog.ShowDialog() == true)
        {
            var img = _postProcessedBuffer ?? _lastResult.RepairedImage ?? _lastResult.FusedImage;
            int bitDepth = saveDialog.FileName.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || saveDialog.FileName.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) ? 16 : 8;
            _imageIO.SaveImage(img, saveDialog.FileName, bitDepth);
            MessageBox.Show($"Exported successfully to:\n{saveDialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void UpdateDisplayBitmap()
    {
        if (SelectedViewTab == 6)
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
            1 => DepthMapBitmap,
            2 => ConfidenceMapBitmap,
            3 => MotionMapBitmap,
            4 => ArtifactMapBitmap,
            5 => SelectedFrame != null && File.Exists(SelectedFrame.FilePath) ? new BitmapImage(new Uri(SelectedFrame.FilePath, UriKind.Absolute)) : null,
            _ => FusedBitmap
        };
    }
}
