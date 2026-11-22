namespace GenshinFpsUnlocker.Host.Tests;

internal static class WindowTrayPolicyTests
{
    public static void Run(Harness h)
    {
        h.Case("托盘驻留开启时最小化和关闭进入托盘", () =>
        {
            Harness.True(WindowTrayPolicy.ShouldHideToTray(true), "开启后应隐藏到托盘");
        });

        h.Case("托盘驻留关闭时最小化和关闭不进入托盘", () =>
        {
            Harness.False(WindowTrayPolicy.ShouldHideToTray(false), "关闭后不应隐藏到托盘");
        });
    }
}
