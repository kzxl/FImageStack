using System.IO;
using System.Windows.Media.Imaging;
using FImageStack.UI.Common;
using FImageStack.UI.Utils;

namespace FImageStack.UI.ViewModels;

public sealed class FrameItemViewModel : ViewModelBase
{
    private bool _isSelected = true;
    private BitmapImage? _thumbnail;
    private double _sharpnessScore = 100.0;
    private bool _isExcluded;
    private float _priorityWeight = 1.0f;
    private bool _isBadFrame;
    private bool _isDuplicate;
    private string _qualityBadge = "✅ OK";
    private string _qualityTooltip = "Good quality frame";

    public int Index { get; }
    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? "";

    public float PriorityWeight
    {
        get => _priorityWeight;
        set => SetProperty(ref _priorityWeight, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsExcluded
    {
        get => _isExcluded;
        set => SetProperty(ref _isExcluded, value);
    }

    public double SharpnessScore
    {
        get => _sharpnessScore;
        set => SetProperty(ref _sharpnessScore, value);
    }

    public bool IsBadFrame
    {
        get => _isBadFrame;
        set
        {
            if (SetProperty(ref _isBadFrame, value))
            {
                OnPropertyChanged(nameof(StatusBorderColorHex));
                OnPropertyChanged(nameof(StatusBackgroundColorHex));
            }
        }
    }

    public bool IsDuplicate
    {
        get => _isDuplicate;
        set
        {
            if (SetProperty(ref _isDuplicate, value))
            {
                OnPropertyChanged(nameof(StatusBorderColorHex));
                OnPropertyChanged(nameof(StatusBackgroundColorHex));
            }
        }
    }

    public string QualityBadge
    {
        get => _qualityBadge;
        set => SetProperty(ref _qualityBadge, value);
    }

    public string QualityTooltip
    {
        get => _qualityTooltip;
        set => SetProperty(ref _qualityTooltip, value);
    }

    public string StatusBorderColorHex => IsBadFrame ? "#EF4444" : (IsDuplicate ? "#F59E0B" : "Transparent");
    public string StatusBackgroundColorHex => IsBadFrame ? "#261318" : (IsDuplicate ? "#262013" : "Transparent");

    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnail == null && File.Exists(FilePath))
            {
                try
                {
                    _thumbnail = BitmapHelper.LoadThumbnail(FilePath, 100);
                }
                catch { }
            }
            return _thumbnail;
        }
    }

    public FrameItemViewModel(string filePath, int index)
    {
        FilePath = filePath;
        Index = index;
    }
}
