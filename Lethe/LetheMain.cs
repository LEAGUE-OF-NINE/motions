using BepInEx;
using BepInEx.Unity.IL2CPP;


namespace MyPlugin;

[BepInPlugin(GUID, NAME, VERSION)]
public class Main : BasePlugin
{
    public const string GUID = $"{AUTHOR}.{NAME}";
    public const string NAME = "Lethe";
    public const string VERSION = "1.0.0";
    public const string AUTHOR = "The League";


    public override void Load()
    {
        
    }
}
