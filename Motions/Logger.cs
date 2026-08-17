using BepInEx.Logging;

namespace Motions;

public static class Logger
{
    public static ManualLogSource sharedLog;

    public static void LogInfo(object item, int logLevel = 2)
    {
        if (Plugin.ConfigLogLevel.Value >= logLevel) sharedLog.LogInfo(item);
    }

    public static void LogWarning(object item, int logLevel = 1)
    {
        if (Plugin.ConfigLogLevel.Value >= logLevel) sharedLog.LogWarning(item);
    }

    public static void LogError(object item, int logLevel = 1)
    {
        if (Plugin.ConfigLogLevel.Value >= logLevel) sharedLog.LogError(item);
    }

    public static void LogFatalError(object item, int logLevel = 0)
    {
        if (Plugin.ConfigLogLevel.Value >= logLevel) sharedLog.LogFatal(item);
    }
}
