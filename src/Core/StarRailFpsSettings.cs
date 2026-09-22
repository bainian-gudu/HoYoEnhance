using System.Text.Json;
using System.Text.Json.Nodes;

namespace GenshinFpsUnlocker.Host;

/// <summary>
/// 星铁画面设置值（<c>GraphicsSettings_Model_h*</c>）的纯文本处理：挑出最新值名、
/// 读取 / 改写里面的 FPS 字段。
///
/// 刻意不碰注册表：这里的改写会整段回写游戏自己的画面设置，写错就是「开关打开了但
/// 解锁静默失效」，所以这段逻辑要在测试台里离线可覆盖（不需要 Windows / 注册表）。
/// </summary>
internal static class StarRailFpsSettings
{
    /// <summary>画面设置值名前缀。</summary>
    public const string ValuePrefix = "GraphicsSettings_Model_h";

    /// <summary>设置值里的帧率字段名。</summary>
    public const string FpsPropertyName = "FPS";

    /// <summary>
    /// 按前缀找出后缀版本号最大的值名；没有匹配时返回 false。
    /// 后缀不是数字时按 0 处理，同号取靠后的那个（与旧实现一致）。
    /// </summary>
    public static bool TryPickNewestValueName(IEnumerable<string> names, out string name)
    {
        name = string.Empty;
        var bestVersion = -1L;
        var found = false;

        foreach (var candidate in names)
        {
            if (candidate is null) continue;
            if (!candidate.StartsWith(ValuePrefix, StringComparison.OrdinalIgnoreCase)) continue;

            var suffix = candidate[ValuePrefix.Length..];
            var version = long.TryParse(suffix, out var parsed) ? parsed : 0;
            if (found && version < bestVersion) continue;

            bestVersion = version;
            name = candidate;
            found = true;
        }

        return found;
    }

    /// <summary>从设置 JSON 里读 FPS 字段（大小写不敏感）。</summary>
    public static bool TryReadFps(string json, out int fps)
    {
        fps = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals(FpsPropertyName, StringComparison.OrdinalIgnoreCase)) continue;
                if (property.Value.ValueKind != JsonValueKind.Number) return false;
                fps = property.Value.GetInt32();
                return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    /// <summary>
    /// 把设置 JSON 里的 FPS 字段改成目标值，其余字段原样保留。
    /// 原有键名的大小写沿用（<c>fps</c> 不会被改写成 <c>FPS</c>）。
    /// </summary>
    public static string WriteFps(string json, int fps)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject obj) throw new InvalidOperationException("画面设置不是 JSON 对象");

        string? key = null;
        foreach (var pair in obj)
        {
            if (pair.Key.Equals(FpsPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                key = pair.Key;
                break;
            }
        }

        obj[key ?? FpsPropertyName] = fps;
        return obj.ToJsonString();
    }
}
