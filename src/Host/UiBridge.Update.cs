namespace GenshinFpsUnlocker.Host;

/// <summary>更新检查与交棒：查询 GitHub Releases、通知页面并启动安装目录内的更新器。</summary>
internal sealed partial class UiBridge
{
    /// <summary>
    /// 启动自动检查。即使当前是「启动进托盘」、WebView 还没创建，也先查一次；
    /// 结果暂存，等页面就绪后再展示。
    /// </summary>
    public void StartAutoUpdateCheck()
    {
        if (!_config.AutoCheckUpdate || _autoUpdateCheckStarted) return;
        _autoUpdateCheckStarted = true;
        _ = RunHostedUpdateCheckAsync("auto");
    }

    /// <summary>托盘菜单主动检查：先打开主界面，再把检查结果交给页面展示。</summary>
    public void CheckUpdateFromTray()
    {
        try
        {
            if (_form.IsHandleCreated && !_form.IsDisposed)
                _form.BeginInvoke(() => _form.RestoreFromTrayPublic());
            else
                _form.RestoreFromTrayPublic();
        }
        catch (Exception ex)
        {
            AppLog.Debug("CheckUpdateFromTray restore: " + ex.Message);
        }

        _ = RunHostedUpdateCheckAsync("tray");
    }

    private async Task<object?> CheckUpdateForUiAsync()
    {
        var result = await CheckUpdateAsync();
        return BuildUpdatePayload(result);
    }

    private async Task RunHostedUpdateCheckAsync(string source)
    {
        var result = await CheckUpdateAsync();

        // 自动检查只提示真正的新版本，且尊重「跳过此版本」；失败不打扰用户。
        if (source == "auto")
        {
            if (result.Status != UpdateCheckStatus.UpdateAvailable) return;
            if (UpdateService.IsSameVersion(result.LatestVersion, _config.SkipUpdateVersion)) return;
        }

        if (_uiReady && _uiBootstrapCompleted)
            PostUpdateNotice(source, result);
        else
            _pendingUpdateNotice = (source, result);
    }

    private (string Source, UpdateCheckResult Result)? TakePendingUpdateNotice()
    {
        var pending = _pendingUpdateNotice;
        _pendingUpdateNotice = null;
        return pending;
    }

    private object? BuildUpdateNoticePayload((string Source, UpdateCheckResult Result)? pending)
        => pending is { } notice
            ? new
            {
                source = notice.Source,
                update = BuildUpdatePayload(notice.Result),
            }
            : null;

    private Task<UpdateCheckResult> CheckUpdateAsync()
    {
        var current = typeof(UiBridge).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return UpdateService.CheckAsync(current);
    }

    private void PostUpdateNotice(string source, UpdateCheckResult result)
        => Post(new
        {
            type = "update",
            source,
            update = BuildUpdatePayload(result),
        });

    private static object BuildUpdatePayload(UpdateCheckResult result) => new
    {
        status = result.Status switch
        {
            UpdateCheckStatus.UpdateAvailable => "available",
            UpdateCheckStatus.UpToDate => "up-to-date",
            _ => "error",
        },
        currentVersion = result.CurrentVersion,
        latestVersion = result.LatestVersion,
        title = result.ReleaseName,
        notes = result.ReleaseNotes,
        releaseUrl = result.ReleaseUrl,
        publishedAt = result.PublishedAt,
        error = result.Error,
    };
}
