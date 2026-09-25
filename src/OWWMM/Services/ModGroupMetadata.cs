using System.Text.Json;
using OWWMM.Models;

namespace OWWMM.Services;

public sealed record ModGroupMetadata(string? Title = null, string? Subtitle = null);

public sealed partial class ModService
{
    public const string GroupMetadataFileName = ".OWWMM.group.json";
    public static ModGroupMetadata? ReadGroupMetadata(string directory)
    {
        var path = Path.Combine(directory, GroupMetadataFileName);
        if (!File.Exists(path)) return null;
        EnsureNotReparsePoint(directory, "Mod 分组目录");
        EnsureNotReparsePoint(path, "Mod 分组资料");
        return JsonSerializer.Deserialize<ModGroupMetadata>(File.ReadAllText(path));
    }

    private static CharacterEntry ApplyGroupMetadata(CharacterEntry entry, ModGroupMetadata? metadata)
    {
        if (metadata is null) return entry;
        var folder = Path.GetFileName(entry.DirectoryPath);
        return entry with
        {
            FolderName = folder,
            Title = string.IsNullOrWhiteSpace(metadata.Title) ? folder : metadata.Title,
            SubtitleOverride = string.IsNullOrWhiteSpace(metadata.Subtitle) ? folder : metadata.Subtitle
        };
    }

    private static void WriteJsonAtomically<T>(string path, T value)
    {
        if (File.Exists(path)) EnsureNotReparsePoint(path, "Mod 分组资料");
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // A failed edit restores both the old directory name and the old metadata.
    public string SaveModGroup(string root, CharacterEntry? group, string folderName,
        string? title, string? subtitle, byte[]? iconPng = null)
    {
        folderName = folderName.Trim();
        ValidateImmediateFolderName(folderName, "Mod 分组文件夹名称");
        if (IsIgnoredModDirectory(folderName)) throw new IOException("该名称由管理器保留。");
        var existing = group is not null && Directory.Exists(group.DirectoryPath);
        var originalPath = existing ? Path.GetFullPath(group!.DirectoryPath) : null;
        if (group is { IsUngroupedGroup: true } or { IsPathOccupiedByRootMod: true })
            throw new IOException("此条目不能作为分组编辑。");
        if (existing) ValidateEditableGroup(root, group!);
        var path = existing ? RenameModGroup(root, group!, folderName) : CreateModGroup(root, folderName);
        var metadataPath = Path.Combine(path, GroupMetadataFileName);
        byte[]? previous = null;
        var metadataWritten = false;
        try
        {
            if (File.Exists(metadataPath))
            {
                EnsureNotReparsePoint(metadataPath, "Mod 分组资料");
                previous = File.ReadAllBytes(metadataPath);
            }
            WriteJsonAtomically(metadataPath, new ModGroupMetadata(
                string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                string.IsNullOrWhiteSpace(subtitle) ? null : subtitle.Trim()));
            metadataWritten = true;
            if (iconPng is not null)
            {
                var iconPath = GetCategoryIconDestination(path);
                if (File.Exists(iconPath)) EnsureNotReparsePoint(iconPath, "Mod 分组图标");
                var temporary = iconPath + ".tmp-" + Guid.NewGuid().ToString("N");
                try { File.WriteAllBytes(temporary, iconPng); File.Move(temporary, iconPath, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return path;
        }
        catch
        {
            if (metadataWritten)
            {
                if (previous is null) File.Delete(metadataPath);
                else File.WriteAllBytes(metadataPath, previous);
            }
            if (existing && originalPath != path) Directory.Move(path, originalPath!);
            else if (!existing && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
            throw;
        }
    }
}
