using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GenshinFpsUnlocker.Host;

internal enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed,
}

internal sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseName,
    string? ReleaseNotes,
    string? ReleaseUrl,
    string? PublishedAt,
    string? Error);

/// <summary>
/// 在线更新：查询 GitHub Releases，并启动安装目录内的 Kachina 更新程序。
/// 下载、校验、替换文件都由更新程序完成；宿主只负责发现新版本和交棒。
/// </summary>
internal static class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/bainian-gudu/HoYoEnhance/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    public static async Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        var current = NormalizeVersion(currentVersion) ?? currentVersion;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HoYoEnhance", current));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await Http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Failed(
                    current,
                    $"GitHub Releases 返回 {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var root = document.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                return Failed(current, "GitHub 最新发行版仍是草稿");
            if (root.TryGetProperty("prerelease", out var prerelease)
                && prerelease.ValueKind == JsonValueKind.True)
                return Failed(current, "GitHub 最新发行版是预发行版");

            var tag = ReadString(root, "tag_name");
            if (!TryParseVersion(tag, out var latest) || !TryParseVersion(current, out var installed))
                return Failed(current, "无法解析发行版版本号");

            var latestVersion = FormatVersion(latest);
            var releaseName = ReadString(root, "name") ?? $"HoYoEnhance {latestVersion}";
            var notes = ReadString(root, "body");
            var releaseUrl = ReadString(root, "html_url");
            var publishedAt = ReadString(root, "published_at");

            return latest > installed
                ? new UpdateCheckResult(
                    UpdateCheckStatus.UpdateAvailable,
                    current,
                    latestVersion,
                    releaseName,
                    notes,
                    releaseUrl,
                    publishedAt,
                    null)
                : new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    current,
                    latestVersion,
                    releaseName,
                    notes,
                    releaseUrl,
                    publishedAt,
                    null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(current, "检查更新超时");
        }
        catch (HttpRequestException ex)
        {
            return Failed(current, "无法连接 GitHub Releases：" + ex.Message);
        }
        catch (JsonException ex)
        {
            return Failed(current, "GitHub Releases 响应无法解析：" + ex.Message);
        }
        catch (Exception ex)
        {
            return Failed(current, ex.Message);
        }
    }

    public static bool TryLaunchUpdater(out string? error)
    {
        error = null;
        var updater = AppPaths.UpdaterPath;
        if (!File.Exists(updater))
        {
            error = "未找到更新程序：" + updater;
            return false;
        }

        // 更新器可能在已提权宿主中被拉起，沿用卸载器/注入模块的可信校验，
        // 避免用户可写目录里的同名 exe 借用当前令牌执行。
        if (!ModuleTrust.IsTrustworthy(
                updater,
                AppPaths.UpdaterFileName,
                "更新程序",
                out error,
                elevatedHint: "请退出管理员实例后按普通权限重试。"))
        {
            AppLog.Error("拒绝启动更新程序: " + error);
            return false;
        }

        try
        {
            var directory = AppPaths.ExeDirectory.Replace("\"", "\\\"");
            var start = new ProcessStartInfo
            {
                FileName = updater,
                // -I 非交互启动；-D 明确安装目录，便携包和自定义安装路径都按当前目录更新。
                Arguments = $"-I -D \"{directory}\"",
                WorkingDirectory = AppPaths.ExeDirectory,
                // CreateProcess 继承当前进程令牌：宿主已提权时更新器直接获得管理员权限，
                // 不再经过 shell / UAC；普通权限实例仍由更新器自身按需提权。
                UseShellExecute = false,
            };
            Process.Start(start)?.Dispose();
            AppLog.Info(
                Elevation.IsAdministrator()
                    ? "已继承管理员令牌启动 Kachina 更新程序: " + updater
                    : "已启动 Kachina 更新程序: " + updater);
            return true;
        }
        catch (Exception ex)
        {
            error = "启动更新程序失败：" + ex.Message;
            return false;
        }
    }

    public static bool IsSameVersion(string? left, string? right)
    {
        return TryParseVersion(left, out var a)
               && TryParseVersion(right, out var b)
               && a == b;
    }

    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        var suffix = text.IndexOfAny(['-', '+']);
        if (suffix >= 0) text = text[..suffix];

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 4) return false;

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0) return false;
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    private static string? NormalizeVersion(string? value)
        => TryParseVersion(value, out var version) ? FormatVersion(version) : null;

    private static string FormatVersion(Version version)
        => version.Revision > 0 ? version.ToString(4) : version.ToString(3);

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString()
            : null;

    private static UpdateCheckResult Failed(string current, string error)
        => new(
            UpdateCheckStatus.Failed,
            current,
            null,
            null,
            null,
            null,
            null,
            error);

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(12),
        };
    }
}
