namespace HoYoEnhance.Contracts;

/// <summary>受支持的游戏。两个游戏各自持有独立配置，互不共享。</summary>
[TsName("GameId")]
public enum GameId
{
    [TsValue("genshin")]
    Genshin,

    [TsValue("starRail")]
    StarRail,
}
