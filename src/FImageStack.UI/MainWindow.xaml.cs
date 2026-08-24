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
        HandleImageInteraction(sender, e, isClick: false);
    }

    private void Image_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            HandleImageInteraction(sender, e, isClick: true);
        }
    }

    private void HandleImageInteraction(object sender, MouseEventArgs e, bool isClick)
    {
        if (DataContext is MainViewModel vm && sender is Image img && img.Source is BitmapSource bs)
        {
            if (img.ActualWidth <= 0 || img.ActualHeight <= 0) return;

            var pos = e.GetPosition(img);
            float pixelX = (float)(pos.X * (bs.PixelWidth / img.ActualWidth));
            float pixelY = (float)(pos.Y * (bs.PixelHeight / img.ActualHeight));

            // Inspector HUD coordinates
            vm.InspectPixel((int)pixelX, (int)pixelY);

            // Manual Focus Override Painting
            if (vm.IsRetouchModeActive && (isClick || e.LeftButton == MouseButtonState.Pressed))
            {
                vm.ApplyBrushStroke(pixelX, pixelY);
            }
        }
    }
}