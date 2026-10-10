namespace GenshinFpsUnlocker.Host.Tests;

internal static class UpdateServiceTests
{
    public static void Run(Harness h)
    {
        h.Case("发行版标签支持 v 前缀和预发行后缀", () =>
        {
            Harness.True(UpdateService.TryParseVersion("v1.2.3", out var plain), "普通版本应可解析");
            Harness.Equal("1.2.3.0", plain.ToString(4), "版本归一化");
            Harness.True(UpdateService.TryParseVersion("1.2.3-beta.1", out var pre), "预发行后缀应可解析");
            Harness.Equal("1.2.3.0", pre.ToString(4), "预发行后缀不参与数字比较");
        });

        h.Case("版本比较补零后判断相等", () =>
        {
            Harness.True(UpdateService.IsSameVersion("1.0.1", "v1.0.1.0"), "缺少 revision 的版本应相等");
            Harness.False(UpdateService.IsSameVersion("1.0.1", "1.0.2"), "不同补丁版本不应相等");
            Harness.False(UpdateService.IsSameVersion("latest", "1.0.1"), "非数字版本不应误判相等");
        });
    }
}
