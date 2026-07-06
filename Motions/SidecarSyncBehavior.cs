using System;
using System.Collections.Generic;
using FMOD;
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

    private Transform GetFirstTargetTransform()
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

    private void PositionVfx(VfxCue cue)
    {
        if (cue.ActiveInstance == null) return;
        var t = cue.ActiveInstance.transform;
        var offset = new Vector3(cue.OffsetX, cue.OffsetY, cue.OffsetZ);

        switch (cue.SpawnTarget)
        {
            case VfxSpawnTarget.Enemy:
                var enemyTransform = GetFirstTargetTransform();
                if (enemyTransform != null)
                {
                    t.SetParent(enemyTransform);
                    t.localPosition = offset;
                }
                break;

            case VfxSpawnTarget.Center:
                t.SetParent(null);
                var selfPos = SandboxRenderer.transform.position;
                var targetPos = selfPos;
                var target = GetFirstTargetTransform();
                if (target != null) targetPos = target.position;
                t.position = (selfPos + targetPos) / 2f + offset;
                break;

            case VfxSpawnTarget.Self:
            default:
                t.SetParent(SandboxRenderer.transform);
                t.localPosition = offset;
                break;
        }
    }

    void Update()
    {
        if (IsModdedSkillActive && OriginalRenderer != null && SandboxRenderer != null)
        {
            var color = SandboxRenderer.color;
            color.a = OriginalRenderer.color.a;
            SandboxRenderer.color = color;
        }

        if (MasterDirector != null && SlaveDirector != null)
        {
            if (MasterDirector.state == PlayState.Playing && IsModdedSkillActive && ShouldSync)
            {
                SlaveDirector.time = MasterDirector.time;
                SlaveDirector.Evaluate();
            }
        }

        // ---- Sound cues ----
        if (IsModdedSkillActive && SlaveDirector != null && SoundCues.Count > 0)
        {
            float currentTime = (float)SlaveDirector.time;
            for (int i = 0; i < SoundCues.Count; i++)
            {
                var cue = SoundCues[i];

                if (!cue.Triggered && currentTime >= cue.StartTime)
                {
                    cue.Triggered = true;
                    float sfxVol = SoundManager.Instance != null ? SoundManager.Instance.Volume_SFX : 1f;
                    cue.ActiveChannel = FMODAudioUtil.PlaySound(cue.WavData, cue.ClipIn, sfxVol);
                    SoundCues[i] = cue;
                    Logger.LogInfo($"[SidecarSync] Fired FMOD sound cue at t={currentTime:F3}s (clipIn={cue.ClipIn:F3}s, dur={cue.Duration:F3}s)");
                }

                if (cue.Triggered && cue.Duration > 0f && cue.ActiveChannel.hasHandle())
                {
                    float endTime = cue.StartTime + cue.Duration;
                    if (currentTime >= endTime)
                    {
                        cue.ActiveChannel.stop();
                        cue.ActiveChannel = default;
                        SoundCues[i] = cue;
                    }
                }
            }
        }

        // ---- VFX cues ----
        if (IsModdedSkillActive && SlaveDirector != null && VfxCues.Count > 0)
        {
            float currentTime = (float)SlaveDirector.time;
            for (int i = 0; i < VfxCues.Count; i++)
            {
                var cue = VfxCues[i];

                if (!cue.Triggered && currentTime >= cue.StartTime)
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

                if (cue.Triggered && cue.ActiveInstance != null && cue.Duration > 0f)
                {
                    float endTime = cue.StartTime + cue.Duration;
                    if (currentTime >= endTime)
                    {
                        UnityEngine.Object.Destroy(cue.ActiveInstance);
                        cue.ActiveInstance = null;
                        VfxCues[i] = cue;
                    }
                }
            }
        }
    }
}
