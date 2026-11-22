namespace GenshinFpsUnlocker.Host.Tests;

internal static class GameSelectionPolicyTests
{
    public static void Run(Harness harness)
    {
        harness.Case("运行游戏的页面切换只作为临时查看", () =>
        {
            Harness.True(GameSelectionPolicy.IsTransientView(GameId.StarRail, GameId.StarRail), "选择运行中的游戏不应覆盖持久选择");
            Harness.False(GameSelectionPolicy.IsTransientView(GameId.StarRail, GameId.Genshin), "选择未运行的游戏应保存为用户选择");
            Harness.False(GameSelectionPolicy.IsTransientView(GameId.Genshin, null), "无运行游戏时选择应持久化");
        });
    }
}
