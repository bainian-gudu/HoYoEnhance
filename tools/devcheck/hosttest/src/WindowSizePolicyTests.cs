namespace GenshinFpsUnlocker.Host.Tests;

internal static class WindowSizePolicyTests
{
    public static void Run(Harness h)
    {
        h.Case("小屏工作区会压缩目标高度并保留边距", () =>
        {
            var plan = WindowSizePolicy.FitToWorkArea(
                new WindowDimensions(1180, 760),
                new WindowDimensions(960, 640),
                workWidth: 1366,
                workHeight: 728);

            Harness.Equal(new WindowDimensions(960, 640), plan.Minimum, "最小尺寸应保持在可用区域内");
            Harness.Equal(new WindowDimensions(1180, 648), plan.Target, "目标高度应裁到工作区减边距");
        });

        h.Case("极小工作区会把最小尺寸和窗口一起压到可用区域", () =>
        {
            var plan = WindowSizePolicy.FitToWorkArea(
                new WindowDimensions(1180, 760),
                new WindowDimensions(960, 640),
                workWidth: 1024,
                workHeight: 560);

            Harness.Equal(new WindowDimensions(944, 480), plan.Minimum, "最小尺寸应退到可用区域");
            Harness.Equal(new WindowDimensions(944, 480), plan.Target, "窗口不应超出可用区域");
        });

        h.Case("大工作区保留设计尺寸", () =>
        {
            var plan = WindowSizePolicy.FitToWorkArea(
                new WindowDimensions(1180, 760),
                new WindowDimensions(960, 640),
                workWidth: 2560,
                workHeight: 1400);

            Harness.Equal(new WindowDimensions(960, 640), plan.Minimum, "最小尺寸");
            Harness.Equal(new WindowDimensions(1180, 760), plan.Target, "目标尺寸");
        });

        h.Case("无效工作区不改变原尺寸", () =>
        {
            var desired = new WindowDimensions(1180, 760);
            var minimum = new WindowDimensions(960, 640);
            var plan = WindowSizePolicy.FitToWorkArea(desired, minimum, 0, 0);

            Harness.Equal(minimum, plan.Minimum, "无效工作区时保留最小尺寸");
            Harness.Equal(desired, plan.Target, "无效工作区时保留目标尺寸");
        });

        h.Case("窗口在工作区内居中", () =>
        {
            var position = WindowSizePolicy.CenterInWorkArea(
                new WindowDimensions(1180, 648),
                workLeft: 0,
                workTop: 0,
                workWidth: 1366,
                workHeight: 728);

            Harness.Equal(new WindowPosition(93, 40), position, "主屏工作区居中坐标");
        });

        h.Case("负坐标副屏也能正确居中", () =>
        {
            var position = WindowSizePolicy.CenterInWorkArea(
                new WindowDimensions(1180, 760),
                workLeft: -1920,
                workTop: 0,
                workWidth: 1920,
                workHeight: 1080);

            Harness.Equal(new WindowPosition(-1550, 160), position, "副屏工作区居中坐标");
        });
    }
}
