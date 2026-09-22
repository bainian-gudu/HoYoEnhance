namespace GenshinFpsUnlocker.Host.Tests;

/// <summary>
/// 登录自启 Run 值的归属判定：只认当前产品名的 exe，
/// 避免误删其他程序的 Run 值。
/// </summary>
internal static class AutostartTests
{
    public static void Run(Harness h)
    {
        h.Case("当前品牌的自启值判定为本程序", () =>
        {
            Harness.True(
                Autostart.IsOwnRunValue(
                    "\"C:\\Program Files\\HoYoEnhance\\HoYoEnhance.exe\" --autostart"),
                "HoYoEnhance.exe 的自启值应允许删除");
        });

        h.Case("其他程序的自启值不误删", () =>
        {
            Harness.False(
                Autostart.IsOwnRunValue("\"C:\\Tools\\notepad.exe\" --autostart"),
                "其他程序的 Run 值不能删");
            Harness.False(
                Autostart.IsOwnRunValue(
                    "\"C:\\Program Files\\HoYoEnhance\\other.exe\" --autostart"),
                "目录含产品名但 exe 不是本程序时不能删");
        });

        h.Case("无效自启值不判定为本程序", () =>
        {
            Harness.False(Autostart.IsOwnRunValue(null), "null 不能判定为本程序");
            Harness.False(Autostart.IsOwnRunValue(""), "空串不能判定为本程序");
        });
    }
}
