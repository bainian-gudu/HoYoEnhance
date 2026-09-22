using Microsoft.Win32;

namespace GenshinFpsUnlocker.Host;

/// <summary>星穹铁道注册表解锁的结果类型。</summary>
internal enum StarRailFpsOutcome
{
    /// <summary>已经是 120 FPS，本次没有改动注册表。</summary>
    AlreadyAtTarget,
    /// <summary>写入 120 FPS 成功（需要重启游戏生效）。</summary>
    Written,
    /// <summary>游戏还没写过画面设置（没有 GraphicsSettings_Model_h* 值）。</summary>
    ValueMissing,
    /// <summary>值存在但不是可解析的 JSON，或没有 FPS 字段。</summary>
    UnsupportedValue,
    /// <summary>读写注册表失败（权限 / 键不存在）。</summary>
    RegistryError,
}

/// <summary>一次注册表核对的结果。</summary>
internal readonly record struct StarRailFpsResult(
    StarRailFpsOutcome Outcome,
    int? CurrentFps,
    string? ValueName,
    string Detail)
{
    /// <summary>是否已经处于目标状态（含本次写入成功）。</summary>
    public bool Ok => Outcome is StarRailFpsOutcome.AlreadyAtTarget or StarRailFpsOutcome.Written;

    /// <summary>本次是否真的改了注册表。</summary>
    public bool Changed => Outcome == StarRailFpsOutcome.Written;
}

/// <summary>
/// 崩坏：星穹铁道的帧率解锁：直接改注册表里的画面设置，不注入游戏进程。
///
/// 规则（用户确认的方案）：
///   1) 只在启用解锁时才动注册表；
///   2) 先读 <c>GraphicsSettings_Model_h&lt;版本号&gt;</c>，已经是 120 FPS 就不覆盖；
///   3) 没到 120 才写入 120，只支持 120 这一个值；
///   4) 关闭开关不回写，游戏沿用注册表里现有的设置。
///
/// 版本号按前缀匹配而不是写死：游戏每次更新都会换一个 <c>_h</c> 后缀，
/// 写死某个数字会在版本更新后静默失效。存在多个时取后缀最大的那个。
/// </summary>
internal static class StarRailFpsRegistry
{
    /// <summary>该游戏唯一支持的解锁帧率。</summary>
    public const int TargetFps = 120;

    /// <summary>可能的注册表位置（国服键名是中文；另两个是历史 / 国际服写法）。</summary>
    private static readonly string[] CandidateKeyPaths =
    [
        @"Software\miHoYo\崩坏：星穹铁道",
        @"Software\miHoYo\Star Rail",
        @"Software\miHoYo\StarRail",
        @"Software\Cognosphere\Star Rail",
    ];

    /// <summary>读取当前注册表里的帧率设置（不改动）。</summary>
    public static StarRailFpsResult Read()
    {
        foreach (var path in CandidateKeyPaths)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(path, writable: false);
                if (key is null) continue;

                var target = FindNewestModelValue(key);
                if (target is null)
                    return new StarRailFpsResult(StarRailFpsOutcome.ValueMissing, null, null,
                        $"{path} 下还没有画面设置（GraphicsSettings_Model_h*）：请先启动一次游戏");

                var (name, json, _) = target.Value;
                if (!StarRailFpsSettings.TryReadFps(json, out var fps))
                    return new StarRailFpsResult(StarRailFpsOutcome.UnsupportedValue, null, name,
                        $"无法从 {name} 解析 FPS 字段");

                return new StarRailFpsResult(StarRailFpsOutcome.AlreadyAtTarget, fps, name,
                    $"{name} 当前 {fps} FPS");
            }
            catch (Exception ex)
            {
                return new StarRailFpsResult(StarRailFpsOutcome.RegistryError, null, null,
                    $"读取注册表失败：{ex.Message}");
            }
        }

        return new StarRailFpsResult(StarRailFpsOutcome.ValueMissing, null, null,
            "注册表里没有找到星穹铁道的画面设置：请先启动一次游戏");
    }

    /// <summary>
    /// 启用解锁时的核对入口：已经是 120 就不写，否则写入 120。
    /// </summary>
    public static StarRailFpsResult Ensure()
    {
        foreach (var path in CandidateKeyPaths)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(path, writable: false);
                if (key is null) continue;

                var target = FindNewestModelValue(key);
                if (target is null)
                    return new StarRailFpsResult(StarRailFpsOutcome.ValueMissing, null, null,
                        $"{path} 下还没有画面设置：请先启动一次游戏再开启解锁");

                var (name, json, kind) = target.Value;
                if (!StarRailFpsSettings.TryReadFps(json, out var fps))
                    return new StarRailFpsResult(StarRailFpsOutcome.UnsupportedValue, null, name,
                        $"无法从 {name} 解析 FPS 字段，已跳过写入");

                if (fps == TargetFps)
                    return new StarRailFpsResult(StarRailFpsOutcome.AlreadyAtTarget, fps, name,
                        $"{name} 已经是 {TargetFps} FPS，未覆盖");

                var updated = StarRailFpsSettings.WriteFps(json, TargetFps);
                using var writable = Registry.CurrentUser.OpenSubKey(path, writable: true);
                if (writable is null)
                    return new StarRailFpsResult(StarRailFpsOutcome.RegistryError, fps, name,
                        $"注册表项不可写：{path}");

                // 按游戏原本写的类型回写：画面设置出现过 REG_MULTI_SZ 写法，一律写成
                // REG_SZ 会让游戏读不到，表现为「开关打开了但解锁静默失效」。
                if (kind == RegistryValueKind.MultiString)
                    writable.SetValue(name, new[] { updated }, RegistryValueKind.MultiString);
                else
                    writable.SetValue(name, updated, RegistryValueKind.String);
                return new StarRailFpsResult(StarRailFpsOutcome.Written, TargetFps, name,
                    $"{name}：{fps} → {TargetFps} FPS（重启游戏后生效）");
            }
            catch (Exception ex)
            {
                return new StarRailFpsResult(StarRailFpsOutcome.RegistryError, null, null,
                    $"写入注册表失败：{ex.Message}");
            }
        }

        return new StarRailFpsResult(StarRailFpsOutcome.ValueMissing, null, null,
            "注册表里没有找到星穹铁道的画面设置：请先启动一次游戏再开启解锁");
    }

    /// <summary>
    /// 按前缀找出后缀版本号最大的画面设置值，连同它的值类型一起返回
    /// （写回时要保持原类型）。
    /// </summary>
    private static (string Name, string Json, RegistryValueKind Kind)? FindNewestModelValue(RegistryKey key)
    {
        if (!StarRailFpsSettings.TryPickNewestValueName(key.GetValueNames(), out var name)) return null;

        var raw = key.GetValue(name);
        var json = raw switch
        {
            string s => s,
            string[] array when array.Length > 0 => array[0],
            _ => null,
        };
        if (json is null) return null;

        // 值类型读不到时退回 REG_SZ：能读能写总比整段拒绝写入好，且这是历史默认。
        RegistryValueKind kind;
        try { kind = key.GetValueKind(name); }
        catch { kind = RegistryValueKind.String; }

        return (name, json, kind);
    }
}
