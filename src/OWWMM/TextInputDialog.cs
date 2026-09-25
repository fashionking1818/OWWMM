using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace OWWMM;

internal static class TextInputDialog
{
    public static async Task<string?> ShowAsync(
        Window owner,
        string title,
        string message,
        string watermark,
        string confirmText,
        string cancelText,
        string? initialText = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 470,
            Height = 250,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#151821")
        };
        var input = new TextBox { Watermark = watermark, Text = initialText ?? string.Empty };
        var cancel = new Button { Content = cancelText, MinWidth = 90 };
        var confirm = new Button { Content = confirmText, MinWidth = 90, Classes = { "accent" } };
        cancel.Click += (_, _) => dialog.Close(null);
        confirm.Click += (_, _) => dialog.Close(input.Text?.Trim());
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            dialog.Close(input.Text?.Trim());
        };

        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 12,
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    [Grid.RowProperty] = 1,
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.LightSlateGray
                },
                new Border { [Grid.RowProperty] = 2, Child = input },
                new StackPanel
                {
                    [Grid.RowProperty] = 3,
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, confirm }
                }
            }
        };
        dialog.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        return await dialog.ShowDialog<string?>(owner);
    }
}
