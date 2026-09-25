namespace OWWMM.Services;

public enum AppLanguage
{
    Chinese,
    English
}

public sealed class UiStrings
{
    public UiStrings(AppLanguage language) => Language = language;

    public AppLanguage Language { get; }
    public bool IsEnglish => Language == AppLanguage.English;

    public string WindowTitle => "One WuWa Mod Manager";
    public string HeaderSubtitle => IsEnglish ? "OWWMM · Manage WWMI Mods by group" : "OWWMM · 按分组管理 WWMI 模组";
    public string RootPathWatermark => IsEnglish ? "Select a WWMI\\Mods folder" : "请选择 WWMI\\Mods 文件夹";
    public string ChooseFolder => IsEnglish ? "Choose folder" : "选择文件夹";
    public string ChangeFolder => IsEnglish ? "Change folder" : "更改文件夹";
    public string ChooseFolderTooltip => IsEnglish
        ? "The selected folder is saved automatically and restored next time"
        : "选择成功后会自动保存，下次启动优先恢复此目录";
    public string DropArchiveTooltip => IsEnglish
        ? "Drop a 7z/zip/rar/tar archive anywhere in this window to import it"
        : "可将 7z/zip/rar/tar 压缩包拖入窗口任意位置导入";
    public string Refresh => IsEnglish ? "Refresh" : "刷新";
    public string LanguageTooltip => IsEnglish ? "Switch interface language" : "切换界面语言";
    public string CharacterHeader => "Mod";
    public string AddModGroup => IsEnglish ? "Create Mod group" : "新建 Mod 分组";
    public string RenameModGroup => IsEnglish ? "Edit selected group" : "编辑所选分组";
    public string DeleteModGroup => IsEnglish ? "Delete selected empty group" : "删除所选空分组";
    public string ResetGroupIcon => IsEnglish ? "Reset selected group icon" : "重置所选分组图标";
    public string AddModGroupTitle => IsEnglish ? "Create Mod group" : "新建 Mod 分组";
    public string AddModGroupMessage => IsEnglish
        ? "Create a first-level folder under WWMI\\Mods. Its direct child folders will appear as Mods."
        : "在 WWMI\\Mods 下创建一级目录；其中的直属子目录会作为 Mod 显示。";
    public string AddModGroupWatermark => IsEnglish ? "Group folder name" : "分组文件夹名称";
    public string Create => IsEnglish ? "Create" : "创建";
    public string ModGroupCreated(string name) => IsEnglish ? $"Mod group created: {name}" : $"已创建 Mod 分组：{name}";
    public string RenameModGroupTitle => IsEnglish ? "Edit Mod group" : "编辑 Mod 分组";
    public string RenameModGroupMessage(string name) => IsEnglish
        ? $"Enter a new folder name for “{name}”."
        : $"请输入分组“{name}”的新文件夹名称。";
    public string ModGroupRenamed(string name) => IsEnglish ? $"Mod group updated: {name}" : $"Mod 分组已更新：{name}";
    public string DeleteModGroupTitle => IsEnglish ? "Delete empty Mod group" : "删除空 Mod 分组";
    public string DeleteModGroupConfirm(string name) => IsEnglish
        ? $"Delete the empty Mod group “{name}”? Only an empty group can be deleted."
        : $"确定删除空 Mod 分组“{name}”吗？仅空分组可以删除。";
    public string ModGroupDeleted(string name) => IsEnglish ? $"Deleted Mod group: {name}" : $"已删除 Mod 分组：{name}";
    public string GroupIconReset(string name) => IsEnglish ? $"Reset group icon: {name}" : $"已重置分组图标：{name}";
    public string CategoryIconPickerTitle => IsEnglish ? "Select a group icon" : "选择分组图标";
    public string CropIconTitle => IsEnglish ? "Crop group icon" : "裁剪分组图标";
    public string CropIconMessage => IsEnglish
        ? "Drag the square to choose the visible area. Use the slider or mouse wheel to resize it."
        : "拖动方框选择要显示的区域；使用滑块或鼠标滚轮调整选区大小。";
    public string SaveIcon => IsEnglish ? "Save icon" : "保存图标";
    public string CategoryIconSaved(string name) => IsEnglish ? $"Icon updated: {name}" : $"已更新图标：{name}";
    public string CategoryIconUnavailable => IsEnglish
        ? "This virtual or occupied entry cannot use a group icon."
        : "虚拟分组或被根目录 Mod 占用的条目不能设置分组图标。";
    public string SearchCharacterWatermark => IsEnglish ? "Search groups…" : "搜索分组…";
    public string CharacterStructureHint => IsEnglish
        ? "First-level folders under WWMI\\Mods are groups. A folder with a top-level .ini file appears under Ungrouped Mods."
        : "WWMI\\Mods 下的一级目录通常是分组；自身含顶层 .ini 文件的目录会归入“未分类 Mod”。";
    public string ModFallbackTitle => IsEnglish ? "Mod" : "Mod";
    public string SelectCharacter => IsEnglish ? "Select a Mod group" : "请选择 Mod 分组";
    public string ImportMod => IsEnglish ? "Import Mod" : "导入 Mod";
    public string SearchModWatermark => IsEnglish ? "Search Mod…" : "搜索 Mod…";
    public string ActivateSelected => IsEnglish ? "Activate selected" : "激活所选";
    public string DisableSelected => IsEnglish ? "Disable selected" : "停用所选";
    public string MoveSelected => IsEnglish ? "Move to group…" : "移动到分组…";
    public string BatchSelectionTooltip => IsEnglish
        ? "Use Ctrl or Shift to select multiple Mods"
        : "使用 Ctrl 或 Shift 多选 Mod";
    public string SelectModsForBatch => IsEnglish ? "Select one or more Mods first." : "请先选择一个或多个 Mod。";
    public string BatchToggleStatus(int succeeded, int failed, bool enabled) => IsEnglish
        ? $"{(enabled ? "Activated" : "Disabled")} {succeeded} selected Mod(s)"
          + (failed > 0 ? $"; {failed} failed." : ".")
        : $"已{(enabled ? "激活" : "停用")} {succeeded} 个所选 Mod"
          + (failed > 0 ? $"；{failed} 个失败。" : "。");
    public string SelectModWatermark => IsEnglish ? "Select a Mod" : "选择一个 Mod";
    public string DetailPlaceholder => IsEnglish ? "Preview images and notes appear here" : "预览图和说明会显示在这里";
    public string SaveName => IsEnglish ? "Save name" : "保存名称";
    public string OpenFolder => IsEnglish ? "Open folder" : "打开目录";
    public string DeleteMod => IsEnglish ? "Delete Mod" : "删除 Mod";
    public string StateNone => IsEnglish ? "Not selected" : "未选择";
    public string PreviousImage => IsEnglish ? "‹ Previous" : "‹ 上一张";
    public string NextImage => IsEnglish ? "Next ›" : "下一张 ›";
    public string AddImage => IsEnglish ? "Add image" : "添加图片";
    public string DeleteCurrentImage => IsEnglish ? "Delete image" : "删除当前图片";
    public string Readme => "README";
    public string UnsavedChanges => IsEnglish ? "Unsaved changes" : "有未保存修改";
    public string SaveReadme => IsEnglish ? "Save notes" : "保存说明";
    public string ReadmeWatermark => IsEnglish ? "Write Preview/readme.txt here…" : "在这里编写 Preview/readme.txt…";
    public string FooterHint => IsEnglish
        ? "Reload WWMI (F10) after switching, or restart the game"
        : "切换后重新加载 WWMI（F10）或重启游戏生效";
    public string ZoomOut => IsEnglish ? "Zoom out" : "缩小";
    public string ZoomIn => IsEnglish ? "Zoom in" : "放大";
    public string Close => IsEnglish ? "Close (Esc)" : "关闭 (Esc)";
    public string Previous => IsEnglish ? "Previous" : "上一张";
    public string Next => IsEnglish ? "Next" : "下一张";
    public string Ready => IsEnglish ? "Ready" : "准备就绪";
    public string ChooseFolderDialogTitle => IsEnglish
        ? "Select the WWMI\\Mods folder"
        : "选择 WWMI\\Mods 文件夹";
    public string PreviewPickerTitle => IsEnglish ? "Select preview images" : "选择预览图片";
    public string ImageFileType => IsEnglish ? "Images" : "图片";
    public string ArchiveFileType => IsEnglish ? "7-Zip archives" : "7-Zip 支持的压缩包";
    public string PasswordTitle => IsEnglish ? "Archive password required" : "压缩包需要密码";
    public string PasswordMessage(string archiveName) => IsEnglish
        ? $"Enter the password for “{archiveName}”."
        : $"请输入压缩包“{archiveName}”的密码。";
    public string PasswordRetryMessage(string archiveName) => IsEnglish
        ? $"The password was incorrect. Enter it again for “{archiveName}”."
        : $"密码不正确，请重新输入压缩包“{archiveName}”的密码。";
    public string PasswordWatermark => IsEnglish ? "Password" : "密码";
    public string ShowPassword => IsEnglish ? "Show password" : "显示密码";
    public string PasswordContinue => IsEnglish ? "Continue" : "继续";
    public string ImportCancelled => IsEnglish ? "Import cancelled." : "已取消导入。";
    public string UnsupportedArchive => IsEnglish
        ? "Drop a supported Mod archive (.7z, .zip, .rar or .tar)."
        : "请拖入支持的 Mod 压缩包（.7z、.zip、.rar 或 .tar）。";
    public string ImportFailed(string details) => IsEnglish
        ? $"Import failed: {details}"
        : $"导入失败：{details}";
    public string PasswordAttemptsExceeded => IsEnglish
        ? "The archive password was incorrect too many times; import was cancelled."
        : "压缩包密码错误次数过多，已取消导入。";
    public string DeletePreviewTitle => IsEnglish ? "Delete preview image" : "删除预览图";
    public string DeletePreviewConfirm(string fileName) => IsEnglish
        ? $"Permanently delete “{fileName}”?\nThis action cannot be undone."
        : $"确定永久删除“{fileName}”吗？\n此操作无法撤销。";
    public string Delete => IsEnglish ? "Delete" : "删除";
    public string Cancel => IsEnglish ? "Cancel" : "取消";
    public string DeleteModTitle => IsEnglish ? "Delete Mod" : "删除 Mod";
    public string DeleteModConfirm(string name, bool hasUnsavedNotes = false) => IsEnglish
        ? $"Permanently delete Mod “{name}” and all of its files?\nThis action cannot be undone."
          + (hasUnsavedNotes ? "\nUnsaved README changes will also be discarded." : string.Empty)
        : $"确定永久删除 Mod“{name}”及其全部文件吗？\n此操作无法撤销。"
          + (hasUnsavedNotes ? "\n尚未保存的 README 修改也会一并丢弃。" : string.Empty);
    public string SelectLanguageTitle => IsEnglish ? "Select interface language" : "选择界面语言";
    public string SelectLanguageMessage => IsEnglish ? "Choose the language used by the manager." : "请选择管理器使用的语言。";
    public string ChineseLanguage => "中文";
    public string EnglishLanguage => "English";

    public string ModCount(int count) => IsEnglish ? $"{count} Mods" : $"{count} 个 Mod";
    public string FilteredModCount(int visible, int total) => IsEnglish
        ? $"Showing {visible} / {total}"
        : $"显示 {visible} / {total}";
    public string ScanStatus(string path, int iconCount) => IsEnglish
        ? $"Scanned: {path} ({iconCount} group icons loaded)"
        : $"已扫描：{path}（已加载 {iconCount} 个分组图标）";
    public string NoRoot => IsEnglish ? "Select a Mod folder first." : "请先选择 Mod 文件夹。";
    public string CharacterName(string chineseName, string englishName) => IsEnglish ? englishName : chineseName;
    public string CharacterModStatus(string character, int count, bool exists, string folder) =>
        exists
            ? (IsEnglish ? $"{character}: {count} Mods found" : $"{character}：找到 {count} 个 Mod")
            : (IsEnglish
                ? $"{character}: no local Mods; use “Import Mod” to create the {folder} folder"
                : $"{character}：暂无本地 Mod，可使用“导入 Mod”创建英文目录 {folder}");
    public string CharacterPathOccupied(string folder) => IsEnglish
        ? $"The {folder} folder is a root-level Mod. Manage it under Ungrouped Mods."
        : $"目录 {folder} 是根目录 Mod，请在“未分类 Mod”中管理。";
    public string ImportTitle(string character) => IsEnglish
        ? $"Import a Mod archive for {character}"
        : $"为 {character} 导入 Mod 压缩包";
    public string ImportProgress(string fileName) => IsEnglish
        ? $"Checking and importing {fileName} with 7-Zip…"
        : $"正在使用 7-Zip 检查并导入 {fileName}…";
    public string ImportFailedForArchive(string fileName, string details) => IsEnglish
        ? $"Import failed for {fileName}: {details}"
        : $"导入 {fileName} 失败：{details}";
    public string ImportPartial(int imported, int failed, bool cancelled) => IsEnglish
        ? $"Imported {imported} Mod(s); {failed} archive(s) failed"
            + (cancelled ? "; import cancelled." : ".")
        : $"已导入 {imported} 个 Mod；{failed} 个压缩包失败"
            + (cancelled ? "；导入已取消。" : "。");
    public string Imported(int count, IEnumerable<string?> names) => IsEnglish
        ? $"Imported {count} Mod(s): {string.Join(", ", names)}"
        : $"已导入 {count} 个 Mod：{string.Join("、", names)}";
    public string NoCharacter => IsEnglish ? "Select a Mod group first." : "请先选择 Mod 分组。";
    public string ToggleStatus(string name, bool enabled) => IsEnglish
        ? $"{name}: {(enabled ? "activated" : "disabled")}. Press F10 in-game to reload."
        : $"{name}：{(enabled ? "已激活" : "已停用")}。请在游戏中按 F10 重新加载。";
    public string RenameStatus(string name, bool enabled) => IsEnglish
        ? $"Mod renamed to {name} ({(enabled ? "active" : "DISABLED")} state preserved)"
        : $"Mod 已重命名为：{name}（{(enabled ? "激活状态" : "停用状态")}已保留）";
    public string ModFolderMissing => IsEnglish ? "The Mod folder does not exist." : "Mod 目录不存在。";
    public string OpenedFolder(string path) => IsEnglish ? $"Opened: {path}" : $"已打开：{path}";
    public string SavedNotes(string path) => IsEnglish ? $"Notes saved: {path}" : $"说明已保存：{path}";
    public string NoMod => IsEnglish ? "Select a Mod first." : "请先选择一个 Mod。";
    public string ModChanged => IsEnglish ? "The current Mod changed; adding images was cancelled." : "当前 Mod 已发生变化，已取消添加图片。";
    public string NoImagesAdded => IsEnglish
        ? "No images added (they may already be in Preview or use an unsupported format)."
        : "没有添加图片（可能已位于 Preview 中或格式不受支持）。";
    public string ImagesAdded(int count) => IsEnglish ? $"Added {count} preview image(s)." : $"已添加 {count} 张预览图。";
    public string NoImageToDelete => IsEnglish ? "There is no preview image to delete." : "当前没有可删除的预览图。";
    public string ImageDeleted(string name) => IsEnglish ? $"Deleted preview image: {name}" : $"已删除预览图：{name}";
    public string ModDeleted(string name) => IsEnglish ? $"Deleted Mod: {name}" : $"已删除 Mod：{name}";
    public string LoadingPreview => IsEnglish ? "Loading preview…" : "正在加载预览图…";
    public string NoPreview => IsEnglish ? "No preview image\nClick “Add image” below" : "暂无预览图\n点击下方“添加图片”";
    public string ImageUnreadable => IsEnglish ? "Image cannot be read" : "图片无法读取";
    public string ImageReadFailed(string details) => IsEnglish
        ? $"Image read failed (retried 3 times): {details}"
        : $"图片读取失败（已重试 3 次）：{details}";
    public string ImagePosition(int index, int total) => $"{index} / {total}";

    public string TranslateError(string message)
    {
        if (!IsEnglish) return message;
        return message switch
        {
            "准备就绪" => Ready,
            "请先选择 Mod 文件夹。" => NoRoot,
            "请先选择角色。" => NoCharacter,
            "请先选择角色或分类。" => NoCharacter,
            "请先选择 Mod 或角色。" => NoCharacter,
            "请先选择一个 Mod。" => NoMod,
            "Mod 目录不存在。" => ModFolderMissing,
            "当前没有可删除的预览图。" => NoImageToDelete,
            "压缩包密码错误。" => IsEnglish ? "The archive password is incorrect." : message,
            "压缩包需要密码。" => IsEnglish ? "The archive requires a password." : message,
            "内置解压组件缺失，请重新解压完整发行版（包含 ThirdParty 文件夹）。" => "The bundled archive component is missing. Extract the complete release, including the ThirdParty folder.",
            "未找到解压组件，请将 7zz 或 7z 加入 PATH。" => "Archive component not found. Add 7zz or 7z to PATH.",
            _ => TranslateErrorPrefix(message)
        };
    }

    private static string TranslateErrorPrefix(string message)
    {
        var replacements = new[]
        {
            ("打开文件夹选择器失败：", "Failed to open folder picker: "),
            ("打开压缩包选择器失败：", "Failed to open archive picker: "),
            ("打开图片选择器失败：", "Failed to open image picker: "),
            ("创建 Mod 分组失败：", "Creating the Mod group failed: "),
            ("重命名 Mod 分组失败：", "Renaming the Mod group failed: "),
            ("删除 Mod 分组失败：", "Deleting the Mod group failed: "),
            ("重置分组图标失败：", "Resetting the group icon failed: "),
            ("设置分组图标失败：", "Setting the group icon failed: "),
            ("导入失败：", "Import failed: "),
            ("添加图片失败：", "Adding images failed: "),
            ("删除图片失败：", "Deleting the image failed: "),
            ("密码错误：", "Password error: "),
            ("说明保存失败：", "Saving notes failed: "),
            ("重命名失败：", "Renaming failed: "),
            ("删除 Mod 失败：", "Deleting the Mod failed: "),
            ("打开目录失败：", "Opening the folder failed: "),
            ("Mod 目录不存在：", "Mod folder does not exist: "),
            ("角色目录不存在：", "Character folder does not exist: "),
            ("目录不存在：", "Directory does not exist: "),
            ("无法切换：", "Cannot change state: "),
            ("无法重命名：", "Cannot rename: "),
            ("图片读取失败（已重试 3 次）：", "Image read failed (retried 3 times): ")
        };
        var translated = message;
        foreach (var (prefix, translation) in replacements)
        {
            if (translated.StartsWith(prefix, StringComparison.Ordinal))
            {
                translated = translation + translated[prefix.Length..];
                break;
            }
        }
        var details = new[]
        {
            ("请选择 WWMI\\Mods 文件夹，而不是其它目录。", "Select the WWMI\\Mods folder, not another directory."),
            ("当前版本只使用 WWMI\\Mods 一级分组结构。", "This version uses first-level groups under WWMI\\Mods."),
            ("目标名称已存在", "the target name already exists"),
            ("同名 Mod 分类已存在", "a Mod group with the same name already exists"),
            ("Mod 名称不能为空。", "The Mod name cannot be empty."),
            ("Mod 名称包含系统不允许的字符。", "The Mod name contains characters not allowed by the system."),
            ("Mod 名称不能以空格或句点结尾。", "The Mod name cannot end with a space or period."),
            ("名称中无需填写 DISABLED；管理器会按当前激活状态自动保留该前缀。", "Do not type DISABLED in the name; the manager preserves it according to the current state."),
            ("压缩包名称无法生成安全的导入目录。", "The archive name cannot produce a safe import folder."),
            ("压缩包中没有找到 Mod INI，未导入任何文件。", "No Mod INI was found in the archive; nothing was imported."),
            ("压缩包需要密码。", "The archive requires a password."),
            ("压缩包密码错误。", "The archive password is incorrect."),
            ("安全检查失败", "Safety check failed"),
            ("无法从停用目录名恢复 Mod 名称。请先手动重命名该目录。", "The Mod name cannot be recovered from the disabled folder name. Rename it manually first.")
        };
        foreach (var (source, translation) in details)
            translated = translated.Replace(source, translation, StringComparison.Ordinal);
        return translated;
    }
}
