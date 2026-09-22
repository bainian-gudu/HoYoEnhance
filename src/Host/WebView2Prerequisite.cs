using System.Diagnostics;
using Microsoft.Win32;

namespace GenshinFpsUnlocker.Host;

/// <summary>
/// 单用户基线只检查 WebView2：.NET 已随 Host 自包含发布，Stub 静态链接 CRT，
/// 不再有 .NET Desktop Runtime / VCRedist 的安装或回退分支。
/// </summary>
internal static class WebView2Prerequisite
{
    public const string WebView2RuntimeUrl =
        "https://developer.microsoft.com/microsoft-edge/webview2/";

    public const string WebView2RuntimeDirectX64 =
        "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>
    /// 缺失时提示用户安装。quiet / autostart 不弹窗，只记日志并返回 false。
    /// </summary>
    public static bool EnsureOrPrompt(bool quiet)
    {
        if (IsInstalled(out var detail))
        {
            AppLog.Info("WebView2 runtime: " + detail);
            return true;
        }

        if (quiet)
        {
            AppLog.Error("WebView2 runtime missing (quiet): " + detail);
            return false;
        }

        var r = MessageBox.Show(
            "未检测到 Microsoft Edge WebView2 Runtime。\n\n" +
            "主界面需要 WebView2 才能显示。Windows 10/11 通常已预装；\n" +
            "若被卸载或企业环境缺失，请安装官方 Evergreen Runtime 后重试。\n\n" +
            $"检测详情：{detail}\n\n" +
            $"下载页：{WebView2RuntimeUrl}\n" +
            $"安装包(x64)：{WebView2RuntimeDirectX64}\n\n" +
            "是 = 打开下载页并退出；否 = 退出。",
            AppPaths.ProductDisplayName + " — 缺少 WebView2 运行时",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (r == DialogResult.Yes) OpenUrl(WebView2RuntimeUrl);
        return false;
    }

    /// <summary>检测 Evergreen WebView2 Runtime（注册表 pv 或安装路径）。</summary>
    public static bool IsInstalled(out string detail)
    {
        var notes = new List<string>();
        try
        {
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var path in new[]
                         {
                             @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                             @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                         })
                {
                    try
                    {
                        using var key = root.OpenSubKey(path);
                        var pv = key?.GetValue("pv") as string;
                        if (!string.IsNullOrWhiteSpace(pv) && pv != "0.0.0.0")
                        {
                            detail = $"registry pv={pv}";
                            return true;
                        }
                        if (pv is not null) notes.Add($"pv={pv}");
                    }
                    catch (Exception ex) { notes.Add(ex.Message); }
                }
            }
        }
        catch (Exception ex) { notes.Add(ex.Message); }

        try
        {
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var c in new[]
                     {
                         Path.Combine(pf86, "Microsoft", "EdgeWebView", "Application", "msedgewebview2.exe"),
                         Path.Combine(pf, "Microsoft", "EdgeWebView", "Application", "msedgewebview2.exe"),
                     })
            {
                if (File.Exists(c))
                {
                    detail = "found " + c;
                    return true;
                }
            }
        }
        catch (Exception ex) { notes.Add(ex.Message); }

        detail = notes.Count == 0 ? "WebView2 runtime not found" : string.Join("; ", notes);
        return false;
    }

    /// <summary>用默认浏览器打开 URL；失败则尝试复制到剪贴板。</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "open url " + url);
            try { Clipboard.SetText(url); } catch { /* ignore */ }
            MessageBox.Show(
                "无法打开浏览器，已尝试复制链接到剪贴板：\n" + url,
                "下载",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
