namespace GenshinFpsUnlocker.Host;

/// <summary>
/// 注入会话保活循环的退出策略。功能全部关闭或 Host 已请求 Stub 退出时，
/// 必须离开当前附着循环，回到外层重新判定是否需要注入。
/// </summary>
internal static class InjectionSessionPolicy
{
    public static bool ShouldExitKeepalive(bool needsInjection, IpcStatus status) =>
        !needsInjection || status == IpcStatus.Exiting;
}
