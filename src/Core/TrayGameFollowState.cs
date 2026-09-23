namespace GenshinFpsUnlocker.Host;

/// <summary>
/// 托盘游戏页面策略：启动时跟随运行中的游戏，所有游戏都退出后统一回到默认页，
/// 不依赖启动前选择的配置页，也不碰 WinForms。
/// 运行状态刚变为空时只给出待确认信号，由调用方延迟一段时间再调用
/// <see cref="ConfirmExit"/>，避免监视循环单次检测失败造成误回退。
///
/// 输入来自 <see cref="UnlockService"/>：运行会话号在出现新的游戏进程 PID 时 +1。
/// 页面自动切换只改展示游戏，不改用户保存的选择；进程退出后统一回到目录定义的默认页。
/// </summary>
internal sealed class TrayGameFollowState
{
    /// <summary>
    /// 一次状态推进的结论：Switch 为 false 时其余字段无意义。
    /// NeedsExitConfirm 表示运行状态刚变为空，需要调用方稍后确认是否真的退出。
    /// </summary>
    internal readonly record struct Decision(
        bool Switch, GameId Game, bool IsFollow, bool IsRestore, bool NeedsExitConfirm);

    /// <summary>当前运行会话的游戏（即使页面原本已选中该游戏也要跟踪退出）。</summary>
    private GameId? _sessionGame;
    /// <summary>等待退出确认的游戏：运行状态短暂抖动时不能立即回退。</summary>
    private GameId? _pendingExitGame;
    /// <summary>已经处理过的运行会话号。</summary>
    private int _seenSession;

    public Decision Update(int runningSession, GameId? runningGame, GameId displayGame)
    {
        // 新进程会话总是登记退出跟踪；仅当页面不同于正在运行的游戏时切换页面。
        if (runningSession != _seenSession)
        {
            _seenSession = runningSession;
            _pendingExitGame = null;
            _sessionGame = runningGame;
            if (runningGame is GameId game)
            {
                if (displayGame != game)
                {
                    return new Decision(true, game, IsFollow: true, IsRestore: false, NeedsExitConfirm: false);
                }
            }
            return default;
        }

        // 同一会话内运行状态恢复：取消等待中的退出确认（检测抖动）。
        if (runningGame is not null)
        {
            _pendingExitGame = null;
            return default;
        }

        // 运行状态变为空时先等待确认，避免一次检测失败就误触发页面回退。
        if (_sessionGame is GameId sessionGame && _pendingExitGame != sessionGame)
        {
            _pendingExitGame = sessionGame;
            return new Decision(false, default, false, false, NeedsExitConfirm: true);
        }
        return default;
    }

    /// <summary>
    /// 退出确认：调用方在延迟窗口结束后再次询问。运行已恢复则不回退；
    /// 会话确认结束后统一回到目录定义的默认页，无论当前展示哪款游戏。
    /// </summary>
    public Decision ConfirmExit(GameId? runningGame, GameId displayGame)
    {
        if (runningGame is not null)
        {
            _pendingExitGame = null;
            return default;
        }
        if (_pendingExitGame is not GameId exited || _sessionGame != exited) return default;

        _pendingExitGame = null;
        _sessionGame = null;
        if (displayGame != GameCatalog.DefaultGame)
            return new Decision(true, GameCatalog.DefaultGame, IsFollow: false, IsRestore: true, NeedsExitConfirm: false);
        return default;
    }
}
