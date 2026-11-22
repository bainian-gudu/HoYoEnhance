namespace GenshinFpsUnlocker.Host.Tests;

internal static class GameDefinitionTests
{
    public static void Run(Harness harness)
    {
        harness.Case("前后端共享游戏目录与宿主定义一致", () =>
        {
            Harness.Equal(GameDefinitions.DefaultGame, GameCatalog.DefaultGame, "默认游戏来源");
            Harness.Equal(GameDefinitions.All.Count, GameCatalog.All.Count, "游戏数量");
            foreach (var definition in GameDefinitions.All)
            {
                var descriptor = GameCatalog.Get(definition.Id);
                Harness.Equal(definition.Key, descriptor.Key, "游戏键");
                Harness.Equal(definition.DisplayName, descriptor.DisplayName, "游戏名");
                Harness.Equal(definition.StubFileName, descriptor.StubFileName, "注入模块名");
                Harness.Equal(definition.FpsViaRegistry, descriptor.FpsViaRegistry, "帧率实现方式");
                Harness.Equal(definition.LockedFps, descriptor.LockedFps, "固定帧率");
                Harness.Equal(definition.SupportsDiveMosaic, descriptor.SupportsDiveMosaic, "游戏功能能力");
            }
        });
    }
}
