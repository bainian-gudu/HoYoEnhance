namespace HoYoEnhance.Contracts;

/// <summary>WebView2 桥接使用的单个游戏配置 DTO。</summary>
[TsName("GameProfile")]
public sealed class GameProfileDto
{
    public int TargetFps { get; init; } = 120;
    public bool Enabled { get; init; } = true;
    public bool AntiBlurPerspective { get; init; }
    public bool AntiBlurDiveMosaic { get; init; }
    public bool HideUid { get; init; }
    public string? GamePath { get; init; }
}

/// <summary>WebView2 桥接使用的游戏档案容器。</summary>
[TsName("GameProfiles")]
public sealed class GameProfilesDto
{
    public GameProfileDto Genshin { get; init; } = new();
    public GameProfileDto StarRail { get; init; } = new();
}

/// <summary>
/// WebView2 桥接使用的完整配置 DTO。字段名与前端 JSON 契约一致，
/// 生成器据此产出 TypeScript 类型，避免两端各维护一份接口。
/// </summary>
[TsName("UnlockerConfig")]
public sealed class UnlockerConfigDto
{
    public GameId ActiveGame { get; init; } = GameId.Genshin;
    public GameProfilesDto Games { get; init; } = new();
    public bool MasterEnabled { get; init; } = true;
    public bool AutoWatch { get; init; } = true;
    public bool StartMinimized { get; init; }
    public bool AutoStartWithWindows { get; init; }
    public bool AutoStartAsAdministrator { get; init; }
    public int PollIntervalMs { get; init; } = 1000;
    public bool SafetyNoticeAcknowledged { get; init; }
    public bool ShowSafetyNoticeOnStartup { get; init; } = true;
    public bool DefenderExclusionApplied { get; init; }
    public bool DebugLogging { get; init; } = true;

    /// <summary>持久化仍是字符串；这里用属性指向生成的 LogLevel 联合类型。</summary>
    [TsType("LogLevel")]
    public string LogLevel { get; init; } = "Debug";

    public int LogRetainDays { get; init; } = 14;
    public bool SuppressAdminHint { get; init; }
}
