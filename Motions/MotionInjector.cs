using System;
using Il2CppInterop.Runtime;
using Il2CppSystem.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Motions;

/// <summary>
/// Handles sidecar attachment, motion injection, and custom motion playback.
/// Operates on <see cref="MotionData"/> caches and <see cref="CueExtractor"/>.
/// </summary>
public static class MotionInjector
{
    /// <summary>
    /// Replaces the character's motion with custom timelines built from JSON + bundle data.
    /// </summary>
    public static void InjectCustomMotion(SD.CharacterAppearance characterAppearance, MOTION_DETAIL motionDetail, string jsonPath, string appearanceID, System.Collections.Generic.List<TrackAsset> allVfxTracks)
    {
        try
        {
            TimelineAsset bundleTimeline = MotionData.FindTimelineForAppearance(appearanceID, motionDetail);
            var customTimelines = TimelineBuilder.GetTimelines(motionDetail.ToString(), jsonPath, bundleTimeline, appearanceID, allVfxTracks);

            if (customTimelines == null || customTimelines.Count == 0)
                return;

            foreach (var tl in customTimelines)
            {
                CueExtractor.StripAudioTracks(tl);
                MotionData.ProcessedTimelines.Add(tl);
            }

            characterAppearance.RemoveMotion(motionDetail);
            var timelineList = new Il2CppSystem.Collections.Generic.List<TimelineAsset>();
            var gameObj = new Il2CppSystem.Collections.Generic.List<GameObject>();

            foreach (var tl in customTimelines)
                timelineList.Add(tl);

            characterAppearance.AddMotion(motionDetail, timelineList, gameObj);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to inject custom motion {motionDetail} for {characterAppearance.charInfo.appearanceID}: {ex}");
        }
    }

    /// <summary>
    /// Creates the sidecar GameObject on the character: a sandbox SpriteRenderer + Animator
    /// + PlayableDirector for running custom animation clips, plus the <see cref="SidecarSyncBehavior"/>.
    /// </summary>
    public static void AttachSidecar(SD.CharacterAppearance character, string forcedID = null)
    {
        try
        {
            if (character.transform.FindChild("Motions_Sandbox_Test") != null) return;

            string appearanceID = forcedID ?? character.charInfo.appearanceID;
            TimelineAsset customTimeline = MotionData.FindTimelineForAppearance(appearanceID);

            if (customTimeline == null) return;

            GameObject sandboxObj = new("Motions_Sandbox_Test");
            sandboxObj.transform.SetParent(character.transform);
            sandboxObj.transform.localPosition = Vector3.zero;
            sandboxObj.transform.localScale = Vector3.one;

            var testRenderer = sandboxObj.AddComponent<SpriteRenderer>();
            testRenderer.sortingLayerName = "Front";
            testRenderer.sortingOrder = 999;
            testRenderer.enabled = false;

            var testAnimator = sandboxObj.AddComponent<Animator>();
            var slaveDirector = sandboxObj.AddComponent<PlayableDirector>();
            slaveDirector.extrapolationMode = DirectorWrapMode.None;

            var syncScript = sandboxObj.AddComponent<SidecarSyncBehavior>();
            syncScript.MasterDirector = character._playableDirector;
            syncScript.SlaveDirector = slaveDirector;
            syncScript.SlaveAnimator = testAnimator;
            syncScript.SandboxRenderer = testRenderer;
            syncScript.OriginalRenderer = character.sprenderer_charactermotion;
            syncScript.Appearance = character;

            MotionData.PatchedCharacters.Add(character);
            Logger.LogWarning($"Animation Sidecar attached to {appearanceID}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Sidecar initialization failed: {ex}");
        }
    }

    /// <summary>
    /// Plays the custom motion on the sidecar: assigns sound/VFX cues, starts the slave director, and syncs.
    /// </summary>
    public static void PlayCustomMotion(SD.CharacterAppearance appearance, MOTION_DETAIL motiondetail, int index)
    {
        string appearanceID = appearance.charInfo.appearanceID;
        if (string.IsNullOrEmpty(appearanceID)) return;

        var sandboxTransform = appearance.transform.FindChild("Motions_Sandbox_Test");

        if (sandboxTransform == null && MotionData.HasBundle(appearanceID))
        {
            AttachSidecar(appearance, appearanceID);
            sandboxTransform = appearance.transform.FindChild("Motions_Sandbox_Test");
        }

        var syncScript = sandboxTransform?.GetComponent<SidecarSyncBehavior>();
        if (syncScript == null) return;

        var key = new MotionKey
        {
            AppearanceID = appearanceID,
            Motion = motiondetail,
            Index = index
        };

        // GetOrCacheTimeline populates SoundCueCache/VfxCueCache as a side effect,
        // so reading caches after this call is safe.
        TimelineAsset customTimeline = CueExtractor.GetOrCacheTimeline(appearanceID, motiondetail, index);

        // ---- Sound cues ----
        syncScript.SoundCues.Clear();
        if (MotionData.SoundCueCache.TryGetValue(key, out var cues))
        {
            if (syncScript.SoundCues.Capacity < cues.Count)
                syncScript.SoundCues.Capacity = cues.Count;

            for (int i = 0; i < cues.Count; i++)
            {
                var c = cues[i];
                syncScript.SoundCues.Add(new SoundCue
                {
                    StartTime = c.StartTime,
                    ClipIn = c.ClipIn,
                    Duration = c.Duration,
                    WavData = c.WavData,
                    Triggered = false
                });
            }
        }

        // ---- VFX cues (only replace if custom VFX actually exist) ----
        if (MotionData.VfxCueCache.TryGetValue(key, out var vfxCues) && vfxCues.Count > 0)
        {
            for (int i = 0; i < syncScript.VfxCues.Count; i++)
            {
                var old = syncScript.VfxCues[i];
                if (old.ActiveInstance != null)
                    UnityEngine.Object.Destroy(old.ActiveInstance);
            }
            syncScript.VfxCues.Clear();

            if (syncScript.VfxCues.Capacity < vfxCues.Count)
                syncScript.VfxCues.Capacity = vfxCues.Count;

            for (int i = 0; i < vfxCues.Count; i++)
            {
                var c = vfxCues[i];

                GameObject preloaded = null;
                if (c.Prefab != null)
                {
                    preloaded = UnityEngine.Object.Instantiate(c.Prefab, syncScript.SandboxRenderer.transform);
                    preloaded.SetActive(false);
                }

                syncScript.VfxCues.Add(new VfxCue
                {
                    StartTime = c.StartTime,
                    Duration = c.Duration,
                    Prefab = c.Prefab,
                    Triggered = false,
                    ActiveInstance = preloaded,
                    SpawnTarget = c.SpawnTarget,
                    OffsetX = c.OffsetX,
                    OffsetY = c.OffsetY,
                    OffsetZ = c.OffsetZ
                });
            }
        }

        // ---- Play timeline on sidecar ----
        if (customTimeline != null)
        {
            syncScript.IsModdedSkillActive = true;
            syncScript.SandboxRenderer.enabled = true;

            syncScript.SlaveDirector.playableAsset = customTimeline;

            string motionName = motiondetail.ToString();
            bool isSpecial = motionName.StartsWith("S") || motionName.ToLower().Contains("parrying");
            syncScript.ShouldSync = isSpecial;

            syncScript.SlaveDirector.time = 0;
            if (motiondetail == MOTION_DETAIL.Idle)
                syncScript.SlaveDirector.extrapolationMode = DirectorWrapMode.Loop;
            else
                syncScript.SlaveDirector.extrapolationMode = DirectorWrapMode.None;

            if (!isSpecial)
                syncScript.SlaveDirector.Play();

            foreach (var track in customTimeline.flattenedTracks)
            {
                var animTrack = track.TryCast<AnimationTrack>();
                if (animTrack != null)
                    syncScript.SlaveDirector.SetGenericBinding(track, syncScript.SlaveAnimator);
            }

            if (syncScript.OriginalRenderer != null)
                syncScript.OriginalRenderer.enabled = false;
        }
        else
        {
            syncScript.IsModdedSkillActive = false;
            syncScript.SandboxRenderer.enabled = false;

            if (syncScript.OriginalRenderer != null)
                syncScript.OriginalRenderer.enabled = true;
        }
    }
}
