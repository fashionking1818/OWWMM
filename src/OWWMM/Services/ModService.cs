using OWWMM.Models;

namespace OWWMM.Services;

public sealed partial class ModService
{
    private const string DisabledPrefix = "DISABLED ";
    private const string ImportStagingPrefix = ".OWWMM-import-";
    public const string CategoryIconFileName = ".OWWMM.icon.png";
    public const long MaximumPreviewImageBytes = 100L * 1024 * 1024;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    };

    public static string? FindRelativeCharactersRoot(IEnumerable<string> startPaths, int maximumAncestors = 10)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var start in startPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(comparison))
        {
            DirectoryInfo? current;
            try { current = new DirectoryInfo(Path.GetFullPath(start)); }
            catch { continue; }

            for (var i = 0; current is not null && i < maximumAncestors; i++, current = current.Parent)
            {
                var candidates = new[]
                {
                    current.Name.Equals("Mods", StringComparison.OrdinalIgnoreCase)
                        ? current.FullName
                        : string.Empty,
                    Path.Combine(current.FullName, "WWMI", "Mods"),
                    Path.Combine(current.FullName, "Mods")
                };
                var match = candidates.FirstOrDefault(Directory.Exists);
                if (!string.IsNullOrEmpty(match)) return Path.GetFullPath(match);
            }
        }
        return null;
    }

    public string ResolveCharactersRoot(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath)) throw new ArgumentException("目录不能为空。", nameof(selectedPath));
        var fullPath = Path.GetFullPath(selectedPath);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"目录不存在：{fullPath}");

        if (!Path.GetFileName(fullPath).Equals("Mods", StringComparison.OrdinalIgnoreCase))
            throw new IOException("请选择 WWMI\\Mods 文件夹，而不是其它目录。");
        return fullPath;
    }

    public bool IsFlatCharactersRoot(string charactersRoot) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(charactersRoot)))
            .Equals("Mods", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<CharacterEntry> ScanCharacters(string charactersRoot)
    {
        if (!Directory.Exists(charactersRoot)) throw new DirectoryNotFoundException($"Mods 目录不存在：{charactersRoot}");
        if (!IsFlatCharactersRoot(charactersRoot))
            throw new IOException("当前版本只使用 WWMI\\Mods 一级分组结构。");
        var allDirectories = Directory.EnumerateDirectories(charactersRoot)
            .Select(path => new DirectoryInfo(path))
            .Where(info => !IsIgnoredModDirectory(info.Name))
            .GroupBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var directMods = allDirectories.Where(ContainsTopLevelModIni).ToList();
        var result = allDirectories
            .Except(directMods)
            .Select(info => ApplyGroupMetadata(new CharacterEntry(
                info.Name,
                info.Name,
                info.FullName,
                true,
                false,
                CountModDirectories(info.FullName),
                GetCustomCategoryIconPath(info.FullName)), ReadGroupMetadata(info.FullName)))
            .ToList();
        if (directMods.Count > 0)
        {
            result.Add(new CharacterEntry(
                "未分类 Mod",
                "Ungrouped Mods",
                Path.GetFullPath(charactersRoot),
                true,
                false,
                directMods.Count,
                IsUngroupedGroup: true));
        }
        return result
            .OrderBy(entry => entry.Subtitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CountModDirectories(string? characterDirectory)
    {
        if (string.IsNullOrWhiteSpace(characterDirectory) || !Directory.Exists(characterDirectory)) return 0;

        return Directory.EnumerateDirectories(characterDirectory)
            .Count(path => !IsIgnoredModDirectory(Path.GetFileName(path)));
    }

    public IReadOnlyList<ModEntry> ScanMods(string characterPath, bool directModsOnly = false) =>
        !Directory.Exists(characterPath)
            ? Array.Empty<ModEntry>()
            : Directory.EnumerateDirectories(characterPath)
            .Select(path => new DirectoryInfo(path))
            .Where(info => !IsIgnoredModDirectory(info.Name))
            .Where(info => !directModsOnly || ContainsTopLevelModIni(info))
            .OrderBy(info => GetDisplayName(info.Name), StringComparer.CurrentCultureIgnoreCase)
            .Select(info => new ModEntry(
                info.FullName,
                info.Name,
                GetDisplayName(info.Name),
                !info.Name.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase),
                0))
            .ToList();

    public string? GetModsRoot(string charactersRoot)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(charactersRoot));
        return Path.GetFileName(fullPath).Equals("Mods", StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }

    public string CreateModGroup(string charactersRoot, string requestedName)
    {
        var modsRoot = GetModsRoot(charactersRoot)
            ?? throw new IOException("只能在 WWMI\\Mods 目录中创建 Mod 分类。");
        EnsureNotReparsePoint(modsRoot, "Mods 根目录");
        var name = requestedName.Trim();
        ValidateImmediateFolderName(name, "Mod 分类名称");
        var path = Path.Combine(modsRoot, name);
        if (Directory.Exists(path) || File.Exists(path))
            throw new IOException($"同名 Mod 分类已存在：{name}");
        if (Directory.EnumerateDirectories(modsRoot)
            .Select(path => new DirectoryInfo(path))
            .Any(directory => ContainsTopLevelModIni(directory)
                              && GetDisplayName(directory.Name).Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new IOException($"该名称已被根目录 Mod 占用：{name}");
        Directory.CreateDirectory(path);
        return path;
    }

    public string RenameModGroup(string charactersRoot, CharacterEntry group, string requestedName)
    {
        ArgumentNullException.ThrowIfNull(group);
        var root = ValidateEditableGroup(charactersRoot, group);
        var name = requestedName.Trim();
        ValidateImmediateFolderName(name, "Mod 分组名称");
        var current = Path.GetFullPath(group.DirectoryPath);
        var target = Path.Combine(root, name);
        if (current.Equals(target, StringComparison.Ordinal)) return current;
        if (OperatingSystem.IsWindows() && current.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            var temporary = Path.Combine(root, ImportStagingPrefix + Guid.NewGuid().ToString("N"));
            Directory.Move(current, temporary);
            try { Directory.Move(temporary, target); }
            catch { Directory.Move(temporary, current); throw; }
            return target;
        }
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException($"同名 Mod 分组已存在：{name}");
        if (Directory.EnumerateDirectories(root)
            .Select(path => new DirectoryInfo(path))
            .Any(directory => ContainsTopLevelModIni(directory)
                              && GetDisplayName(directory.Name).Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new IOException($"该名称已被根目录 Mod 占用：{name}");
        Directory.Move(current, target);
        return target;
    }

    public void DeleteEmptyModGroup(string charactersRoot, CharacterEntry group)
    {
        ValidateEditableGroup(charactersRoot, group);
        var path = Path.GetFullPath(group.DirectoryPath);
        if (Directory.EnumerateDirectories(path).Any())
            throw new IOException("只能删除不包含 Mod 或子目录的空分组。");
        var files = Directory.EnumerateFiles(path).ToList();
        if (files.Any(file => !Path.GetFileName(file).Equals(CategoryIconFileName, StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(file).Equals(GroupMetadataFileName, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("该分组仍包含文件，未执行删除。");
        foreach (var file in files) File.Delete(file);
        Directory.Delete(path);
    }

    public bool ResetCategoryIcon(string categoryDirectory)
    {
        var directory = Path.GetFullPath(categoryDirectory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Mod 分组目录不存在：{directory}");
        EnsureNotReparsePoint(directory, "Mod 分组目录");
        var icon = Path.Combine(directory, CategoryIconFileName);
        if (!File.Exists(icon)) return false;
        EnsureNotReparsePoint(icon, "Mod 分组图标");
        File.Delete(icon);
        return true;
    }

    private static string ValidateEditableGroup(string charactersRoot, CharacterEntry group)
    {
        if (group.IsUngroupedGroup || group.IsPathOccupiedByRootMod)
            throw new InvalidOperationException("虚拟条目或根目录 Mod 不能作为分组编辑。");
        var root = Path.GetFullPath(charactersRoot);
        if (!Path.GetFileName(root).Equals("Mods", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(root))
            throw new IOException("只能管理 WWMI\\Mods 下的 Mod 分组。");
        EnsureNotReparsePoint(root, "Mods 根目录");
        var groupPath = Path.GetFullPath(group.DirectoryPath);
        var parent = Directory.GetParent(groupPath)?.FullName;
        if (parent is null || !Path.GetFullPath(parent).Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安全检查失败：只能管理 Mods 根目录的直属分组。");
        if (!Directory.Exists(groupPath)) throw new DirectoryNotFoundException($"Mod 分组目录不存在：{groupPath}");
        EnsureNotReparsePoint(groupPath, "Mod 分组目录");
        if (ContainsTopLevelModIni(new DirectoryInfo(groupPath)))
            throw new InvalidOperationException("根目录 Mod 不能作为普通分组管理。");
        return root;
    }

    public string GetCategoryIconDestination(string categoryDirectory)
    {
        var directory = Path.GetFullPath(categoryDirectory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Mod 分类目录不存在：{directory}");
        EnsureNotReparsePoint(directory, "Mod 分类目录");
        return Path.Combine(directory, CategoryIconFileName);
    }

    public static string? GetCustomCategoryIconPath(string? categoryDirectory)
    {
        if (string.IsNullOrWhiteSpace(categoryDirectory)) return null;
        try
        {
            var info = new FileInfo(Path.Combine(categoryDirectory, CategoryIconFileName));
            if (info.Exists && info.Length is > 0 and <= IconCropService.MaximumSourceBytes
                && (info.Attributes & FileAttributes.ReparsePoint) == 0)
                return info.FullName;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void ValidateImmediateFolderName(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || Path.IsPathRooted(name)
            || Path.GetFileName(name) != name
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.EndsWith(' ')
            || name.EndsWith('.'))
            throw new IOException($"{description}无效：{name}");
    }

    private static bool IsIgnoredModDirectory(string name) =>
        name.Equals("Preview", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(ImportStagingPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsTopLevelModIni(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Any(file => file.Extension.Equals(".ini", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public string SetEnabled(ModEntry mod, bool enabled)
    {
        var current = new DirectoryInfo(mod.DirectoryPath);
        if (!current.Exists) throw new DirectoryNotFoundException($"Mod 目录不存在：{current.FullName}");
        var currentlyEnabled = !current.Name.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase);
        if (currentlyEnabled == enabled) return current.FullName;

        var baseName = GetDisplayName(current.Name);
        if (string.IsNullOrWhiteSpace(baseName)) throw new IOException("无法从停用目录名恢复 Mod 名称。请先手动重命名该目录。");
        var targetName = enabled ? baseName : DisabledPrefix + baseName;
        var targetPath = Path.Combine(current.Parent!.FullName, targetName);
        if (Directory.Exists(targetPath) || File.Exists(targetPath))
            throw new IOException($"无法切换：目标名称已存在：{targetName}");

        Directory.Move(current.FullName, targetPath);
        mod.ApplyRename(targetPath, targetName, baseName, enabled);
        return targetPath;
    }

    public string RenameMod(ModEntry mod, string requestedDisplayName)
    {
        var current = new DirectoryInfo(mod.DirectoryPath);
        if (!current.Exists) throw new DirectoryNotFoundException($"Mod 目录不存在：{current.FullName}");
        var displayName = requestedDisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) throw new IOException("Mod 名称不能为空。");
        if (displayName is "." or ".." || displayName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("Mod 名称包含系统不允许的字符。");
        if (displayName.EndsWith(' ') || displayName.EndsWith('.'))
            throw new IOException("Mod 名称不能以空格或句点结尾。");
        if (displayName.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase))
            throw new IOException("名称中无需填写 DISABLED；管理器会按当前激活状态自动保留该前缀。");

        var isEnabled = !current.Name.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase);
        var targetName = isEnabled ? displayName : DisabledPrefix + displayName;
        var targetPath = Path.Combine(current.Parent!.FullName, targetName);
        if (string.Equals(current.FullName, targetPath, StringComparison.Ordinal)) return current.FullName;
        if (Directory.Exists(targetPath) || File.Exists(targetPath))
            throw new IOException($"无法重命名：目标名称已存在：{targetName}");

        Directory.Move(current.FullName, targetPath);
        mod.ApplyRename(targetPath, targetName, displayName, isEnabled);
        return targetPath;
    }

    public void DeleteMod(ModEntry mod, string characterPath)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (string.IsNullOrWhiteSpace(characterPath))
            throw new ArgumentException("角色目录不能为空。", nameof(characterPath));

        var characterFullPath = Path.GetFullPath(characterPath);
        if (!Directory.Exists(characterFullPath))
            throw new DirectoryNotFoundException($"角色目录不存在：{characterFullPath}");
        EnsureNotReparsePoint(characterFullPath, "角色目录");

        var modFullPath = Path.GetFullPath(mod.DirectoryPath);
        var parent = Path.GetDirectoryName(modFullPath);
        if (parent is null || !string.Equals(
                Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                characterFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安全检查失败：只能删除当前角色目录直属的 Mod。");

        if (!Directory.Exists(modFullPath))
            throw new DirectoryNotFoundException($"Mod 目录不存在：{modFullPath}");
        EnsureNotReparsePoint(modFullPath, "Mod 目录");
        if (IsIgnoredModDirectory(Path.GetFileName(modFullPath)))
            throw new InvalidOperationException("安全检查失败：不能删除管理器保留目录。");

        Directory.Delete(modFullPath, recursive: true);
    }

    public IReadOnlyList<string> GetPreviewImages(string modPath)
    {
        var preview = FindPreviewDirectory(modPath);
        if (preview is null) return Array.Empty<string>();
        var images = Directory.EnumerateFiles(preview)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var image in images) EnsureNotReparsePoint(image, "预览图片");
        return images;
    }

    public string LoadReadme(string modPath)
    {
        var preview = FindPreviewDirectory(modPath);
        if (preview is null) return string.Empty;
        var readme = Directory.EnumerateFiles(preview)
            .FirstOrDefault(path => Path.GetFileName(path).Equals("readme.txt", StringComparison.OrdinalIgnoreCase));
        if (readme is not null) EnsureNotReparsePoint(readme, "readme.txt");
        return readme is null ? string.Empty : File.ReadAllText(readme);
    }

    public string SaveReadme(string modPath, string content)
    {
        var preview = GetOrCreatePreviewDirectory(modPath);
        var existing = Directory.EnumerateFiles(preview)
            .FirstOrDefault(path => Path.GetFileName(path).Equals("readme.txt", StringComparison.OrdinalIgnoreCase));
        if (existing is not null) EnsureNotReparsePoint(existing, "readme.txt");
        var readmePath = existing ?? Path.Combine(preview, "readme.txt");
        var temporaryPath = Path.Combine(preview, $".readme.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, new System.Text.UTF8Encoding(false));
            File.Move(temporaryPath, readmePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        return readmePath;
    }

    public IReadOnlyList<string> AddPreviewImages(string modPath, IEnumerable<string> sourcePaths)
    {
        var sources = sourcePaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Path.GetFullPath)
            .Where(source => File.Exists(source) && ImageExtensions.Contains(Path.GetExtension(source)))
            .ToList();
        foreach (var source in sources)
        {
            if (new FileInfo(source).Length > MaximumPreviewImageBytes)
                throw new IOException($"预览图片超过 100 MB 限制：{Path.GetFileName(source)}");
        }

        var preview = GetOrCreatePreviewDirectory(modPath);
        var added = new List<string>();
        try
        {
            foreach (var source in sources)
            {
                var destination = GetUniqueDestination(preview, Path.GetFileName(source));
                if (Path.GetFullPath(destination).Equals(source, StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(source, destination, overwrite: false);
                added.Add(destination);
            }
            return added;
        }
        catch
        {
            foreach (var path in added.AsEnumerable().Reverse())
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch { }
            }
            throw;
        }
    }

    public void DeletePreviewImage(string modPath, string imagePath)
    {
        var preview = FindPreviewDirectory(modPath) ?? throw new DirectoryNotFoundException("Preview 目录不存在。");
        var fullImagePath = Path.GetFullPath(imagePath);
        var imageParent = Path.GetDirectoryName(fullImagePath);
        if (!string.Equals(Path.GetFullPath(preview).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(imageParent ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安全检查失败：只能删除当前 Mod 的 Preview 直属图片。");
        if (!ImageExtensions.Contains(Path.GetExtension(fullImagePath)))
            throw new InvalidOperationException("安全检查失败：该文件不是受支持的预览图。");
        EnsureNotReparsePoint(fullImagePath, "预览图片");
        File.Delete(fullImagePath);
    }

    public static string GetDisplayName(string folderName)
    {
        if (!folderName.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase)) return folderName;
        var remainder = folderName["DISABLED".Length..].TrimStart(' ', '_', '-');
        return remainder;
    }

    private static string GetOrCreatePreviewDirectory(string modPath)
    {
        if (!Directory.Exists(modPath)) throw new DirectoryNotFoundException($"Mod 目录不存在：{modPath}");
        var preview = FindPreviewDirectory(modPath) ?? Path.Combine(modPath, "Preview");
        Directory.CreateDirectory(preview);
        return preview;
    }

    private static string? FindPreviewDirectory(string modPath)
    {
        var preview = FindChildDirectory(modPath, "Preview");
        if (preview is not null) EnsureNotReparsePoint(preview, "Preview 目录");
        return preview;
    }

    private static void EnsureNotReparsePoint(string path, string description)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"安全检查失败：{description}不能是符号链接或重解析点：{path}");
    }

    private static string? FindChildDirectory(string parent, string name) =>
        Directory.EnumerateDirectories(parent)
            .FirstOrDefault(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string GetUniqueDestination(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate)) return candidate;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; i < 10_000; i++)
        {
            candidate = Path.Combine(directory, $"{name}_{i}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException($"无法为图片生成唯一文件名：{fileName}");
    }
}
