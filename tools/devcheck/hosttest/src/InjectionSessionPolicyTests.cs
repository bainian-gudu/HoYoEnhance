namespace GenshinFpsUnlocker.Host.Tests;

internal static class InjectionSessionPolicyTests
{
    public static void Run(Harness h)
    {
        h.Case("功能全部关闭后保活循环退出", () =>
        {
            Harness.True(
                InjectionSessionPolicy.ShouldExitKeepalive(needsInjection: false, IpcStatus.Ready),
                "功能全关后必须退出保活，重新启用才能走 ResetForNewInject");
        });

        h.Case("收到 Exiting 后保活循环退出", () =>
        {
            Harness.True(
                InjectionSessionPolicy.ShouldExitKeepalive(needsInjection: true, IpcStatus.Exiting),
                "Exiting 不应被当成正常附着状态");
        });

        h.Case("功能仍启用且 Ready 时保持保活", () =>
        {
            Harness.False(
                InjectionSessionPolicy.ShouldExitKeepalive(needsInjection: true, IpcStatus.Ready),
                "正常 Ready 会话不应退出");
        });
    }
}
