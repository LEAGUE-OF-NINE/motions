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
    public static void InjectCustomMotion(SD.CharacterAppearance appearance, MOTION_DETAIL detail, string jsonPath, string appearanceID, System.Collections.Generic.List<TrackAsset> allVfxTracks)
    {
        try
        {
            string motionName = detail.ToString();

            // For skills the game selects a timeline by coin index, so 'name_N' bundle assets are coins
            // and GetTimelines already emits one timeline per coin. Other motions are dispatched with
            // index -1 - the game picks a variant itself - so 'name_N' assets are variants instead, and
            // each needs its own master timeline in the motion's list for the game to have anything to
            // pick from. The variant index is encoded in the timeline name so we can recover the pick.
            var bundleTimelines = new System.Collections.Generic.List<TimelineAsset>
            {
                MotionData.FindTimelineForAppearance(appearanceID, detail)
            };

            if (!motionName.StartsWith("S"))
            {
                for (int variant = 1; ; variant++)
                {
                    var extra = MotionData.FindTimelineForAppearance(appearanceID, detail, variant);
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
                if (MotionData.TryGetSpriteMotion(appearanceID, detail, coin, out var spriteMotion))
                {
                    coinDurations[coin] = spriteMotion.Duration;
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

            foreach (var timeline in customTimelines)
            {
                CueExtractor.StripAudioTracks(timeline);
                MotionData.ProcessedTimelines.Add(timeline);
            }

            appearance.RemoveMotion(detail);
            var timelineList = new Il2CppSystem.Collections.Generic.List<TimelineAsset>();
            var gameObjects = new Il2CppSystem.Collections.Generic.List<GameObject>();

            foreach (var timeline in customTimelines)
                timelineList.Add(timeline);

            appearance.AddMotion(detail, timelineList, gameObjects);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to inject custom motion {detail} for {appearance.charInfo.appearanceID}: {ex}");
        }
    }

    /// <summary>
    /// Creates the sidecar GameObject on the character: a sandbox SpriteRenderer + Animator
    /// + PlayableDirector for running custom animation clips, plus the <see cref="SidecarSyncBehavior"/>.
    /// </summary>
    public static void AttachSidecar(SD.CharacterAppearance appearance, string forcedID = null)
    {
        try
        {
            if (appearance.transform.FindChild("Motions_Sandbox_Test") != null) return;

            string appearanceID = forcedID ?? appearance.charInfo.appearanceID;
            TimelineAsset customTimeline = MotionData.FindTimelineForAppearance(appearanceID);

            // A sprite motion has no TimelineAsset to find, and a props-only character has neither,
            // so the bundle check alone would bail out.
            if (customTimeline == null && !MotionData.HasSpriteMotion(appearanceID)
                && !MotionData.HasProps(appearanceID)) return;

            GameObject sandboxObj = new("Motions_Sandbox_Test");
            sandboxObj.transform.SetParent(appearance.transform);
            sandboxObj.transform.localPosition = Vector3.zero;
            sandboxObj.transform.localScale = Vector3.one;

            var sandboxRenderer = sandboxObj.AddComponent<SpriteRenderer>();
            sandboxRenderer.sortingLayerName = "Front";
            sandboxRenderer.sortingOrder = 999;
            sandboxRenderer.enabled = false;

            var slaveAnimator = sandboxObj.AddComponent<Animator>();
            var slaveDirector = sandboxObj.AddComponent<PlayableDirector>();
            slaveDirector.extrapolationMode = DirectorWrapMode.None;

            var sync = sandboxObj.AddComponent<SidecarSyncBehavior>();
            sync.MasterDirector = appearance._playableDirector;
            sync.SlaveDirector = slaveDirector;
            sync.SlaveAnimator = slaveAnimator;
            sync.SandboxRenderer = sandboxRenderer;
            sync.OriginalRenderer = appearance.sprenderer_charactermotion;
            sync.Appearance = appearance;

            if (MotionData.HasProps(appearanceID))
            {
                var rig = sandboxObj.AddComponent<PropRig>();
                rig.Sync = sync;
                rig.AppearanceID = appearanceID;
            }

            MotionData.PatchedCharacters.Add(appearance);
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
    private static void RegisterTrailSource(SD.CharacterAppearance appearance, SidecarSyncBehavior sync)
    {
        try
        {
            // Resolved on use, not in AttachSidecar: the trail component initializes after we attach.
            var trail = appearance.GetComponentInChildren<CharacterAppearanceTrail>(true);
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
    public static void PlayCustomMotion(SD.CharacterAppearance appearance, MOTION_DETAIL detail, int index)
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

        var sync = sandboxTransform?.GetComponent<SidecarSyncBehavior>();
        if (sync == null) return;

        RegisterTrailSource(appearance, sync);

        var key = MotionKey.Create(appearanceID, detail, index);

        sync.CurrentMotion = detail;
        sync.CurrentCoin = index;

        MotionData.TryGetSpriteMotion(appearanceID, detail, index, out var spriteMotion);

        TimelineAsset customTimeline;

        if (spriteMotion != null)
        {
            // An empty fixed-length timeline: the slave director needs a playableAsset to have a
            // clock, but nothing on it should play. Frames are stepped in SidecarSyncBehavior.
            if (!MotionData.ClockTimelines.TryGetValue(key, out customTimeline) || customTimeline == null)
            {
                customTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
                customTimeline.name = $"SpriteClock_{detail}_{index}";
                customTimeline.durationMode = TimelineAsset.DurationMode.FixedLength;
                customTimeline.fixedDuration = spriteMotion.Duration;
                MotionData.ClockTimelines[key] = customTimeline;
            }

            sync.Frames = spriteMotion.Sprites;
            sync.FrameTimes = spriteMotion.Times;
            sync.ResetFrameCursor();
        }
        else
        {
            // GetOrCacheTimeline populates SoundCueCache/VfxCueCache as a side effect,
            // so reading caches after this call is safe.
            customTimeline = CueExtractor.GetOrCacheTimeline(appearanceID, detail, index);

            // Must be cleared, or a previous sprite motion's frames keep rendering over a bundle one.
            sync.Frames = null;
            sync.FrameTimes = null;
        }

        // Props author their action times as fractions of this.
        sync.MotionDuration = customTimeline != null ? customTimeline.duration : 0.0;

        LoadSoundCues(sync, spriteMotion, key);
        LoadVfxCues(sync, key);
        StartTimeline(sync, customTimeline, detail);
    }

    /// <summary>Refills the sidecar's sound cues for the motion about to play, from the sprite
    /// motion's own list or the bundle timeline's extracted one.</summary>
    private static void LoadSoundCues(SidecarSyncBehavior sync, SpriteMotion spriteMotion,
                                      MotionKey key)
    {
        sync.SoundCues.Clear();

        var cueSource = spriteMotion != null
            ? spriteMotion.Sounds
            : (MotionData.SoundCueCache.TryGetValue(key, out var cached) ? cached : null);

        if (cueSource == null) return;

        if (sync.SoundCues.Capacity < cueSource.Count)
            sync.SoundCues.Capacity = cueSource.Count;

        // Copied, not shared: SoundCue is a struct carrying Triggered and ActiveChannel, and
        // the cached list is reused on every play of the motion.
        for (int i = 0; i < cueSource.Count; i++)
        {
            var cue = cueSource[i];
            sync.SoundCues.Add(new SoundCue
            {
                StartTime = cue.StartTime,
                ClipIn = cue.ClipIn,
                Duration = cue.Duration,
                WavData = cue.WavData,
                Triggered = false
            });
        }
    }

    /// <summary>Refills the sidecar's VFX cues, pre-instantiating each prefab inactive so the cue
    /// only has to switch it on. Left alone when this motion has no custom VFX of its own - the
    /// previous motion's cues are cheaper to keep than to rebuild.</summary>
    private static void LoadVfxCues(SidecarSyncBehavior sync, MotionKey key)
    {
        if (!MotionData.VfxCueCache.TryGetValue(key, out var vfxCues) || vfxCues.Count == 0) return;

        for (int i = 0; i < sync.VfxCues.Count; i++)
        {
            var old = sync.VfxCues[i];
            if (old.ActiveInstance != null)
                UnityEngine.Object.Destroy(old.ActiveInstance);
        }
        sync.VfxCues.Clear();

        if (sync.VfxCues.Capacity < vfxCues.Count)
            sync.VfxCues.Capacity = vfxCues.Count;

        for (int i = 0; i < vfxCues.Count; i++)
        {
            var cue = vfxCues[i];

            GameObject preloaded = null;
            if (cue.Prefab != null)
            {
                preloaded = UnityEngine.Object.Instantiate(cue.Prefab, sync.SandboxRenderer.transform);
                preloaded.SetActive(false);
            }

            sync.VfxCues.Add(new VfxCue
            {
                StartTime = cue.StartTime,
                Duration = cue.Duration,
                Prefab = cue.Prefab,
                Triggered = false,
                ActiveInstance = preloaded,
                SpawnTarget = cue.SpawnTarget,
                OffsetX = cue.OffsetX,
                OffsetY = cue.OffsetY,
                OffsetZ = cue.OffsetZ
            });
        }
    }

    /// <summary>Hands the timeline to the slave director and swaps the sidecar in for the original
    /// renderer. A null timeline is the "nothing custom here" case: the sidecar steps aside and the
    /// game's own renderer comes back on.</summary>
    private static void StartTimeline(SidecarSyncBehavior sync, TimelineAsset customTimeline,
                                      MOTION_DETAIL detail)
    {
        if (customTimeline == null)
        {
            sync.IsModdedSkillActive = false;
            sync.SandboxRenderer.enabled = false;

            if (sync.OriginalRenderer != null)
                sync.OriginalRenderer.enabled = true;
            return;
        }

        sync.IsModdedSkillActive = true;
        sync.SandboxRenderer.enabled = true;
        sync.SlaveDirector.playableAsset = customTimeline;

        // Skills and parries are driven frame-by-frame off the master director instead of played
        // free-running, so their hit timings stay locked to the game's own animation.
        string motionName = detail.ToString();
        bool isSpecial = motionName.StartsWith("S") || motionName.ToLower().Contains("parrying");
        sync.ShouldSync = isSpecial;

        sync.SlaveDirector.time = 0;
        sync.SlaveDirector.extrapolationMode = detail == MOTION_DETAIL.Idle
            ? DirectorWrapMode.Loop
            : DirectorWrapMode.None;

        if (!isSpecial)
            sync.SlaveDirector.Play();

        foreach (var track in customTimeline.flattenedTracks)
        {
            var animTrack = track.TryCast<AnimationTrack>();
            if (animTrack != null)
                sync.SlaveDirector.SetGenericBinding(track, sync.SlaveAnimator);
        }

        if (sync.OriginalRenderer != null)
            sync.OriginalRenderer.enabled = false;
    }
}
