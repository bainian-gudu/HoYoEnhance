namespace GenshinFpsUnlocker.Host.Tests;

/// <summary>
/// 主窗口位置记忆的回归断言：重点是「移动窗口之后位置真的会落盘」——
/// 旧实现先把新位置写进内存配置、再拿配置字段和窗口位置比，结果每次都判成
/// 「没变化」，config.json 里一个 windowLeft / windowTop 都没写出来。
/// </summary>
internal static class WindowLocationStateTests
{
    public static void Run(Harness h)
    {
        h.Case("首次运行移动窗口后有待落盘位置", () =>
        {
            var state = new WindowLocationState();

            var scheduled = state.Observe(new Point(120, 80), null, null);
            Harness.True(scheduled, "位置从无到有应安排一次落盘");
            Harness.Equal(new Point(120, 80), state.Pending!.Value, "待落盘位置");
        });

        h.Case("已保存值就是当前位置时不再安排落盘", () =>
        {
            var state = new WindowLocationState();

            var scheduled = state.Observe(new Point(300, 200), 300, 200);
            Harness.False(scheduled, "位置没变不该再写盘");
            Harness.Equal<Point?>(null, state.Pending, "没有待落盘改动");
        });

        h.Case("落盘成功后同一位置不再重复写", () =>
        {
            var state = new WindowLocationState();

            state.Observe(new Point(120, 80), null, null);
            state.MarkSaved();
            Harness.Equal<Point?>(null, state.Pending, "落盘成功后应清空");

            var again = state.Observe(new Point(120, 80), 120, 80);
            Harness.False(again, "同一位置第二次不该再写盘");
        });

        h.Case("拖回已保存位置会撤销待落盘改动", () =>
        {
            var state = new WindowLocationState();

            state.Observe(new Point(700, 500), 300, 200);
            Harness.True(state.Pending is not null, "先有一次未落盘的改动");

            var scheduled = state.Observe(new Point(300, 200), 300, 200);
            Harness.False(scheduled, "拖回原处不必写盘");
            Harness.Equal<Point?>(null, state.Pending, "待落盘改动应被撤销");
        });

        h.Case("写盘失败时位置留在状态里，退出前还能补写", () =>
        {
            var state = new WindowLocationState();

            state.Observe(new Point(420, 260), null, null);
            // 模拟 TrySave 失败：不调用 MarkSaved，改动必须留着。
            Harness.Equal(new Point(420, 260), state.Pending!.Value, "失败后位置不能丢");

            var moved = state.Observe(new Point(500, 300), null, null);
            Harness.True(moved, "再次移动仍应安排落盘");
            Harness.Equal(new Point(500, 300), state.Pending!.Value, "待落盘位置应更新到最新");
        });

        h.Case("内存配置已被改成新值也不影响落盘判断", () =>
        {
            // 旧实现的 bug 就在这里：调用方先把 _config.WindowLeft/Top 写成新位置，
            // 落盘路径再比较「配置字段 == 窗口位置」就永远相等，于是从不写盘。
            // 现在的判据是「有没有待落盘位置」，与配置字段是否已经同步无关。
            var state = new WindowLocationState();
            int? configLeft = null;
            int? configTop = null;

            var location = new Point(640, 360);
            Harness.True(state.Observe(location, configLeft, configTop), "应安排落盘");

            configLeft = location.X;
            configTop = location.Y;

            Harness.Equal(location, state.Pending!.Value, "配置字段同步后待落盘位置仍然存在");
            Harness.Equal(configLeft, state.Pending!.Value.X, "落盘取的就是这个位置");
            Harness.Equal(configTop, state.Pending!.Value.Y, "落盘取的就是这个位置");
        });
    }
}
