using System.Diagnostics;
using System.Globalization;

namespace OWWMM.Services;

public sealed record ImportResult(IReadOnlyList<string> ImportedDirectories, string SevenZipPath);

public sealed class ArchivePasswordRequiredException : IOException
{
    public ArchivePasswordRequiredException()
        : base("压缩包需要密码。")
    {
    }
}

public sealed class ArchivePasswordInvalidException : IOException
{
    public ArchivePasswordInvalidException()
        : base("压缩包密码错误。")
    {
    }
}

public sealed class SevenZipImportService
{
    private readonly string _baseDirectory;

    public SevenZipImportService(string? baseDirectory = null)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
    }

    private const string DisabledPrefix = "DISABLED ";
    private const long MaximumExpandedBytes = 20L * 1024 * 1024 * 1024;
    private const int MaximumEntryCount = 100_000;
    private const int MaximumDirectoryDepth = 64;
    // Supplying a probe password prevents 7-Zip from waiting for interactive
    // console input when an archive is encrypted and the UI has not prompted yet.
    private const string PasswordProbe = "__OWWMMPasswordProbe__";
    private static readonly TimeSpan SevenZipTimeout = TimeSpan.FromMinutes(30);
    public string? FindSevenZip()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            // The Windows distribution carries the full codec DLL, including
            // RAR support. Never depend on a system installation or working dir.
            var bundle = Path.Combine(_baseDirectory, "ThirdParty", "7zip", "win-x64");
            var executable = Path.Combine(bundle, "7z.exe");
            return File.Exists(executable) && File.Exists(Path.Combine(bundle, "7z.dll"))
                ? executable
                : null;
        }

        var executableNames = OperatingSystem.IsWindows()
            ? new[] { "7z.exe", "7zz.exe", "7za.exe" }
            : new[] { "7zz", "7z", "7za" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.AddRange(executableNames.Select(name => Path.Combine(directory, name)));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<ImportResult> ImportAsync(string archivePath, string characterDirectory, string? password = null)
    {
        var sevenZip = FindSevenZip() ?? throw new FileNotFoundException(OperatingSystem.IsWindows()
            ? "内置解压组件缺失，请重新解压完整发行版（包含 ThirdParty 文件夹）。"
            : "未找到解压组件，请将 7zz 或 7z 加入 PATH。");
        var archive = Path.GetFullPath(archivePath);
        if (!File.Exists(archive)) throw new FileNotFoundException("压缩包不存在。", archive);
        await ValidateArchiveAsync(sevenZip, archive, password);

        Directory.CreateDirectory(characterDirectory);
        var importName = SanitizeImportName(Path.GetFileNameWithoutExtension(archive));
        // New imports stay disabled until the user explicitly activates them,
        // preventing a newly unpacked Mod from changing the running game.
        var destination = GetUniqueDirectory(characterDirectory, DisabledPrefix + importName);
        var stagingRoot = Path.Combine(characterDirectory, $".OWWMM-import-{Guid.NewGuid():N}");
        var extractionDirectory = Path.Combine(stagingRoot, importName);
        if (!IsChildOf(stagingRoot, extractionDirectory))
            throw new IOException("压缩包名称无法生成安全的导入目录。");
        Directory.CreateDirectory(extractionDirectory);
        try
        {
            var integrity = await RunSevenZipAsync(sevenZip, new[] { "t", archive, PasswordArgument(password), "-bd", "-bso1", "-bse1" });
            if (integrity.ExitCode != 0)
            {
                ThrowForPasswordFailure(integrity, password);
                throw new IOException($"7-Zip 完整性测试失败（退出码 {integrity.ExitCode}）：{GetUsefulError(integrity)}");
            }
            var extraction = await RunSevenZipAsync(sevenZip, new[] { "x", archive, $"-o{extractionDirectory}", PasswordArgument(password), "-y", "-bd", "-bso1", "-bse1" });
            if (extraction.ExitCode != 0)
            {
                ThrowForPasswordFailure(extraction, password);
                throw new IOException($"7-Zip 解压失败（退出码 {extraction.ExitCode}）：{GetUsefulError(extraction)}");
            }

            ValidateExtractedTree(extractionDirectory);
            if (!HasIni(new DirectoryInfo(extractionDirectory), SearchOption.AllDirectories))
                throw new IOException("压缩包中没有找到 Mod INI，未导入任何文件。");

            // 暂存目录与目标目录位于同一角色目录下，因此最终移动是同卷原子操作。
            // 压缩包中的层级不再被分析或拆分，Preview 等内容会完整保留在此 Mod 目录中。
            Directory.Move(extractionDirectory, destination);
            return new ImportResult(new[] { destination }, sevenZip);
        }
        finally
        {
            if (Directory.Exists(stagingRoot) && IsChildOf(characterDirectory, stagingRoot))
            {
                try { Directory.Delete(stagingRoot, recursive: true); }
                catch { /* 暂存目录清理失败不应覆盖导入结果或原始错误。 */ }
            }
        }
    }

    private static async Task ValidateArchiveAsync(string sevenZip, string archive, string? password)
    {
        var listing = await RunSevenZipAsync(sevenZip, new[] { "l", "-slt", archive, PasswordArgument(password) });
        if (listing.ExitCode != 0)
        {
            ThrowForPasswordFailure(listing, password);
            throw new IOException($"7-Zip 无法读取该压缩包：{GetUsefulError(listing)}");
        }

        var inEntries = false;
        long expandedBytes = 0;
        var entryCount = 0;
        foreach (var rawLine in listing.StandardOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("----------", StringComparison.Ordinal))
            {
                inEntries = true;
                continue;
            }
            if (!inEntries) continue;
            if (line.StartsWith("Path = ", StringComparison.Ordinal))
            {
                var entryPath = line[7..];
                if (Path.IsPathRooted(entryPath) || entryPath.Split('/', '\\').Any(part => part == ".."))
                    throw new IOException($"压缩包包含不安全路径，已拒绝导入：{entryPath}");
                entryCount++;
                if (entryCount > MaximumEntryCount)
                    throw new IOException($"压缩包条目超过 {MaximumEntryCount:N0} 个安全限制，已拒绝导入。");
                var depth = entryPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Length;
                if (depth > MaximumDirectoryDepth)
                    throw new IOException($"压缩包目录深度超过 {MaximumDirectoryDepth} 层安全限制，已拒绝导入：{entryPath}");
            }
            else if (line.StartsWith("Size = ", StringComparison.Ordinal)
                     && long.TryParse(line[7..], NumberStyles.None, CultureInfo.InvariantCulture, out var size))
            {
                expandedBytes = checked(expandedBytes + size);
                if (expandedBytes > MaximumExpandedBytes)
                    throw new IOException("压缩包展开后超过 20 GB 安全限制，已拒绝导入。");
            }
        }
    }

    private static void ValidateExtractedTree(string root)
    {
        var stack = new Stack<(DirectoryInfo Directory, int Depth)>();
        stack.Push((new DirectoryInfo(root), 0));
        var entryCount = 0;
        long expandedBytes = 0;
        while (stack.Count > 0)
        {
            var (directory, depth) = stack.Pop();
            if (depth > MaximumDirectoryDepth)
                throw new IOException($"解压结果目录深度超过 {MaximumDirectoryDepth} 层安全限制。");
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                if (++entryCount > MaximumEntryCount)
                    throw new IOException($"解压结果条目超过 {MaximumEntryCount:N0} 个安全限制。");
                if (!IsChildOf(root, item.FullName)) throw new IOException("解压结果越过了临时目录边界。");
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"压缩包包含符号链接或重解析点，已拒绝导入：{item.Name}");
                if (item is DirectoryInfo child)
                {
                    stack.Push((child, depth + 1));
                }
                else if (item is FileInfo file)
                {
                    expandedBytes = checked(expandedBytes + file.Length);
                    if (expandedBytes > MaximumExpandedBytes)
                        throw new IOException("实际解压内容超过 20 GB 安全限制，已拒绝导入。");
                }
            }
        }
    }

    private static bool IsJunk(string name) =>
        name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
        || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

    private static bool HasIni(DirectoryInfo directory, SearchOption searchOption) =>
        directory.EnumerateFiles("*", searchOption)
            .Any(file => file.Extension.Equals(".ini", StringComparison.OrdinalIgnoreCase));

    private static string SanitizeImportName(string name)
    {
        var result = string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(result) || result is "." or ".." ? "Imported Mod" : result;
    }

    private static string GetUniqueDirectory(string parent, string name)
    {
        var candidate = Path.Combine(parent, name);
        if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        for (var i = 2; i < 10_000; i++)
        {
            candidate = Path.Combine(parent, $"{name} ({i})");
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
        throw new IOException($"无法为导入的 Mod 生成唯一目录名：{name}");
    }

    private static bool IsChildOf(string parent, string child)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedChild = Path.GetFullPath(child);
        return normalizedChild.StartsWith(normalizedParent, comparison);
    }

    private static async Task<ProcessResult> RunSevenZipAsync(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new IOException("无法启动 7-Zip。 ");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(SevenZipTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
            await Task.WhenAll(outputTask, errorTask);
            throw new IOException($"7-Zip 操作超过 {SevenZipTimeout.TotalMinutes:0} 分钟，已终止。");
        }
        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static string GetUsefulError(ProcessResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        return string.IsNullOrWhiteSpace(message) ? "未知错误" : message.Trim();
    }

    private static string PasswordArgument(string? password) => $"-p{password ?? PasswordProbe}";

    private static void ThrowForPasswordFailure(ProcessResult result, string? password)
    {
        if (!LooksLikePasswordFailure(result)) return;
        if (password is null) throw new ArchivePasswordRequiredException();
        throw new ArchivePasswordInvalidException();
    }

    private static bool LooksLikePasswordFailure(ProcessResult result)
    {
        var message = $"{result.StandardOutput}\n{result.StandardError}";
        return message.Contains("encrypted", StringComparison.OrdinalIgnoreCase)
            || message.Contains("wrong password", StringComparison.OrdinalIgnoreCase)
            || message.Contains("password", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
