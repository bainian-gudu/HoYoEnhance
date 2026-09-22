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
        PathUtil.EnsureDir(AppPaths.DataDirectory);
    }

}
