using UnityEngine;
using UnityEngine.Timeline;
using Il2CppInterop.Runtime;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System;
using DG.Tweening;

namespace Motions;

public static class TimelineBuilder
{
    private const string NamePrefix = "Custom_Created_";

    /// <summary>
    /// Recovers the variant index encoded in a built timeline's name, or -1 if it isn't one of ours.
    /// Lets us see which variant the game picked by reading the master director's asset.
    /// </summary>
    public static int GetVariantIndex(string timelineName)
    {
        if (string.IsNullOrEmpty(timelineName) || !timelineName.StartsWith(NamePrefix))
            return -1;

        int start = timelineName.IndexOf("_Var", StringComparison.Ordinal);
        if (start < 0) return -1;
        start += 4;

        int end = timelineName.IndexOf('_', start);
        if (end < 0) end = timelineName.Length;

        return int.TryParse(timelineName.Substring(start, end - start), out int variant) ? variant : -1;
    }

    // The interop Newtonsoft.Json is an Il2Cpp proxy and can't deserialize into
    // managed plugin types, so we use the runtime's System.Text.Json instead.
    // Lenient options match Newtonsoft's tolerance of comments/trailing commas.
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static SkillData LoadSkillData(string jsonPath)
    {
        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SkillData>(File.ReadAllText(jsonPath), JsonOptions);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[TimelineBuilder] JSON deserialize failed: {ex}");
            return null;
        }
    }

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
                // time is a fraction of the coin, and nothing validates the JSON: a typo'd 55555 (or a
                // negative) puts the marker outside the fixed-length timeline, where it never fires and
                // the coin never hands off. Clamped rather than skipped - a hand-off late is recoverable.
                double at = duration * Math.Clamp(data.hitCheckers[i].time, 0.0, 1.0);
                AddHitChecker(track, at, false, data.hitCheckers[i].isNextMotionCoinDelay);
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
            shaker.strength = shake.strength;
            shaker.vibrato = shake.vibrato;
            shaker.randomness = shake.randomness;
            shaker.fadeOut = shake.fadeOut;
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

    /// <summary>
    /// The move info a ToTarget or ToTargetWide phase asks for. Built in one place because both
    /// used to set ease and the radius flags on the info object they were about to REPLACE with a
    /// fresh one, which quietly dropped easingType, attackerRadius and targetRadius from every
    /// phase ever written. Fields set at construction cannot be lost that way.
    /// </summary>
    private static TweenMoveInfo_ToTarget MoveToTarget(SkillPhase phase)
    {
        var info = new TweenMoveInfo_ToTarget
        {
            arriveRadius = phase.move != null ? phase.move.x : 0f,
            duration = phase.duration != 0.0f ? phase.duration : 0.066f,
            isInclude_attakcerRadius = phase.attackerRadius,
            isInclude_targetRadius = phase.targetRadius
        };

        if (!string.IsNullOrEmpty(phase.easingType) && phase.easingType != "Unset"
            && Enum.TryParse<Ease>(phase.easingType, true, out Ease ease))
        {
            info.ease = ease;
        }

        return info;
    }

    /// <summary>
    /// The move info a Relative or MoveEnemy phase asks for. Both types carry ease and duration
    /// like ToTarget does; nothing here read them until now, so a Relative phase snapped to its
    /// destination however it was authored. duration 0 is the old behaviour and stays the default.
    /// </summary>
    private static TweenMoveInfo_Relative MoveRelative(SkillPhase phase, Vector3 movePos)
    {
        var info = new TweenMoveInfo_Relative
        {
            movePos = movePos,
            isRefreshDir = phase.isRefreshDir,
            duration = phase.duration
        };

        if (!string.IsNullOrEmpty(phase.easingType) && phase.easingType != "Unset"
            && Enum.TryParse<Ease>(phase.easingType, true, out Ease ease))
        {
            info.ease = ease;
        }

        return info;
    }

    /// <summary>Writes one coin's phases onto <paramref name="track"/> as game markers. A phase with
    /// several steps repeats its marker evenly across the phase's slice of the coin, so one authored
    /// phase can be a multi-hit flurry.</summary>
    public static void SetupSkillFromJson(TrackAsset track, CoinData data)
    {
        if (data?.phases == null || data.phases.Length == 0) return;

        foreach (var phase in data.phases)
        {
            for (int i = 0; i < phase.steps; i++)
            {
                // Single-step phases fire at `start`; the rest spread across start..end inclusive.
                double t = phase.steps == 1 ? 0 : i / (double)(phase.steps - 1);
                double time = data.totalDuration * (phase.start + (phase.end - phase.start) * t);

                AddPhaseMarker(track, phase, time);
            }
        }

        Logger.LogInfo("[TimelineBuilder] JSON skill setup complete.");
    }

    /// <summary>One phase, one marker. Creating and configuring the marker live in the same arm on
    /// purpose: they used to be two if-chains over the same <c>phase.type</c> strings, so adding a
    /// type meant editing both and forgetting the second left an unconfigured marker on the track.
    /// An unrecognised type is skipped, which is what makes a typo a no-op rather than a crash.</summary>
    private static void AddPhaseMarker(TrackAsset track, SkillPhase phase, double time)
    {
        Vector3 movePos = phase.move != null ? phase.move.ToVector3() : Vector3.zero;

        switch (phase.type)
        {
            case "Relative":
            {
                var tween = track.CreateMarker(Il2CppType.Of<SkillGiveTiming_TweenMove_Relative>(), time)
                                 .Cast<SkillGiveTiming_TweenMove_Relative>();
                tween.moveInfo = MoveRelative(phase, movePos);
                break;
            }

            // Same marker type as Relative, named so the enemy-moving patch can pick it out.
            case "MoveEnemy":
            {
                var tween = track.CreateMarker(Il2CppType.Of<SkillGiveTiming_TweenMove_Relative>(), time)
                                 .Cast<SkillGiveTiming_TweenMove_Relative>();
                tween.name = "MoveEnemy";
                tween.moveInfo = MoveRelative(phase, movePos);
                break;
            }

            case "ToTarget":
            {
                var tween = track.CreateMarker(Il2CppType.Of<SkillGiveTiming_TweenMove_ToTarget>(), time)
                                 .Cast<SkillGiveTiming_TweenMove_ToTarget>();
                tween.moveInfo = MoveToTarget(phase);
                break;
            }

            case "ToTargetWide":
            {
                var tween = track.CreateMarker(Il2CppType.Of<SkillGiveTiming_TweenMove_ToTarget_Wide>(), time)
                                 .Cast<SkillGiveTiming_TweenMove_ToTarget_Wide>();
                tween.moveInfo = MoveToTarget(phase);
                tween.moveInfo_wide = new TweenMoveInfo_ToTarget_Wide { arriveRadius_Vector = movePos };
                break;
            }

            case "GiveDamage":
            {
                var damage = track.CreateMarker(Il2CppType.Of<SkillGiveTiming_GiveDamage>(), time)
                                  .Cast<SkillGiveTiming_GiveDamage>();

                damage.ratios = phase.damageRatio > 0 ? phase.damageRatio : 1f;
                damage.info = new OnGiveDamageInfo
                {
                    multiHit = phase.damage != null ? phase.damage.multiHit : 1,
                    isUpAttack = phase.damage != null && phase.damage.isUpAttack,
                    multiHitDuration = phase.damage != null ? phase.damage.multiHitDuration : 0f
                };
                damage.sturnInfo = Sturn(phase.sturn);
                break;
            }
        }
    }

    /// <summary>The knockback a GiveDamage phase applies. <paramref name="sturn"/> null means the
    /// phase did not ask for one, and the defaults below are the ones every unconfigured hit used
    /// before the field existed - so omitting it keeps the old behaviour.</summary>
    private static OnGiveSturnInfo Sturn(SturnData sturn)
    {
        if (sturn == null)
        {
            return new OnGiveSturnInfo
            {
                sturnType = STURN_TYPE.KNOCKBACK,
                sturnDir = STURN_DIR.DIR_TOTARGET,
                sturnTiming = STURN_TIMING.ALL,
                forcePower = 5.0f,
                randomPower = 5.0f,
            };
        }

        return new OnGiveSturnInfo
        {
            sturnType = ParseOr(sturn.sturnType, STURN_TYPE.KNOCKBACK, nameof(sturn.sturnType)),
            sturnDir = ParseOr(sturn.sturnDir, STURN_DIR.DIR_TOTARGET, nameof(sturn.sturnDir)),
            sturnTiming = ParseOr(sturn.sturnTiming, STURN_TIMING.ALL, nameof(sturn.sturnTiming)),
            forcePower = sturn.forcePower,
            randomPower = sturn.randomPower,
            airborneAngle = sturn.airborneAngle,
            isRotateTarget = sturn.isRotateTarget,
            targetRotateAngle = sturn.targetRotateAngle,
        };
    }

    /// <summary>One sturn enum field, falling back to the documented default. The fallback is
    /// returned rather than pre-assigned to the out parameter: Enum.TryParse overwrites that with
    /// <c>default(T)</c> on failure, which is NONE for all three of these - so a typo used to
    /// silently produce a no-knockback hit instead of the default one it names.</summary>
    private static T ParseOr<T>(string text, T fallback, string field) where T : unmanaged, Enum
    {
        if (string.IsNullOrEmpty(text)) return fallback;

        if (Il2CppSystem.Enum.TryParse<T>(text, true, out T parsed)) return parsed;

        Logger.LogWarning($"[TimelineBuilder] {field}: '{text}' is not a valid {typeof(T).Name}, " +
                          $"using {fallback}.");
        return fallback;
    }


    /// <summary>
    /// Clones the original timeline, keeps it intact, but prunes the contents of specific tracks.
    /// Returns a list of timelines, one for each coin, with graduated hitmarkers.
    /// </summary>
    public static List<TimelineAsset> GetTimelines(
        string timelineName,
        string jsonPath,
        TimelineAsset bundleTimeline = null,
        string appearanceID = null,
        List<TrackAsset> originalVfxTracks = null,
        int variantIndex = 0,
        double?[] coinDurations = null)
    {
        SkillData data = LoadSkillData(jsonPath);

        // No JSON, or a JSON carrying only settings: fall back to a dummy coin, which both clears
        // the game's default logic and keeps the bundle's own timeline injectable.
        if (data?.coins == null || data.coins.Length == 0)
            data = OneFallbackCoin(data, bundleTimeline, coinDurations);

        var timelines = new List<TimelineAsset>();

        for (int coinIdx = 0; coinIdx < data.coins.Length; coinIdx++)
        {
            string name = $"{NamePrefix}{timelineName}_Var{variantIndex}_Coin_{coinIdx}";
            double sourceDuration = SourceDuration(data.coins[coinIdx], bundleTimeline,
                                                   coinDurations, coinIdx);

            timelines.Add(BuildCoinTimeline(name, data.coins[coinIdx], sourceDuration,
                                            bundleTimeline, originalVfxTracks));
        }

        return timelines;
    }

    /// <summary>The stand-in for a character with no skill JSON: one coin, as long as whatever is
    /// actually going to play.</summary>
    private static SkillData OneFallbackCoin(SkillData data, TimelineAsset bundleTimeline,
                                             double?[] coinDurations)
    {
        // The common case for a sprite-only mod: PNGs and no S1.json at all.
        bool fromSpriteMotion = coinDurations != null && coinDurations.Length > 0
                                && coinDurations[0].HasValue;

        double fallback = fromSpriteMotion ? coinDurations[0].Value
            : bundleTimeline != null ? bundleTimeline.duration
            : 1.0;

        // The hit checker is where the coin may hand off, so the usual 0.15 default silently
        // truncates the animation to 15% of its length. A bundle author has a timeline in front of
        // them and learns this; someone who dropped PNGs in a folder just sees their two-second
        // animation stop after a third of a second, with nothing logged. So when the length came
        // from a sprite motion and there is no JSON to say otherwise, hand off at the end instead.
        // Writing an explicit hitCheckers array still overrides this, and bundles are untouched.
        var defaultHitCheckers = fromSpriteMotion
            ? new HitCheckerData[] { new HitCheckerData { time = 1.0, isNextMotionCoinDelay = 0f } }
            : new HitCheckerData[0];

        data ??= new SkillData();
        data.coins = new CoinData[]
        {
            new CoinData
            {
                totalDuration = fallback,
                phases = new SkillPhase[0],
                hitCheckers = defaultHitCheckers
            }
        };

        return data;
    }

    /// <summary>How long the animation itself runs, used only when the JSON declines to say. A
    /// sprite motion is what actually plays, so its length wins over the bundle's.</summary>
    private static double SourceDuration(CoinData coinData, TimelineAsset bundleTimeline,
                                         double?[] coinDurations, int coinIdx)
    {
        if (coinDurations != null && coinIdx < coinDurations.Length && coinDurations[coinIdx].HasValue)
            return coinDurations[coinIdx].Value;

        return bundleTimeline != null ? bundleTimeline.duration : coinData.totalDuration;
    }

    /// <summary>One coin's timeline: the animation clips, then the four marker tracks the game reads
    /// its timings off, then whichever of the bundle's VFX tracks this coin asked for.</summary>
    private static TimelineAsset BuildCoinTimeline(string name, CoinData coinData,
                                                   double sourceDuration,
                                                   TimelineAsset bundleTimeline,
                                                   List<TrackAsset> originalVfxTracks)
    {
        double targetDuration = coinData.totalDuration > 0 ? coinData.totalDuration : sourceDuration;
        coinData.totalDuration = targetDuration;

        TimelineAsset dummyTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
        dummyTimeline.name = name;

        AddAnimationTracks(dummyTimeline, bundleTimeline, targetDuration);

        var appearanceTrack = dummyTimeline.CreateTrack(Il2CppType.Of<CharacterAppearanceTimelineTrack>(), null, "Appearance Track").Cast<TrackAsset>();
        var skillTrack = dummyTimeline.CreateTrack(Il2CppType.Of<SkillGiveTimingTrack>(), null, "Skill Timing Track").Cast<TrackAsset>();
        var onBattleCamZoomTrack = dummyTimeline.CreateTrack(Il2CppType.Of<OnBattleCamZoomTrack_Transform>(), null, "On Battle Cam Zoom Track").Cast<TrackAsset>();
        var onBattleCamRotateTrack = dummyTimeline.CreateTrack(Il2CppType.Of<OnBattleCamRotateTrack>(), null, "On Battle Cam Rotate Track").Cast<TrackAsset>();

        SetupAppearanceTrackMarkers(appearanceTrack, coinData);
        SetupSkillFromJson(skillTrack, coinData);
        SetupBattleCamZoomFromJson(onBattleCamZoomTrack, coinData);
        SetupBattleCamRotateFromJson(onBattleCamRotateTrack, coinData);

        // Native camera shake markers: CharacterApperacneResiver handles these automatically.
        SetupCameraShakeMarkers(appearanceTrack, coinData);

        CopyVfxTracks(dummyTimeline, coinData, originalVfxTracks);

        // Without this the timeline's length collapses to its longest clip, which cuts short any
        // motion whose clips don't span the whole duration (parries, guard).
        dummyTimeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        dummyTimeline.fixedDuration = targetDuration;

        return dummyTimeline;
    }

    /// <summary>Mirrors the bundle's animation tracks onto the new timeline, clip timings and all.
    /// With no bundle - a sprite-only motion - one empty clip spanning the coin stands in, so the
    /// director still has something to run its clock against.</summary>
    private static void AddAnimationTracks(TimelineAsset into, TimelineAsset bundleTimeline,
                                           double targetDuration)
    {
        if (bundleTimeline == null)
        {
            var soloTrack = into.CreateTrack(Il2CppType.Of<AnimationTrack>(), null, "Animation Track 0")
                                .Cast<AnimationTrack>();
            var soloClip = soloTrack.CreateClip<AnimationPlayableAsset>();
            soloClip.start = 0.0;
            soloClip.duration = targetDuration;
            return;
        }

        int animTrackIdx = 0;

        foreach (var bundleTrack in bundleTimeline.flattenedTracks)
        {
            var trackType = bundleTrack.GetIl2CppType().Name;
            Logger.LogInfo($"[TimelineBuilder] Found track: '{bundleTrack.name}' of type: '{trackType}'");

            if (!trackType.Contains("AnimationTrack")) continue;

            var animTrack = into.CreateTrack(Il2CppType.Of<AnimationTrack>(), null,
                                             $"Animation Track {animTrackIdx++}")
                                .Cast<AnimationTrack>();

            foreach (var bundleClip in bundleTrack.clips)
            {
                var clip = animTrack.CreateClip<AnimationPlayableAsset>();
                clip.start = bundleClip.start;
                clip.duration = bundleClip.duration;
            }
        }
    }

    /// <summary>Copies over the bundle VFX tracks this coin named. coinData.vfx holds 1-based
    /// indices into the bundle's VFX tracks; an index pointing at nothing is skipped, so a stale
    /// number in the JSON costs that one effect rather than the whole coin.</summary>
    private static void CopyVfxTracks(TimelineAsset into, CoinData coinData,
                                      List<TrackAsset> originalVfxTracks)
    {
        if (coinData.vfx == null || coinData.vfx.Length == 0 || originalVfxTracks == null) return;

        foreach (int idx in coinData.vfx)
        {
            int i = idx - 1;
            if (i < 0 || i >= originalVfxTracks.Count) continue;

            var origTrack = originalVfxTracks[i];
            var newTrack = into.CreateTrack(origTrack.GetIl2CppType(), null, origTrack.name);

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
}
