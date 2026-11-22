namespace GenshinFpsUnlocker.Host;

/// <summary>Core 交互接口的 WinForms 实现：文件选择与配置导出对话框。</summary>
internal sealed class WinFormsUserInteraction : IUserInteraction
{
    private readonly IWin32Window _owner;

    public WinFormsUserInteraction(IWin32Window owner)
    {
        _owner = owner;
    }

    public string? SelectGameExecutable(GameDescriptor game)
    {
        var exeFilter = string.Join(";", game.ExeNames);
        using var dialog = new OpenFileDialog
        {
            Title = $"选择{game.DisplayName}主程序（{GameCatalog.ExeNameList(game)}）",
            Filter = $"{game.DisplayName}主程序|{exeFilter}|可执行文件 (*.exe)|*.exe|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog(_owner) == DialogResult.OK ? dialog.FileName : null;
    }

    public ConfigExportResult ExportConfig(string json)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"选择保存目录（导出 {ConfigExport.FileName}）",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = ConfigExport.DefaultDirectory(),
        };

        if (dialog.ShowDialog(_owner) != DialogResult.OK) return ConfigExportResult.Cancel();
        var directory = dialog.SelectedPath;
        if (ConfigExport.ExistsInDirectory(directory) && !ConfirmOverwrite(directory))
            return ConfigExportResult.Cancel();

        try
        {
            return ConfigExportResult.Success(ConfigExport.WriteToDirectory(directory, json));
        }
        catch (Exception ex)
        {
            AppLog.Warn("config export failed: " + ex.Message);
            return ConfigExportResult.Fail(ex.Message);
        }
    }

    private bool ConfirmOverwrite(string directory)
    {
        var target = Path.Combine(PathUtil.Normalize(directory), ConfigExport.FileName);
        var text = $"{ConfigExport.FileName} 已存在，是否覆盖？\n\n{target}";
        var caption = AppPaths.ProductDisplayName + " — 导出配置";
        return MessageBox.Show(
            _owner,
            text,
            caption,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes;
    }
}
