using Il2CppSystem;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

namespace Motions;

/// <summary>
/// Extracts sound/VFX cues from bundle timelines and manages timeline caching.
/// Works with <see cref="MotionData"/> for storage.
/// </summary>
public static class CueExtractor
{
    /// <summary>
    /// Returns a cloned, cue-extracted, audio-stripped TimelineAsset for the given appearance/motion/index.
    /// Populates SoundCueCache and VfxCueCache as a side effect.
    /// </summary>
    public static TimelineAsset GetOrCacheTimeline(string appearanceID, MOTION_DETAIL detail, int index = -1)
    {
        var key = new MotionKey
        {
            AppearanceID = appearanceID,
            Motion = detail,
            Index = index
        };

        if (MotionData.TimelineCache.TryGetValue(key, out var cached))
            return cached;

        var timeline = MotionData.FindTimelineForAppearance(appearanceID, detail, index);
        if (timeline == null)
            return null;

        var instance = UnityEngine.Object.Instantiate(timeline);

        ExtractSoundCues(instance, appearanceID, detail, index);
        ExtractVfxCues(instance, appearanceID, detail, index);
        StripAudioTracks(instance);

        MotionData.TimelineCache[key] = instance;
        return instance;
    }

    /// <summary>
    /// Pre-loads buff aura prefabs from bundles into MotionData.BuffAuraPrefabs.
    /// Called once at battle init. No instantiation — that happens per-unit when the buff is applied.
    /// </summary>
    public static void EagerCacheBuffEffects()
    {
        foreach (var pair in MotionData.LoadedBuffAssets)
        {
            BUFF_UNIQUE_KEYWORD keyword = pair.Key;

            if (MotionData.BuffAuraPrefabs.ContainsKey(keyword))
                continue;

            Logger.LogInfo($"Caching buff aura prefab for {keyword} ({(int)keyword})");

            var prefab = MotionData.FindBuffAuraPrefab(keyword, out bool isFront);
            if (prefab == null)
            {
                Logger.LogError($"Couldn't find buff aura prefab for {keyword}");
                continue;
            }

            MotionData.BuffAuraPrefabs.Add(keyword, prefab);
            MotionData.BuffAuraIsFront.Add(keyword, isFront);
        }
    }

    /// <summary>
    /// Pre-populates timeline and VFX caches for all skill-type motions (coins 0-100).
    /// Called at battle-init to avoid mid-combat lag on first play.
    /// </summary>
    public static void EagerCacheMotions(string appearanceID)
    {
        foreach (var detailObj in System.Enum.GetValues(typeof(MOTION_DETAIL)))
        {
            MOTION_DETAIL detail = (MOTION_DETAIL)detailObj;
            string motionName = detail.ToString();

            if (!motionName.StartsWith("S")) continue;

            for (int i = 0; i <= 100; i++)
            {
                GetOrCacheTimeline(appearanceID, detail, i);
            }
        }
        Logger.LogInfo($"[EagerCache] Pre-populated timeline/VFX caches for {appearanceID}.");
    }

    // ---- Cue extraction ---------------------------------------------------

    public static void ExtractSoundCues(TimelineAsset timeline, string appearanceID, MOTION_DETAIL motion, int index)
    {
        var key = new MotionKey
        {
            AppearanceID = appearanceID,
            Motion = motion,
            Index = index
        };

        if (MotionData.SoundCueCache.ContainsKey(key)) return;

        var cues = new System.Collections.Generic.List<SoundCue>();

        foreach (var track in timeline.flattenedTracks)
        {
            foreach (var clip in track.clips)
            {
                Logger.LogInfo($"Checking clip: {clip.displayName}");
                string clipName = clip.displayName?.ToLower();
                if (string.IsNullOrEmpty(clipName)) continue;

                var bytes = MotionData.FindBytesAsset(appearanceID, clipName);
                if (bytes == null) continue;

                Logger.LogInfo($"Got bytes: {bytes.Length} | clipIn={clip.clipIn:F3}s | duration={clip.duration:F3}s");
                cues.Add(new SoundCue
                {
                    StartTime = (float)clip.start,
                    ClipIn = (float)clip.clipIn,
                    Duration = (float)clip.duration,
                    WavData = bytes,
                    Triggered = false
                });
                Logger.LogInfo($"Sound cue registered for: AppearanceID: {key.AppearanceID} Motion: {key.Motion} Index: {key.Index}");
            }
        }

        MotionData.SoundCueCache[key] = cues;
    }

    public static void ExtractVfxCues(TimelineAsset timeline, string appearanceID, MOTION_DETAIL motion, int index)
    {
        var key = new MotionKey
        {
            AppearanceID = appearanceID,
            Motion = motion,
            Index = index
        };

        if (MotionData.VfxCueCache.ContainsKey(key)) return;

        var cues = new System.Collections.Generic.List<VfxCue>();
        var tracksToRemove = new System.Collections.Generic.List<TrackAsset>();

        foreach (var track in timeline.flattenedTracks)
        {
            var trackType = track.GetIl2CppType().Name;

            if (trackType.Contains("ControlTrack"))
            {
                foreach (var clip in track.clips)
                {
                    string clipName = clip.displayName;
                    if (string.IsNullOrEmpty(clipName)) continue;

                    VfxSpawnTarget spawnTarget = VfxSpawnTarget.Self;
                    float offsetX = 0f, offsetY = 0f, offsetZ = 0f;
                    string prefabName = clipName;

                    int atIndex = clipName.IndexOf('@');
                    if (atIndex >= 0)
                    {
                        prefabName = clipName.Substring(0, atIndex);
                        string suffix = clipName.Substring(atIndex + 1).ToLower();

                        if (suffix == "enemy")
                            spawnTarget = VfxSpawnTarget.Enemy;
                        else if (suffix == "center")
                            spawnTarget = VfxSpawnTarget.Center;
                        else if (suffix.StartsWith("offset_"))
                        {
                            var parts = suffix.Substring("offset_".Length).Split('_');
                            if (parts.Length >= 1) float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out offsetX);
                            if (parts.Length >= 2) float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out offsetY);
                            if (parts.Length >= 3) float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out offsetZ);
                        }
                    }

                    var prefab = MotionData.FindPrefabAsset(appearanceID, prefabName);
                    if (prefab != null)
                    {
                        cues.Add(new VfxCue
                        {
                            StartTime = (float)clip.start,
                            Duration = (float)clip.duration,
                            Prefab = prefab,
                            Triggered = false,
                            SpawnTarget = spawnTarget,
                            OffsetX = offsetX,
                            OffsetY = offsetY,
                            OffsetZ = offsetZ
                        });
                        Logger.LogInfo($"VFX cue registered for: {prefabName} (target={spawnTarget}, offset=({offsetX},{offsetY},{offsetZ}))");
                    }
                }
                tracksToRemove.Add(track);
            }
        }

        foreach (var track in tracksToRemove)
        {
            timeline.DeleteTrack(track);
            Logger.LogInfo($"[ExtractVfxCues] Removed control track: {track.name}");
        }

        MotionData.VfxCueCache[key] = cues;
    }

    public static void StripAudioTracks(TimelineAsset timeline)
    {
        if (timeline == null) return;

        var tracksToRemove = new List<TrackAsset>();

        foreach (var track in timeline.flattenedTracks)
        {
            var typeName = track.GetIl2CppType().Name;

            bool isUnityAudio = typeName.Contains("AudioTrack");
            bool hasFMODClips = false;

            foreach (var clip in track.clips)
            {
                var asset = clip.asset;
                if (asset == null) continue;

                var assetType = asset.GetIl2CppType().Name;
                if (assetType.Contains("FMOD"))
                {
                    hasFMODClips = true;
                    break;
                }
            }

            if (isUnityAudio || hasFMODClips)
            {
                tracksToRemove.Add(track);
            }
        }

        foreach (var track in tracksToRemove)
        {
            timeline.DeleteTrack(track);
            Logger.LogInfo($"[StripAudioTracks] Removed track: {track.name}");
        }
    }
}
