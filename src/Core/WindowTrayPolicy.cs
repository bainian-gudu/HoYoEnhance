namespace GenshinFpsUnlocker.Host;

/// <summary>
/// 主窗口的托盘驻留策略。开关开启时，启动、最小化和关闭都驻留托盘；
/// 开关关闭时，最小化交给系统任务栏，关闭窗口结束进程。
/// </summary>
internal static class WindowTrayPolicy
{
    public static bool ShouldHideToTray(bool startMinimized) => startMinimized;
}
