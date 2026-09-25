using Avalonia.Media.Imaging;

namespace OWWMM.Models;

public sealed record CharacterEntry(
    string Title,
    string FolderName,
    string DirectoryPath,
    bool DirectoryExists,
    bool IsKnownCharacter = true,
    int ModCount = 0,
    string? IconPath = null,
    bool IsUngroupedGroup = false,
    bool IsPathOccupiedByRootMod = false)
{
    public string Name => Title;
    public string? SubtitleOverride { get; init; }
    public string Subtitle => SubtitleOverride ?? FolderName;
    public bool UseEnglishAvailability { get; set; }
    public string DisplayName => Title;
    public bool ShowSubtitle => !string.IsNullOrWhiteSpace(Subtitle)
        && !Title.Equals(Subtitle, StringComparison.OrdinalIgnoreCase);
    public string AvailabilityText => IsPathOccupiedByRootMod
        ? (UseEnglishAvailability ? "Folder used by a root Mod" : "目录已被根目录 Mod 占用")
        : UseEnglishAvailability
            ? (ModCount > 0 ? $"{ModCount} Mods" : "No local Mods")
            : (ModCount > 0 ? $"{ModCount} 个 Mod" : "暂无本地 Mod");
    public string AvatarFallbackText => string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName[..1];
    public string IconTooltip => IsUngroupedGroup || IsPathOccupiedByRootMod
        ? (UseEnglishAvailability ? "This entry has no editable group icon" : "此条目不能设置分组图标")
        : (UseEnglishAvailability ? "Change group icon" : "点击更换分组图标");
    public string? AvatarSource => IconPath;
    public Bitmap? AvatarImage { get; set; }

    public override string ToString() => Title;

}
