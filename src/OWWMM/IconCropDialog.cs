using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OWWMM.Services;

namespace OWWMM;

internal static class IconCropDialog
{
    private const double ViewportSize = 340;

    public static async Task<IconCropSelection?> ShowAsync(
        Window owner,
        UiStrings text,
        string sourcePath,
        IconCropService service)
    {
        var sourceInfo = service.InspectSource(sourcePath);
        var preview = service.LoadPreview(sourcePath, sourceInfo);
        var dialog = new Window
        {
            Title = text.CropIconTitle,
            Width = 460,
            Height = 610,
            MinWidth = 420,
            MinHeight = 560,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#151821")
        };

        var fitScale = Math.Min(ViewportSize / sourceInfo.PixelWidth, ViewportSize / sourceInfo.PixelHeight);
        var imageWidth = sourceInfo.PixelWidth * fitScale;
        var imageHeight = sourceInfo.PixelHeight * fitScale;
        var imageLeft = (ViewportSize - imageWidth) / 2;
        var imageTop = (ViewportSize - imageHeight) / 2;
        var maximumCropSize = Math.Min(imageWidth, imageHeight);
        var cropSize = maximumCropSize * 0.85;
        var cropLeft = imageLeft + (imageWidth - cropSize) / 2;
        var cropTop = imageTop + (imageHeight - cropSize) / 2;

        var canvas = new Canvas
        {
            Width = ViewportSize,
            Height = ViewportSize,
            ClipToBounds = true,
            Background = Brush.Parse("#090B10")
        };
        var image = new Image
        {
            Source = preview,
            Width = imageWidth,
            Height = imageHeight,
            Stretch = Stretch.Fill
        };
        Canvas.SetLeft(image, imageLeft);
        Canvas.SetTop(image, imageTop);

        var shade = new Border
        {
            Width = cropSize,
            Height = cropSize,
            BorderBrush = Brush.Parse("#A99CFF"),
            BorderThickness = new Thickness(3),
            Background = Brush.Parse("#12000000"),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            CornerRadius = new CornerRadius(4)
        };
        Canvas.SetLeft(shade, cropLeft);
        Canvas.SetTop(shade, cropTop);
        canvas.Children.Add(image);
        canvas.Children.Add(shade);

        var slider = new Slider
        {
            Minimum = 0.15,
            Maximum = 1,
            Value = 0.85,
            TickFrequency = 0.05,
            Width = 260,
            VerticalAlignment = VerticalAlignment.Center
        };

        void UpdateCropSize(double ratio)
        {
            var centerX = cropLeft + cropSize / 2;
            var centerY = cropTop + cropSize / 2;
            cropSize = maximumCropSize * Math.Clamp(ratio, slider.Minimum, slider.Maximum);
            cropLeft = Math.Clamp(centerX - cropSize / 2, imageLeft, imageLeft + imageWidth - cropSize);
            cropTop = Math.Clamp(centerY - cropSize / 2, imageTop, imageTop + imageHeight - cropSize);
            shade.Width = cropSize;
            shade.Height = cropSize;
            Canvas.SetLeft(shade, cropLeft);
            Canvas.SetTop(shade, cropTop);
        }

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty) UpdateCropSize(slider.Value);
        };

        var dragging = false;
        Point lastPoint = default;
        shade.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            dragging = true;
            lastPoint = e.GetPosition(canvas);
            e.Pointer.Capture(canvas);
            e.Handled = true;
        };
        canvas.PointerMoved += (_, e) =>
        {
            if (!dragging || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            var point = e.GetPosition(canvas);
            cropLeft = Math.Clamp(cropLeft + point.X - lastPoint.X, imageLeft, imageLeft + imageWidth - cropSize);
            cropTop = Math.Clamp(cropTop + point.Y - lastPoint.Y, imageTop, imageTop + imageHeight - cropSize);
            lastPoint = point;
            Canvas.SetLeft(shade, cropLeft);
            Canvas.SetTop(shade, cropTop);
        };
        canvas.PointerReleased += (_, e) =>
        {
            dragging = false;
            e.Pointer.Capture(null);
        };
        canvas.PointerWheelChanged += (_, e) =>
        {
            slider.Value = Math.Clamp(slider.Value + e.Delta.Y * 0.05, slider.Minimum, slider.Maximum);
            e.Handled = true;
        };

        var cancel = new Button { Content = text.Cancel, MinWidth = 90 };
        var save = new Button { Content = text.SaveIcon, MinWidth = 100, Classes = { "accent" } };
        cancel.Click += (_, _) => dialog.Close(null);
        save.Click += (_, _) => dialog.Close(new IconCropSelection(
            (cropLeft - imageLeft) / imageWidth,
            (cropTop - imageTop) / imageHeight,
            cropSize / imageWidth,
            cropSize / imageHeight));

        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 12,
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = text.CropIconTitle, FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    [Grid.RowProperty] = 1,
                    Text = text.CropIconMessage,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.LightSlateGray
                },
                new Border
                {
                    [Grid.RowProperty] = 2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    CornerRadius = new CornerRadius(10),
                    ClipToBounds = true,
                    Child = canvas
                },
                new StackPanel
                {
                    [Grid.RowProperty] = 3,
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "−", FontSize = 18, VerticalAlignment = VerticalAlignment.Center },
                        slider,
                        new TextBlock { Text = "+", FontSize = 18, VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                new StackPanel
                {
                    [Grid.RowProperty] = 4,
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, save }
                }
            }
        };
        dialog.Closed += (_, _) => preview.Dispose();
        return await dialog.ShowDialog<IconCropSelection?>(owner);
    }
}
