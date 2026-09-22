using Microsoft.Web.WebView2.WinForms;

namespace GenshinFpsUnlocker.Host;

/// <summary>
/// 主窗口：嵌入 WebView2 呈现设计稿 UI；是否将最小化 / 关闭驻留托盘跟随 UI 开关。
/// </summary>
internal sealed partial class MainForm : Form
{
    private readonly AppConfig _config;
    private readonly UnlockService _service;
    private readonly UiBridge _bridge;
    private WebView2 _webView = null!;
    private Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment> _webEnvironmentTask = null!;
    private Panel _webLoadingSurface = null!;
    private Label _webLoadingText = null!;
    private NotifyIcon _tray = null!;

    private bool _reallyExit;
    private bool _syncingUi;
    private bool _webReady;
    /// <summary>仅 WebView2 已接管客户区绘制时允许启用亚克力。</summary>
    internal bool IsWebContentReady => _webReady;
    private bool _suppressResizeHide;
    /// <summary>正在执行最小化→托盘，防止 Resize 重入导致闪烁/连弹。</summary>
    private bool _hidingToTray;
    /// <summary>
    /// 启动时若「最小化到托盘」：在 Web 就绪前拦截 Show，避免主窗闪几秒再消失。
    /// </summary>
    private bool _allowVisible = true;
    /// <summary>仍处于「启动进托盘」阶段（尚未完成首次入托盘）。</summary>
    private bool _startupTrayPending;
    /// <summary>
    /// 「启动进托盘」本进程只允许发生一次。
    /// 之后用户主动打开的主窗（托盘图标 / 二次点快捷方式唤醒 / UI showWindow）
    /// 绝不能再被任何延迟到达的兜底调用藏回托盘。
    /// </summary>
    private bool _startupTrayDone;
    /// <summary>启动托盘时暂存正常位置，恢复时用。</summary>
    private Point _restoreLocation;
    private bool _hasRestoreLocation;
    /// <summary>窗口位置落盘节流：拖动时 LocationChanged 连发，合并成一次保存。</summary>
    private System.Windows.Forms.Timer? _windowLocationSaveTimer;
    /// <summary>待落盘的用户摆放位置，见 <see cref="WindowLocationState"/>。</summary>
    private readonly WindowLocationState _windowLocation = new();
    /// <summary>设计尺寸只在首个窗口句柄建好后套用一次，句柄重建（托盘切换）不得重置用户尺寸。</summary>
    private bool _initialSizeApplied;
    private CancellationTokenSource? _wakeCts;
    private System.Windows.Forms.Timer? _trayRecoveryTimer;

    /// <summary>
    /// 设计尺寸与最小尺寸（逻辑像素，和 Web UI 的 CSS 断点同一套单位）。
    /// 真正下发给窗口前会按当前 DPI 折算成设备像素：只按设备像素给尺寸的话，
    /// 150% / 200% 缩放下的逻辑宽度会缩水，顶栏会换行、溢出。
    /// </summary>
    private static readonly Size DefaultLogicalSize = new(1180, 760);
    private static readonly Size MinimumLogicalSize = new(960, 640);

    public MainForm(AppConfig config, UnlockService service, bool? startMinimizedOverride = null)
    {
        _config = config;
        _service = service;
        _bridge = new UiBridge(config, service, this);
        var startMinimized = startMinimizedOverride ?? _config.StartMinimized;

        Text = AppPaths.ProductTitle;
        Size = DefaultLogicalSize;
        MinimumSize = MinimumLogicalSize;
        StartPosition = FormStartPosition.CenterScreen;
        // 有历史位置就直接恢复；没有才走 CenterScreen 首次居中。
        if (TryGetSavedWindowLocation(out var savedLocation))
        {
            StartPosition = FormStartPosition.Manual;
            Location = savedLocation;
        }
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ShowInTaskbar = true;
        BackColor = UiStyle.IsUiDark ? UiStyle.UiDarkBg : UiStyle.UiLightBg;

        // 启动进托盘：多管齐下防止「闪几秒再消失」
        // 1) SetVisibleCore 拒绝显示  2) 屏外+透明  3) TOOLWINDOW/NOACTIVATE
        // 4) 不等 Web 就绪，构造末尾即 FinishStartupToTray
        if (startMinimized)
        {
            _allowVisible = false;
            _startupTrayPending = true;
            _inTray = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            try { Opacity = 0; } catch { /* ignore */ }
        }
        try
        {
            var ico = AppIcon.LoadClone();
            if (ico is not null) Icon = ico;
        }
        catch { /* ignore */ }
        UiStyle.ApplyToForm(this);

        if (!startMinimized)
            EnsureLoadingSurface();

        // 托盘必须先于 WebView2 控件和环境创建，登录阶段也要尽快显示。
        WireTrayFallback();
        WireStartupToTray();

        WireInstanceWake();

        WireLoadHandler();

        WireWindowEvents();
    }

    public void RequestExit()
    {
        _reallyExit = true;
        Close();
    }

}
