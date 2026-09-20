namespace GenshinFpsUnlocker.Host.Tests;

/// <summary>
/// 导出配置到用户所选目录的行为断言。
/// 目录选择框本身要 Windows 桌面会话，这里只测它背后的写盘约定：
/// 文件名固定、内容原样落盘、同名文件覆盖、目录无效时报错而不是静默丢弃。
/// </summary>
internal static class ConfigExportTests
{
    private const string Sample = "{\n  \"activeGame\": \"genshin\"\n}\n";

    public static void Run(Harness h)
    {
        h.Case("导出把配置写进所选目录并返回完整路径", () =>
        {
            var root = NewTempRoot();
            try
            {
                var target = Path.Combine(root, "导出");
                Directory.CreateDirectory(target);

                var path = ConfigExport.WriteToDirectory(target, Sample);

                Harness.Equal(
                    PathUtil.Normalize(Path.Combine(target, "config.json")),
                    PathUtil.Normalize(path),
                    "导出路径");
                Harness.True(File.Exists(path), "导出文件必须落盘");
                Harness.Equal(Sample, File.ReadAllText(path), "导出内容");
            }
            finally { Cleanup(root); }
        });

        h.Case("导出覆盖目录里同名的旧配置", () =>
        {
            var root = NewTempRoot();
            try
            {
                var stale = Path.Combine(root, "config.json");
                File.WriteAllText(stale, "{\"activeGame\":\"starRail\"}");

                var path = ConfigExport.WriteToDirectory(root, Sample);

                Harness.Equal(Sample, File.ReadAllText(path), "旧文件应被新内容替换");
            }
            finally { Cleanup(root); }
        });

        h.Case("目录不存在时导出失败而不是静默丢弃", () =>
        {
            var missing = Path.Combine(Path.GetTempPath(), $"devcheck-export-missing-{Guid.NewGuid():N}");
            var threw = false;
            try { ConfigExport.WriteToDirectory(missing, Sample); }
            catch (InvalidOperationException) { threw = true; }

            Harness.True(threw, "不存在的目录必须报错");
        });

        h.Case("空目录参数被拒绝", () =>
        {
            var threw = false;
            try { ConfigExport.WriteToDirectory("   ", Sample); }
            catch (InvalidOperationException) { threw = true; }

            Harness.True(threw, "空目录必须报错");
        });

        h.Case("ExistsInDirectory 只认目录下的 config.json", () =>
        {
            var root = NewTempRoot();
            try
            {
                Harness.False(ConfigExport.ExistsInDirectory(root), "空目录不应报存在");
                File.WriteAllText(Path.Combine(root, "other.json"), "{}");
                Harness.False(ConfigExport.ExistsInDirectory(root), "其它文件名不算");
                File.WriteAllText(Path.Combine(root, ConfigExport.FileName), "{}");
                Harness.True(ConfigExport.ExistsInDirectory(root), "同名文件应被识别");
                Harness.False(ConfigExport.ExistsInDirectory(null), "空目录参数返回 false");
            }
            finally { Cleanup(root); }
        });
    }

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"devcheck-config-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
    }
}
