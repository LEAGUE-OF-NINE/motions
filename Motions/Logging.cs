using FMODUnity;
using Il2CppSystem.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Motions;

public static class MotionLogger
{
    public static void LogTimelineDetailed(TimelineAsset timeline, int index)
    {
        Logger.LogInfo($"  [{index}] Timeline: {timeline.name} ({timeline.duration:F3}s)");

        foreach (var track in timeline.flattenedTracks)
        {
            string trackType = track.GetIl2CppType().Name;
            Logger.LogInfo($"    Track: {track.name} [{trackType}]");

            // 1. Log Clips (Nested Assets, Sound, etc.)
            foreach (var clip in track.clips)
            {
                string assetType = clip.asset != null ? clip.asset.GetIl2CppType().Name : "null";
                Logger.LogInfo($"      -> CLIP: {clip.displayName} [{assetType}] @ {clip.start:F3}s (Dur: {clip.duration:F3}s)");

                // Deep dive into clip asset if it exists
                if (clip.asset != null)
                {
                    // If it's an AnimationPlayableAsset, log the clip name
                    var animAsset = clip.asset.TryCast<AnimationPlayableAsset>();
                    if (animAsset != null && animAsset.clip != null)
                    {
                        Logger.LogInfo($"         [AnimAsset] Clip Name: {animAsset.clip.name}");
                    }

                    // Generic property discovery for sound/FMOD
                    // AudioPlayableAsset (Unity)
                    var audioAsset = clip.asset.TryCast<AudioPlayableAsset>();
                    if (audioAsset != null && audioAsset.clip != null)
                    {
                        Logger.LogInfo($"         [AudioAsset] Clip Name: {audioAsset.clip.name}");
                    }

                    // FMOD usually has specific assets like 'FMODEventPlayable'
                    // We log the type and name to help the user identify it.
                    Logger.LogInfo($"         [Asset Detail] Type: {assetType}, Name: {clip.asset.name}");

                    if (assetType.Contains("FMOD"))
                    {
                        var fmodEvent = clip.asset.TryCast<FMODEventPlayable>();
                        if (fmodEvent != null)
                        {
                            // Logger.LogWarning($"            [FMOD Event] Reference: {fmodEvent.eventReference}");
                            Logger.LogWarning($"            [FMOD Event] Reference: {fmodEvent.eventName}");
                            Logger.LogWarning($"            [FMOD Event] Reference: {fmodEvent.eventName}");
                        }
                    }

                    // OnBattleCamZoomClip Deep Logging
                    if (assetType == "OnBattleCamZoomClip")
                    {
                        var zoomClip = clip.asset.TryCast<OnBattleCamZoomClip>();
                        if (zoomClip != null && zoomClip.template != null)
                        {
                            var behaviour = zoomClip.template;
                            Logger.LogInfo($"         [ZoomClip] Template (Behaviour) Found: {behaviour.GetIl2CppType().Name}");

                            if (behaviour.zoomInfo != null)
                            {
                                var info = behaviour.zoomInfo;
                                Logger.LogInfo($"         [ZoomInfo] attacker={info.SetZoomAttacker}, targets={info.SetZoomTargets}, between={info.SetZoomBetweenPoint}, axisY={info.AxizY}");
                                Logger.LogInfo($"         [ZoomInfo] size={info.size}, dur={info.duration}, relative={info.isRelative}, speed={info.focusSpeed}, ease={info.easeType}");
                            }
                        }
                    }

                    // OnBattleCamRotateClip Deep Logging
                    if (assetType == "OnBattleCamRotateClip")
                    {
                        var rotateClip = clip.asset.TryCast<OnBattleCamRotateClip>();
                        if (rotateClip != null && rotateClip.template != null)
                        {
                            var behaviour = rotateClip.template;
                            Logger.LogInfo($"         [RotateClip] Template (Behaviour) Found: {behaviour.GetIl2CppType().Name}");

                            if (behaviour.rotateInfo != null)
                            {
                                var info = behaviour.rotateInfo;
                                Logger.LogInfo($"         [RotateInfo] targetAngle=({info.targetAngle.x}, {info.targetAngle.y}, {info.targetAngle.z}), dur={info.duration}, speed={info.focusRotateSpeed}, ease={info.easeType}");
                            }
                        }
                    }


                    if (assetType == "EffectActivateTimelineClip")
                    {
                        var effectClip = clip.asset.TryCast<EffectActivateTimelineClip>();
                        if (effectClip != null && effectClip.template != null)
                        {
                            var template = effectClip.template;
                            var activeTime = template.effectActiveInfo.activeTime;
                            var attackEffect = template.attackEffect;
                            Logger.LogInfo($"         [EffectClip] Template (Behaviour) name: {effectClip.name}, ActiveTime: {activeTime}");

                        }
                    }
                }
            }

            // 2. Log Markers
            var markers = track.m_Markers.markers;
            for (int m = 0; m < markers.Count; m++)
            {
                var marker = markers[m];
                string markerType = marker.Cast<Il2CppSystem.Object>().GetIl2CppType().Name;
                Logger.LogInfo($"      -> MARKER: {markerType} @ {marker.time:F3}s");

                // --- DEEP FIELD LOGGING (Manual) ---

                // HitCheaker
                var hitChecker = marker.TryCast<CharacterAppearanceMarker_HitCheaker>();
                if (hitChecker != null && hitChecker.hitCheakerInfo != null)
                {
                    var info = hitChecker.hitCheakerInfo;
                    Logger.LogInfo($"         [HitCheakerInfo] isCanNext={info.isCanNextMotion}, delay={info.isNextMotionCoinDelay}");
                }

                // GiveDamage
                var damage = marker.TryCast<SkillGiveTiming_GiveDamage>();
                if (damage != null && damage.info != null)
                {
                    var info = damage.info;
                    Logger.LogInfo($"         [DamageInfo] multiHit={info.multiHit}, multiHitDur={info.multiHitDuration}");
                }

                // TweenMove Relative
                var tweenRel = marker.TryCast<SkillGiveTiming_TweenMove_Relative>();
                if (tweenRel != null && tweenRel.moveInfo != null)
                {
                    var info = tweenRel.moveInfo;
                    Logger.LogInfo($"         [TweenRel] pos=({info.movePos.x}, {info.movePos.y}), refreshDir={info.isRefreshDir}");
                }

                // TweenMove ToTarget Wide
                var tweenWide = marker.TryCast<SkillGiveTiming_TweenMove_ToTarget_Wide>();
                if (tweenWide != null)
                {
                    if (tweenWide.moveInfo != null)
                        Logger.LogInfo($"         [TweenWide.moveInfo] arriveRadius={tweenWide.moveInfo.arriveRadius}");
                    if (tweenWide.moveInfo_wide != null)
                        Logger.LogInfo($"         [TweenWide.wideInfo] arriveRadius_Vector=({tweenWide.moveInfo_wide.arriveRadius_Vector.x}, {tweenWide.moveInfo_wide.arriveRadius_Vector.y})");
                }
            }
        }
    }

    private static void LogTrackDetails(TrackAsset track, string category, MOTION_DETAIL motiondetail)
    {
        // Log basic track info
        Logger.LogInfo($"[TimelineLog] [{category}] {motiondetail} | Track Name: {track.name}");

        // --- LOG CLIPS (Common in Animation Tracks) ---
        foreach (var clip in track.clips)
        {
            Logger.LogInfo($"    -> CLIP: {clip.displayName} | Start: {clip.start:F3}s | End: {(clip.start + clip.duration):F3}s | Dur: {clip.duration:F3}s");
        }

        // --- LOG MARKERS (Common in Skill/Appearance Tracks) ---
        var markers = track.m_Markers.markers;
        for (int i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            string markerType = marker.Cast<Il2CppSystem.Object>().GetIl2CppType().Name;

            Logger.LogInfo($"    -> MARKER: {markerType} | Time: {marker.time:F3}s");

            // Specific Damage Data
            var damage = marker.TryCast<SkillGiveTiming_GiveDamage>();
            if (damage != null && damage.info != null)
            {
                Logger.LogInfo($"       >> Damage Data: MultiHit={damage.info.multiHit}, Rate={damage.info.multiHitDuration}");
            }
        }
    }

    public static void LogAttackEfects(List<CharacterAttackEffect> effects)
    {
        foreach (var effect in effects)
        {
            if (effect == null) continue;

            string effectName = effect.name ?? "Unnamed Effect";
            string animatorName = effect._thisAnimator?.name ?? "None";
            string particleName = effect._thisParticle?.name ?? "None";
            string timelineName = effect._thisTimeLine?.name ?? "None";
            

            Logger.LogInfo($"[AttackEffect] Name: {effectName} | Type: {effect.CurEffectType} | effect.FixedPos={effect.FixedPos} | effect.FixedScale={effect.FixedScale}");
            Logger.LogInfo($"      - Fields: Animator={animatorName}, Particle={particleName}, Timeline={timelineName}");

            // Check the _animators list
            if (effect._animators != null)
            {
                Logger.LogInfo($"      - _animators List Count: {effect._animators.Count}");
                for (int i = 0; i < effect._animators.Count; i++)
                {
                    var anim = effect._animators[i];
                    if (anim != null) Logger.LogInfo($"        [{i}] {anim.name}");
                }
            }

            // Component Discovery
            var directAnimator = effect.GetComponent<Animator>();
            var directParticle = effect.GetComponent<ParticleSystem>();
            var directDirector = effect.GetComponent<PlayableDirector>();

            if (directAnimator != null) Logger.LogInfo($"      + Found Animator on GameObject: {directAnimator.name}");
            if (directParticle != null) Logger.LogInfo($"      + Found ParticleSystem on GameObject: {directParticle.name}");
            if (directDirector != null) Logger.LogInfo($"      + Found PlayableDirector on GameObject: {directDirector.name}");

            // Children Discovery (Common for many-part effects)
            var childAnimators = effect.GetComponentsInChildren<Animator>();
            var childParticles = effect.GetComponentsInChildren<ParticleSystem>();
            if (childAnimators.Count > (directAnimator != null ? 1 : 0)) Logger.LogInfo($"      + Found {childAnimators.Count} Child Animators");
            if (childParticles.Count > (directParticle != null ? 1 : 0)) Logger.LogInfo($"      + Found {childParticles.Count} Child Particles");

            // Tree Logging
            Logger.LogInfo($"      - Hierarchy Tree:");
            LogHierarchy(effect.transform, "        ");
        }
    }

    private static void LogHierarchy(Transform t, string indent)
    {
        Logger.LogInfo($"{indent} {t.name}");
        for (int i = 0; i < t.childCount; i++)
        {
            LogHierarchy(t.GetChild(i), indent + "  ");
        }
    }
}