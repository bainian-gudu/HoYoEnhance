namespace GenshinFpsUnlocker.Host;

/// <summary>不依赖 WinForms 的窗口尺寸。</summary>
internal readonly record struct WindowDimensions(int Width, int Height);

/// <summary>窗口在工作区内的最小尺寸与首次显示尺寸。</summary>
internal readonly record struct WindowSizePlan(WindowDimensions Minimum, WindowDimensions Target);

/// <summary>不依赖 WinForms 的窗口坐标。</summary>
internal readonly record struct WindowPosition(int X, int Y);

/// <summary>
/// 主窗口尺寸策略：把设计尺寸按当前 DPI 折算后，再限制到屏幕工作区。
/// 小分辨率或高缩放时优先保留窗口边缘留白，避免窗口超出屏幕。
/// </summary>
internal static class WindowSizePolicy
{
    public const int DefaultScreenMargin = 40;

    public static WindowSizePlan FitToWorkArea(
        WindowDimensions desired,
        WindowDimensions minimum,
        int workWidth,
        int workHeight,
        int margin = DefaultScreenMargin)
    {
        if (workWidth <= 0 || workHeight <= 0)
            return new WindowSizePlan(minimum, desired);

        var safeMargin = Math.Max(0, margin);
        var availableWidth = Math.Max(1, workWidth - safeMargin * 2);
        var availableHeight = Math.Max(1, workHeight - safeMargin * 2);

        var minimumWidth = Math.Clamp(minimum.Width, 1, availableWidth);
        var minimumHeight = Math.Clamp(minimum.Height, 1, availableHeight);
        var targetWidth = Math.Clamp(desired.Width, minimumWidth, availableWidth);
        var targetHeight = Math.Clamp(desired.Height, minimumHeight, availableHeight);

        return new WindowSizePlan(
            new WindowDimensions(minimumWidth, minimumHeight),
            new WindowDimensions(targetWidth, targetHeight));
    }

    public static WindowPosition CenterInWorkArea(
        WindowDimensions size,
        int workLeft,
        int workTop,
        int workWidth,
        int workHeight)
    {
        if (workWidth <= 0 || workHeight <= 0)
            return new WindowPosition(workLeft, workTop);

        return new WindowPosition(
            workLeft + Math.Max(0, (workWidth - size.Width) / 2),
            workTop + Math.Max(0, (workHeight - size.Height) / 2));
    }
}
