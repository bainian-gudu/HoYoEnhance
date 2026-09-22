namespace GenshinFpsUnlocker.Host.Tests;

/// <summary>
/// 星铁画面设置值处理的断言。这段代码会把游戏自己写的画面设置 JSON 整段回写，
/// 写错的表现是「开关打开了但解锁静默失效」——不容易在界面上看出来，所以要用例钉住：
/// 挑值名的规则、FPS 字段的大小写、以及改写时其它字段必须原样保留。
/// </summary>
internal static class StarRailFpsSettingsTests
{
    public static void Run(Harness h)
    {
        h.Case("挑出后缀版本号最大的画面设置值名", () =>
        {
            var names = new[]
            {
                "GraphicsSettings_Model_h43",
                "GraphicsSettings_Model_h44",
                "GraphicsSettings_Model_h9",
                "无关的值",
            };

            Harness.True(StarRailFpsSettings.TryPickNewestValueName(names, out var name), "应能挑出值名");
            // 按数值比大小而不是按字符串：h9 的字典序比 h44 大，但版本号是 9 < 44。
            Harness.Equal("GraphicsSettings_Model_h44", name, "应取后缀数值最大的那个");
        });

        h.Case("值名前缀大小写不敏感，非数字后缀按 0 处理", () =>
        {
            var names = new[] { "graphicssettings_model_h1", "GraphicsSettings_Model_h" };

            Harness.True(StarRailFpsSettings.TryPickNewestValueName(names, out var name), "应能挑出值名");
            Harness.Equal("graphicssettings_model_h1", name, "无后缀（按 0）应输给 h1");
        });

        h.Case("没有匹配的值名时返回假", () =>
        {
            Harness.False(StarRailFpsSettings.TryPickNewestValueName([], out _), "空列表应为假");
            Harness.False(
                StarRailFpsSettings.TryPickNewestValueName(["FPS", "GraphicsSettings"], out _),
                "只有前缀不算匹配");
        });

        h.Case("读取 FPS 字段：大小写不敏感，非数字 / 非对象为假", () =>
        {
            Harness.True(StarRailFpsSettings.TryReadFps("{\"FPS\":60}", out var fps), "标准写法应可读");
            Harness.Equal(60, fps, "读到的帧率");

            Harness.True(StarRailFpsSettings.TryReadFps("{\"fps\":120}", out var lower), "小写字段名也应可读");
            Harness.Equal(120, lower, "读到的帧率");

            Harness.False(StarRailFpsSettings.TryReadFps("{\"FPS\":\"60\"}", out _), "字符串帧率应为假");
            Harness.False(StarRailFpsSettings.TryReadFps("{\"Other\":60}", out _), "没有 FPS 字段应为假");
            Harness.False(StarRailFpsSettings.TryReadFps("[60]", out _), "根不是对象应为假");
            Harness.False(StarRailFpsSettings.TryReadFps("不是 JSON", out _), "非法 JSON 应为假");
        });

        h.Case("改写 FPS 时其它字段原样保留、原键名大小写不变", () =>
        {
            const string original = "{\"FPS\":60,\"Width\":1920,\"Height\":1080,\"GraphicsQuality\":4}";
            var updated = StarRailFpsSettings.WriteFps(original, 120);

            Harness.True(StarRailFpsSettings.TryReadFps(updated, out var fps), "改写结果应仍可读");
            Harness.Equal(120, fps, "改写后的帧率");

            foreach (var kept in new[] { "\"Width\":1920", "\"Height\":1080", "\"GraphicsQuality\":4" })
                Harness.Contains(updated, kept, "其它字段必须原样保留");

            var lower = StarRailFpsSettings.WriteFps("{\"fps\":60,\"Width\":1920}", 120);
            Harness.Contains(lower, "\"fps\":120", "原有键名的小写写法应沿用，不该改写成 FPS");
            Harness.Contains(lower, "\"Width\":1920", "其它字段必须原样保留");
        });

        h.Case("缺少 FPS 字段时补一个 FPS，非对象 JSON 拒绝改写", () =>
        {
            var added = StarRailFpsSettings.WriteFps("{\"Width\":1920}", 120);
            Harness.Contains(added, "\"FPS\":120", "应补出 FPS 字段");
            Harness.Contains(added, "\"Width\":1920", "其它字段必须原样保留");

            var threw = false;
            try { StarRailFpsSettings.WriteFps("[60]", 120); }
            catch (InvalidOperationException) { threw = true; }
            Harness.True(threw, "根不是对象时应抛 InvalidOperationException，而不是写出半截内容");
        });

        h.Case("改写后再读取应稳定（往返一致）", () =>
        {
            const string original = "{\"FPS\":60,\"Width\":1920}";
            var once = StarRailFpsSettings.WriteFps(original, 120);
            var twice = StarRailFpsSettings.WriteFps(once, 120);

            Harness.True(StarRailFpsSettings.TryReadFps(twice, out var fps), "二次改写后仍可读");
            Harness.Equal(120, fps, "二次改写结果不变");
            Harness.Equal(once, twice, "同值重复改写应是幂等的");
        });
    }
}
