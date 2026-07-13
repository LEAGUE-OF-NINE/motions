using System;
using System.IO;
using System.Text.Json;

namespace Motions
{
    internal static class CharVFXParse
    {
        public static CharacterVFX Parse(string jsonPath)
        {
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                Logger.LogWarning($"[CharVFXParse] Character VFX file not found: {jsonPath}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);

                CharacterVFX data = JsonSerializer.Deserialize<CharacterVFX>(json, TimelineBuilder.JsonOptions);

                if (data == null)
                {
                    Logger.LogWarning($"[CharVFXParse] Failed to deserialize '{jsonPath}'.");
                    return null;
                }

                if (data.allVFX == null)
                    data.allVFX = Array.Empty<CharVFX>();

                Logger.LogInfo($"[CharVFXParse] Loaded {data.allVFX.Length} character VFX entries.");

                return data;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CharVFXParse] Failed parsing '{jsonPath}': {ex}");
                return null;
            }
        }
    }
}