using System.Text.Json;
using System.Text.Json.Serialization;
using HoYoEnhance.Contracts;

namespace GenshinFpsUnlocker.Host.Tests;

internal static class ConfigContractTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Run(Harness h)
    {
        h.Case("配置 DTO 使用 camelCase 与字符串枚举", () =>
        {
            var dto = new UnlockerConfigDto
            {
                ActiveGame = GameId.StarRail,
                Games = new GameProfilesDto
                {
                    StarRail = new GameProfileDto
                    {
                        TargetFps = 120,
                        GamePath = @"C:\Games\Star Rail\Game\StarRail.exe",
                    },
                },
                LogLevel = "Warn",
            };

            var json = JsonSerializer.Serialize(dto, Options);
            Harness.Contains(json, "\"activeGame\":\"starRail\"", "游戏键必须是 camelCase 字符串");
            Harness.Contains(json, "\"logLevel\":\"Warn\"", "日志级别必须保持现有字符串");
            Harness.Contains(json, "\"starRail\"", "games 里必须有星铁档案");
            Harness.Contains(json, "\"gamePath\":\"C:\\\\Games\\\\Star Rail\\\\Game\\\\StarRail.exe\"", "游戏路径必须原样输出");
        });

        h.Case("配置 DTO 能读回现有 JSON 契约", () =>
        {
            const string json = """
                {
                  "activeGame": "starRail",
                  "games": {
                    "genshin": { "targetFps": 120, "enabled": true, "hideUid": false },
                    "starRail": { "targetFps": 120, "enabled": true, "gamePath": null }
                  },
                  "logLevel": "Info"
                }
                """;

            var dto = JsonSerializer.Deserialize<UnlockerConfigDto>(json, Options);
            Harness.Equal(GameId.StarRail, dto!.ActiveGame, "activeGame");
            Harness.Equal("Info", dto.LogLevel, "logLevel");
            Harness.Equal(120, dto.Games.StarRail.TargetFps, "星铁帧率");
            Harness.Equal<string?>(null, dto.Games.StarRail.GamePath, "空路径");
        });
    }
}
