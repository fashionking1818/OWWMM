using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OWWMM.Models;
using OWWMM.Services;

namespace OWWMM;

internal sealed record ModGroupEdit(string FolderName, string? Title, string? Subtitle, byte[]? IconPng);

internal static class ModGroupDialog
{
    public static async Task<ModGroupEdit?> ShowAsync(Window owner, UiStrings text, CharacterEntry? group = null)
    {
        var en = text.IsEnglish;
        var dialog = new Window
        {
            Title = group is null ? text.AddModGroupTitle : text.RenameModGroupTitle,
            Width = 520, Height = 540, MinWidth = 460, MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#151821")
        };
        var folderName = group is null ? "" : Path.GetFileName(group.DirectoryPath);
        if (string.IsNullOrEmpty(folderName)) folderName = group?.FolderName ?? "";
        var folder = new TextBox { Text = folderName };
        var metadata = group is { DirectoryExists: true } ? ModService.ReadGroupMetadata(group.DirectoryPath) : null;
        var title = new TextBox { Text = metadata?.Title
            ?? (metadata is null && group?.Title != folderName ? group?.Title : "") };
        var subtitle = new TextBox { Text = metadata?.Subtitle
            ?? (metadata is null && group?.Subtitle != folderName ? group?.Subtitle : "") };
        void UpdateDefaults()
        {
            title.Watermark = folder.Text;
            subtitle.Watermark = folder.Text;
        }
        folder.TextChanged += (_, _) => UpdateDefaults();
        UpdateDefaults();
        var icon = new Image { Width = 72, Height = 72, Stretch = Stretch.UniformToFill, Source = group?.AvatarImage };
        var chooseIcon = new Button { Content = en ? "Choose icon…" : "选择图标…" };
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
        byte[]? iconPng = null;
        Bitmap? selectedBitmap = null;
        var iconBusy = false;
        chooseIcon.Click += async (_, _) =>
        {
            iconBusy = true;
            chooseIcon.IsEnabled = false;
            try
            {
                var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = text.CategoryIconPickerTitle,
                    FileTypeFilter = [new FilePickerFileType(text.ImageFileType) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"] }]
                });
                if (files.Count == 0) return;
                var service = new IconCropService();
                var selection = await IconCropDialog.ShowAsync(dialog, text, files[0].Path.LocalPath, service);
                if (selection is null) return;
                var temporary = Path.Combine(Path.GetTempPath(), "OWWMM-icon-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    service.SaveCroppedIcon(files[0].Path.LocalPath, temporary, selection.Value);
                    var bytes = File.ReadAllBytes(temporary);
                    using var stream = new MemoryStream(bytes);
                    var bitmap = new Bitmap(stream);
                    icon.Source = bitmap;
                    selectedBitmap?.Dispose();
                    selectedBitmap = bitmap;
                    iconPng = bytes;
                    error.Text = "";
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (Exception ex) { error.Text = text.TranslateError(ex.Message); }
            finally { iconBusy = false; chooseIcon.IsEnabled = true; }
        };
        void Confirm()
        {
            if (iconBusy) return;
            if (string.IsNullOrWhiteSpace(folder.Text))
            {
                error.Text = en ? "Enter a folder name." : "请输入文件夹名。";
                folder.Focus();
                return;
            }
            dialog.Close(new ModGroupEdit(folder.Text.Trim(), title.Text, subtitle.Text, iconPng));
        }
        var save = new Button { Content = group is null ? text.Create : text.SaveName, Classes = { "accent" } };
        save.Click += (_, _) => Confirm();
        var cancel = new Button { Content = text.Cancel };
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { dialog.Close(null); e.Handled = true; }
            else if (e.Key == Key.Enter && e.Source is TextBox) { Confirm(); e.Handled = true; }
        };
        StackPanel Field(string label, TextBox input) => new()
        {
            Spacing = 5, Children = { new TextBlock { Text = label }, input }
        };
        dialog.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 14,
                Children =
                {
                    new TextBlock { Text = dialog.Title, FontSize = 21, FontWeight = FontWeight.SemiBold },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { icon, chooseIcon } },
                    Field(en ? "Folder name" : "文件夹名", folder),
                    Field(en ? "Title" : "标题", title),
                    Field(en ? "Subtitle" : "副标题", subtitle),
                    new TextBlock { Text = en ? "Leave either field empty to follow the folder name." : "标题或副标题留空时自动跟随文件夹名。", Foreground = Brushes.LightSlateGray },
                    error,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { cancel, save } }
                }
            }
        };
        dialog.Opened += (_, _) => folder.Focus();
        try { return await dialog.ShowDialog<ModGroupEdit?>(owner); }
        finally { icon.Source = null; selectedBitmap?.Dispose(); }
    }
}
