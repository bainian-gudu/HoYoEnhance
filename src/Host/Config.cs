using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenshinFpsUnlocker.Host;

/// <summary>
/// 应用配置（JSON）。主路径：用户数据目录下的 config.json。
/// 持久化策略：写临时文件 → Flush → File.Replace 原子替换 → 保留 .bak 备份；
/// 读失败时依次尝试主文件 / .bak / .tmp，降低丢失与半截写入风险。
/// 配置里不认识的字段由反序列化器直接忽略。
/// </summary>
internal sealed partial class AppConfig
{
    /// <summary>当前正在配置的游戏：概览 / 设置 / 使用指南跟着它切换。</summary>
    public GameId ActiveGame { get; set; } = GameId.Genshin;

    /// <summary>
    /// 每个游戏一份的独立配置（帧率、开关、画面效果、游戏路径）。
    /// 两款游戏互不共享，切换游戏不会互相覆盖。
    /// </summary>
    public GameProfiles Games
    {
        get => _games;
        set => _games = value ?? new GameProfiles();
    }
    private GameProfiles _games = new();

    /// <summary>当前游戏档案（读配置的快捷方式）。</summary>
    [JsonIgnore]
    public GameProfile ActiveProfile => Games.Get(ActiveGame);

    /// <summary>取指定游戏的档案。</summary>
    public GameProfile Profile(GameId game) => Games.Get(game);

    /// <summary>是否监视游戏进程并在启动后自动注入。</summary>
    public bool AutoWatch { get; set; } = true;

    /// <summary>
    /// 启动时是否最小化到系统托盘。
    /// 默认 false：打开软件显示主窗口；关窗/点最小化仍进托盘后台。
    /// 勾选后：下次启动直接进托盘。
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// 主窗口左上角位置（设备像素、虚拟桌面坐标，可为负）。
    /// null = 从未记录过：首次打开居中；记录后每次打开恢复到用户移动到的位置。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WindowLeft { get; set; }

    /// <summary>主窗口左上角 Y 坐标，语义见 <see cref="WindowLeft"/>。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WindowTop { get; set; }

    /// <summary>
    /// 是否随 Windows 登录自动启动（见 <see cref="Autostart.SyncLoginStartup"/>）：
    /// 默认写 HKCU\...\Run（标准权限）；与 <see cref="AutoStartAsAdministrator"/>
    /// 同时开启时改登记最高权限计划任务，登录即高权限、不弹 UAC。
    /// </summary>
    public bool AutoStartWithWindows { get; set; } = false;

    /// <summary>
    /// 以管理员权限运行：手动启动时请求一次 UAC；与 <see cref="AutoStartWithWindows"/>
    /// 同时开启时，登录自启用最高权限计划任务启动（不弹 UAC）。
    /// </summary>
    public bool AutoStartAsAdministrator { get; set; } = false;

    /// <summary>
    /// 总开关。关闭时：不注入、不强制帧率，后台仍可待命。
    /// 与 <see cref="Enabled"/> 的区别：总开关优先级更高，可一键暂停全部解锁行为。
    /// </summary>
    public bool MasterEnabled { get; set; } = true;

    /// <summary>监视循环基准轮询间隔（毫秒，200–10000）。</summary>
    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>用户是否已确认过安全声明。</summary>
    public bool SafetyNoticeAcknowledged { get; set; } = false;

    /// <summary>每次非自启动启动时是否再次展示安全声明。</summary>
    public bool ShowSafetyNoticeOnStartup { get; set; } = true;

    /// <summary>安装器是否已尝试添加 Defender 排除（仅记录状态）。</summary>
    public bool DefenderExclusionApplied { get; set; } = false;

    /// <summary>是否写调试日志（默认开启，便于排查注入问题）。</summary>
    public bool DebugLogging { get; set; } = true;

    /// <summary>最低日志级别：Trace / Debug / Info / Warn / Error（默认 Debug）。</summary>
    public string LogLevel { get; set; } = "Debug";

    /// <summary>日志保留天数（超过则清理 app-*.log）。</summary>
    public int LogRetainDays { get; set; } = 14;

    /// <summary>是否隐藏界面上的「建议管理员运行」提示条（默认显示；可在设置中关闭）。</summary>
    public bool SuppressAdminHint { get; set; } = false;


    /// <summary>配置文件完整路径（不序列化）。</summary>
    [JsonIgnore]
    public static string ConfigPath => AppPaths.ConfigPath;

    /// <summary>
    /// 本实例是否成功反序列化自磁盘配置文件。
    /// false = 走的是默认值（文件缺失/残损），调用方不得把默认值当成
    /// 「用户明确关闭了某项」去执行破坏性同步（例如删除开机自启注册表项）。
    /// </summary>
    [JsonIgnore]
    public bool LoadedFromDisk { get; internal set; }

    [JsonIgnore]
    private static string BackupPath => ConfigPath + ".bak";

    [JsonIgnore]
    private static string TempPath => ConfigPath + ".tmp";

    private static readonly object IoLock = new();

    // ---- 落盘合并 ----
    // 批量窗口 >0 时，Save/TrySave 只标脏不写盘，窗口关闭时统一写一次。
    private int _batchDepth;
    private bool _batchDirty;
    /// <summary>上次成功落盘的 JSON；内容没变就跳过整套原子写（备份/Replace/校验读）。</summary>
    private string? _lastSavedJson;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 枚举写成字符串（genshin / starRail），配置文件里可读、也不会因枚举顺序变化而错位。
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        // 未知字段保留兼容，避免旧/新版本互相抹掉键
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>钳制数值范围并规范化游戏路径。</summary>
    public void Sanitize()
    {
        Games.Sanitize();
        PollIntervalMs = Math.Clamp(PollIntervalMs, 200, 10000);
        LogRetainDays = Math.Clamp(LogRetainDays, 1, 90);
        if (string.IsNullOrWhiteSpace(LogLevel)) LogLevel = "Debug";
    }
}
