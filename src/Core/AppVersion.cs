using System.Reflection;

namespace GenshinFpsUnlocker.Host;

/// <summary>
/// 应用版本号。唯一来源是仓库根 VERSION 文件，经 Directory.Build.props 写入程序集版本；
/// 运行时代码只读程序集版本，不再各自硬编码。
/// </summary>
internal static class AppVersion
{
    /// <summary>三段式版本号（X.Y.Z），例如 1.0.0。</summary>
    public static string Current { get; } =
        typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>程序集版本（四段式，X.Y.Z.0）。</summary>
    public static string AssemblyVersion { get; } =
        typeof(AppVersion).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
}
