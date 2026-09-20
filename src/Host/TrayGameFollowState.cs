namespace GenshinFpsUnlocker.Host;

/// <summary>
/// 托盘自动跟随的纯状态机：只决定「什么时候把展示游戏切到运行中的那款 /
/// 游戏退出后什么时候回退」，不碰 WinForms，便于覆盖「手动查看不打断退出回退」等回归场景。
/// 运行状态刚变为空时只给出待确认信号，由调用方延迟一段时间再调用
/// <see cref="ConfirmExit"/>，避免监视循环单次检测失败造成误回退。
///
/// 输入来自 <see cref="UnlockService"/>：运行会话号在出现新的游戏进程 PID 时 +1。
/// 自动跟随只改展示游戏，不改用户保存的选择；游戏退出时回退到跟随前的展示游戏，
/// 即使中途用户手动切回运行中的那款也不改变这个回退目标。
/// </summary>
internal sealed class TrayGameFollowState
{
    /// <summary>
    /// 一次状态推进的结论：Switch 为 false 时其余字段无意义。
    /// NeedsExitConfirm 表示运行状态刚变为空，需要调用方稍后确认是否真的退出。
    /// </summary>
    internal readonly record struct Decision(
        bool Switch, GameId Game, bool IsFollow, bool IsRestore, bool NeedsExitConfirm);

    /// <summary>当前自动跟随的那款游戏（null = 没有跟随）。</summary>
    private GameId? _followedGame;
    /// <summary>等待退出确认的游戏：运行状态短暂抖动时不能立即回退。</summary>
    private GameId? _pendingExitGame;
    /// <summary>跟随之前展示的游戏：游戏退出且当前仍停留在跟随游戏上时回到这里。</summary>
    private GameId _restoreGame = GameId.Genshin;
    /// <summary>已经处理过的运行会话号。</summary>
    private int _seenSession;

    public Decision Update(int runningSession, GameId? runningGame, GameId displayGame)
    {
        // 新的游戏进程会话：自动跟随一次；用户当前展示的就是它则不必切换。
        if (runningSession != _seenSession)
        {
            _seenSession = runningSession;
            _pendingExitGame = null;
            if (runningGame is GameId game)
            {
                if (displayGame != game)
                {
                    _followedGame = game;
                    _restoreGame = displayGame;
                    return new Decision(true, game, IsFollow: true, IsRestore: false, NeedsExitConfirm: false);
                }
                // 展示已经是这款：如果之前跟随的是别的游戏，旧关系作废。
                if (_followedGame != game) _followedGame = null;
            }
            return default;
        }

        // 同一会话内运行状态恢复：取消等待中的退出确认（检测抖动）。
        if (runningGame is not null)
        {
            _pendingExitGame = null;
            return default;
        }

        // 运行状态变为空：先不急着回退，让调用方过一小段时间再确认，
        // 避免监视循环单次检测失败就把托盘误切回启动前的游戏。
        if (_followedGame is GameId followed && _pendingExitGame != followed)
        {
            _pendingExitGame = followed;
            return new Decision(false, default, false, false, NeedsExitConfirm: true);
        }
        return default;
    }

    /// <summary>
    /// 退出确认：调用方在延迟窗口结束后再次询问。运行已恢复则不回退；
    /// 仍为空且当前还停留在跟随游戏上，才回退到启动前的展示游戏。
    /// </summary>
    public Decision ConfirmExit(GameId? runningGame, GameId displayGame)
    {
        if (runningGame is not null)
        {
            _pendingExitGame = null;
            return default;
        }
        if (_pendingExitGame is not GameId exited || _followedGame != exited) return default;

        _pendingExitGame = null;
        _followedGame = null;
        if (displayGame == exited && _restoreGame != exited)
            return new Decision(true, _restoreGame, IsFollow: false, IsRestore: true, NeedsExitConfirm: false);
        return default;
    }
}
