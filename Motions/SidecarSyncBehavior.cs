using System;
using System.Collections.Generic;
using FMOD;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Motions;

/// <summary>
/// Attached to the sidecar GameObject. Syncs the slave PlayableDirector with the
/// master, fires sound/VFX cues from pre-extracted lists, and copies alpha from
/// the original sprite renderer.
/// </summary>
public class SidecarSyncBehavior : MonoBehaviour
{
    public SidecarSyncBehavior(IntPtr ptr) : base(ptr) { }

    public PlayableDirector MasterDirector;
    public PlayableDirector SlaveDirector;
    public Animator SlaveAnimator;
    public SpriteRenderer SandboxRenderer;
    public SpriteRenderer OriginalRenderer;
    public SD.CharacterAppearance Appearance;

    public List<SoundCue> SoundCues = new();
    public List<VfxCue> VfxCues = new();

    public bool IsModdedSkillActive = false;
    public bool ShouldSync = true;

    /// <summary>What PlayCustomMotion last started, so props can match their actions to it.</summary>
    public MOTION_DETAIL CurrentMotion;
    public int CurrentCoin = -1;
    /// <summary>Length of the slave's own asset. Only the timebase when nothing is syncing us.</summary>
    public double MotionDuration;

    /// <summary>
    /// The length action fractions are measured against.
    /// <para>
    /// While ShouldSync is set, Update copies MasterDirector.time straight into the slave, so the
    /// numerator is master time and the denominator has to be the master's asset to match: the
    /// timeline TimelineBuilder built, whose fixedDuration is the coin's totalDuration - the base
    /// every phase, hit checker, shake and zoom fraction in this project is expressed against.
    /// </para><para>
    /// MotionDuration is the slave's asset instead - for a bundle mod, the bundle's own animation,
    /// of unrelated length. Dividing master time by it scaled every prop action by
    /// totalDuration/bundleDuration, so a strike authored for its GiveDamage phase landed early or
    /// late by whatever the bundle was. Unsynced motions run the slave on its own clock, where
    /// MotionDuration is right and is the fallback.
    /// </para>
    /// </summary>
    public double ActionTimebase()
    {
        if (ShouldSync && MasterDirector != null)
        {
            var asset = MasterDirector.playableAsset;
            if (asset != null && asset.duration > 0) return asset.duration;
        }

        return MotionDuration;
    }

    /// <summary>Bundle-free playback. Null when the motion came from a bundle.</summary>
    public Sprite[] Frames;
    public double[] FrameTimes;
    private int _frameCursor;

    /// <summary>Called when a new motion starts so the cursor does not carry over.</summary>
    public void ResetFrameCursor() => _frameCursor = 0;


    /// <summary>The transform of the first current target, or null. Public because props strike it.</summary>
    public Transform GetFirstTargetTransform()
    {
        try
        {
            if (Appearance == null) return null;
            var view = Appearance.GetView();
            if (view == null) return null;
            var viewer = view.GetCurrentSkillViewer();
            if (viewer == null) return null;
            var targets = viewer.GetCurrentTargets();
            if (targets != null && targets.Count > 0)
                return targets[0].transform;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Positions of every current target, appended to <paramref name="into"/>. Public because a prop
    /// strike can spread across them or aim at their middle, and GetFirstTargetTransform drops
    /// everything past the first. Positions not transforms: one interop pass per frame, not per slot.
    /// </summary>
    [HideFromIl2Cpp]
    public void GetTargetPositions(System.Collections.Generic.List<Vector3> into)
    {
        try
        {
            if (Appearance == null) return;
            var view = Appearance.GetView();
            if (view == null) return;
            var viewer = view.GetCurrentSkillViewer();
            if (viewer == null) return;
            var targets = viewer.GetCurrentTargets();
            if (targets == null) return;

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target != null && target.transform != null) into.Add(target.transform.position);
            }
        }
        catch { }
    }

    [HideFromIl2Cpp]
    private void PositionVfx(VfxCue cue)
    {
        if (cue.ActiveInstance == null) return;
        var instance = cue.ActiveInstance.transform;
        var offset = new Vector3(cue.OffsetX, cue.OffsetY, cue.OffsetZ);

        switch (cue.SpawnTarget)
        {
            case VfxSpawnTarget.Enemy:
                var enemyTransform = GetFirstTargetTransform();
                if (enemyTransform != null)
                {
                    instance.SetParent(enemyTransform);
                    instance.localPosition = offset;
                }
                break;

            case VfxSpawnTarget.Center:
                instance.SetParent(null);
                var selfPos = SandboxRenderer.transform.position;
                var targetPos = selfPos;
                var target = GetFirstTargetTransform();
                if (target != null) targetPos = target.position;
                instance.position = (selfPos + targetPos) / 2f + offset;
                break;

            case VfxSpawnTarget.Self:
            default:
                instance.SetParent(SandboxRenderer.transform);
                instance.localPosition = offset;
                break;
        }
    }

    void Update()
    {
        MirrorOriginalMaterial();
        SyncToMaster();

        // Everything below reads the slave director's clock, and none of it means anything while
        // the game's own motion is playing.
        if (!IsModdedSkillActive || SlaveDirector == null) return;

        double t = SlaveDirector.time;

        StepSpriteFrame(t);
        TickSoundCues((float)t);
        TickVfxCues((float)t);
    }

    /// <summary>Shares the original renderer's material and alpha, so every material effect the
    /// game applies to the character - fades, tints, damage flashes - follows onto the sidecar.
    /// </summary>
    [HideFromIl2Cpp]
    private void MirrorOriginalMaterial()
    {
        if (!IsModdedSkillActive || OriginalRenderer == null || SandboxRenderer == null) return;

        SandboxRenderer.sharedMaterial = OriginalRenderer.sharedMaterial;

        var color = SandboxRenderer.color;
        color.a = OriginalRenderer.color.a;
        SandboxRenderer.color = color;
    }

    /// <summary>Drives the slave director off the master's clock rather than letting it run free.
    /// Only for motions that asked for it (ShouldSync) - skills and parries, whose hit timings have
    /// to stay locked to the game's own animation.</summary>
    [HideFromIl2Cpp]
    private void SyncToMaster()
    {
        if (MasterDirector == null || SlaveDirector == null) return;
        if (MasterDirector.state != PlayState.Playing || !IsModdedSkillActive || !ShouldSync) return;

        SlaveDirector.time = MasterDirector.time;
        SlaveDirector.Evaluate();
    }

    /// <summary>Puts the right sprite on screen for a bundle-free motion.</summary>
    [HideFromIl2Cpp]
    private void StepSpriteFrame(double t)
    {
        if (Frames == null || Frames.Length == 0) return;

        // Looping motions wrap back to 0; the cursor only walks forward otherwise, so this is
        // O(1) amortised rather than a search. SpriteMotionSpec.FrameIndexAt encodes the same
        // rule declaratively and is what the tests pin.
        if (_frameCursor >= Frames.Length || t < FrameTimes[_frameCursor])
            _frameCursor = 0;

        while (_frameCursor + 1 < Frames.Length && FrameTimes[_frameCursor + 1] <= t)
            _frameCursor++;

        SandboxRenderer.sprite = Frames[_frameCursor];
    }

    /// <summary>Fires each sound cue as its time is reached, and stops the ones that have run their
    /// authored duration. Cues are structs, so each edit is written back into the list.</summary>
    [HideFromIl2Cpp]
    private void TickSoundCues(float t)
    {
        for (int i = 0; i < SoundCues.Count; i++)
        {
            var cue = SoundCues[i];

            if (!cue.Triggered && t >= cue.StartTime)
            {
                cue.Triggered = true;
                float sfxVol = SoundManager.Instance != null ? SoundManager.Instance.Volume_SFX : 1f;
                cue.ActiveChannel = FMODAudioUtil.PlaySound(cue.WavData, cue.ClipIn, sfxVol);
                SoundCues[i] = cue;
                Logger.LogInfo($"[SidecarSync] Fired FMOD sound cue at t={t:F3}s (clipIn={cue.ClipIn:F3}s, dur={cue.Duration:F3}s)");
            }

            // Duration 0 means "play the whole clip", so only a cue that named one is cut short.
            if (cue.Triggered && cue.Duration > 0f && cue.ActiveChannel.hasHandle()
                && t >= cue.StartTime + cue.Duration)
            {
                cue.ActiveChannel.stop();
                cue.ActiveChannel = default;
                SoundCues[i] = cue;
            }
        }
    }

    /// <summary>Switches on each VFX cue as its time is reached and destroys it at the end of its
    /// duration. The instance is usually pre-built and inactive; a cue that has none builds one.
    /// </summary>
    [HideFromIl2Cpp]
    private void TickVfxCues(float t)
    {
        for (int i = 0; i < VfxCues.Count; i++)
        {
            var cue = VfxCues[i];

            if (!cue.Triggered && t >= cue.StartTime)
            {
                cue.Triggered = true;
                if (cue.ActiveInstance != null)
                {
                    PositionVfx(cue);
                    cue.ActiveInstance.SetActive(true);
                }
                else if (cue.Prefab != null)
                {
                    cue.ActiveInstance = UnityEngine.Object.Instantiate(cue.Prefab, SandboxRenderer.transform);
                    PositionVfx(cue);
                }
                VfxCues[i] = cue;
            }

            if (cue.Triggered && cue.ActiveInstance != null && cue.Duration > 0f
                && t >= cue.StartTime + cue.Duration)
            {
                UnityEngine.Object.Destroy(cue.ActiveInstance);
                cue.ActiveInstance = null;
                VfxCues[i] = cue;
            }
        }
    }
}
