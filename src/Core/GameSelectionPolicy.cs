namespace GenshinFpsUnlocker.Host;

internal static class GameSelectionPolicy
{
    public static bool IsTransientView(GameId selectedGame, GameId? runningGame) =>
        runningGame == selectedGame;
}
