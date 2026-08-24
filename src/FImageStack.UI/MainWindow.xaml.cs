using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FImageStack.UI.ViewModels;

namespace FImageStack.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Image_MouseMove(object sender, MouseEventArgs e)
    {
        UpdateInspectorCoordinates(sender, e);
    }

    private void Image_MouseDown(object sender, MouseButtonEventArgs e)
    {
        UpdateInspectorCoordinates(sender, e);
    }

    private void UpdateInspectorCoordinates(object sender, MouseEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Image img && img.Source is BitmapSource bs)
        {
            if (img.ActualWidth <= 0 || img.ActualHeight <= 0) return;

            var pos = e.GetPosition(img);
            int pixelX = (int)(pos.X * (bs.PixelWidth / img.ActualWidth));
            int pixelY = (int)(pos.Y * (bs.PixelHeight / img.ActualHeight));

            vm.InspectPixel(pixelX, pixelY);
        }
    }
}