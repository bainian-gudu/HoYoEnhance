namespace GenshinFpsUnlocker.Host.Tests;

internal static class GamePollingPolicyTests
{
    public static void Run(Harness harness)
    {
        harness.Case("游戏轮询节奏统一钳制", () =>
        {
            Harness.Equal(500, GamePollingPolicy.RunningStateIntervalMs(200), "状态轮询最小间隔");
            Harness.Equal(1000, GamePollingPolicy.RunningStateIntervalMs(1000), "默认状态轮询间隔");
            Harness.Equal(2000, GamePollingPolicy.RunningStateIntervalMs(10000), "状态轮询最大间隔");
            Harness.Equal(5000, GamePollingPolicy.WatchIdleIntervalMs(10000), "空闲轮询最大间隔");
            Harness.Equal(800, GamePollingPolicy.WatchActiveIntervalMs(10000), "活跃轮询上限");
            Harness.Equal(3000, GamePollingPolicy.RunningFlickerWindowMs(200), "进程抖动容错最小值");
            Harness.Equal(1000, GamePollingPolicy.ExitConfirmationIntervalMs(500), "退出确认最小值");
            Harness.Equal(5000, GamePollingPolicy.ExitConfirmationIntervalMs(10000), "退出确认最大值");
        });
    }
}
