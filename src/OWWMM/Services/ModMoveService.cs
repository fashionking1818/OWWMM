using OWWMM.Models;

namespace OWWMM.Services;

public sealed partial class ModService
{
    public string MoveMod(string modsRoot, ModEntry mod, string destinationGroupDirectory)
    {
        var root = ResolveCharactersRoot(modsRoot);
        EnsureNotReparsePoint(root, "Mods 根目录");
        var source = Path.GetFullPath(mod.DirectoryPath);
        var parent = Directory.GetParent(source) ?? throw new IOException("Mod 目录无效。");
        var inRoot = parent.FullName.Equals(root, StringComparison.OrdinalIgnoreCase);
        if (!inRoot && !string.Equals(parent.Parent?.FullName, root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("只能移动 Mods 根目录或分组中的直属 Mod。");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Mod 目录不存在。");
        EnsureNotReparsePoint(parent.FullName, "Mod 分组目录");
        EnsureNotReparsePoint(source, "Mod 目录");
        if (!inRoot)
            ValidateEditableGroup(root, new CharacterEntry(parent.Name, parent.Name, parent.FullName, true));
        else if (!ContainsTopLevelModIni(new DirectoryInfo(source)))
            throw new IOException("请选择 Mod，而不是整个分组。");
        var destination = Path.GetFullPath(destinationGroupDirectory);
        ValidateEditableGroup(root, new CharacterEntry(Path.GetFileName(destination), Path.GetFileName(destination), destination, true));
        if (destination.Equals(source, StringComparison.OrdinalIgnoreCase)
            || destination.Equals(parent.FullName, StringComparison.OrdinalIgnoreCase))
            throw new IOException("请选择其它目标分组。");
        var target = Path.Combine(destination, Path.GetFileName(source));
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException($"目标分组已有同名 Mod：{Path.GetFileName(source)}");
        // No character matching or INI rewriting: the user owns classification.
        Directory.Move(source, target);
        var folderName = Path.GetFileName(target);
        mod.ApplyRename(target, folderName, GetDisplayName(folderName),
            !folderName.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase));
        return target;
    }
}
