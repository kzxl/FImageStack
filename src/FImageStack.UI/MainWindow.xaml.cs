using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FImageStack.UI.ViewModels;

namespace FImageStack.UI;

public partial class MainWindow : Window
{
    private Point _lastPanPoint;
    private bool _isPanning;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // Auto fit on initial load or image changed
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.DisplayBitmap) && vm.ZoomScale == 1.0)
                {
                    Dispatcher.BeginInvoke(new Action(FitImageToViewport));
                }
            };
        }
    }

    private void ImageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm)
            {
                double zoomFactor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
                vm.ZoomScale = Math.Clamp(vm.ZoomScale * zoomFactor, 0.1, 10.0);
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Zoom error: {ex.Message}");
        }
    }

    private void ImageScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm)
            {
                // Pan with Middle Mouse Button, Right Mouse Button, or Left Button when not in retouch mode
                if (e.MiddleButton == MouseButtonState.Pressed ||
                    e.RightButton == MouseButtonState.Pressed ||
                    (e.LeftButton == MouseButtonState.Pressed && !vm.IsRetouchModeActive && (Keyboard.IsKeyDown(Key.Space) || vm.ZoomScale > 1.0)))
                {
                    _isPanning = true;
                    _lastPanPoint = e.GetPosition(ImageScrollViewer);
                    ImageScrollViewer.CaptureMouse();
                    Cursor = Cursors.SizeAll;
                    e.Handled = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MouseDown pan error: {ex.Message}");
        }
    }

    private void ImageScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            if (_isPanning)
            {
                Point currentPoint = e.GetPosition(ImageScrollViewer);
                double deltaX = _lastPanPoint.X - currentPoint.X;
                double deltaY = _lastPanPoint.Y - currentPoint.Y;

                ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset + deltaX);
                ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset + deltaY);

                _lastPanPoint = currentPoint;
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MouseMove pan error: {ex.Message}");
        }
    }

    private void ImageScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_isPanning)
            {
                _isPanning = false;
                ImageScrollViewer.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MouseUp pan error: {ex.Message}");
        }
    }

    private void FitImageToViewport()
    {
        try
        {
            if (DataContext is MainViewModel vm && MainDisplayImage.Source is BitmapSource bs)
            {
                double viewW = ImageScrollViewer.ActualWidth - 40;
                double viewH = ImageScrollViewer.ActualHeight - 40;
                if (viewW > 50 && viewH > 50 && bs.PixelWidth > 0 && bs.PixelHeight > 0)
                {
                    double scaleX = viewW / bs.PixelWidth;
                    double scaleY = viewH / bs.PixelHeight;
                    vm.ZoomScale = Math.Clamp(Math.Min(scaleX, scaleY), 0.1, 5.0);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fit viewport error: {ex.Message}");
        }
    }

    private void Image_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            HandleImageInteraction(sender, e, isClick: false);
        }
    }

    private void Image_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning && e.LeftButton == MouseButtonState.Pressed)
        {
            HandleImageInteraction(sender, e, isClick: true);
        }
    }

    private void HandleImageInteraction(object sender, MouseEventArgs e, bool isClick)
    {
        try
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Interaction error: {ex.Message}");
        }
    }
}