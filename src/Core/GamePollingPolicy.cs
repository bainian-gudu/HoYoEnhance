namespace GenshinFpsUnlocker.Host;

internal static class GamePollingPolicy
{
    public static int RunningStateIntervalMs(int configuredMs) => Math.Clamp(configuredMs, 500, 2000);

    public static int WatchIdleIntervalMs(int configuredMs) => Math.Clamp(configuredMs, 500, 5000);

    public static int WatchActiveIntervalMs(int configuredMs) =>
        Math.Clamp(Math.Min(configuredMs, 800), 300, 2000);

    public static int RunningFlickerWindowMs(int configuredMs) =>
        Math.Clamp(configuredMs * 3, 3000, 15000);

    public static int ExitConfirmationIntervalMs(int configuredMs) =>
        Math.Clamp(configuredMs * 2, 1000, 5000);
}
