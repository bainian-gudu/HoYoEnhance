namespace GenshinFpsUnlocker.Host.Tests;

/// <summary>
/// 路径判定的断言。这里的两个函数是安全边界：
/// <see cref="PathUtil.IsUnder"/> 决定「待加载 / 待执行的文件是否真在本程序目录里」，
/// <see cref="PathUtil.IsDangerousRootOrProfile"/> 决定「这个目录能不能被当成程序目录」。
/// 两者都容易被前缀相似、尾分隔符、大小写、<c>..</c> 这类输入绕过，所以要有用例钉住。
/// </summary>
internal static class PathUtilTests
{
    /// <summary>造一个跨平台稳定的绝对路径（测试在 Windows 与 Linux 上都要跑）。</summary>
    private static string TempRoot() =>
        PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "hoyo-pathutil-" + Guid.NewGuid().ToString("N")));

    public static void Run(Harness h)
    {
        h.Case("子路径与自身都算在父目录下", () =>
        {
            var root = TempRoot();
            var child = Path.Combine(root, "ui", "index.html");

            Harness.True(PathUtil.IsUnder(child, root), "目录下的文件应判为在父目录内");
            Harness.True(PathUtil.IsUnder(root, root), "自身也应在父目录内（模块就在程序目录里）");
        });

        h.Case("前缀相似的兄弟目录不算在父目录下", () =>
        {
            var root = TempRoot();
            // 这是 IsUnder 追加分隔符的理由：纯 StartsWith 会把 FooBar 判成 Foo 的子目录。
            Harness.False(PathUtil.IsUnder(root + "Bar", root), "FooBar 不是 Foo 的子目录");
            Harness.False(PathUtil.IsUnder(root + "Bar\\x.dll", root), "FooBar\\x.dll 也不是");
        });

        h.Case("父目录之外（含 .. 逃逸）一律为假", () =>
        {
            var root = TempRoot();
            var sibling = TempRoot();

            Harness.False(PathUtil.IsUnder(sibling, root), "无关目录不应判为在父目录内");
            Harness.False(PathUtil.IsUnder(Path.Combine(root, "..", "escape.dll"), root),
                ".. 逃逸到父目录后不应再算在子目录内");
        });

        h.Case("尾分隔符与大小写不影响判定", () =>
        {
            var root = TempRoot();
            var child = Path.Combine(root, "Stub.dll");

            Harness.True(PathUtil.IsUnder(child, root + Path.DirectorySeparatorChar), "父目录带尾分隔符仍应命中");
            Harness.True(PathUtil.IsUnder(child.ToUpperInvariant(), root), "Windows 路径比较应忽略大小写");
        });

        h.Case("空输入返回假（旧写法的空值守卫是死代码）", () =>
        {
            var root = TempRoot();

            // 旧实现先拼分隔符再判空：Normalize("") 得到 ""，拼完变成 "\"，判空永不成立。
            Harness.False(PathUtil.IsUnder(null, root), "child 为空应为假");
            Harness.False(PathUtil.IsUnder("", root), "child 为空串应为假");
            Harness.False(PathUtil.IsUnder(root, null), "parent 为空应为假");
            Harness.False(PathUtil.IsUnder(root, "   "), "parent 为空白应为假");
            Harness.False(PathUtil.IsUnder(null, null), "两者都空应为假");
        });

        h.Case("危险目录判定：空、根、用户配置目录为真，普通目录为假", () =>
        {
            Harness.True(PathUtil.IsDangerousRootOrProfile(null), "空路径应判为危险");
            Harness.True(PathUtil.IsDangerousRootOrProfile(""), "空串应判为危险");

            var driveRoot = PathUtil.Normalize(Path.GetPathRoot(Path.GetTempPath()) ?? "/");
            Harness.True(PathUtil.IsDangerousRootOrProfile(driveRoot), $"盘符 / 根目录应判为危险：{driveRoot}");

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
                Harness.True(PathUtil.IsDangerousRootOrProfile(profile), "用户配置目录应判为危险");

            Harness.False(PathUtil.IsDangerousRootOrProfile(TempRoot()), "普通临时目录不应判为危险");
        });

        h.Case("规范化会去掉尾分隔符与引号，便于统一比较", () =>
        {
            var root = TempRoot();

            Harness.Equal(root, PathUtil.Normalize(root + Path.DirectorySeparatorChar), "尾分隔符应被去掉");
            Harness.Equal(root, PathUtil.Normalize("\"" + root + "\""), "路径两侧的引号应被去掉");
            Harness.Equal(string.Empty, PathUtil.Normalize(null), "空输入应返回空串");
        });
    }
}
