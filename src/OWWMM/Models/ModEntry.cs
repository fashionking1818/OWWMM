using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OWWMM.Models;

public sealed class ModEntry : INotifyPropertyChanged
{
    private string _directoryPath;
    private string _folderName;
    private string _displayName;
    private bool _isEnabled;
    private int _previewCount;

    public ModEntry(string directoryPath, string folderName, string displayName, bool isEnabled, int previewCount)
    {
        _directoryPath = directoryPath;
        _folderName = folderName;
        _displayName = displayName;
        _isEnabled = isEnabled;
        _previewCount = previewCount;
    }

    public string DirectoryPath { get => _directoryPath; private set => SetField(ref _directoryPath, value); }
    public string FolderName { get => _folderName; private set => SetField(ref _folderName, value); }
    public string DisplayName { get => _displayName; private set => SetField(ref _displayName, value); }
    public bool IsEnabled { get => _isEnabled; set => SetField(ref _isEnabled, value); }
    public int PreviewCount { get => _previewCount; set => SetField(ref _previewCount, value); }
    public string StateText => IsEnabled ? "已激活" : "已停用";
    public string PreviewText => PreviewCount == 0 ? "无预览图" : $"{PreviewCount} 张预览图";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplyRename(string directoryPath, string folderName, string displayName, bool isEnabled)
    {
        DirectoryPath = directoryPath;
        FolderName = folderName;
        DisplayName = displayName;
        IsEnabled = isEnabled;
        OnPropertyChanged(nameof(StateText));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsEnabled)) OnPropertyChanged(nameof(StateText));
        if (propertyName == nameof(PreviewCount)) OnPropertyChanged(nameof(PreviewText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
