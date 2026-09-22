namespace GenshinFpsUnlocker.Host;

/// <summary>
/// Core 需要的少量用户交互。具体实现留在 WinForms 外壳，
/// 核心逻辑因此不依赖窗口、对话框或 UI 线程。
/// </summary>
internal interface IUserInteraction
{
    /// <summary>让用户选择指定游戏的主程序；取消时返回 null。</summary>
    string? SelectGameExecutable(GameDescriptor game);

    /// <summary>导出配置；用户取消、写入失败都由返回值表达。</summary>
    ConfigExportResult ExportConfig(string json);
}
