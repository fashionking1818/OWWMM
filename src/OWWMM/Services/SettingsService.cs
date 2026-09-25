using System.Text.Json;
using System.Text.Json.Serialization;

namespace OWWMM.Services;

public sealed record WindowLayoutSettings(
    double Width,
    double Height,
    double LeftPanelWidth,
    double MiddlePanelWidth,
    bool Maximized);

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private readonly string _relativeAnchor;

    public SettingsService(string? settingsPath = null, string? relativeAnchor = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(AppContext.BaseDirectory, "config", "settings.json");
        _relativeAnchor = Path.GetFullPath(relativeAnchor ?? AppContext.BaseDirectory);
    }

    /// <summary>
    /// Indicates whether this application copy already has its portable
    /// configuration file beside the executable.
    /// </summary>
    public bool HasPortableSettings => File.Exists(_settingsPath);

    public string? LoadLastRoot()
    {
        try
        {
            var settings = TryReadSettings(_settingsPath);
            if (settings is null) return null;

            if (!string.IsNullOrWhiteSpace(settings.RelativeRoot) && !Path.IsPathRooted(settings.RelativeRoot))
            {
                var relativeCandidate = Path.GetFullPath(Path.Combine(_relativeAnchor, settings.RelativeRoot));
                if (Path.GetFileName(relativeCandidate).Equals("character", StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Directory.GetParent(relativeCandidate);
                    if (parent is not null
                        && parent.Name.Equals("Mods", StringComparison.OrdinalIgnoreCase)
                        && Directory.Exists(parent.FullName))
                        return parent.FullName;
                }
                if (Directory.Exists(relativeCandidate)) return relativeCandidate;
            }

            // An absolute fallback is used only when the directory cannot be
            // represented relatively, for example when it is on another drive.
            if (string.IsNullOrWhiteSpace(settings.LastRoot)) return null;
            var absoluteCandidate = Path.IsPathRooted(settings.LastRoot)
                ? settings.LastRoot
                : Path.Combine(_relativeAnchor, settings.LastRoot);
            var absoluteFullPath = Path.GetFullPath(absoluteCandidate);
            if (Path.GetFileName(absoluteFullPath).Equals("character", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Directory.GetParent(absoluteFullPath);
                if (parent is not null
                    && parent.Name.Equals("Mods", StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(parent.FullName))
                    return parent.FullName;
            }
            return Directory.Exists(absoluteFullPath) ? absoluteFullPath : null;
        }
        catch
        {
            return null;
        }
    }

    public void SaveLastRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        string? relativePath = Path.GetRelativePath(_relativeAnchor, fullPath);
        if (Path.IsPathRooted(relativePath)) relativePath = null;
        var existing = TryReadSettings(_settingsPath) ?? new AppSettings();
        WriteSettings(existing with
        {
            RelativeRoot = relativePath,
            // Keep an absolute fallback only for paths that cannot be represented relatively
            // (for example, a directory on another drive).
            LastRoot = relativePath is null ? fullPath : null
        });
    }

    public AppLanguage? LoadLanguage()
    {
        try
        {
            var settings = TryReadSettings(_settingsPath);
            return settings?.Language?.Trim().ToLowerInvariant() switch
            {
                "en" or "english" => AppLanguage.English,
                "zh" or "chinese" or "中文" => AppLanguage.Chinese,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    public void SaveLanguage(AppLanguage language)
    {
        var existing = TryReadSettings(_settingsPath) ?? new AppSettings();
        WriteSettings(existing with
        {
            LastRoot = existing.RelativeRoot is null ? existing.LastRoot : null,
            Language = language == AppLanguage.English ? "en" : "zh"
        });
    }

    public bool LoadRevealPassword()
    {
        try
        {
            var settings = TryReadSettings(_settingsPath);
            return settings?.RevealPassword ?? false;
        }
        catch
        {
            return false;
        }
    }

    public void SaveRevealPassword(bool revealPassword)
    {
        var existing = TryReadSettings(_settingsPath) ?? new AppSettings();
        WriteSettings(existing with
        {
            LastRoot = existing.RelativeRoot is null ? existing.LastRoot : null,
            RevealPassword = revealPassword
        });
    }

    public WindowLayoutSettings? LoadWindowLayout()
    {
        var settings = TryReadSettings(_settingsPath);
        if (settings?.WindowWidth is not > 0 || settings.WindowHeight is not > 0
            || settings.LeftPanelWidth is not > 0 || settings.MiddlePanelWidth is not > 0)
            return null;
        var values = new[]
        {
            settings.WindowWidth.Value,
            settings.WindowHeight.Value,
            settings.LeftPanelWidth.Value,
            settings.MiddlePanelWidth.Value
        };
        if (values.Any(value => double.IsNaN(value) || double.IsInfinity(value))) return null;
        return new WindowLayoutSettings(values[0], values[1], values[2], values[3], settings.WindowMaximized == true);
    }

    public void SaveWindowLayout(WindowLayoutSettings layout)
    {
        var existing = TryReadSettings(_settingsPath) ?? new AppSettings();
        WriteSettings(existing with
        {
            WindowWidth = layout.Width,
            WindowHeight = layout.Height,
            LeftPanelWidth = layout.LeftPanelWidth,
            MiddlePanelWidth = layout.MiddlePanelWidth,
            WindowMaximized = layout.Maximized
        });
    }

    public string GetDisplayPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relativePath = Path.GetRelativePath(_relativeAnchor, fullPath);
        return Path.IsPathRooted(relativePath) ? fullPath : relativePath;
    }

    private static AppSettings? TryReadSettings(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private void WriteSettings(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        var temporaryPath = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed record AppSettings(
        string? RelativeRoot = null,
        string? LastRoot = null,
        string? Language = null,
        bool? RevealPassword = null,
        double? WindowWidth = null,
        double? WindowHeight = null,
        double? LeftPanelWidth = null,
        double? MiddlePanelWidth = null,
        bool? WindowMaximized = null);
}
