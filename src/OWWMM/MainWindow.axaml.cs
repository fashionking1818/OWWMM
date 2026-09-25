using System.Diagnostics;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;
using OWWMM.Models;
using OWWMM.Services;

namespace OWWMM;

public partial class MainWindow : Window
{
    private const int PreviewDecodeWidth = 1600;
    private const int PreviewBitmapCacheCapacity = 3;
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".zip", ".rar", ".tar"
    };
    private readonly ModService _modService = new();
    private readonly IconCropService _iconCropService = new();
    private readonly SevenZipImportService _sevenZipImportService = new();
    private readonly SettingsService _settingsService = new();
    private bool _revealPassword;
    private readonly ScaleTransform _fullscreenTransform = new(1, 1);
    private readonly Dictionary<string, Bitmap> _characterAvatarCache = new(StringComparer.Ordinal);
    private UiStrings _text = new(AppLanguage.Chinese);
    private ObservableCollection<CharacterEntry> _characters = new();
    private IReadOnlyList<ModEntry> _allMods = Array.Empty<ModEntry>();
    private List<string> _previewImages = new();
    private readonly Dictionary<string, PreviewBitmapCacheEntry> _previewBitmapCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _previewBitmapLru = new();
    private readonly HashSet<string> _previewPrefetchInFlight = new(StringComparer.OrdinalIgnoreCase);
    private string? _charactersRoot;
    private CharacterEntry? _activeCharacter;
    private ModEntry? _activeMod;
    private Bitmap? _currentBitmap;
    private Bitmap? _fullscreenBitmap;
    private int _previewIndex = -1;
    private bool _loadingReadme;
    private bool _readmeDirty;
    private string _savedReadmeText = string.Empty;
    private double _fullscreenZoom = 1;
    private int _previewLoadGeneration;
    private CancellationTokenSource? _previewLoadCancellation;
    private CancellationTokenSource? _fullscreenLoadCancellation;
    private CancellationTokenSource _previewCacheCancellation = new();
    private bool _suppressSelectionChanges;
    private bool _isImportBusy;
    private bool _isStartupBusy = true;
    private bool _isClosing;
    private double _normalWindowWidth = 1320;
    private double _normalWindowHeight = 820;
    private ColumnDefinition LeftPanelColumn => PanelsGrid.ColumnDefinitions[0];
    private ColumnDefinition MiddlePanelColumn => PanelsGrid.ColumnDefinitions[2];

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowLayout();
        _revealPassword = _settingsService.LoadRevealPassword();
        ApplyLanguage();
        FullscreenImageControl.RenderTransform = _fullscreenTransform;
        FullscreenImageControl.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        ShowCatalogWithoutRoot();
        UpdateBusyControls();
        Opened += OnOpened;
        Closing += OnClosing;
        SizeChanged += OnWindowSizeChanged;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void RestoreWindowLayout()
    {
        var layout = _settingsService.LoadWindowLayout();
        if (layout is null) return;
        _normalWindowWidth = Math.Clamp(layout.Width, MinWidth, 3840);
        _normalWindowHeight = Math.Clamp(layout.Height, MinHeight, 2160);
        Width = _normalWindowWidth;
        Height = _normalWindowHeight;
        LeftPanelColumn.Width = new GridLength(Math.Clamp(layout.LeftPanelWidth, 220, 800));
        MiddlePanelColumn.Width = new GridLength(Math.Clamp(layout.MiddlePanelWidth, 280, 1000));
        if (layout.Maximized) WindowState = WindowState.Maximized;
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (WindowState != WindowState.Normal || e.NewSize.Width < MinWidth || e.NewSize.Height < MinHeight) return;
        _normalWindowWidth = e.NewSize.Width;
        _normalWindowHeight = e.NewSize.Height;
    }

    private void SaveWindowLayout()
    {
        try
        {
            _settingsService.SaveWindowLayout(new WindowLayoutSettings(
                _normalWindowWidth,
                _normalWindowHeight,
                Math.Max(LeftPanelColumn.MinWidth, LeftPanelColumn.ActualWidth),
                Math.Max(MiddlePanelColumn.MinWidth, MiddlePanelColumn.ActualWidth),
                WindowState == WindowState.Maximized));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"保存窗口布局失败：{ex.Message}");
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        var hasPortableSettings = _settingsService.HasPortableSettings;
        try
        {
            var savedLanguage = _settingsService.LoadLanguage();
            var savedRoot = _settingsService.LoadLastRoot();
            var initial = savedRoot ?? FindLikelyModsRoot();
            if (initial is not null) LoadRoot(initial);
            if (savedLanguage is null)
            {
                var selectedLanguage = await LanguageDialog.ShowAsync(this, _text);
                if (_isClosing) return;
                SetLanguage(selectedLanguage ?? AppLanguage.Chinese, persist: true);
            }
            else
            {
                SetLanguage(savedLanguage.Value, persist: !hasPortableSettings);
            }

            // A language-only settings file is not a completed first-time setup.
            if (_charactersRoot is null && !_isClosing)
                await ChooseFolderAsync();
        }
        finally
        {
            _isStartupBusy = false;
            if (!_isClosing) UpdateBusyControls();
        }
    }

    private async void OnLanguageClick(object? sender, RoutedEventArgs e)
    {
        var selectedLanguage = await LanguageDialog.ShowAsync(this, _text);
        if (selectedLanguage is null || selectedLanguage == _text.Language) return;
        SetLanguage(selectedLanguage.Value, persist: true);
        SetStatus(selectedLanguage == AppLanguage.English ? "Language changed to English." : "语言已切换为中文。");
    }

    private void SetLanguage(AppLanguage language, bool persist)
    {
        _text = new UiStrings(language);
        ApplyLanguage();
        if (!persist) return;
        try
        {
            _settingsService.SaveLanguage(language);
        }
        catch (Exception ex)
        {
            SetStatus($"{(_text.IsEnglish ? "Language preference could not be saved" : "语言偏好保存失败")}：{ex.Message}", error: true);
        }
    }

    private void ApplyLanguage()
    {
        Title = _text.WindowTitle;
        HeaderSubtitleText.Text = _text.HeaderSubtitle;
        RootPathTextBox.Watermark = _text.RootPathWatermark;
        ChooseFolderButton.Content = _charactersRoot is null ? _text.ChooseFolder : _text.ChangeFolder;
        ToolTip.SetTip(ChooseFolderButton, _text.ChooseFolderTooltip);
        ToolTip.SetTip(RootLayout, _text.DropArchiveTooltip);
        RefreshButton.Content = _text.Refresh;
        ToolTip.SetTip(LanguageButton, _text.LanguageTooltip);
        Avalonia.Automation.AutomationProperties.SetName(LanguageButton, _text.LanguageTooltip);
        CharacterHeaderText.Text = _text.CharacterHeader;
        ToolTip.SetTip(AddModGroupButton, _text.AddModGroup);
        Avalonia.Automation.AutomationProperties.SetName(AddModGroupButton, _text.AddModGroup);
        EditGroupMenuItem.Header = _text.RenameModGroup;
        DeleteGroupMenuItem.Header = _text.DeleteModGroup;
        ResetGroupIconMenuItem.Header = _text.ResetGroupIcon;
        CharacterSearchTextBox.Watermark = _text.SearchCharacterWatermark;
        CharacterStructureHintText.Text = _text.CharacterStructureHint;
        ImportModButton.Content = _text.ImportMod;
        ActivateModsMenuItem.Header = _text.ActivateSelected;
        DisableModsMenuItem.Header = _text.DisableSelected;
        MoveModsMenuItem.Header = _text.MoveSelected;
        ToolTip.SetTip(ModListBox, _text.BatchSelectionTooltip);
        SearchTextBox.Watermark = _text.SearchModWatermark;
        ModNameTextBox.Watermark = _text.SelectModWatermark;
        SaveModNameButton.Content = _text.SaveName;
        OpenModFolderMenuItem.Header = _text.OpenFolder;
        DeleteModMenuItem.Header = _text.DeleteMod;
        PreviousImageButton.Content = _text.PreviousImage;
        NextImageButton.Content = _text.NextImage;
        AddImagesButton.Content = _text.AddImage;
        DeleteImageMenuItem.Header = _text.DeleteCurrentImage;
        ReadmeHeaderText.Text = _text.Readme;
        SaveReadmeButton.Content = _text.SaveReadme;
        ReadmeTextBox.Watermark = _text.ReadmeWatermark;
        FooterHintText.Text = _text.FooterHint;
        ToolTip.SetTip(FullscreenZoomOutButton, _text.ZoomOut);
        ToolTip.SetTip(FullscreenZoomInButton, _text.ZoomIn);
        ToolTip.SetTip(FullscreenCloseButton, _text.Close);
        ToolTip.SetTip(FullscreenPreviousButton, _text.Previous);
        ToolTip.SetTip(FullscreenNextButton, _text.Next);
        Avalonia.Automation.AutomationProperties.SetName(FullscreenZoomOutButton, _text.ZoomOut);
        Avalonia.Automation.AutomationProperties.SetName(FullscreenZoomInButton, _text.ZoomIn);
        Avalonia.Automation.AutomationProperties.SetName(FullscreenCloseButton, _text.Close);
        Avalonia.Automation.AutomationProperties.SetName(FullscreenPreviousButton, _text.Previous);
        Avalonia.Automation.AutomationProperties.SetName(FullscreenNextButton, _text.Next);

        foreach (var character in _characters)
            character.UseEnglishAvailability = _text.IsEnglish;
        if (_characters.Count > 0)
            ApplyCharacterFilter(_activeCharacter);
        if (_activeMod is null)
        {
            DetailPathText.Text = _text.DetailPlaceholder;
            DetailStateText.Text = _text.StateNone;
        }
        else
        {
            UpdateDetailHeader(_activeMod);
        }
        if (_activeCharacter is not null)
            ModPanelTitle.Text = _activeCharacter.DisplayName;
        else
        {
            ModPanelTitle.Text = _text.ModFallbackTitle;
            ModCountText.Text = _text.SelectCharacter;
        }
        if (_allMods.Count > 0 || _activeCharacter is not null)
            ApplyFilter();
        if (_charactersRoot is null && _activeCharacter is not null)
            ModCountText.Text = _text.NoRoot;
        if (_previewImages.Count == 0)
            NoPreviewText.Text = _text.NoPreview;
        SetReadmeDirty(_readmeDirty);
        StatusText.Text = _text.TranslateError(StatusText.Text ?? string.Empty);
    }

    private async void OnChooseFolderClick(object? sender, RoutedEventArgs e)
        => await ChooseFolderAsync();

    private async Task<bool> ChooseFolderAsync()
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = _text.ChooseFolderDialogTitle,
                AllowMultiple = false
            });
            if (folders.Count == 0) return false;
            LoadRoot(folders[0].Path.LocalPath);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"{(_text.IsEnglish ? "Failed to open folder picker" : "打开文件夹选择器失败")}：{ex.Message}", error: true);
            return false;
        }
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (_charactersRoot is null)
        {
            SetStatus(_text.NoRoot, error: true);
            return;
        }
        var selectedCharacter = _activeCharacter?.FolderName;
        LoadRoot(_charactersRoot, selectedCharacter);
    }

    private async void OnAddModGroupClick(object? sender, RoutedEventArgs e)
    {
        if (_charactersRoot is null || _modService.GetModsRoot(_charactersRoot) is null)
        {
            SetStatus(_text.NoRoot, error: true);
            return;
        }

        try
        {
            var edit = await ModGroupDialog.ShowAsync(this, _text);
            if (edit is null) return;
            var created = _modService.SaveModGroup(_charactersRoot, null, edit.FolderName,
                edit.Title, edit.Subtitle, edit.IconPng);
            var folderName = Path.GetFileName(created);
            LoadRoot(_charactersRoot, folderName);
            SetStatus(_text.ModGroupCreated(folderName));
        }
        catch (Exception ex)
        {
            SetStatus($"创建 Mod 分组失败：{ex.Message}", error: true);
        }
    }

    private async void OnRenameModGroupClick(object? sender, RoutedEventArgs e)
    {
        if (_charactersRoot is null || CharacterListBox.SelectedItem is not CharacterEntry group
            || group.IsUngroupedGroup || group.IsPathOccupiedByRootMod)
            return;
        if (!SaveReadmeIfDirty(showStatus: true)) return;
        try
        {
            var edit = await ModGroupDialog.ShowAsync(this, _text, group);
            if (edit is null) return;
            var path = _modService.SaveModGroup(_charactersRoot, group, edit.FolderName,
                edit.Title, edit.Subtitle, edit.IconPng);
            var folderName = Path.GetFileName(path);
            ClearCharacterAvatarCache();
            LoadRoot(_charactersRoot, folderName);
            SetStatus(_text.ModGroupRenamed(folderName));
        }
        catch (Exception ex)
        {
            SetStatus($"重命名 Mod 分组失败：{ex.Message}", error: true);
        }
    }

    private async void OnDeleteModGroupClick(object? sender, RoutedEventArgs e)
    {
        if (_charactersRoot is null || CharacterListBox.SelectedItem is not CharacterEntry group
            || group.IsUngroupedGroup || group.IsPathOccupiedByRootMod)
            return;
        var confirmed = await ConfirmDialog.ShowAsync(this, _text.DeleteModGroupTitle,
            _text.DeleteModGroupConfirm(group.FolderName), _text.Delete, _text.Cancel);
        if (!confirmed || !ReferenceEquals(group, CharacterListBox.SelectedItem)) return;
        try
        {
            _modService.DeleteEmptyModGroup(_charactersRoot, group);
            ClearCharacterAvatarCache();
            LoadRoot(_charactersRoot);
            SetStatus(_text.ModGroupDeleted(group.FolderName));
        }
        catch (Exception ex)
        {
            SetStatus($"删除 Mod 分组失败：{ex.Message}", error: true);
        }
    }

    private void OnResetGroupIconClick(object? sender, RoutedEventArgs e)
    {
        if (_charactersRoot is null || CharacterListBox.SelectedItem is not CharacterEntry group
            || group.IsUngroupedGroup || group.IsPathOccupiedByRootMod) return;
        try
        {
            if (!_modService.ResetCategoryIcon(group.DirectoryPath)) return;
            var folderName = group.FolderName;
            ClearCharacterAvatarCache();
            LoadRoot(_charactersRoot, folderName);
            SetStatus(_text.GroupIconReset(group.DisplayName));
        }
        catch (Exception ex)
        {
            SetStatus($"重置分组图标失败：{ex.Message}", error: true);
        }
    }

    private async void OnCategoryIconPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || sender is not Control { DataContext: CharacterEntry category }) return;
        await ChangeCategoryIconAsync(category);
    }

    private async void OnCategoryIconKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)
            || sender is not Control { DataContext: CharacterEntry category }) return;
        e.Handled = true;
        await ChangeCategoryIconAsync(category);
    }

    private async Task ChangeCategoryIconAsync(CharacterEntry category)
    {
        if (_charactersRoot is null)
        {
            SetStatus(_text.NoRoot, error: true);
            return;
        }
        if (category.IsUngroupedGroup || category.IsPathOccupiedByRootMod)
        {
            SetStatus(_text.CategoryIconUnavailable, error: true);
            return;
        }

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _text.CategoryIconPickerTitle,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(_text.ImageFileType)
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            SetStatus($"打开图片选择器失败：{ex.Message}", error: true);
            return;
        }
        if (files.Count == 0) return;

        try
        {
            var sourcePath = files[0].Path.LocalPath;
            var selection = await IconCropDialog.ShowAsync(this, _text, sourcePath, _iconCropService);
            if (selection is null) return;
            var destination = _modService.GetCategoryIconDestination(category.DirectoryPath);
            _iconCropService.SaveCroppedIcon(sourcePath, destination, selection.Value);
            ClearCharacterAvatarCache();
            LoadRoot(_charactersRoot, category.FolderName);
            SetStatus(_text.CategoryIconSaved(
                category.DisplayName));
        }
        catch (Exception ex)
        {
            SetStatus($"设置分组图标失败：{ex.Message}", error: true);
        }
    }

    private void ShowCatalogWithoutRoot()
    {
        _characters = new ObservableCollection<CharacterEntry>();
        CharacterSearchTextBox.Text = string.Empty;
        CharacterListBox.ItemsSource = _characters;
        UpdateBusyControls();
    }

    private void LoadRoot(string selectedPath, string? preferredCharacterFolder = null)
    {
        try
        {
            if (!SaveReadmeIfDirty(showStatus: true)) return;
            var charactersRoot = _modService.ResolveCharactersRoot(selectedPath);
            var characters = _modService.ScanCharacters(charactersRoot);
            foreach (var character in characters)
                character.UseEnglishAvailability = _text.IsEnglish;
            var loadedAvatarCount = LoadCharacterAvatars(characters);
            _charactersRoot = charactersRoot;
            ChooseFolderButton.Content = _text.ChangeFolder;
            _characters = new ObservableCollection<CharacterEntry>(characters);
            RootPathTextBox.Text = _settingsService.GetDisplayPath(charactersRoot);
            CharacterSearchTextBox.Text = string.Empty;
            CharacterListBox.ItemsSource = _characters;
            _settingsService.SaveLastRoot(charactersRoot);
            var index = preferredCharacterFolder is null
                ? 0
                : _characters.ToList().FindIndex(character => character.FolderName.Equals(preferredCharacterFolder, StringComparison.OrdinalIgnoreCase));
            CharacterListBox.SelectedIndex = index >= 0 ? index : 0;
            SetStatus(_text.ScanStatus(_settingsService.GetDisplayPath(charactersRoot), loadedAvatarCount));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    private int LoadCharacterAvatars(IReadOnlyList<CharacterEntry> characters)
    {
        var loadedCount = 0;
        foreach (var character in characters)
        {
            if (character.AvatarSource is not { } source) continue;

            try
            {
                if (!_characterAvatarCache.TryGetValue(source, out var bitmap))
                {
                    using var stream = source.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
                        ? AssetLoader.Open(new Uri(source))
                        : File.OpenRead(source);
                    bitmap = new Bitmap(stream);
                    _characterAvatarCache[source] = bitmap;
                }

                character.AvatarImage = bitmap;
                loadedCount++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"角色头像加载失败：{character.FolderName}，{ex.Message}");
            }
        }

        return loadedCount;
    }

    private void ClearCharacterAvatarCache()
    {
        CharacterListBox.ItemsSource = null;
        foreach (var bitmap in _characterAvatarCache.Values) bitmap.Dispose();
        _characterAvatarCache.Clear();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!SaveReadmeIfDirty(showStatus: true))
        {
            e.Cancel = true;
            return;
        }
        _previewLoadCancellation?.Cancel();
        _fullscreenLoadCancellation?.Cancel();
        _isClosing = true;
        SaveWindowLayout();
        ClearPreviewBitmapCache();
        ClearCharacterAvatarCache();
    }

    private void OnCharacterSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanges) return;
        if (!SaveReadmeIfDirty(showStatus: true))
        {
            RestoreSelection(CharacterListBox, _activeCharacter);
            return;
        }
        if (CharacterListBox.SelectedItem is not CharacterEntry character)
        {
            _activeCharacter = null;
            _allMods = Array.Empty<ModEntry>();
            ModListBox.ItemsSource = _allMods;
            ImportModButton.IsEnabled = false;
            ClearDetails();
            return;
        }

        try
        {
            _activeCharacter = character;
            _allMods = _charactersRoot is null || character.IsPathOccupiedByRootMod
                ? Array.Empty<ModEntry>()
                : _modService.ScanMods(character.DirectoryPath, character.IsUngroupedGroup);
            if (_charactersRoot is not null)
            {
                var directoryExists = !character.IsPathOccupiedByRootMod
                    && Directory.Exists(character.DirectoryPath);
                if (character.ModCount != _allMods.Count || character.DirectoryExists != directoryExists)
                {
                    var index = _characters.IndexOf(character);
                    var refreshed = character with
                    {
                        ModCount = _allMods.Count,
                        DirectoryExists = directoryExists
                    };
                    // Replace just this row so its count updates without
                    // resetting the list or recursively switching characters.
                    if (index >= 0) _characters[index] = refreshed;
                    character = refreshed;
                    _activeCharacter = refreshed;
                    ApplyCharacterFilter(refreshed);
                }
            }
            ModPanelTitle.Text = character.DisplayName;
            SearchTextBox.Text = string.Empty;
            UpdateBusyControls();
            ApplyFilter();
            ModListBox.SelectedIndex = _allMods.Count > 0 ? 0 : -1;
            if (_allMods.Count == 0) ClearDetails();
            if (_charactersRoot is null)
            {
                ModCountText.Text = _text.NoRoot;
                return;
            }
            SetStatus(character.IsPathOccupiedByRootMod
                ? _text.CharacterPathOccupied(character.FolderName)
                : _text.CharacterModStatus(
                character.DisplayName,
                _allMods.Count,
                character.DirectoryExists,
                character.FolderName));
        }
        catch (Exception ex)
        {
            _allMods = Array.Empty<ModEntry>();
            ModListBox.ItemsSource = _allMods;
            ClearDetails();
            SetStatus(ex.Message, error: true);
        }
    }

    private async void OnImportModClick(object? sender, RoutedEventArgs e)
    {
        if (_charactersRoot is null) { SetStatus(_text.NoRoot, error: true); return; }
        if (CharacterListBox.SelectedItem is not CharacterEntry character
            || character.IsUngroupedGroup
            || character.IsPathOccupiedByRootMod)
        {
            SetStatus(_text.NoCharacter, error: true);
            return;
        }
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _text.ImportTitle(character.DisplayName),
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(_text.ArchiveFileType)
                    {
                        Patterns = ArchiveExtensions.Select(extension => $"*{extension}").ToArray()
                    }
                }
            });
        }
        catch (Exception ex)
        {
            SetStatus($"{(_text.IsEnglish ? "Failed to open archive picker" : "打开压缩包选择器失败")}：{ex.Message}", error: true);
            return;
        }
        if (files.Count == 0) return;
        await ImportArchivesAsync(character, files.Select(file => file.Path.LocalPath).ToList());
    }

    private void OnRootDragOver(object? sender, DragEventArgs e)
    {
        var archives = TryGetDroppedArchives(e);
        e.DragEffects = archives.Count > 0
            && _charactersRoot is not null && !_isStartupBusy
            && !_isImportBusy
            && CharacterListBox.SelectedItem is CharacterEntry
                { IsUngroupedGroup: false, IsPathOccupiedByRootMod: false }
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnRootDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_isImportBusy) return;
        if (CharacterListBox.SelectedItem is not CharacterEntry
            { IsUngroupedGroup: false, IsPathOccupiedByRootMod: false } character)
        {
            SetStatus(_text.NoCharacter, error: true);
            return;
        }

        var archives = TryGetDroppedArchives(e);
        if (archives.Count == 0)
        {
            SetStatus(_text.UnsupportedArchive, error: true);
            return;
        }

        await ImportArchivesAsync(character, archives);
    }

    private async Task ImportArchivesAsync(CharacterEntry character, IReadOnlyList<string> archivePaths)
    {
        if (_charactersRoot is null) { SetStatus(_text.NoRoot, error: true); return; }
        if (_isStartupBusy) return;
        var archives = archivePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => File.Exists(path) && ArchiveExtensions.Contains(Path.GetExtension(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (archives.Count == 0)
        {
            SetStatus(_text.UnsupportedArchive, error: true);
            return;
        }
        if (_isImportBusy) return;

        var importCharactersRoot = _charactersRoot;
        SetImportBusy(true);
        var importedNames = new List<string?>();
        var failedCount = 0;
        var cancelled = false;
        try
        {
            foreach (var archivePath in archives)
            {
                SetStatus(_text.ImportProgress(Path.GetFileName(archivePath)));
                try
                {
                    var result = await ImportArchiveWithPasswordAsync(character.DirectoryPath, archivePath);
                    if (result is null)
                    {
                        cancelled = true;
                        break;
                    }
                    importedNames.AddRange(result.ImportedDirectories.Select(path => ModService.GetDisplayName(Path.GetFileName(path))));
                    // Refresh after each archive so the newly imported Mod is
                    // immediately visible while a multi-file drop continues.
                    if (result.ImportedDirectories.Count > 0 && importCharactersRoot is not null)
                        LoadRoot(importCharactersRoot, character.FolderName);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    SetStatus(_text.ImportFailedForArchive(Path.GetFileName(archivePath), ex.Message), error: true);
                }
            }

            if (failedCount == 0 && !cancelled)
                SetStatus(_text.Imported(importedNames.Count, importedNames));
            else if (importedNames.Count > 0)
                SetStatus(_text.ImportPartial(importedNames.Count, failedCount, cancelled));
            else if (cancelled)
                SetStatus(_text.ImportCancelled);
        }
        finally
        {
            SetImportBusy(false);
        }
    }

    private async Task<ImportResult?> ImportArchiveWithPasswordAsync(string characterDirectory, string archivePath)
    {
        var archiveName = Path.GetFileName(archivePath);
        string? password = null;
        var passwordAttempts = 0;
        while (true)
        {
            try
            {
                return await _sevenZipImportService.ImportAsync(archivePath, characterDirectory, password);
            }
            catch (ArchivePasswordRequiredException)
            {
                var prompt = await PasswordDialog.ShowAsync(this, _text, archiveName, _revealPassword, retry: false);
                SaveRevealPasswordPreference(prompt.RevealPassword);
                if (prompt.Password is null) return null;
                password = prompt.Password;
            }
            catch (ArchivePasswordInvalidException)
            {
                passwordAttempts++;
                if (passwordAttempts >= 3) throw new IOException(_text.PasswordAttemptsExceeded);
                var prompt = await PasswordDialog.ShowAsync(this, _text, archiveName, _revealPassword, retry: true);
                SaveRevealPasswordPreference(prompt.RevealPassword);
                if (prompt.Password is null) return null;
                password = prompt.Password;
            }
        }
    }

    private void SaveRevealPasswordPreference(bool revealPassword)
    {
        _revealPassword = revealPassword;
        try
        {
            _settingsService.SaveRevealPassword(revealPassword);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"保存密码显示偏好失败：{ex.Message}");
        }
    }

    private static IReadOnlyList<string> TryGetDroppedArchives(DragEventArgs e)
    {
        try
        {
            var files = e.DataTransfer.TryGetFiles() ?? Array.Empty<IStorageItem>();
            return files
                .OfType<IStorageFile>()
                .Select(file => file.Path.LocalPath)
                .Where(path => File.Exists(path) && ArchiveExtensions.Contains(Path.GetExtension(path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void SetImportBusy(bool busy)
    {
        _isImportBusy = busy;
        UpdateBusyControls();
    }

    private void UpdateBusyControls()
    {
        var busy = _isStartupBusy || _isImportBusy;
        LanguageButton.IsEnabled = !busy;
        ChooseFolderButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        AddModGroupButton.IsEnabled = !busy && _charactersRoot is not null
            && _modService.GetModsRoot(_charactersRoot) is not null;
        var selectedGroup = CharacterListBox.SelectedItem as CharacterEntry;
        var canEditGroup = !busy && selectedGroup is
            { IsUngroupedGroup: false, IsPathOccupiedByRootMod: false } && _charactersRoot is not null;
        EditGroupMenuItem.IsEnabled = canEditGroup;
        DeleteGroupMenuItem.IsEnabled = canEditGroup && selectedGroup!.DirectoryExists && selectedGroup.ModCount == 0;
        ResetGroupIconMenuItem.IsEnabled = !busy && selectedGroup is
            { IsUngroupedGroup: false, IsPathOccupiedByRootMod: false, DirectoryExists: true }
            && ModService.GetCustomCategoryIconPath(selectedGroup.DirectoryPath) is not null;
        CharacterSearchTextBox.IsEnabled = !busy;
        CharacterListBox.IsEnabled = !busy;
        SearchTextBox.IsEnabled = !busy;
        ModListBox.IsEnabled = !busy;
        var hasSelectedMods = ModListBox.SelectedItems?.OfType<ModEntry>().Any() == true;
        ActivateModsMenuItem.IsEnabled = !busy && hasSelectedMods;
        DisableModsMenuItem.IsEnabled = !busy && hasSelectedMods;
        MoveModsMenuItem.IsEnabled = !busy && hasSelectedMods && _charactersRoot is not null;
        ImportModButton.IsEnabled = !busy && _charactersRoot is not null
            && CharacterListBox.SelectedItem is CharacterEntry
                { IsUngroupedGroup: false, IsPathOccupiedByRootMod: false };
        SaveModNameButton.IsEnabled = !busy && _activeMod is not null;
        OpenModFolderMenuItem.IsEnabled = !busy && _activeMod is not null;
        DeleteModMenuItem.IsEnabled = !busy && _activeMod is not null;
        AddImagesButton.IsEnabled = !busy && _activeMod is not null;
        DeleteImageMenuItem.IsEnabled = !busy && _activeMod is not null
            && _previewIndex >= 0 && _previewIndex < _previewImages.Count;
        SaveReadmeButton.IsEnabled = !busy && _activeMod is not null;
        ModNameTextBox.IsEnabled = !busy && _activeMod is not null;
        ReadmeTextBox.IsEnabled = !busy && _activeMod is not null;
        var canNavigatePreview = !busy && _previewImages.Count > 1;
        PreviousImageButton.IsEnabled = canNavigatePreview;
        NextImageButton.IsEnabled = canNavigatePreview;
        FullscreenPreviousButton.IsEnabled = canNavigatePreview;
        FullscreenNextButton.IsEnabled = canNavigatePreview;
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnCharacterListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(CharacterListBox).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed)
            return;
        if (FindDataContext<CharacterEntry>(e.Source) is { } group)
            CharacterListBox.SelectedItem = group;
    }

    private void OnModListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(ModListBox).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed)
            return;
        if (FindDataContext<ModEntry>(e.Source) is not { } mod) return;
        if (ModListBox.SelectedItems?.Contains(mod) != true)
            ModListBox.SelectedItem = mod;
    }

    private static T? FindDataContext<T>(object? source) where T : class
    {
        for (var control = source as Control; control is not null; control = control.Parent as Control)
            if (control.DataContext is T value) return value;
        return null;
    }

    private void OnCharacterSearchTextChanged(object? sender, TextChangedEventArgs e)
        => ApplyCharacterFilter(_activeCharacter);

    private void ApplyCharacterFilter(CharacterEntry? preferredSelection = null)
    {
        var query = CharacterSearchTextBox.Text?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(query)
            ? _characters.ToList()
            : _characters.Where(character => CharacterMatchesSearch(character, query)).ToList();
        var selected = preferredSelection is null
            ? null
            : filtered.FirstOrDefault(character =>
                character.FolderName.Equals(preferredSelection.FolderName, StringComparison.OrdinalIgnoreCase));

        _suppressSelectionChanges = true;
        try
        {
            CharacterListBox.ItemsSource = filtered;
            CharacterListBox.SelectedItem = selected;
        }
        finally { _suppressSelectionChanges = false; }
        UpdateBusyControls();
    }

    private bool CharacterMatchesSearch(CharacterEntry character, string query)
    {
        if (character.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || character.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || character.FolderName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || Path.GetFileName(character.DirectoryPath).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            return true;

        return false;
    }

    private void ApplyFilter()
    {
        var query = SearchTextBox.Text?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(query)
            ? _allMods.ToList()
            : _allMods.Where(mod => mod.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        ModListBox.ItemsSource = filtered;
        ModCountText.Text = query.Length == 0 ? _text.ModCount(filtered.Count) : _text.FilteredModCount(filtered.Count, _allMods.Count);
    }

    private void OnModSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanges) return;
        var selected = ModListBox.SelectedItem as ModEntry;
        UpdateBusyControls();
        if (ReferenceEquals(selected, _activeMod)) return;
        if (!SaveReadmeIfDirty(showStatus: true))
        {
            RestoreSelection(ModListBox, _activeMod);
            return;
        }
        _activeMod = selected;
        if (_activeMod is null) ClearDetails();
        else LoadDetails(_activeMod);
    }

    private void OnActivateSelectedClick(object? sender, RoutedEventArgs e) => SetSelectedModsEnabled(true);
    private void OnDisableSelectedClick(object? sender, RoutedEventArgs e) => SetSelectedModsEnabled(false);

    private void SetSelectedModsEnabled(bool enabled)
    {
        var selected = ModListBox.SelectedItems?.OfType<ModEntry>().Distinct().ToList()
            ?? new List<ModEntry>();
        if (selected.Count == 0)
        {
            SetStatus(_text.SelectModsForBatch, error: true);
            return;
        }

        var succeeded = 0;
        var failed = 0;
        foreach (var mod in selected)
        {
            try
            {
                _modService.SetEnabled(mod, enabled);
                succeeded++;
            }
            catch (Exception ex)
            {
                failed++;
                Debug.WriteLine($"批量切换失败：{mod.DisplayName}，{ex.Message}");
            }
        }

        if (_activeMod is not null && selected.Contains(_activeMod))
        {
            UpdateDetailHeader(_activeMod);
            ReloadPreviewImages(_previewIndex);
        }
        if (_activeCharacter?.IsUngroupedGroup == true && _activeMod is not null)
            RefreshUngroupedAfterMutation(_activeMod.DirectoryPath);
        SetStatus(_text.BatchToggleStatus(succeeded, failed, enabled), error: failed > 0);
        UpdateBusyControls();
    }

    private void OnModToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || toggle.DataContext is not ModEntry mod) return;
        var requested = toggle.IsChecked == true;
        try
        {
            _modService.SetEnabled(mod, requested);
            if (ReferenceEquals(mod, _activeMod))
            {
                UpdateDetailHeader(mod);
                ReloadPreviewImages(_previewIndex);
            }
            RefreshUngroupedAfterMutation(mod.DirectoryPath);
            SetStatus(_text.ToggleStatus(mod.DisplayName, requested));
        }
        catch (Exception ex)
        {
            mod.IsEnabled = !requested;
            SetStatus(ex.Message, error: true);
        }
    }

    private void OnSaveModNameClick(object? sender, RoutedEventArgs e)
    {
        if (_activeMod is null) return;
        try
        {
            _modService.RenameMod(_activeMod, ModNameTextBox.Text ?? string.Empty);
            UpdateDetailHeader(_activeMod);
            ReloadPreviewImages(_previewIndex);
            var renamedPath = _activeMod.DirectoryPath;
            var renamedName = _activeMod.DisplayName;
            var renamedEnabled = _activeMod.IsEnabled;
            RefreshUngroupedAfterMutation(renamedPath);
            SetStatus(_text.RenameStatus(renamedName, renamedEnabled));
        }
        catch (Exception ex)
        {
            ModNameTextBox.Text = _activeMod.DisplayName;
            SetStatus($"重命名失败：{ex.Message}", error: true);
        }
    }

    private void OnOpenModFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_activeMod is null || !Directory.Exists(_activeMod.DirectoryPath))
        {
            SetStatus(_text.ModFolderMissing, error: true);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(_activeMod.DirectoryPath) { UseShellExecute = true });
            SetStatus(_text.OpenedFolder(_activeMod.DirectoryPath));
        }
        catch (Exception ex)
        {
            SetStatus($"打开目录失败：{ex.Message}", error: true);
        }
    }

    private void RefreshUngroupedAfterMutation(string preferredModPath)
    {
        if (_activeCharacter?.IsUngroupedGroup != true || _charactersRoot is null) return;
        var root = _charactersRoot;
        LoadRoot(root, "Ungrouped Mods");
        var preferred = _allMods.FirstOrDefault(mod =>
            mod.DirectoryPath.Equals(preferredModPath, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null) ModListBox.SelectedItem = preferred;
    }

    private async void OnDeleteModClick(object? sender, RoutedEventArgs e)
    {
        if (_activeMod is null || _activeCharacter is null)
        {
            SetStatus(_text.NoMod, error: true);
            return;
        }

        var targetMod = _activeMod;
        var targetCharacter = _activeCharacter;
        var deletedName = targetMod.DisplayName;
        var confirmed = await ConfirmDialog.ShowAsync(
            this,
            _text.DeleteModTitle,
            _text.DeleteModConfirm(deletedName, _readmeDirty),
            _text.Delete,
            _text.Cancel);
        if (!confirmed) return;

        // A modal confirmation can outlive a selection change. Never delete a
        // different Mod than the one shown in the confirmation dialog.
        if (!ReferenceEquals(targetMod, _activeMod) || !ReferenceEquals(targetCharacter, _activeCharacter))
        {
            SetStatus(_text.ModChanged, error: true);
            return;
        }

        var previousIndex = ModListBox.SelectedIndex;
        try
        {
            ClearPreviewBitmapCache();
            _modService.DeleteMod(targetMod, targetCharacter.DirectoryPath);
            _activeMod = null;
            _previewImages.Clear();
            _previewIndex = -1;
            _allMods = _modService.ScanMods(targetCharacter.DirectoryPath, targetCharacter.IsUngroupedGroup);
            ApplyFilter();
            // Keep the character's displayed local Mod count in sync without
            // rebuilding the entire character list or losing its selection.
            var characterIndex = _characters.ToList().FindIndex(character => ReferenceEquals(character, _activeCharacter));
            if (characterIndex >= 0)
            {
                var refreshedCharacters = _modService.ScanCharacters(_charactersRoot!).ToList();
                foreach (var character in refreshedCharacters)
                    character.UseEnglishAvailability = _text.IsEnglish;
                LoadCharacterAvatars(refreshedCharacters);
                _characters = new ObservableCollection<CharacterEntry>(refreshedCharacters);
                _activeCharacter = _characters.FirstOrDefault(character =>
                    character.FolderName.Equals(targetCharacter.FolderName, StringComparison.OrdinalIgnoreCase));
                ApplyCharacterFilter(_activeCharacter);
            }

            ClearDetails();
            if (_allMods.Count > 0)
            {
                ModListBox.SelectedIndex = Math.Clamp(previousIndex, 0, _allMods.Count - 1);
            }
            SetStatus(_text.ModDeleted(deletedName));
        }
        catch (Exception ex)
        {
            SetStatus($"删除 Mod 失败：{ex.Message}", error: true);
            if (_activeCharacter is not null)
            {
                _allMods = _modService.ScanMods(_activeCharacter.DirectoryPath, _activeCharacter.IsUngroupedGroup);
                ApplyFilter();
            }
        }
    }

    private async void OnMoveSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (_charactersRoot is null || _activeCharacter is null || _isImportBusy) return;
        if (!SaveReadmeIfDirty(showStatus: true)) return;
        var root = _charactersRoot;
        var sourceGroup = _activeCharacter;
        var selected = ModListBox.SelectedItems?.OfType<ModEntry>().ToList() ?? [];
        if (selected.Count == 0) return;
        try
        {
            var destinations = _modService.ScanCharacters(root)
                .Where(group => !group.IsUngroupedGroup && !group.IsPathOccupiedByRootMod
                    && !group.DirectoryPath.Equals(sourceGroup.DirectoryPath, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var group in destinations) group.UseEnglishAvailability = _text.IsEnglish;
            if (destinations.Count == 0)
            {
                SetStatus(_text.IsEnglish ? "Create another group first." : "请先创建另一个分组。", error: true);
                return;
            }
            var target = await MoveModsDialog.ShowAsync(this, _text, selected.Count, destinations);
            if (target is null || _charactersRoot != root) return;
            if (!Directory.Exists(target.DirectoryPath))
                _modService.CreateModGroup(root, Path.GetFileName(target.DirectoryPath));
            SetImportBusy(true);
            ClearPreviewBitmapCache();
            var failures = new List<string>();
            var moved = 0;
            foreach (var mod in selected)
            {
                try { _modService.MoveMod(root, mod, target.DirectoryPath); moved++; }
                catch (Exception ex) { failures.Add($"{mod.DisplayName}: {_text.TranslateError(ex.Message)}"); }
            }
            _activeMod = null;
            ClearDetails();
            LoadRoot(root, failures.Count == 0 ? target.FolderName : sourceGroup.FolderName);
            SetStatus((_text.IsEnglish ? $"Moved {moved}/{selected.Count} Mod(s) to {target.DisplayName}."
                : $"已移动 {moved}/{selected.Count} 个 Mod 到 {target.DisplayName}。")
                + (failures.Count == 0 ? "" : " " + string.Join("；", failures)), error: failures.Count > 0);
        }
        catch (Exception ex) { SetStatus(_text.TranslateError(ex.Message), error: true); }
        finally { SetImportBusy(false); }
    }

    private void LoadDetails(ModEntry mod)
    {
        ClearPreviewBitmapCache();
        UpdateDetailHeader(mod);
        SetReadmeContent(string.Empty);
        _previewImages.Clear();
        ShowPreviewImage(-1);
        try
        {
            SetReadmeContent(_modService.LoadReadme(mod.DirectoryPath));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
        }
        try
        {
            ReloadPreviewImages(0);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    private void UpdateDetailHeader(ModEntry mod)
    {
        ModNameTextBox.Text = mod.DisplayName;
        DetailPathText.Text = mod.DirectoryPath;
        DetailStateText.Text = mod.IsEnabled
            ? (_text.IsEnglish ? "Activated" : "已激活")
            : (_text.IsEnglish ? "Disabled" : "已停用");
        DetailStateText.Foreground = mod.IsEnabled ? Brushes.LightGreen : Brushes.LightGray;
        UpdateBusyControls();
    }

    private void ClearDetails()
    {
        CloseFullscreenPreview();
        ClearPreviewBitmapCache();
        _activeMod = null;
        ModNameTextBox.Text = string.Empty;
        ModNameTextBox.IsEnabled = false;
        SaveModNameButton.IsEnabled = false;
        OpenModFolderMenuItem.IsEnabled = false;
        DeleteModMenuItem.IsEnabled = false;
        AddImagesButton.IsEnabled = false;
        DeleteImageMenuItem.IsEnabled = false;
        SaveReadmeButton.IsEnabled = false;
        ReadmeTextBox.IsEnabled = false;
        DetailPathText.Text = _text.DetailPlaceholder;
        DetailStateText.Text = _text.StateNone;
        SetReadmeContent(string.Empty);
        _previewImages.Clear();
        ShowPreviewImage(-1);
    }

    private void OnReadmeTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_loadingReadme || _activeMod is null) return;
        UpdateReadmeDirtyState();
    }

    private void OnSaveReadmeClick(object? sender, RoutedEventArgs e) => SaveReadmeIfDirty(showStatus: true, force: true);

    private bool SaveReadmeIfDirty(bool showStatus, bool force = false)
    {
        UpdateReadmeDirtyState();
        if (_activeMod is null || (!_readmeDirty && !force)) return true;
        try
        {
            var currentText = ReadmeTextBox.Text ?? string.Empty;
            var path = _modService.SaveReadme(_activeMod.DirectoryPath, currentText);
            _savedReadmeText = currentText;
            SetReadmeDirty(false);
            if (showStatus) SetStatus(_text.SavedNotes(path));
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"说明保存失败：{ex.Message}", error: true);
            return false;
        }
    }

    private void SetReadmeContent(string content)
    {
        _loadingReadme = true;
        try
        {
            ReadmeTextBox.Text = content;
            // 以控件实际接受的文本为基准，兼容控件可能执行的换行规范化。
            _savedReadmeText = ReadmeTextBox.Text ?? string.Empty;
            SetReadmeDirty(false);
        }
        finally
        {
            _loadingReadme = false;
        }
    }

    private void UpdateReadmeDirtyState()
    {
        if (_activeMod is null)
        {
            SetReadmeDirty(false);
            return;
        }

        SetReadmeDirty(!string.Equals(ReadmeTextBox.Text ?? string.Empty, _savedReadmeText, StringComparison.Ordinal));
    }

    private void SetReadmeDirty(bool dirty)
    {
        _readmeDirty = dirty;
        ReadmeDirtyText.Text = dirty ? _text.UnsavedChanges : string.Empty;
    }

    private void RestoreSelection(ListBox control, object? previousSelection)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _suppressSelectionChanges = true;
            control.SelectedItem = previousSelection;
            _suppressSelectionChanges = false;
        }, DispatcherPriority.Input);
    }

    private async void OnAddImagesClick(object? sender, RoutedEventArgs e)
    {
        if (_activeMod is null)
        {
            SetStatus(_text.NoMod, error: true);
            return;
        }
        var targetMod = _activeMod;
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _text.PreviewPickerTitle,
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(_text.ImageFileType) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" } }
                }
            });
        }
        catch (Exception ex)
        {
            SetStatus($"{(_text.IsEnglish ? "Failed to open image picker" : "打开图片选择器失败")}：{ex.Message}", error: true);
            return;
        }
        if (files.Count == 0) return;
        if (!ReferenceEquals(targetMod, _activeMod))
        {
            SetStatus(_text.ModChanged, error: true);
            return;
        }

        try
        {
            var added = _modService.AddPreviewImages(targetMod.DirectoryPath, files.Select(file => file.Path.LocalPath));
            ReloadPreviewImages(Math.Max(0, _previewImages.Count));
            SetStatus(added.Count == 0 ? _text.NoImagesAdded : _text.ImagesAdded(added.Count));
        }
        catch (Exception ex)
        {
            SetStatus($"添加图片失败：{ex.Message}", error: true);
        }
    }

    private async void OnDeleteImageClick(object? sender, RoutedEventArgs e)
    {
        if (_activeMod is null || _previewIndex < 0 || _previewIndex >= _previewImages.Count)
        {
            SetStatus(_text.NoImageToDelete, error: true);
            return;
        }
        var path = _previewImages[_previewIndex];
        var confirmed = await ConfirmDialog.ShowAsync(this, _text.DeletePreviewTitle,
            _text.DeletePreviewConfirm(Path.GetFileName(path)), _text.Delete, _text.Cancel);
        if (!confirmed) return;

        try
        {
            ReleaseCurrentBitmap();
            _modService.DeletePreviewImage(_activeMod.DirectoryPath, path);
            var nextIndex = Math.Min(_previewIndex, _previewImages.Count - 2);
            ReloadPreviewImages(nextIndex);
            SetStatus(_text.ImageDeleted(Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            SetStatus($"删除图片失败：{ex.Message}", error: true);
            ReloadPreviewImages(_previewIndex);
        }
    }

    private void OnPreviousImageClick(object? sender, RoutedEventArgs e) => MovePreview(-1);
    private void OnNextImageClick(object? sender, RoutedEventArgs e) => MovePreview(1);
    private void OnFullscreenPreviousClick(object? sender, RoutedEventArgs e) => MovePreview(-1);
    private void OnFullscreenNextClick(object? sender, RoutedEventArgs e) => MovePreview(1);

    private void MovePreview(int direction)
    {
        if (_previewImages.Count == 0) return;
        ShowPreviewImage((_previewIndex + direction + _previewImages.Count) % _previewImages.Count);
    }

    private void ReloadPreviewImages(int preferredIndex)
    {
        if (_activeMod is null) return;
        _previewImages = _modService.GetPreviewImages(_activeMod.DirectoryPath).ToList();
        PrunePreviewBitmapCache();
        _activeMod.PreviewCount = _previewImages.Count;
        ShowPreviewImage(_previewImages.Count == 0 ? -1 : Math.Clamp(preferredIndex, 0, _previewImages.Count - 1));
    }

    private async void ShowPreviewImage(int index)
    {
        _previewLoadCancellation?.Cancel();
        var loadGeneration = ++_previewLoadGeneration;
        ReleaseCurrentBitmap();
        _previewIndex = index;
        UpdateBusyControls();
        if (index < 0 || index >= _previewImages.Count)
        {
            NoPreviewText.Text = _text.NoPreview;
            NoPreviewText.IsVisible = true;
            PreviewPositionText.Text = "0 / 0";
            PreviewFileNameText.Text = string.Empty;
            FullscreenPositionText.Text = "0 / 0";
            FullscreenFileNameText.Text = string.Empty;
            if (FullscreenPreviewOverlay.IsVisible) CloseFullscreenPreview();
            return;
        }

        var loadCancellation = new CancellationTokenSource();
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            loadCancellation.Token,
            _previewCacheCancellation.Token);
        _previewLoadCancellation = linkedCancellation;
        var cancellationToken = linkedCancellation.Token;
        var imagePath = _previewImages[index];
        NoPreviewText.Text = _text.LoadingPreview;
        NoPreviewText.IsVisible = true;
        Exception? lastError = null;
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var bitmap = TryGetCachedPreviewBitmap(imagePath)
                        ?? await DecodePreviewBitmapAsync(imagePath, PreviewDecodeWidth, cancellationToken);
                    if (loadGeneration != _previewLoadGeneration || cancellationToken.IsCancellationRequested)
                    {
                        if (!_previewBitmapCache.Values.Any(entry => ReferenceEquals(entry.Bitmap, bitmap)))
                            bitmap.Dispose();
                        return;
                    }

                    _currentBitmap = AddPreviewBitmapToCache(imagePath, bitmap);
                    PreviewImageControl.Source = _currentBitmap;
                    FullscreenImageControl.Source = _currentBitmap;
                    NoPreviewText.IsVisible = false;
                    PreviewPositionText.Text = $"{index + 1} / {_previewImages.Count}";
                    FullscreenPositionText.Text = $"{index + 1} / {_previewImages.Count}";
                    PreviewFileNameText.Text = Path.GetFileName(_previewImages[index]);
                    FullscreenFileNameText.Text = Path.GetFileName(_previewImages[index]);
                    PrefetchAdjacentPreviewImages(index);
                    if (FullscreenPreviewOverlay.IsVisible)
                        LoadFullscreenImage(imagePath, loadGeneration);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt < 2) await Task.Delay(120 * (attempt + 1), cancellationToken);
                }
            }

            if (loadGeneration == _previewLoadGeneration)
            {
                NoPreviewText.IsVisible = true;
                NoPreviewText.Text = _text.ImageUnreadable;
                PreviewPositionText.Text = $"{index + 1} / {_previewImages.Count}";
                PreviewFileNameText.Text = Path.GetFileName(_previewImages[index]);
                FullscreenPositionText.Text = $"{index + 1} / {_previewImages.Count}";
                FullscreenFileNameText.Text = Path.GetFileName(_previewImages[index]);
                if (FullscreenPreviewOverlay.IsVisible) CloseFullscreenPreview();
                SetStatus(_text.ImageReadFailed(lastError?.Message ?? string.Empty), error: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 新的图片加载已接管界面。
        }
        finally
        {
            if (ReferenceEquals(_previewLoadCancellation, linkedCancellation)) _previewLoadCancellation = null;
            linkedCancellation.Dispose();
            loadCancellation.Dispose();
        }
    }

    private static Task<Bitmap> DecodePreviewBitmapAsync(
        string path,
        int? decodeWidth,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > ModService.MaximumPreviewImageBytes)
                throw new IOException($"预览图片超过 100 MB 限制：{fileInfo.Name}");

            Bitmap? bitmap = null;
            try
            {
                var sourceWidth = 0;
                if (decodeWidth is not null)
                {
                    using var metadataSource = OpenPreviewImageReadStream(path);
                    using var codec = SKCodec.Create(metadataSource);
                    sourceWidth = codec?.Info.Width ?? 0;
                }
                using var source = OpenPreviewImageReadStream(path);
                bitmap = decodeWidth is int width && sourceWidth > width
                    ? Bitmap.DecodeToWidth(source, width, BitmapInterpolationMode.MediumQuality)
                    : new Bitmap(source);
                cancellationToken.ThrowIfCancellationRequested();
                return bitmap;
            }
            catch
            {
                bitmap?.Dispose();
                throw;
            }
        }, cancellationToken);
    }

    private static FileStream OpenPreviewImageReadStream(string path)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            FileOptions.SequentialScan);

    private Bitmap? TryGetCachedPreviewBitmap(string path)
    {
        if (!_previewBitmapCache.TryGetValue(path, out var entry)) return null;
        if (!TryGetPreviewFileStamp(path, out var length, out var lastWriteTimeUtcTicks)
            || entry.Length != length
            || entry.LastWriteTimeUtcTicks != lastWriteTimeUtcTicks)
        {
            RemovePreviewBitmapFromCache(path);
            return null;
        }

        _previewBitmapLru.Remove(entry.LruNode);
        _previewBitmapLru.AddLast(entry.LruNode);
        return entry.Bitmap;
    }

    private Bitmap AddPreviewBitmapToCache(string path, Bitmap bitmap)
    {
        if (!TryGetPreviewFileStamp(path, out var length, out var lastWriteTimeUtcTicks))
            return bitmap;

        if (_previewBitmapCache.TryGetValue(path, out var existing))
        {
            if (existing.Length == length && existing.LastWriteTimeUtcTicks == lastWriteTimeUtcTicks)
            {
                if (!ReferenceEquals(existing.Bitmap, bitmap)) bitmap.Dispose();
                _previewBitmapLru.Remove(existing.LruNode);
                _previewBitmapLru.AddLast(existing.LruNode);
                return existing.Bitmap;
            }
            RemovePreviewBitmapFromCache(path);
        }

        var node = _previewBitmapLru.AddLast(path);
        _previewBitmapCache[path] = new PreviewBitmapCacheEntry(bitmap, length, lastWriteTimeUtcTicks, node);
        TrimPreviewBitmapCache(path);
        return bitmap;
    }

    private void PrefetchAdjacentPreviewImages(int index)
    {
        if (_previewImages.Count < 2) return;
        var generation = _previewLoadGeneration;
        var adjacentIndices = new[]
        {
            (index - 1 + _previewImages.Count) % _previewImages.Count,
            (index + 1) % _previewImages.Count
        };
        foreach (var adjacentIndex in adjacentIndices.Distinct())
        {
            var path = _previewImages[adjacentIndex];
            if (_previewBitmapCache.ContainsKey(path) || !_previewPrefetchInFlight.Add(path)) continue;
            _ = PrefetchPreviewImageAsync(path, generation, _previewCacheCancellation.Token);
        }
    }

    private async Task PrefetchPreviewImageAsync(string path, int generation, CancellationToken cancellationToken)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = await DecodePreviewBitmapAsync(path, PreviewDecodeWidth, cancellationToken);
            if (generation != _previewLoadGeneration || cancellationToken.IsCancellationRequested)
                return;
            bitmap = AddPreviewBitmapToCache(path, bitmap);
            bitmap = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 切换 Mod 或关闭窗口后，不再保留旧预览的后台解码结果。
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"预加载预览图失败：{Path.GetFileName(path)}，{ex.Message}");
        }
        finally
        {
            bitmap?.Dispose();
            _previewPrefetchInFlight.Remove(path);
        }
    }

    private void PrunePreviewBitmapCache()
    {
        var available = _previewImages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _previewBitmapCache.Keys.Where(path => !available.Contains(path)).ToList())
            RemovePreviewBitmapFromCache(path);
    }

    private void TrimPreviewBitmapCache(string protectedPath)
    {
        while (_previewBitmapCache.Count > PreviewBitmapCacheCapacity)
        {
            var candidate = _previewBitmapLru.First;
            while (candidate is not null
                   && (candidate.Value.Equals(protectedPath, StringComparison.OrdinalIgnoreCase)
                       || (_previewIndex >= 0
                           && _previewIndex < _previewImages.Count
                           && candidate.Value.Equals(_previewImages[_previewIndex], StringComparison.OrdinalIgnoreCase))))
            {
                candidate = candidate.Next;
            }
            if (candidate is null) return;
            RemovePreviewBitmapFromCache(candidate.Value);
        }
    }

    private void RemovePreviewBitmapFromCache(string path)
    {
        if (!_previewBitmapCache.Remove(path, out var entry)) return;
        _previewBitmapLru.Remove(entry.LruNode);
        DisposeBitmapLater(entry.Bitmap);
    }

    private void ClearPreviewBitmapCache()
    {
        _previewLoadCancellation?.Cancel();
        _fullscreenLoadCancellation?.Cancel();
        _previewCacheCancellation.Cancel();
        _previewCacheCancellation.Dispose();
        _previewCacheCancellation = new CancellationTokenSource();
        _previewLoadGeneration++;
        ReleaseCurrentBitmap();
        foreach (var entry in _previewBitmapCache.Values) DisposeBitmapLater(entry.Bitmap);
        _previewBitmapCache.Clear();
        _previewBitmapLru.Clear();
        _previewPrefetchInFlight.Clear();
    }

    private static bool TryGetPreviewFileStamp(string path, out long length, out long lastWriteTimeUtcTicks)
    {
        try
        {
            var info = new FileInfo(path);
            info.Refresh();
            length = info.Length;
            lastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks;
            return info.Exists && length <= ModService.MaximumPreviewImageBytes;
        }
        catch
        {
            length = 0;
            lastWriteTimeUtcTicks = 0;
            return false;
        }
    }

    private void OnPreviewImageClick(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (!OpenFullscreenPreview()) return;
        e.Handled = true;
    }

    private void OnPreviewImageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || !OpenFullscreenPreview()) return;
        e.Handled = true;
    }

    private bool OpenFullscreenPreview()
    {
        if (_currentBitmap is null || _previewIndex < 0 || _previewIndex >= _previewImages.Count) return false;
        FullscreenPreviewOverlay.IsVisible = true;
        FullscreenImageControl.Source = _currentBitmap;
        SetFullscreenZoom(1, absolute: true);
        LoadFullscreenImage(_previewImages[_previewIndex], _previewLoadGeneration);
        FullscreenPreviewOverlay.Focus();
        Dispatcher.UIThread.Post(
            () => FullscreenPreviewOverlay.Focus(),
            DispatcherPriority.Input);
        return true;
    }

    private void OnFullscreenCloseClick(object? sender, RoutedEventArgs e) => CloseFullscreenPreview();
    private void OnFullscreenZoomInClick(object? sender, RoutedEventArgs e) => SetFullscreenZoom(0.25);
    private void OnFullscreenZoomOutClick(object? sender, RoutedEventArgs e) => SetFullscreenZoom(-0.25);

    private void OnFullscreenPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        SetFullscreenZoom(e.Delta.Y > 0 ? 0.25 : -0.25);
        e.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!FullscreenPreviewOverlay.IsVisible) return;
        switch (e.Key)
        {
            case Key.Escape:
                CloseFullscreenPreview();
                break;
            case Key.Left:
                MovePreview(-1);
                break;
            case Key.Right:
                MovePreview(1);
                break;
            case Key.Add:
            case Key.OemPlus:
                SetFullscreenZoom(0.25);
                break;
            case Key.Subtract:
            case Key.OemMinus:
                SetFullscreenZoom(-0.25);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void SetFullscreenZoom(double value, bool absolute = false)
    {
        _fullscreenZoom = Math.Clamp(absolute ? value : _fullscreenZoom + value, 0.5, 4);
        _fullscreenTransform.ScaleX = _fullscreenZoom;
        _fullscreenTransform.ScaleY = _fullscreenZoom;
    }

    private void CloseFullscreenPreview()
    {
        FullscreenPreviewOverlay.IsVisible = false;
        _fullscreenLoadCancellation?.Cancel();
        FullscreenImageControl.Source = _currentBitmap;
        var bitmapToDispose = _fullscreenBitmap;
        _fullscreenBitmap = null;
        if (bitmapToDispose is not null) DisposeBitmapLater(bitmapToDispose);
        SetFullscreenZoom(1, absolute: true);
        PreviewImageControl.Focus();
    }

    private async void LoadFullscreenImage(string path, int generation)
    {
        _fullscreenLoadCancellation?.Cancel();
        var loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_previewCacheCancellation.Token);
        _fullscreenLoadCancellation = loadCancellation;
        var cancellationToken = loadCancellation.Token;
        Bitmap? decoded = null;
        try
        {
            Exception? lastError = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    decoded = await DecodePreviewBitmapAsync(path, decodeWidth: null, cancellationToken);
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt < 2) await Task.Delay(120 * (attempt + 1), cancellationToken);
                }
            }

            if (decoded is null)
            {
                Debug.WriteLine($"原始预览图加载失败：{Path.GetFileName(path)}，{lastError?.Message}");
                return;
            }
            if (!FullscreenPreviewOverlay.IsVisible
                || generation != _previewLoadGeneration
                || _previewIndex < 0
                || _previewIndex >= _previewImages.Count
                || !_previewImages[_previewIndex].Equals(path, StringComparison.OrdinalIgnoreCase)
                || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var previous = _fullscreenBitmap;
            _fullscreenBitmap = decoded;
            decoded = null;
            FullscreenImageControl.Source = _fullscreenBitmap;
            if (previous is not null) DisposeBitmapLater(previous);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 关闭全屏或切换图片时保留缩略预览，不显示错误页。
        }
        finally
        {
            decoded?.Dispose();
            if (ReferenceEquals(_fullscreenLoadCancellation, loadCancellation)) _fullscreenLoadCancellation = null;
            loadCancellation.Dispose();
        }
    }

    private void ReleaseCurrentBitmap()
    {
        PreviewImageControl.Source = null;
        FullscreenImageControl.Source = null;
        _fullscreenLoadCancellation?.Cancel();
        var fullscreenBitmapToDispose = _fullscreenBitmap;
        _fullscreenBitmap = null;
        if (fullscreenBitmapToDispose is not null) DisposeBitmapLater(fullscreenBitmapToDispose);
        var bitmapToDispose = _currentBitmap;
        _currentBitmap = null;
        if (bitmapToDispose is not null
            && !_previewBitmapCache.Values.Any(entry => ReferenceEquals(entry.Bitmap, bitmapToDispose)))
            DisposeBitmapLater(bitmapToDispose);
    }

    private static void DisposeBitmapLater(Bitmap bitmap)
        => DispatcherTimer.RunOnce(bitmap.Dispose, TimeSpan.FromMilliseconds(250));

    private sealed record PreviewBitmapCacheEntry(
        Bitmap Bitmap,
        long Length,
        long LastWriteTimeUtcTicks,
        LinkedListNode<string> LruNode);

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = _text.TranslateError(message);
        StatusText.Foreground = error ? Brushes.LightCoral : Brushes.LightSlateGray;
    }

    private static string? FindLikelyModsRoot()
        => ModService.FindRelativeCharactersRoot(new[] { AppContext.BaseDirectory, Environment.CurrentDirectory });
}

internal static class ConfirmDialog
{
    public static async Task<bool> ShowAsync(Window owner, string title, string message, string confirmText, string cancelText)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 430,
            Height = 210,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#151821")
        };

        var cancel = new Button { Content = cancelText, MinWidth = 90 };
        var confirm = new Button { Content = confirmText, MinWidth = 90, Classes = { "danger" } };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);

        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15 },
                new StackPanel
                {
                    [Grid.RowProperty] = 1,
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, confirm }
                }
            }
        };
        return await dialog.ShowDialog<bool>(owner);
    }
}

internal static class LanguageDialog
{
    public static async Task<AppLanguage?> ShowAsync(Window owner, UiStrings text)
    {
        var dialog = new Window
        {
            Title = "语言 / Language",
            Width = 430,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#151821")
        };

        var chinese = new Button { Content = text.ChineseLanguage, MinWidth = 100 };
        var english = new Button { Content = text.EnglishLanguage, MinWidth = 100 };
        (text.Language == AppLanguage.Chinese ? chinese : english).Classes.Add("accent");
        chinese.Click += (_, _) => dialog.Close(AppLanguage.Chinese);
        english.Click += (_, _) => dialog.Close(AppLanguage.English);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = text.SelectLanguageTitle, FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = text.SelectLanguageMessage, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { chinese, english }
                }
            }
        };
        return await dialog.ShowDialog<AppLanguage?>(owner);
    }
}

internal sealed record PasswordDialogResult(string? Password, bool RevealPassword);

internal static class PasswordDialog
{
    public static async Task<PasswordDialogResult> ShowAsync(
        Window owner,
        UiStrings text,
        string archiveName,
        bool revealPassword,
        bool retry)
    {
        var dialog = new Window
        {
            Title = text.PasswordTitle,
            Width = 460,
            Height = 245,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#151821")
        };

        var passwordBox = new TextBox
        {
            [Grid.RowProperty] = 2,
            Watermark = text.PasswordWatermark,
            PasswordChar = revealPassword ? '\0' : '•',
            RevealPassword = revealPassword,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        var showPassword = new CheckBox { Content = text.ShowPassword, IsChecked = revealPassword };
        showPassword.IsCheckedChanged += (_, _) =>
        {
            var show = showPassword.IsChecked == true;
            passwordBox.RevealPassword = show;
            // Explicitly changing PasswordChar keeps the behavior consistent
            // across Avalonia themes where RevealPassword may only affect the
            // built-in reveal button.
            passwordBox.PasswordChar = show ? '\0' : '•';
        };
        var cancel = new Button { Content = text.Cancel, MinWidth = 90 };
        var continueButton = new Button { Content = text.PasswordContinue, MinWidth = 90, Classes = { "accent" } };
        cancel.Click += (_, _) => dialog.Close(new PasswordDialogResult(null, showPassword.IsChecked == true));
        continueButton.Click += (_, _) => dialog.Close(new PasswordDialogResult(passwordBox.Text ?? string.Empty, showPassword.IsChecked == true));
        passwordBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            dialog.Close(new PasswordDialogResult(passwordBox.Text ?? string.Empty, showPassword.IsChecked == true));
        };

        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(24),
            RowSpacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = text.PasswordTitle,
                    FontSize = 20,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    [Grid.RowProperty] = 1,
                    Text = retry ? text.PasswordRetryMessage(archiveName) : text.PasswordMessage(archiveName),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.LightSlateGray
                },
                passwordBox,
                new Border
                {
                    [Grid.RowProperty] = 3,
                    Child = showPassword
                },
                new StackPanel
                {
                    [Grid.RowProperty] = 4,
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, continueButton }
                }
            }
        };
        dialog.Opened += (_, _) => passwordBox.Focus();
        return await dialog.ShowDialog<PasswordDialogResult>(owner)
            ?? new PasswordDialogResult(null, revealPassword);
    }
}
