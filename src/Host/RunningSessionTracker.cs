namespace GenshinFpsUnlocker.Host;

/// <summary>
/// 运行会话号跟踪：把「游戏进程真的重新启动」和「同一进程的检测抖动 /
/// 系统 PID 复用」区分开。托盘自动跟随只在会话号变化时发生一次。
///
/// 判定规则：
/// - 不同游戏或不同 PID → 新会话；
/// - 同一 (游戏, PID) 在 Clear 后的抖动窗口内重新出现 → 同一会话（不重复跟随）；
/// - 同一 (游戏, PID) 超过抖动窗口才重新出现 → 视为新会话（覆盖 PID 复用）；
/// - 连续多轮没有进程不会刷新抖动窗口，避免长时间空闲后仍把复用的 PID 当成抖动。
/// </summary>
internal sealed class RunningSessionTracker
{
    private int _session;
    private int _lastPid;
    private int _lastGameValue; // 0 = 无
    private int _running;
    private long _clearedTicks;

    /// <summary>当前运行会话号；新进程出现时 +1。</summary>
    public int Session => Volatile.Read(ref _session);

    /// <summary>记录一次检测到的游戏进程；返回当前会话号。</summary>
    public int Observe(GameId game, int pid, long nowTicks, long flickerWindowTicks)
    {
        var sameProcess = pid != 0
            && pid == Volatile.Read(ref _lastPid)
            && Decode(Volatile.Read(ref _lastGameValue)) == game;
        var clearedTicks = Volatile.Read(ref _clearedTicks);
        // 没有经历过 Clear（正常连续轮询）时，同一 (游戏, PID) 就是同一会话。
        var withinFlicker = sameProcess
            && (clearedTicks == 0 || nowTicks - clearedTicks <= flickerWindowTicks);

        Interlocked.Exchange(ref _running, 1);
        if (!sameProcess || !withinFlicker)
        {
            Volatile.Write(ref _lastPid, pid);
            Volatile.Write(ref _lastGameValue, Encode(game));
            Interlocked.Increment(ref _session);
        }
        return Session;
    }

    /// <summary>记录一次「没有检测到游戏进程」；只在从有到无时记录时间戳。</summary>
    public void Clear(long nowTicks)
    {
        if (Interlocked.Exchange(ref _running, 0) == 1)
            Volatile.Write(ref _clearedTicks, nowTicks);
    }

    private static GameId? Decode(int value) => value == 0 ? null : (GameId)(value - 1);
    private static int Encode(GameId game) => (int)game + 1;
}
