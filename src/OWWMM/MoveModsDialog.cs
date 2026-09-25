using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using OWWMM.Models;
using OWWMM.Services;

namespace OWWMM;

internal static class MoveModsDialog
{
    private sealed record Destination(CharacterEntry Group)
    {
        public override string ToString() => $"{Group.DisplayName}  ({Path.GetFileName(Group.DirectoryPath)})";
    }

    public static async Task<CharacterEntry?> ShowAsync(Window owner, UiStrings text,
        int count, IReadOnlyList<CharacterEntry> groups)
    {
        var dialog = new Window
        {
            Title = text.MoveSelected, Width = 490, Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#151821")
        };
        var destinations = groups.Select(group => new Destination(group)).ToList();
        var picker = new ComboBox { ItemsSource = destinations, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var move = new Button { Content = text.MoveSelected, Classes = { "accent" } };
        var cancel = new Button { Content = text.Cancel };
        void Confirm() { if (picker.SelectedItem is Destination item) dialog.Close(item.Group); }
        move.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !picker.IsDropDownOpen) { Confirm(); e.Handled = true; }
            if (e.Key == Key.Escape) { dialog.Close(null); e.Handled = true; }
        };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 20,
            Children =
            {
                new TextBlock { Text = text.IsEnglish ? $"Move {count} Mod(s) to a group" : $"将 {count} 个 Mod 移动到分组", FontSize = 20 },
                picker,
                new TextBlock { Text = text.IsEnglish ? "Choose any group. Mod contents and enabled state are preserved." : "可自由选择分组，保留 Mod 内容和启停状态。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { cancel, move } }
            }
        };
        return await dialog.ShowDialog<CharacterEntry?>(owner);
    }
}
