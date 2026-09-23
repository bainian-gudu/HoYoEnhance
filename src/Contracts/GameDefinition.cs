namespace HoYoEnhance.Contracts;

public sealed record GameDefinition(
    GameId Id,
    string Key,
    string DisplayName,
    string ShortName,
    string[] ProcessNames,
    string[] ExecutableNames,
    string StubFileName,
    bool FpsViaRegistry,
    int LockedFps,
    bool SupportsDiveMosaic);

public static class GameDefinitions
{
    public static GameId DefaultGame => GameId.Genshin;

    public static readonly GameDefinition Genshin = new(
        GameId.Genshin,
        "genshin",
        "原神",
        "原神",
        ["YuanShen", "GenshinImpact"],
        ["YuanShen", "GenshinImpact"],
        "FpsUnlockerStub.dll",
        FpsViaRegistry: false,
        LockedFps: 0,
        SupportsDiveMosaic: true);

    public static readonly GameDefinition StarRail = new(
        GameId.StarRail,
        "starRail",
        "崩坏：星穹铁道",
        "星穹铁道",
        ["StarRail"],
        ["StarRail"],
        "StarRailStub.dll",
        FpsViaRegistry: true,
        LockedFps: 120,
        SupportsDiveMosaic: false);

    public static readonly IReadOnlyList<GameDefinition> All = [Genshin, StarRail];
}
