using System.IO;
using System.Windows.Media.Imaging;
using FImageStack.UI.Common;
using FImageStack.UI.Utils;

namespace FImageStack.UI.ViewModels;

public sealed class FrameItemViewModel : ViewModelBase
{
    private bool _isSelected = true;
    private BitmapImage? _thumbnail;
    private double _sharpnessScore;
    private bool _isExcluded;

    public int Index { get; }
    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? "";

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
