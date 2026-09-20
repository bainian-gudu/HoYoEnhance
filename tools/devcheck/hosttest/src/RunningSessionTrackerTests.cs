namespace GenshinFpsUnlocker.Host.Tests;

/// <summary>
/// 运行会话号跟踪的回归断言：同一进程的检测抖动不能重复触发托盘跟随，
/// 长时间后的 PID 复用仍要识别为新会话。
/// </summary>
internal static class RunningSessionTrackerTests
{
    private const long FlickerWindow = 3000;

    public static void Run(Harness h)
    {
        h.Case("同一进程连续轮询保持同一会话", () =>
        {
            var tracker = new RunningSessionTracker();
            var first = tracker.Observe(GameId.StarRail, 100, 0, FlickerWindow);
            var second = tracker.Observe(GameId.StarRail, 100, 1000, FlickerWindow);
            Harness.Equal(first, second, "没有 Clear 时同一进程不应推进会话号");
        });

        h.Case("抖动窗口内同一进程恢复不重复计会话", () =>
        {
            var tracker = new RunningSessionTracker();
            var first = tracker.Observe(GameId.StarRail, 100, 0, FlickerWindow);
            tracker.Clear(2000);
            var second = tracker.Observe(GameId.StarRail, 100, 2500, FlickerWindow);
            Harness.Equal(first, second, "抖动窗口内恢复应视为同一会话");
        });

        h.Case("超过抖动窗口的同一 PID 视为新会话", () =>
        {
            var tracker = new RunningSessionTracker();
            var first = tracker.Observe(GameId.StarRail, 100, 0, FlickerWindow);
            tracker.Clear(2000);
            var second = tracker.Observe(GameId.StarRail, 100, 6000, FlickerWindow);
            Harness.Equal(first + 1, second, "超出抖动窗口后 PID 复用应算新会话");
        });

        h.Case("不同游戏复用同一 PID 视为新会话", () =>
        {
            var tracker = new RunningSessionTracker();
            var first = tracker.Observe(GameId.Genshin, 100, 0, FlickerWindow);
            tracker.Clear(1000);
            var second = tracker.Observe(GameId.StarRail, 100, 1500, FlickerWindow);
            Harness.Equal(first + 1, second, "不同游戏即使 PID 相同也算新会话");
        });

        h.Case("连续空闲轮询不刷新抖动窗口", () =>
        {
            var tracker = new RunningSessionTracker();
            var first = tracker.Observe(GameId.StarRail, 100, 0, FlickerWindow);
            tracker.Clear(2000);
            tracker.Clear(9000); // 连续空闲：不能把抖动窗口顺延到 9000
            var second = tracker.Observe(GameId.StarRail, 100, 9500, FlickerWindow);
            Harness.Equal(first + 1, second, "长时间空闲后的同一 PID 应算新会话");
        });
    }
}
