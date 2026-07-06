using System;
using UnityEngine;

namespace Motions;

public struct SoundCue
{
    public float StartTime;   // When in the timeline this cue fires (seconds)
    public float ClipIn;      // Offset into the audio file where playback starts (clip.clipIn)
    public float Duration;    // How long the clip plays for (clip.duration); 0 = play to end
    public byte[] WavData;    // Pre-converted WAV bytes, played via FMOD Core
    public bool Triggered;
    public FMOD.Channel ActiveChannel; // Filled at runtime so we can stop it when done
}

public enum VfxSpawnTarget
{
    Self,
    Enemy,
    Center
}

public struct VfxCue
{
    public float StartTime;
    public float Duration;
    public GameObject Prefab;
    public bool Triggered;
    public GameObject ActiveInstance;
    public VfxSpawnTarget SpawnTarget;
    public float OffsetX;
    public float OffsetY;
    public float OffsetZ;
}

public struct MotionKey : IEquatable<MotionKey>
{
    public string AppearanceID;
    public MOTION_DETAIL Motion;
    public int Index;

    public bool Equals(MotionKey other)
    {
        return string.Equals(AppearanceID, other.AppearanceID) &&
               Motion == other.Motion &&
               Index == other.Index;
    }

    public override bool Equals(object obj)
        => obj is MotionKey other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(AppearanceID ?? "", Motion, Index);
    }
}

[System.Serializable]
public class SkillPhase
{
    public string type;
    public double start;
    public double end;
    public int steps;
    public Vec3Data move;
    public bool isRefreshDir;
    public DamageData damage;
    public SturnData sturn;
}

[System.Serializable]
public class DamageData
{
    public int multiHit = 1;
    public bool isUpAttack = true;
    public float multiHitDuration = 0f;
}

[System.Serializable]
public class SturnData
{
    public string sturnType = "KNOCKBACK";
    public string sturnDir = "DIR_TOTARGET";
    public string sturnTiming = "ALL";
    public float forcePower = 5.0f;
    public float randomPower = 5.0f;
    public float airborneAngle = 0.0f;
    public bool isRotateTarget = false;
    public float targetRotateAngle = 0.0f;
}

[System.Serializable]
public class HitCheckerData
{
    public double time;
    public float isNextMotionCoinDelay;
}

[System.Serializable]
public class ZoomData
{
    public double start;
    public double duration;
    
    public bool attacker = true;
    public bool targets = true;
    public float between = 0f;
    public float axisY = 0f;
    public float size = -2f;
    public float zoomDuration = -1f;
    public bool isRelative = true;
    public float focusSpeed = 0.2f;
    public string easeType = "Unset";
}

[System.Serializable]
public class CoinData
{
    public double totalDuration;
    public SkillPhase[] phases;
    public HitCheckerData[] hitCheckers;
    public ZoomData[] zooms;
    public RotateData[] rotates;
    public ShakeData[] shakes;
    public int[] vfx; // indices into the original VFX clip list (1-indexed)
}

[System.Serializable]
public class RotateData
{
    public double start;
    public double duration;
    public Vec3Data targetAngle;
    public float focusRotateSpeed;
    public string easeType = "Unset";
}

[System.Serializable]
public class ShakeData
{
    public double start;
    public double duration;
    public float intensity = 0.15f;
    public float decay = 3.0f;
}

[System.Serializable]
public class SkillData
{
    public CoinData[] coins;
}

[System.Serializable]
public class Vec3Data
{
    public float x;
    public float y;
    public float z;

    public Vector3 ToVector3()
    {
        return new Vector3(x, y, z);
    }
}