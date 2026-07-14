using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppSystem.IO;

namespace Motions;

[BepInPlugin(GUID, NAME, VERSION)]
public class Plugin : BasePlugin
{
    public const string GUID = $"{AUTHOR}.{NAME}";
    public const string NAME = "Motions";
    public const string VERSION = "1.0.0";
    public const string AUTHOR = "Limi";

    public static DirectoryInfo pluginPath = Directory.CreateDirectory(Path.Combine(Paths.PluginPath, NAME));

    // Shares Lethe's mods folder (BepInEx/plugins/Lethe/mods) instead of its own,
    // so custom_motions content packs work whether Lethe or standalone Motions is installed.
    public static DirectoryInfo modsPath = Directory.CreateDirectory(Path.Combine(Paths.PluginPath, "Lethe", "mods"));

    public static ConfigEntry<int> ConfigLogLevel;

    public override void Load()
    {
        ConfigLogLevel = Config.Bind("General", "LogLevel", 2,
            "Show logs based on the value (0 = only fatal, 1 = error and warning logs, 2 = all logs)");
        Logger.sharedLog = base.Log;

        var harmony = new Harmony(GUID);
        Motions.Setup(harmony);
        harmony.PatchAll(typeof(BuffPatches));
        harmony.PatchAll(typeof(CharVFXParse));
    }
}
