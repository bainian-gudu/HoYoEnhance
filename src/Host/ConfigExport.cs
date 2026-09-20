using System.Text;

namespace GenshinFpsUnlocker.Host;

/// <summary>导出结果的三种归宿：写盘成功 / 用户取消 / 写盘失败。</summary>
internal readonly record struct ConfigExportResult(bool Ok, string? Path, string? Detail)
{
    public static ConfigExportResult Success(string path) => new(true, path, null);

    /// <summary>用户主动取消（关掉目录框或放弃覆盖），界面按「无操作」处理。</summary>
    public static ConfigExportResult Cancel() => new(false, null, "已取消");

    public static ConfigExportResult Fail(string detail) => new(false, null, detail);
}

/// <summary>
/// 导出配置到用户选择的目录：写盘逻辑与目录选择拆开，
/// 前者可以在没有窗口的测试台里直接断言。
/// </summary>
internal static class ConfigExport
{
    /// <summary>导出文件名。导入端只校验 JSON 内容，不限制文件名。</summary>
    public const string FileName = "config.json";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>把配置 JSON 写入指定目录并返回完整文件路径；目录不存在或不可写时抛出。</summary>
    public static string WriteToDirectory(string? directory, string json)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("没有选择导出目录。");
        if (json is null) throw new ArgumentNullException(nameof(json));

        var dir = PathUtil.Normalize(directory);
        if (!Directory.Exists(dir)) throw new InvalidOperationException("导出目录不存在：" + dir);

        var path = Path.Combine(dir, FileName);
        File.WriteAllText(path, json, Utf8NoBom);
        return path;
    }

    /// <summary>目标目录下是否已有同名导出文件（覆盖前确认用）。</summary>
    public static bool ExistsInDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        try { return File.Exists(Path.Combine(PathUtil.Normalize(directory), FileName)); }
        catch { return false; }
    }

    /// <summary>弹目录选择框并写盘。取消（含放弃覆盖）返回 <see cref="ConfigExportResult.Cancel"/>。</summary>
    public static ConfigExportResult ExportWithDialog(IWin32Window? owner, string json)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"选择保存目录（导出 {FileName}）",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = DefaultDirectory(),
        };

        var picked = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (picked != DialogResult.OK) return ConfigExportResult.Cancel();

        var directory = dialog.SelectedPath;
        if (ExistsInDirectory(directory) && !ConfirmOverwrite(owner, directory))
            return ConfigExportResult.Cancel();

        try
        {
            return ConfigExportResult.Success(WriteToDirectory(directory, json));
        }
        catch (Exception ex)
        {
            AppLog.Warn("config export failed: " + ex.Message);
            return ConfigExportResult.Fail(ex.Message);
        }
    }

    private static bool ConfirmOverwrite(IWin32Window? owner, string directory)
    {
        var target = Path.Combine(PathUtil.Normalize(directory), FileName);
        var text = $"{FileName} 已存在，是否覆盖？\n\n{target}";
        var caption = AppPaths.ProductDisplayName + " — 导出配置";

        var answer = owner is null
            ? MessageBox.Show(text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            : MessageBox.Show(owner, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return answer == DialogResult.Yes;
    }

    /// <summary>默认落在「文档」；取不到时退回数据目录，保证选择框有可用起点。</summary>
    private static string DefaultDirectory()
    {
        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(documents) && Directory.Exists(documents)) return documents;
        }
        catch { /* 取不到就用数据目录 */ }
        return AppPaths.DataDirectory;
    }
}
