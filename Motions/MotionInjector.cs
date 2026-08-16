using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
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
            string motionName = motionDetail.ToString();

            // For skills the game selects a timeline by coin index, so 'name_N' bundle assets are coins
            // and GetTimelines already emits one timeline per coin. Other motions are dispatched with
            // index -1 - the game picks a variant itself - so 'name_N' assets are variants instead, and
            // each needs its own master timeline in the motion's list for the game to have anything to
            // pick from. The variant index is encoded in the timeline name so we can recover the pick.
            var bundleTimelines = new System.Collections.Generic.List<TimelineAsset>
            {
                MotionData.FindTimelineForAppearance(appearanceID, motionDetail)
            };

            if (!motionName.StartsWith("S"))
            {
                for (int variant = 1; ; variant++)
                {
                    var extra = MotionData.FindTimelineForAppearance(appearanceID, motionDetail, variant);
                    if (extra == null) break;
                    bundleTimelines.Add(extra);
                }
            }

            // Coin N of this motion lives in folder '<Motion>_N'; MotionKey.Create folds 0 to -1.
            // 16 is a ceiling well above any real skill's coin count, so probing a fixed range
            // avoids a second discovery pass.
            var coinDurations = new double?[16];
            bool anySpriteCoin = false;
            for (int coin = 0; coin < coinDurations.Length; coin++)
            {
                // Falls back to coin 0, so every coin of a multi-coin skill gets the right length
                // even when only one folder was supplied.
                if (MotionData.TryGetSpriteMotion(appearanceID, motionDetail, coin, out var sm))
                {
                    coinDurations[coin] = sm.Duration;
                    anySpriteCoin = true;
                }
            }

            var customTimelines = new System.Collections.Generic.List<TimelineAsset>();
            for (int variant = 0; variant < bundleTimelines.Count; variant++)
            {
                var built = TimelineBuilder.GetTimelines(motionName, jsonPath, bundleTimelines[variant], appearanceID, allVfxTracks, variant, anySpriteCoin ? coinDurations : null);
                if (built != null)
                    customTimelines.AddRange(built);
            }

            if (customTimelines.Count == 0)
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

            // A sprite motion has no TimelineAsset to find, and a props-only character has neither,
            // so the bundle check alone would bail out.
            if (customTimeline == null && !MotionData.HasSpriteMotion(appearanceID)
                && !MotionData.HasProps(appearanceID)) return;

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

            if (MotionData.HasProps(appearanceID))
            {
                var rig = sandboxObj.AddComponent<PropRig>();
                rig.Sync = syncScript;
                rig.AppearanceID = appearanceID;
            }

            MotionData.PatchedCharacters.Add(character);
            Logger.LogWarning($"Animation Sidecar attached to {appearanceID}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Sidecar initialization failed: {ex}");
        }
    }

    /// <summary>
    /// Makes sure the sandbox renderer is one of the afterimage trail's source renderers. The trail
    /// bakes its mesh from that list and contributes nothing for a disabled renderer, so keeping the
    /// originals alongside the sandbox lets custom and vanilla motions each trail correctly without
    /// swapping anything per motion. Idempotent, and re-applies itself if the game rebuilds the list.
    /// </summary>
    private static void RegisterTrailSource(SD.CharacterAppearance character, SidecarSyncBehavior sync)
    {
        try
        {
            // Resolved on use, not in AttachSidecar: the trail component initializes after we attach.
            var trail = character.GetComponentInChildren<CharacterAppearanceTrail>(true);
            if (trail == null) return;

            var sources = trail._sourceRenderers;
            int count = sources?.Length ?? 0;

            for (int i = 0; i < count; i++)
                if (sources[i] == sync.SandboxRenderer) return;

            var merged = new Il2CppReferenceArray<SpriteRenderer>(count + 1);
            for (int i = 0; i < count; i++)
                merged[i] = sources[i];
            merged[count] = sync.SandboxRenderer;

            trail._sourceRenderers = merged;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Trail] source registration failed: {ex}");
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

        if (sandboxTransform == null &&
            (MotionData.HasBundle(appearanceID) || MotionData.HasSpriteMotion(appearanceID)
             || MotionData.HasProps(appearanceID)))
        {
            AttachSidecar(appearance, appearanceID);
            sandboxTransform = appearance.transform.FindChild("Motions_Sandbox_Test");
        }

        var syncScript = sandboxTransform?.GetComponent<SidecarSyncBehavior>();
        if (syncScript == null) return;

        RegisterTrailSource(appearance, syncScript);

        var key = MotionKey.Create(appearanceID, motiondetail, index);

        syncScript.CurrentMotion = motiondetail;
        syncScript.CurrentCoin = index;

        MotionData.TryGetSpriteMotion(appearanceID, motiondetail, index, out var spriteMotion);

        TimelineAsset customTimeline;

        if (spriteMotion != null)
        {
            // An empty fixed-length timeline: the slave director needs a playableAsset to have a
            // clock, but nothing on it should play. Frames are stepped in SidecarSyncBehavior.
            if (!MotionData.ClockTimelines.TryGetValue(key, out customTimeline) || customTimeline == null)
            {
                customTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
                customTimeline.name = $"SpriteClock_{motiondetail}_{index}";
                customTimeline.durationMode = TimelineAsset.DurationMode.FixedLength;
                customTimeline.fixedDuration = spriteMotion.Duration;
                MotionData.ClockTimelines[key] = customTimeline;
            }

            syncScript.Frames = spriteMotion.Sprites;
            syncScript.FrameTimes = spriteMotion.Times;
            syncScript.ResetFrameCursor();
        }
        else
        {
            // GetOrCacheTimeline populates SoundCueCache/VfxCueCache as a side effect,
            // so reading caches after this call is safe.
            customTimeline = CueExtractor.GetOrCacheTimeline(appearanceID, motiondetail, index);

            // Must be cleared, or a previous sprite motion's frames keep rendering over a bundle one.
            syncScript.Frames = null;
            syncScript.FrameTimes = null;
        }

        // Props author their action times as fractions of this.
        syncScript.MotionDuration = customTimeline != null ? customTimeline.duration : 0.0;

        // ---- Sound cues ----
        syncScript.SoundCues.Clear();

        var cueSource = spriteMotion != null
            ? spriteMotion.Sounds
            : (MotionData.SoundCueCache.TryGetValue(key, out var cached) ? cached : null);

        if (cueSource != null)
        {
            if (syncScript.SoundCues.Capacity < cueSource.Count)
                syncScript.SoundCues.Capacity = cueSource.Count;

            // Copied, not shared: SoundCue is a struct carrying Triggered and ActiveChannel, and
            // the cached list is reused on every play of the motion.
            for (int i = 0; i < cueSource.Count; i++)
            {
                var c = cueSource[i];
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
