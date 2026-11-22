namespace GenshinFpsUnlocker.Host;

/// <summary>窗口左上角坐标，不依赖 WinForms 的 Point。</summary>
internal readonly record struct WindowLocation(int X, int Y);

/// <summary>
/// 主窗口位置记录的纯状态机：只回答「这次窗口移动算不算用户摆放的位置 /
/// 有没有还没写进 config.json 的改动」，不碰 WinForms，便于在测试台里覆盖回归场景。
///
/// 调用方先 <see cref="Observe"/>（窗口正常可见时才调用），节流窗口结束后取
/// <see cref="Pending"/> 写盘；写盘成功必须调用 <see cref="MarkSaved"/>，
/// 失败就别调 —— 改动会留着，托盘隐藏 / 退出前还能补写一次。
/// </summary>
internal sealed class WindowLocationState
{
    /// <summary>还没写进配置的位置（null = 没有待落盘改动）。</summary>
    private WindowLocation? _pending;

    /// <summary>待落盘的位置；null 表示当前没有需要写盘的改动。</summary>
    public WindowLocation? Pending => _pending;

    /// <summary>
    /// 记录一次窗口位置变化。返回 true 表示位置与已保存值不同，调用方应重新安排一次
    /// 节流落盘；返回 false 表示和已保存值一致，同时撤销还没落盘的改动（用户又拖回原处）。
    ///
    /// 注意判据是「已保存值」而不是内存里的配置对象：调用方通常会先把新位置写进内存配置，
    /// 只比较字段值会让落盘路径误判成「没变化」而永远不写盘。
    /// </summary>
    public bool Observe(WindowLocation location, int? savedLeft, int? savedTop)
    {
        if (savedLeft == location.X && savedTop == location.Y)
        {
            _pending = null;
            return false;
        }

        _pending = location;
        return true;
    }

    /// <summary>写盘成功：清掉待落盘位置。</summary>
    public void MarkSaved() => _pending = null;
}
