using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppSystem.IO;
using Lethe.Patches;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Motions;

/// <summary>
/// Harmony patches that orchestrate the custom motion system.
/// All business logic is delegated to <see cref="MotionData"/>,
/// <see cref="CueExtractor"/>, and <see cref="MotionInjector"/>.
/// </summary>
public class Motions
{
    public static void Setup(Harmony harmony)
    {
        ClassInjector.RegisterTypeInIl2Cpp<SidecarSyncBehavior>();
        ClassInjector.RegisterTypeInIl2Cpp<MoveEnemyMarker>();
        harmony.PatchAll(typeof(Motions));
    }

    // ---- Bundle loading / unloading ---------------------------------------

    [HarmonyPatch(typeof(GlobalGameManager), nameof(GlobalGameManager.LoadScene))]
    [HarmonyPrefix]
    public static void LoadScene(SCENE_STATE state, DelegateEvent onLoadScene)
    {
        switch (state)
        {
            case SCENE_STATE.Battle:
            {
                foreach (var modPath in Directory.GetDirectories(Plugin.modsPath.FullPath))
                {
                    string modName = Path.GetFileName(modPath);
                    if (modName.StartsWith("DISABLED_") || modName.StartsWith("FULLDISABLED_"))
                    {
                        Logger.LogInfo($"Skipping {modPath} due to it being disabled.");
                        continue;
                    }

                    var motionsRoot = Path.Combine(modPath, "custom_motions");
                    if (!Directory.Exists(motionsRoot)) continue;
                    // directory custom_motions:
                    foreach (var charDir in Directory.GetDirectories(motionsRoot))
                    {
                        if (charDir.Contains("MOTIONBUFF_"))
                        {
                                string buffId = Path.GetFileName(charDir).Remove(0, 11);
                                Logger.LogWarning($"Discovered directory for Buff: [{buffId}] at path: {charDir}");

                                BUFF_UNIQUE_KEYWORD keyword = CustomBuffs.ParseBuffUniqueKeyword(buffId);

                                Logger.LogInfo($"Resolved '{buffId}' -> {(int)keyword}");

                                foreach (var bundlePath in Directory.GetFiles(charDir, "*.bundle", SearchOption.AllDirectories))
                                {
                                    Logger.LogInfo($"Loading bundle for {buffId}: {bundlePath}");

                                    var bundle = AssetBundle.LoadFromFile(bundlePath, 0);
                                    if (bundle == null)
                                        continue;

                                    if (!MotionData.LoadedBuffAssets.ContainsKey(keyword))
                                        MotionData.LoadedBuffAssets.Add(keyword, new System.Collections.Generic.List<AssetBundle>());

                                    MotionData.LoadedBuffAssets[keyword].Add(bundle);

                                    Logger.LogWarning($"Loaded motion bundle {bundle.name} for keyword {(int)keyword} ({buffId})");
                                }
                                continue;
                        }

                        string appearanceID = Path.GetFileName(charDir);
                        Logger.LogWarning($"Discovered directory for ID: [{appearanceID}] at path: {charDir}");

                        // Load bundles for this character
                        foreach (var bundlePath in Directory.GetFiles(charDir, "*.bundle", SearchOption.AllDirectories))
                        {
                            Logger.LogInfo($"Loading bundle for {appearanceID}: {bundlePath}");
                            var bundle = UnityEngine.AssetBundle.LoadFromFile(bundlePath, 0);
                            if (bundle != null)
                            {
                                if (!MotionData.LoadedAssets.ContainsKey(appearanceID))
                                    MotionData.LoadedAssets.Add(appearanceID, new System.Collections.Generic.List<UnityEngine.AssetBundle>());

                                MotionData.LoadedAssets[appearanceID].Add(bundle);
                                Logger.LogWarning($"Loaded motion bundle {bundle.name} for {appearanceID}!");
                            }
                        }

                        // Discover JSON definitions
                        foreach (var detailObj in Enum.GetValues(typeof(MOTION_DETAIL)))
                        {
                            MOTION_DETAIL detail = (MOTION_DETAIL)detailObj;
                            string skillName = detail.ToString();
                            var jsonPath = Path.Combine(charDir, $"{skillName}.json");
                            if (File.Exists(jsonPath))
                            {
                                if (!MotionData.CustomMotionDefinitions.ContainsKey(appearanceID))
                                    MotionData.CustomMotionDefinitions.Add(appearanceID, new System.Collections.Generic.Dictionary<MOTION_DETAIL, string>());

                                if (!MotionData.CustomMotionDefinitions[appearanceID].ContainsKey(detail))
                                {
                                    MotionData.CustomMotionDefinitions[appearanceID].Add(detail, jsonPath);
                                    Logger.LogInfo($"Discovered motion definition: {appearanceID} -> {skillName} -> {jsonPath}");
                                }

                            }
                        }
                    }
                }
                break;
            }
            case not SCENE_STATE.Battle:
            {
                MotionData.UnloadAll();
                break;
            }
        }
    }

    [HarmonyPatch(typeof(StageController), nameof(StageController.EndStage))]
    [HarmonyPrefix]
    public static void UnloadBundlesOnStageEnd()
    {
        MotionData.UnloadAll();
    }

    // ---- OnNotify hook (MoveEnemy workaround) -----------------------------

    [HarmonyPatch(typeof(CharacterApperacneResiver), nameof(CharacterApperacneResiver.OnNotify))]
    [HarmonyPrefix]
    private static void OnNotify(CharacterApperacneResiver __instance, Playable origin, INotification noti, Il2CppSystem.Object obj)
    {
        try
        {
            if (noti == null) return;

            var tweenRel = noti.TryCast<SkillGiveTiming_TweenMove_Relative>();
            if (tweenRel != null && tweenRel.moveInfo != null)
            {
                if (tweenRel.name == "MoveEnemy")
                {
                    var targets = __instance._appearance.GetView().GetCurrentSkillViewer().GetCurrentTargets();
                    foreach (var target in targets)
                    {
                        target.transform.position = tweenRel.moveInfo.movePos;
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error in OnNotify hook: {ex}");
        }
    }

    // ---- Motion injection -------------------------------------------------

    [HarmonyPatch(typeof(SD.CharacterAppearance), nameof(SD.CharacterAppearance.Initialize))]
    [HarmonyPostfix]
    private static void Initialize(SD.CharacterAppearance __instance, BattleUnitView unitView)
    {
        string appearanceID = unitView?._unitModel?.GetAppearanceID() ?? __instance.charInfo.appearanceID;
        Logger.LogInfo($"CharacterAppearance.Initialize called for: {appearanceID} (Source: {(unitView != null ? "Model" : "CharInfo")})");
        CueExtractor.EagerCacheBuffEffects();
        bool hasCustomJSON = MotionData.HasDefinition(appearanceID);
        bool hasCustomBundle = MotionData.HasBundle(appearanceID);

        if (hasCustomJSON || hasCustomBundle)
        {
            if (hasCustomBundle)
            {
                Logger.LogInfo($"Custom bundle registered for {appearanceID}, attaching sidecar on init.");
                MotionInjector.AttachSidecar(__instance, appearanceID);
                CueExtractor.EagerCacheMotions(appearanceID);
            }

            // Collect all original VFX tracks across all motions for cross-motion referencing
            var allVfxTracks = new System.Collections.Generic.List<TrackAsset>();
            foreach (var detailObj in Enum.GetValues(typeof(MOTION_DETAIL)))
            {
                MOTION_DETAIL detail = (MOTION_DETAIL)detailObj;
                var motion = __instance.GetMotion(detail);
                if (motion?.timelineAssets != null)
                    foreach (var tl in motion.timelineAssets)
                        foreach (var track in tl.flattenedTracks)
                            foreach (var clip in track.clips)
                                if (clip.asset != null && clip.asset.GetIl2CppType().Name.Contains("EffectActivate"))
                                { allVfxTracks.Add(track); break; }
            }

            Logger.LogInfo($"[VFX Tracks] {appearanceID} - {allVfxTracks.Count} tracks:");
            for (int i = 0; i < allVfxTracks.Count; i++)
            {
                var t = allVfxTracks[i];
                var clipNames = new System.Collections.Generic.List<string>();
                foreach (var c in t.clips)
                    clipNames.Add($"{c.displayName}@{c.start:F2}s");
                Logger.LogInfo($"  {i + 1}: {t.name} [{string.Join(", ", clipNames)}]");
            }

            foreach (var detailObj in Enum.GetValues(typeof(MOTION_DETAIL)))
            {
                MOTION_DETAIL detail = (MOTION_DETAIL)detailObj;
                string motionName = detail.ToString();

                string jsonPath = MotionData.GetDefinitionPath(appearanceID, detail);
                bool hasJSON = jsonPath != null;
                bool isSkill = motionName.StartsWith("S");

                // A motion the character has no timeline for is never dispatched by the game, so a
                // bundle timeline alone is enough reason to inject: it registers the motion slot.
                bool hasBundleTimeline = MotionData.FindTimelineForAppearance(appearanceID, detail) != null;

                if (hasJSON || isSkill || hasBundleTimeline)
                    MotionInjector.InjectCustomMotion(__instance, detail, jsonPath, appearanceID, allVfxTracks);
            }
        }
    }

    // ---- ChangeMotion hooks -----------------------------------------------

    [HarmonyPatch(typeof(SD.CharacterAppearance), nameof(SD.CharacterAppearance.ChangeMotion))]
    [HarmonyPostfix]
    private static void PostChangeMotion(SD.CharacterAppearance __instance, MOTION_DETAIL motiondetail, int index)
    {
        try
        {
            string appearanceID = __instance.charInfo.appearanceID;
            if (MotionData.HasDefinition(appearanceID) || MotionData.HasBundle(appearanceID))
            {
                __instance.SetDisableTrail(true);
                __instance.SetDisableSpine(true);
            }

            var motion = __instance.GetMotion(motiondetail);
            if (motion == null || motion.timelineAssets == null) return;

            // Skills index the motion's timelines by coin, so the game's index already says which bundle
            // asset to play. Other motions come in with -1 and the game chooses; the choice is readable
            // from the name of the timeline it just assigned to the master.
            int resolved = index;
            if (!motiondetail.ToString().StartsWith("S"))
            {
                int variant = TimelineBuilder.GetVariantIndex(__instance._playableDirector?.playableAsset?.name);
                if (variant > 0)
                    resolved = variant;
            }

            MotionInjector.PlayCustomMotion(__instance, motiondetail, resolved);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error in PostChangeMotion hook: {ex}");
        }
    }
}

/// <summary>
/// Marker type registered so Il2Cpp knows about it. Used by the OnNotify hook
/// to intercept MoveEnemy markers and apply enemy positions directly.
/// </summary>
public class MoveEnemyMarker : Il2CppSystem.Object
{
    public MoveEnemyMarker(IntPtr ptr) : base(ptr) { }
}
