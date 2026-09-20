namespace GenshinFpsUnlocker.Host.Tests;

/// <summary>
/// 登录自启 Run 值的归属判定：品牌改名后命令行里是 HoYoEnhance.exe，
/// 不能再按旧产品名字符串判断，否则关闭自启时删不掉旧值，
/// 登录时计划任务与 HKCU\Run 会同时拉起两个实例。
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

        h.Case("历史品牌的自启值判定为本程序", () =>
        {
            Harness.True(
                Autostart.IsOwnRunValue(
                    "\"C:\\Program Files\\HoYoEnhance\\GenshinFpsUnlocker.exe\" --autostart"),
                "旧 exe 名的自启值升级后也应允许清理");
        });

        h.Case("其他程序的自启值不误删", () =>
        {
            Harness.False(
                Autostart.IsOwnRunValue("\"C:\\Tools\\notepad.exe\" --autostart"),
                "其他程序的 Run 值不能删");
            Harness.False(
                Autostart.IsOwnRunValue(
                    "\"C:\\Program Files\\GenshinFpsUnlocker\\other.exe\" --autostart"),
                "目录含旧产品名但 exe 不是本程序时不能删");
        });

        h.Case("无效自启值不判定为本程序", () =>
        {
            Harness.False(Autostart.IsOwnRunValue(null), "null 不能判定为本程序");
            Harness.False(Autostart.IsOwnRunValue(""), "空串不能判定为本程序");
        });
    }
}
