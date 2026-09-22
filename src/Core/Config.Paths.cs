namespace GenshinFpsUnlocker.Host;

/// <summary><c>AppConfig</c> 的路径解析：候选读取位置与数据目录。</summary>
internal sealed partial class AppConfig
{
    private static IEnumerable<string> EnumerateCandidateReadPaths()
    {
        var list = new List<string>
        {
            ConfigPath,
            BackupPath,
            TempPath,
        };

        // 残留的进程临时文件（不可在 try/catch 内 yield）
        try
        {
            var dir = AppPaths.DataDirectory;
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.EnumerateFiles(dir, ".config.*.tmp")
                             .OrderByDescending(File.GetLastWriteTimeUtc)
                             .Take(3))
                    list.Add(f);
            }
        }
        catch { /* ignore */ }

        return list;
    }

    private static void EnsureDataDirectory()
    {
        try
        {
            PathUtil.EnsureDir(AppPaths.DataDirectory);
        }
        catch (Exception ex)
        {
            AppLog.Warn("EnsureDataDirectory: " + ex.Message);
            // 回退：用户文档下的旁路（极少见 LocalAppData 不可写）
            try
            {
                var fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    AppPaths.ProductName);
                PathUtil.EnsureDir(fallback);
            }
            catch
            {
                throw new IOException("无法创建配置目录: " + AppPaths.DataDirectory, ex);
            }
        }
    }

}
