using System.Diagnostics;
using System.Security.Principal;

namespace GenshinFpsUnlocker.Host;

/// <summary>
/// 权限辅助：日常运行 asInvoker（无 UAC）；仅「用户主动以管理员身份重启」时 runas。
/// 开机自启路径绝不可触发 UAC 弹窗。
/// 安装 / 卸载由 Kachina 安装器自行处理提权，宿主不再有任何安装用途的提权路径。
/// </summary>
internal static class Elevation
{
    /// <summary>当前进程是否已具备管理员令牌。</summary>
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 若尚未提权，则用 runas 重新启动自身并带上指定参数，当前进程应随后退出。
    /// 成功拉起返回 true；用户取消 UAC 或失败返回 false。
    /// </summary>
    public static bool TryRelaunchElevated(string arguments, out string error)
    {
        error = string.Empty;
        if (IsAdministrator())
            return true;

        try
        {
            var exe = AppPaths.ExePath;
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas", // 触发一次 UAC（用户主动）
                WorkingDirectory = AppPaths.ExeDirectory,
            };
            Process.Start(psi)?.Dispose();   // 只要句柄别留给 GC，进程本身照跑
            return true;
        }
        catch (Exception ex)
        {
            // 用户点“否”会抛 Win32Exception
            error = ex.Message;
            AppLog.Warn("提权启动失败: " + ex.Message);
            return false;
        }
    }

}
