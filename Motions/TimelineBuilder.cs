using UnityEngine;
using UnityEngine.Timeline;
using Il2CppInterop.Runtime;
using System.IO;
using System.Text.Json;
using System;
using DG.Tweening;

namespace Motions;

public static class TimelineBuilder
{
    // The interop Newtonsoft.Json is an Il2Cpp proxy and can't deserialize into
    // managed plugin types, so we use the runtime's System.Text.Json instead.
    // Lenient options match Newtonsoft's tolerance of comments/trailing commas.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static void AddHitChecker(TrackAsset track, double time, bool isCanNextMotion, float isNextMotionCoinDelay)
    {
        var marker = track.CreateMarker(Il2CppType.Of<CharacterAppearanceMarker_HitCheaker>(), time);
        var hitChecker = marker.Cast<CharacterAppearanceMarker_HitCheaker>();
        hitChecker.hitCheakerInfo = new HitCheakerInfo
        {
            isCanNextMotion = isCanNextMotion,
            isNextMotionCoinDelay = isNextMotionCoinDelay
        };
    }

    public static void SetupAppearanceTrackMarkers(TrackAsset track, CoinData data)
    {
        double duration = data.totalDuration;
        if (data != null && data.hitCheckers != null && data.hitCheckers.Length > 0)
        {
            for (int i = 0; i < data.hitCheckers.Length; i++)
            {
                AddHitChecker(track, duration * data.hitCheckers[i].time, false, data.hitCheckers[i].isNextMotionCoinDelay);
            }
        }
        else
        {
            // Default behavior if no hitcheckers are defined in JSON
            AddHitChecker(track, duration * 0.15, false, 0.0f);
        }
    }

    public static void SetupCameraShakeMarkers(TrackAsset track, CoinData data)
    {
        if (data == null || data.shakes == null)
            return;

        double duration = data.totalDuration;
        foreach (var shake in data.shakes)
        {
            double time = shake.start * duration;
            var marker = track.CreateMarker(Il2CppType.Of<CharacterAppearanceMarker_CameraShaker>(), time);
            var shaker = marker.Cast<CharacterAppearanceMarker_CameraShaker>();
            shaker.duration = (float)shake.duration;
            shaker.strength = shake.intensity;
            shaker.vibrato = 10;
            shaker.randomness = 90f;
            shaker.fadeOut = true;
        }
    }

    public static void SetupBattleCamZoomFromJson(TrackAsset track, CoinData data)
    {
        if (data == null || data.zooms == null)
            return;

        foreach (var zoom in data.zooms)
        {
            var timelineClip = track.CreateClip(Il2CppType.Of<OnBattleCamZoomClip>());
            timelineClip.start = zoom.start * data.totalDuration;
            timelineClip.duration = zoom.duration;

            var asset = timelineClip.asset.TryCast<OnBattleCamZoomClip>();
            if (asset != null && asset.template != null)
            {
                var info = new OnBattleCamZoomInfo();
                info.SetZoomAttacker = zoom.attacker;
                info.SetZoomTargets = zoom.targets;
                info.SetZoomBetweenPoint = zoom.between;
                info.AxizY = zoom.axisY;
                info.size = zoom.size;
                info.duration = zoom.zoomDuration;
                info.isRelative = zoom.isRelative;
                info.focusSpeed = zoom.focusSpeed;

                if (!string.IsNullOrEmpty(zoom.easeType) && zoom.easeType != "Unset")
                {
                    Ease ease = Ease.Unset;
                    if (Enum.TryParse<Ease>(zoom.easeType, true, out ease))
                    {
                        info.easeType = ease;
                    }
                }

                asset.template.zoomInfo = info;
            }
        }
    }

    public static void SetupBattleCamRotateFromJson(TrackAsset track, CoinData data)
    {
        if (data == null || data.rotates == null)
            return;

        foreach (var rotate in data.rotates)
        {
            var timelineClip = track.CreateClip(Il2CppType.Of<OnBattleCamRotateClip>());
            timelineClip.start = rotate.start * data.totalDuration;
            timelineClip.duration = rotate.duration;

            var asset = timelineClip.asset.TryCast<OnBattleCamRotateClip>();
            if (asset != null && asset.template != null)
            {
                var info = new OnBattleCamRotateInfo();
                info.targetAngle = rotate.targetAngle != null ? rotate.targetAngle.ToVector3() : Vector3.zero;
                info.duration = (float)rotate.duration;
                info.focusRotateSpeed = rotate.focusRotateSpeed;

                if (!string.IsNullOrEmpty(rotate.easeType) && rotate.easeType != "Unset")
                {
                    Ease ease = Ease.Unset;
                    if (Enum.TryParse<Ease>(rotate.easeType, true, out ease))
                    {
                        info.easeType = ease;
                    }
                }

                asset.template.rotateInfo = info;
            }
        }
    }

    public static void SetupSkillFromJson(TrackAsset track, CoinData data)
    {
        if (data == null)
        {
            return;
        }

        double duration = data.totalDuration;
        if (data.phases == null || data.phases.Length == 0)
        {
            return;
        }

        foreach (var phase in data.phases)
        {
            if (phase.steps <= 0)
                continue;

            for (int i = 0; i < phase.steps; i++)
            {
                double t = phase.steps == 1
                    ? 0
                    : i / (double)(phase.steps - 1);

                double time = duration *
                              (phase.start + (phase.end - phase.start) * t);

                Il2CppSystem.Type markerType = null;

                if (phase.type == "Relative")
                    markerType = Il2CppType.Of<SkillGiveTiming_TweenMove_Relative>();
                else if (phase.type == "ToTargetWide")
                    markerType = Il2CppType.Of<SkillGiveTiming_TweenMove_ToTarget_Wide>();
                else if (phase.type == "MoveEnemy")
                    markerType = Il2CppType.Of<SkillGiveTiming_TweenMove_Relative>();
                else if (phase.type == "GiveDamage")
                    markerType = Il2CppType.Of<SkillGiveTiming_GiveDamage>();
                else
                    continue;

                var marker = track.CreateMarker(markerType, time);

                if (phase.type == "Relative")
                {
                    var tween = marker.Cast<SkillGiveTiming_TweenMove_Relative>();

                    tween.moveInfo = new TweenMoveInfo_Relative
                    {
                        movePos = phase.move != null
                            ? phase.move.ToVector3()
                            : Vector3.zero,

                        isRefreshDir = phase.isRefreshDir
                    };
                }
                else if (phase.type == "ToTargetWide")
                {
                    var tween = marker.Cast<SkillGiveTiming_TweenMove_ToTarget_Wide>();

                    Vector3 moveVec = phase.move != null
                        ? phase.move.ToVector3()
                        : Vector3.zero;

                    tween.moveInfo = new TweenMoveInfo_ToTarget
                    {
                        arriveRadius = phase.move != null ? phase.move.x : 0f
                    };

                    tween.moveInfo_wide = new TweenMoveInfo_ToTarget_Wide
                    {
                        arriveRadius_Vector = moveVec
                    };
                }
                else if (phase.type == "MoveEnemy")
                {
                    var tween = marker.Cast<SkillGiveTiming_TweenMove_Relative>();

                    Vector3 moveVec = phase.move != null
                        ? phase.move.ToVector3()
                        : Vector3.zero;

                    tween.name = "MoveEnemy";
                    tween.moveInfo = new TweenMoveInfo_Relative
                    {
                        movePos = moveVec,
                        isRefreshDir = phase.isRefreshDir
                    };
                }
                else if (phase.type == "GiveDamage")
                {
                    var damage = marker.Cast<SkillGiveTiming_GiveDamage>();
                    damage.info = new OnGiveDamageInfo
                    {
                        multiHit = phase.damage != null ? phase.damage.multiHit : 1,
                        isUpAttack = phase.damage != null ? phase.damage.isUpAttack : false,
                        multiHitDuration = phase.damage != null ? phase.damage.multiHitDuration : 0f
                    };

                    if (phase.sturn != null)
                    {
                        STURN_TYPE sType = STURN_TYPE.KNOCKBACK;
                        bool typeParsed = Il2CppSystem.Enum.TryParse<STURN_TYPE>(phase.sturn.sturnType, true, out sType);
                        Logger.LogInfo($"[TimelineBuilder] Parsing sturnType: '{phase.sturn.sturnType}' -> {sType} (Success: {typeParsed})");

                        STURN_DIR sDir = STURN_DIR.DIR_TOTARGET;
                        bool dirParsed = Il2CppSystem.Enum.TryParse<STURN_DIR>(phase.sturn.sturnDir, true, out sDir);
                        Logger.LogInfo($"[TimelineBuilder] Parsing sturnDir: '{phase.sturn.sturnDir}' -> {sDir} (Success: {dirParsed})");

                        STURN_TIMING sTiming = STURN_TIMING.ALL;
                        bool timingParsed = Il2CppSystem.Enum.TryParse<STURN_TIMING>(phase.sturn.sturnTiming, true, out sTiming);
                        Logger.LogInfo($"[TimelineBuilder] Parsing sturnTiming: '{phase.sturn.sturnTiming}' -> {sTiming} (Success: {timingParsed})");

                        damage.sturnInfo = new OnGiveSturnInfo
                        {
                            sturnType = sType,
                            sturnDir = sDir,
                            sturnTiming = sTiming,
                            forcePower = phase.sturn.forcePower,
                            randomPower = phase.sturn.randomPower,
                            airborneAngle = phase.sturn.airborneAngle,
                            isRotateTarget = phase.sturn.isRotateTarget,
                            targetRotateAngle = phase.sturn.targetRotateAngle,
                        };
                    }
                    else
                    {
                        damage.sturnInfo = new OnGiveSturnInfo
                        {
                            sturnType = STURN_TYPE.KNOCKBACK,
                            sturnDir = STURN_DIR.DIR_TOTARGET,
                            sturnTiming = STURN_TIMING.ALL,
                            forcePower = 5.0f,
                            randomPower = 5.0f,
                            airborneAngle = 0.0f,
                            isRotateTarget = false,
                            targetRotateAngle = 0.0f,
                        };
                    }

                }
            }
        }

        Logger.LogInfo("[TimelineBuilder] JSON skill setup complete.");
    }


    /// <summary>
    /// Clones the original timeline, keeps it intact, but prunes the contents of specific tracks.
    /// Returns a list of timelines, one for each coin, with graduated hitmarkers.
    /// </summary>
    public static System.Collections.Generic.List<TimelineAsset> GetTimelines(string timelineName, string jsonPath, TimelineAsset bundleTimeline = null, string appearanceID = null, System.Collections.Generic.List<TrackAsset> originalVfxTracks = null)
    {
        // 1. Load JSON data
        SkillData data = null;
        if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
        {
            try
            {
                string json = File.ReadAllText(jsonPath);
                data = JsonSerializer.Deserialize<SkillData>(json, JsonOptions);
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[TimelineBuilder] JSON deserialize failed: {ex}");
            }
        }
        else
        {
            // If No JSON, return a dummy coin to clear default game logic (mostly for skills)
            data = new SkillData
            {
                coins = new CoinData[]
                {
                    new CoinData
                    {
                        totalDuration = bundleTimeline != null ? bundleTimeline.duration : 1.0,
                        phases = new SkillPhase[0],
                        hitCheckers = new HitCheckerData[0]
                    }
                }
            };
        }

        var timelines = new System.Collections.Generic.List<TimelineAsset>();

        if (data == null || data.coins == null || data.coins.Length == 0)
        {
            Logger.LogWarning("[TimelineBuilder] No coin data found in JSON. Returning empty list.");
            return timelines;
        }

        for (int coinIdx = 0; coinIdx < data.coins.Length; coinIdx++)
        {
            var coinData = data.coins[coinIdx];

            double targetDuration = coinData.totalDuration;
            if (bundleTimeline != null)
                targetDuration = bundleTimeline.duration;

            TimelineAsset dummyTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
            dummyTimeline.name = $"Custom_Created_{timelineName}_Coin_{coinIdx}";

            int animTrackIdx = 0;

            if (bundleTimeline != null)
            {
                foreach (var bundleTrack in bundleTimeline.flattenedTracks)
                {
                    var trackType = bundleTrack.GetIl2CppType().Name;
                    Logger.LogInfo($"[TimelineBuilder] Found track: '{bundleTrack.name}' of type: '{trackType}'");
                    if (trackType.Contains("AnimationTrack"))
                    {
                        var newTrack = dummyTimeline.CreateTrack(Il2CppType.Of<AnimationTrack>(), null, $"Animation Track {animTrackIdx++}");
                        var animTrack = newTrack.Cast<AnimationTrack>();

                        foreach (var bundleClip in bundleTrack.clips)
                        {
                            var clip = animTrack.CreateClip<AnimationPlayableAsset>();
                            clip.start = bundleClip.start;
                            clip.duration = bundleClip.duration;
                        }
                    }
                }
            }
            else
            {
                var newTrack = dummyTimeline.CreateTrack(Il2CppType.Of<AnimationTrack>(), null, "Animation Track 0");
                var animTrack = newTrack.Cast<AnimationTrack>();
                var clip = animTrack.CreateClip<AnimationPlayableAsset>();
                clip.start = 0.0;
                clip.duration = targetDuration;
            }

            var appearanceTrack = dummyTimeline.CreateTrack(Il2CppType.Of<CharacterAppearanceTimelineTrack>(), null, "Appearance Track").Cast<TrackAsset>();
            var skillTrack = dummyTimeline.CreateTrack(Il2CppType.Of<SkillGiveTimingTrack>(), null, "Skill Timing Track").Cast<TrackAsset>();
            var onBattleCamZoomTrack = dummyTimeline.CreateTrack(Il2CppType.Of<OnBattleCamZoomTrack_Transform>(), null, "On Battle Cam Zoom Track").Cast<TrackAsset>();
            var onBattleCamRotateTrack = dummyTimeline.CreateTrack(Il2CppType.Of<OnBattleCamRotateTrack>(), null, "On Battle Cam Rotate Track").Cast<TrackAsset>();

            SetupAppearanceTrackMarkers(appearanceTrack, coinData);
            SetupSkillFromJson(skillTrack, coinData);
            SetupBattleCamZoomFromJson(onBattleCamZoomTrack, coinData);
            SetupBattleCamRotateFromJson(onBattleCamRotateTrack, coinData);

            // Native camera shake markers — CharacterApperacneResiver handles these automatically.
            SetupCameraShakeMarkers(appearanceTrack, coinData);

            // VFX: coinData.vfx indices into originalVfxTracks (1-indexed)
            if (coinData.vfx != null && coinData.vfx.Length > 0 && originalVfxTracks != null)
            {
                foreach (int idx in coinData.vfx)
                {
                    int i = idx - 1;
                    if (i < 0 || i >= originalVfxTracks.Count) continue;
                    var origTrack = originalVfxTracks[i];
                    var newTrack = dummyTimeline.CreateTrack(origTrack.GetIl2CppType(), null, origTrack.name);
                    foreach (var c in origTrack.clips)
                    {
                        if (c.asset == null) continue;
                        var newClip = newTrack.CreateClip(c.asset.GetIl2CppType());
                        newClip.displayName = c.displayName;
                        newClip.start = c.start;
                        newClip.duration = c.duration;
                        newClip.asset = c.asset;
                    }
                }
            }

            timelines.Add(dummyTimeline);
        }

        return timelines;
    }
}