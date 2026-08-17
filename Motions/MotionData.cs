using System;
using System.Collections.Generic;
using FX;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Timeline;

namespace Motions;

/// <summary>
/// Central static container for all motion-related caches and asset-lookup helpers.
/// No Harmony patches, no timeline construction, just data.
/// </summary>
public static class MotionData
{
    // --- Bundles from Dashboard -------------------------------------------

    public static readonly Dictionary<string, AssetBundle> DashboardAssets = new();
    public static readonly Dictionary<string, GameObject> CreatedDashboardAssets = new();

    // --- Bundles from ScreenEffect -------------------------------------------

    public static readonly Dictionary<string,AssetBundle> ScreenBorderAssets = new();

    // --- Bundles from BuffEffect -------------------------------------------

    public static readonly Dictionary<BUFF_UNIQUE_KEYWORD, List<AssetBundle>> LoadedBuffAssets = new();

    public static readonly Dictionary<BUFF_UNIQUE_KEYWORD, Effect_Ability> CreatedAbilityEffects = new();

    // ---- Bundle loading ---------------------------------------------------

    public static readonly Dictionary<string, List<AssetBundle>> LoadedAssets = new();

    // ---- Bundle-free sprite motions ---------------------------------------

    /// <summary>Sprite motions loaded from motions/&lt;Motion&gt;/ folders, keyed like TimelineCache.</summary>
    public static readonly Dictionary<MotionKey, SpriteMotion> SpriteMotions = new();

    /// <summary>Appearances with at least one sprite motion, so the gates can ask without scanning.</summary>
    public static readonly HashSet<string> SpriteMotionAppearances = new();

    // ---- Props ------------------------------------------------------------

    /// <summary>"appearanceID/folder" -> the prop's loaded frames. Shared by every instance.</summary>
    public static readonly Dictionary<string, SpriteMotion> PropArt = new();

    /// <summary>appearanceID -> its validated prop entries.</summary>
    public static readonly Dictionary<string, PropEntry[]> Props = new();

    /// <summary>Empty fixed-length timelines that exist only to give the slave director a clock.</summary>
    public static readonly Dictionary<MotionKey, TimelineAsset> ClockTimelines = new();

    /// <summary>"!motions_MyGuy" -> the vanilla appearance ID cloned to build it.</summary>
    public static readonly Dictionary<string, string> CustomAppearanceBases = new();

    // ---- JSON definition registry -----------------------------------------

    /// <summary>appearanceID -> (MOTION_DETAIL -> jsonPath)</summary>
    public static readonly Dictionary<string, Dictionary<MOTION_DETAIL, string>> CustomMotionDefinitions = new();

    /// <summary>appearanceID -> jsonPath</summary>
    public static readonly Dictionary<string, string> CustomAppearanceVFX = new();

    /// <summary>appearanceID -> parsed CharacterVFX.json. Parsed once instead of on every ability typo.</summary>
    public static readonly Dictionary<string, CharacterVFX> AppearanceVFXCache = new();

    /// <summary>"appearanceID/vfxName" -> prefab (null if absent), so bundles aren't rescanned per ability typo.</summary>
    public static readonly Dictionary<string, GameObject> AppearanceVFXPrefabs = new();

    // ---- Caches ----------------------------------------------------------

    /// <summary>Cloned timeline instances, keyed by (appearance, motion, coin index).</summary>
    public static readonly Dictionary<MotionKey, TimelineAsset> TimelineCache = new();

    /// <summary>Sound cues extracted from bundle timelines.</summary>
    public static readonly Dictionary<MotionKey, List<SoundCue>> SoundCueCache = new();

    /// <summary>VFX cues extracted from bundle control tracks.</summary>
    public static readonly Dictionary<MotionKey, List<VfxCue>> VfxCueCache = new();

    /// <summary>Set of timelines we've already stripped/processed so we don't repeat work.</summary>
    public static readonly HashSet<TimelineAsset> ProcessedTimelines = new();

    /// <summary>Characters that already have a sidecar attached.</summary>
    public static readonly HashSet<SD.CharacterAppearance> PatchedCharacters = new();

    // ---- Queries ---------------------------------------------------------

    /// <summary>Named for the same "...ForAppearance" family as FindTimelineForAppearance.</summary>
    public static List<AssetBundle> GetBundlesForAppearance(string appearanceID)
        => LoadedAssets.TryGetValue(appearanceID, out var bundles) ? bundles : null;

    public static bool HasDefinition(string appearanceID)
        => CustomMotionDefinitions.ContainsKey(appearanceID);

    public static bool HasBundle(string appearanceID)
        => LoadedAssets.ContainsKey(appearanceID);

    public static bool HasSpriteMotion(string appearanceID)
        => SpriteMotionAppearances.Contains(appearanceID);

    public static bool HasProps(string appearanceID)
        => Props.ContainsKey(appearanceID);

    public static PropEntry[] GetProps(string appearanceID)
        => Props.TryGetValue(appearanceID, out var entries) ? entries : null;

    /// <summary>
    /// Sprite motion for this coin, falling back to the motion's first coin when there is no
    /// per-coin folder. A multi-coin skill restarts the motion on every coin, so without this a
    /// single S1/ folder animates coin one and leaves the rest blank. Supplying S1_1/, S1_2/ is
    /// how you give each coin its own animation; supplying only S1/ reuses it for all of them.
    /// Bundles keep their stricter behaviour: a missing coin asset there means no custom motion.
    /// </summary>
    public static bool TryGetSpriteMotion(string appearanceID, MOTION_DETAIL detail, int index, out SpriteMotion motion)
    {
        if (SpriteMotions.TryGetValue(MotionKey.Create(appearanceID, detail, index), out motion))
            return true;

        if (index > 0 && SpriteMotions.TryGetValue(MotionKey.Create(appearanceID, detail, 0), out motion))
            return true;

        motion = null;
        return false;
    }

    public static string GetDefinitionPath(string appearanceID, MOTION_DETAIL detail)
    {
        if (CustomMotionDefinitions.TryGetValue(appearanceID, out var dict) &&
            dict.TryGetValue(detail, out var path))
            return path;
        return null;
    }

    // ---- Asset lookup -----------------------------------------------------

    /// <summary>
    /// Searches loaded bundles for a TextAsset named '{clipName}.bytes' and returns its raw bytes.
    /// Used to load custom audio without going through Unity's disabled AudioClip system.
    /// </summary>
    public static byte[] FindBytesAsset(string appearanceID, string clipName)
    {
        if (!LoadedAssets.ContainsKey(appearanceID))
        {
            Logger.LogWarning($"[FindBytesAsset] No loaded assets for '{appearanceID}'.");
            return null;
        }

        string target = clipName.ToLower();
        Logger.LogInfo($"[FindBytesAsset] Searching for '{target}' (+ optional extensions) in bundles for '{appearanceID}'...");

        foreach (var bundle in LoadedAssets[appearanceID])
        {
            foreach (var assetName in bundle.AllAssetNames())
            {
                string lower = assetName.ToLower();

                bool isExact = lower == target + ".bytes" || lower.EndsWith("/" + target + ".bytes");
                bool isFuzzy = (lower.Contains("/" + target + ".") || lower.StartsWith(target + ".")) && lower.EndsWith(".bytes");

                if (isExact || isFuzzy)
                {
                    var asset = bundle.LoadAsset(assetName, Il2CppType.Of<TextAsset>());
                    if (asset != null)
                    {
                        Logger.LogInfo($"[FindBytesAsset] SUCCESS: Found '{assetName}' ({asset.Cast<TextAsset>().bytes.Length} bytes).");
                        return asset.Cast<TextAsset>().bytes;
                    }
                }
            }
        }
        Logger.LogWarning($"[FindBytesAsset] FAILED: No .bytes asset found for '{clipName}' in '{appearanceID}'.");
        return null;
    }

    public static GameObject FindPrefabAsset(string appearanceID, string clipName)
    {
        if (!LoadedAssets.ContainsKey(appearanceID)) return null;
        string target = clipName.ToLower();

        foreach (var bundle in LoadedAssets[appearanceID])
        {
            foreach (var assetName in bundle.AllAssetNames())
            {
                string lower = assetName.ToLower();

                bool isExact = lower == target + ".prefab" || lower.EndsWith("/" + target + ".prefab");
                bool isFuzzy = (lower.Contains("/" + target + ".") || lower.StartsWith(target + ".")) && lower.EndsWith(".prefab");

                if (isExact || isFuzzy)
                {
                    var asset = bundle.LoadAsset(assetName, Il2CppType.Of<GameObject>());
                    if (asset != null)
                        return asset.Cast<GameObject>();
                }
            }
        }
        return null;
    }

    public static GameObject FindPrefabAssetDashboard(string bundleName)
    {
        if (!DashboardAssets.TryGetValue(bundleName, out var bundle))
            return null;

            foreach (var assetName in bundle.AllAssetNames())
            {
                if (!assetName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;

                Logger.LogWarning($"Loading prefab {assetName}");

                var asset = bundle.LoadAsset(assetName, Il2CppType.Of<GameObject>());
                if (asset != null)
                    return asset.Cast<GameObject>();
            }

        return null;
    }

    public static GameObject FindPrefabAssetBuff(BUFF_UNIQUE_KEYWORD keyword)
    {
        if (!LoadedBuffAssets.TryGetValue(keyword, out var bundles))
            return null;

        foreach (var bundle in bundles)
        {
          
            foreach (var assetName in bundle.AllAssetNames())
            {
                if (!assetName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;

                Logger.LogWarning($"Loading prefab {assetName}");

                var asset = bundle.LoadAsset(assetName, Il2CppType.Of<GameObject>());
                if (asset != null)
                    return asset.Cast<GameObject>();
            }
        }

        return null;
    }

    /// <summary>Finds any TimelineAsset in the bundles for this appearance.</summary>
    public static TimelineAsset FindTimelineForAppearance(string appearanceID)
    {
        if (!LoadedAssets.ContainsKey(appearanceID)) return null;

        foreach (var bundle in LoadedAssets[appearanceID])
        {
            foreach (var assetName in bundle.AllAssetNames())
            {
                var asset = bundle.LoadAsset(assetName, Il2CppType.Of<TimelineAsset>());
                if (asset != null)
                    return asset.Cast<TimelineAsset>();
            }
        }
        return null;
    }

    /// <summary>Finds a timeline matching a specific motion detail and optional coin index.</summary>
    public static TimelineAsset FindTimelineForAppearance(string appearanceID, MOTION_DETAIL detail, int index = -1)
    {
        if (!LoadedAssets.ContainsKey(appearanceID)) return null;

        string targetName = detail.ToString().ToLower(); // e.g. "s1"

        if (index > 0)
            targetName = targetName + "_" + index;

        foreach (var bundle in LoadedAssets[appearanceID])
        {
            foreach (var assetName in bundle.AllAssetNames())
            {
                string assetNameLower = assetName.ToLower();
                bool isMatch = assetNameLower == targetName ||
                               assetNameLower.EndsWith("/" + targetName) ||
                               assetNameLower.StartsWith(targetName + ".") ||
                               assetNameLower.Contains("/" + targetName + ".") ||
                               assetNameLower.EndsWith("." + targetName);

                if (isMatch)
                {
                    var asset = bundle.LoadAsset(assetName, Il2CppType.Of<TimelineAsset>());
                    if (asset != null)
                        return asset.Cast<TimelineAsset>();
                }
            }
        }
        return null;
    }

    // ---- Lifecycle --------------------------------------------------------

    /// <summary>
    /// Destroys one motion's runtime-created sprites and textures. They carry
    /// HideFlags.HideAndDontSave, so Unity will never collect them and every battle
    /// transition would leak the whole set. Shared by sprite motions and props because
    /// both build their assets the same way.
    /// </summary>
    private static void DestroyRuntimeAssets(SpriteMotion motion)
    {
        if (motion == null) return;

        if (motion.Sprites != null)
            foreach (var sprite in motion.Sprites)
                if (sprite != null) UnityEngine.Object.Destroy(sprite);

        if (motion.Textures != null)
            foreach (var tex in motion.Textures)
                if (tex != null) UnityEngine.Object.Destroy(tex);
    }

    public static void UnloadAll()
    {
        foreach (var bundles in LoadedAssets.Values)
        {
            foreach (var bundle in bundles)
            {
                if (bundle == null) continue;
                Logger.LogWarning($"Unloading motion bundle {bundle.name}");
                bundle.Unload(false);
            }
        }
        foreach (var bundles in LoadedBuffAssets.Values)
        {
            foreach (var bundle in bundles)
            {
                if (bundle == null) continue;
                Logger.LogWarning($"Unloading buff bundle {bundle.name}");
                bundle.Unload(false);
            }
        }
        foreach (var bundle in ScreenBorderAssets.Values)
        {
            if (bundle == null) continue;
            Logger.LogWarning($"Unloading screen border bundle {bundle.name}");
            bundle.Unload(false);
        }
        foreach (var bundle in DashboardAssets.Values)
        {
            if (bundle == null) continue;
            Logger.LogWarning($"Unloading dashboard bundle {bundle.name}");
            bundle.Unload(false);
        }
        foreach (var motion in SpriteMotions.Values)
            DestroyRuntimeAssets(motion);

        foreach (var art in PropArt.Values)
            DestroyRuntimeAssets(art);

        foreach (var clock in ClockTimelines.Values)
            if (clock != null) UnityEngine.Object.Destroy(clock);

        Logger.LogWarning("Unloading and clearing all custom motions and bundles.");
        PropWorld.Clear();
        LoadedAssets.Clear();
        ScreenBorderPatches.Unload();
        ScreenBorderAssets.Clear();
        LoadedBuffAssets.Clear();
        DashboardAssets.Clear();
        AppearanceVFXCache.Clear();
        AppearanceVFXPrefabs.Clear();
        CustomMotionDefinitions.Clear();
        PatchedCharacters.Clear();
        SoundCueCache.Clear();
        VfxCueCache.Clear();
        TimelineCache.Clear();
        ProcessedTimelines.Clear();
        SpriteMotions.Clear();
        SpriteMotionAppearances.Clear();
        PropArt.Clear();
        Props.Clear();
        ClockTimelines.Clear();
        CustomAppearanceBases.Clear();
    }
}
