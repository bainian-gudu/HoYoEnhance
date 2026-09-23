namespace GenshinFpsUnlocker.Host.Tests;

/// <summary>
/// 托盘自动跟随的回归断言：重点是「手动切回原神后再切回正在运行的星铁，
/// 星铁退出时仍回到原神」，以及退出确认对运行状态抖动的防护。
/// </summary>
internal static class TrayGameFollowStateTests
{
    public static void Run(Harness h)
    {
        h.Case("星铁配置页启动原神，原神退出后停在默认原神页", () =>
        {
            var state = new TrayGameFollowState();

            var follow = state.Update(1, GameId.Genshin, GameId.StarRail);
            Harness.True(follow.Switch, "启动原神时应自动切到原神配置页");
            Harness.Equal(GameId.Genshin, follow.Game, "跟随原神");

            var pending = state.Update(1, null, GameId.Genshin);
            Harness.True(pending.NeedsExitConfirm, "原神退出应等待确认");

            var restore = state.ConfirmExit(null, GameId.Genshin);
            Harness.False(restore.Switch, "默认页已经是原神，无需再切换");
        });

        h.Case("星铁启动自动跟随，确认退出后回到原神", () =>
        {
            var state = new TrayGameFollowState();

            var follow = state.Update(1, GameId.StarRail, GameId.Genshin);
            Harness.True(follow.Switch, "星铁新进程应自动跟随");
            Harness.Equal(GameId.StarRail, follow.Game, "跟随目标");
            Harness.True(follow.IsFollow, "应标记为跟随");

            var pending = state.Update(1, null, GameId.StarRail);
            Harness.True(pending.NeedsExitConfirm, "运行状态变为空时应先等待退出确认");
            Harness.False(pending.Switch, "确认前不能立即回退");

            var restore = state.ConfirmExit(null, GameId.StarRail);
            Harness.True(restore.Switch, "确认退出后应回退");
            Harness.Equal(GameId.Genshin, restore.Game, "回退目标");
            Harness.True(restore.IsRestore, "应标记为回退");
        });

        h.Case("手动回原神再切回运行中的星铁，退出仍回原神", () =>
        {
            var state = new TrayGameFollowState();
            var follow = state.Update(1, GameId.StarRail, GameId.Genshin);
            Harness.Equal(GameId.StarRail, follow.Game, "先自动跟随星铁");

            // 手动切回原神：只是查看，不应触发新的自动切换。
            var backToGenshin = state.Update(1, GameId.StarRail, GameId.Genshin);
            Harness.False(backToGenshin.Switch, "手动切回原神不应自动切走");

            // 再手动切回运行中的星铁：仍属于临时查看，不能把回退目标改成星铁。
            var viewStarRail = state.Update(1, GameId.StarRail, GameId.StarRail);
            Harness.False(viewStarRail.Switch, "展示已经是星铁时无需额外切换");

            var pending = state.Update(1, null, GameId.StarRail);
            Harness.True(pending.NeedsExitConfirm, "星铁退出应进入确认");

            var restore = state.ConfirmExit(null, GameId.StarRail);
            Harness.True(restore.Switch, "确认退出后应回退");
            Harness.Equal(GameId.Genshin, restore.Game, "应回到原神默认页");
            Harness.True(restore.IsRestore, "应标记为回退");
        });

        h.Case("手动回原神后直接退出，保持原神不回跳", () =>
        {
            var state = new TrayGameFollowState();
            state.Update(1, GameId.StarRail, GameId.Genshin);

            var pending = state.Update(1, null, GameId.Genshin);
            Harness.True(pending.NeedsExitConfirm, "运行状态变为空仍会进入确认");

            var exited = state.ConfirmExit(null, GameId.Genshin);
            Harness.False(exited.Switch, "已经停在原神上，确认后不需要切换");
        });

        h.Case("退出确认期间运行恢复，不误回退", () =>
        {
            var state = new TrayGameFollowState();
            state.Update(1, GameId.StarRail, GameId.Genshin);

            var pending = state.Update(1, null, GameId.StarRail);
            Harness.True(pending.NeedsExitConfirm, "首次报告无进程应等待确认");

            // 监视循环下一轮又检测到同一进程：确认作废，不回退。
            var resumed = state.Update(1, GameId.StarRail, GameId.StarRail);
            Harness.False(resumed.Switch, "运行恢复时不应切换");

            var confirm = state.ConfirmExit(GameId.StarRail, GameId.StarRail);
            Harness.False(confirm.Switch, "运行已恢复，确认退出不能回退");
        });

        h.Case("同一进程会话的状态抖动不会重复跟随", () =>
        {
            var state = new TrayGameFollowState();
            state.Update(1, GameId.StarRail, GameId.Genshin);

            // 监视循环偶尔短暂报告无进程：先进入确认，不立即回退。
            var flicker = state.Update(1, null, GameId.Genshin);
            Harness.True(flicker.NeedsExitConfirm, "抖动应进入退出确认");

            var resumed = state.Update(1, GameId.StarRail, GameId.Genshin);
            Harness.False(resumed.Switch, "同一进程会话恢复后不应重复跟随");
            Harness.False(
                state.ConfirmExit(GameId.StarRail, GameId.Genshin).Switch,
                "运行恢复后确认退出不应切换");
        });

        h.Case("星铁页启动星铁，退出后统一回原神默认页", () =>
        {
            var state = new TrayGameFollowState();
            var follow = state.Update(1, GameId.StarRail, GameId.StarRail);
            Harness.False(follow.Switch, "展示已经是星铁，不需要跟随");

            var pending = state.Update(1, null, GameId.StarRail);
            Harness.True(pending.NeedsExitConfirm, "即使启动页与游戏一致也要跟踪退出");

            var restore = state.ConfirmExit(null, GameId.StarRail);
            Harness.True(restore.Switch, "星铁退出后应切回默认页");
            Harness.Equal(GameId.Genshin, restore.Game, "默认页统一为原神");
        });

        h.Case("原神页启动原神后手动切到星铁，退出仍统一回原神", () =>
        {
            var state = new TrayGameFollowState();
            state.Update(1, GameId.Genshin, GameId.Genshin);
            state.Update(1, GameId.Genshin, GameId.StarRail);

            var pending = state.Update(1, null, GameId.StarRail);
            Harness.True(pending.NeedsExitConfirm, "未自动切页的运行会话也要跟踪退出");

            var restore = state.ConfirmExit(null, GameId.StarRail);
            Harness.True(restore.Switch, "会话结束后应统一切回默认页");
            Harness.Equal(GameId.Genshin, restore.Game, "默认页不取决于退出前的页面");
        });

        h.Case("星铁重启（新进程会话）重新跟随一次", () =>
        {
            var state = new TrayGameFollowState();
            state.Update(1, GameId.StarRail, GameId.Genshin);
            state.Update(1, null, GameId.StarRail);
            state.ConfirmExit(null, GameId.StarRail);

            var second = state.Update(2, GameId.StarRail, GameId.Genshin);
            Harness.True(second.Switch, "新的星铁进程应再次跟随");
            Harness.Equal(GameId.StarRail, second.Game, "跟随目标");
            Harness.True(second.IsFollow, "应标记为跟随");
        });

        h.Case("新游戏会话替换旧会话并继续跟踪退出", () =>
        {
            var state = new TrayGameFollowState();
            state.Update(1, GameId.StarRail, GameId.Genshin);
            state.Update(1, null, GameId.StarRail);
            state.ConfirmExit(null, GameId.StarRail);

            // 原神启动：展示已经是原神，不需要切页，但必须登记新会话。
            var genshinStart = state.Update(2, GameId.Genshin, GameId.Genshin);
            Harness.False(genshinStart.Switch, "展示已经是原神，不需要切页");

            var genshinExit = state.Update(2, null, GameId.Genshin);
            Harness.True(genshinExit.NeedsExitConfirm, "新会话也应进入退出确认");
            var restore = state.ConfirmExit(null, GameId.Genshin);
            Harness.False(restore.Switch, "默认页已是原神，无需重复切页");
        });

        h.Case("星铁重启时保留跟随关系，退出仍回原神", () =>
        {
            var state = new TrayGameFollowState();
            state.Update(1, GameId.StarRail, GameId.Genshin);
            // 用户手动切到运行中的星铁：临时查看，跟随关系仍在。
            state.Update(1, GameId.StarRail, GameId.StarRail);

            var restart = state.Update(2, GameId.StarRail, GameId.StarRail);
            Harness.False(restart.Switch, "重启后展示已经是星铁");

            var pending = state.Update(2, null, GameId.StarRail);
            Harness.True(pending.NeedsExitConfirm, "重启后的星铁退出仍应进入确认");

            var restore = state.ConfirmExit(null, GameId.StarRail);
            Harness.True(restore.Switch, "确认后应回退");
            Harness.Equal(GameId.Genshin, restore.Game, "应回到原神默认页");
        });
    }
}
