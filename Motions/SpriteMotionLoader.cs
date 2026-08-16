using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Motions;

/// <summary>
/// Builds <see cref="SpriteMotion"/>s from a character's motions/ folder. Everything here degrades
/// rather than throws: a bad frame is skipped, a bad motion is dropped, and the caller falls back to
/// the bundle path.
/// </summary>
public static class SpriteMotionLoader
{
    public static void LoadCharacterFolder(string charDir, string appearanceID)
    {
        try
        {
            string motionsRoot = Path.Combine(charDir, "motions");
            if (!Directory.Exists(motionsRoot)) return;

            foreach (string motionDir in Directory.GetDirectories(motionsRoot))
            {
                string folderName = Path.GetFileName(motionDir);
                var (name, index) = SpriteMotionSpec.ParseFolderName(
                    folderName, n => Enum.TryParse<MOTION_DETAIL>(n, true, out _));

                if (!Enum.TryParse<MOTION_DETAIL>(name, true, out var detail))
                {
                    Logger.LogWarning($"[SpriteMotion] '{folderName}' is not a known motion name, skipping.");
                    continue;
                }

                // ponytail: coins only in v1. Non-skill motions ignore _N variants; bundles still do them.
                if (index > 0 && !name.StartsWith("S", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogWarning($"[SpriteMotion] '{folderName}' - variants are bundle-only, skipping.");
                    continue;
                }

                var motion = Build(motionDir, folderName);
                if (motion == null) continue;

                MotionData.SpriteMotions[MotionKey.Create(appearanceID, detail, index)] = motion;
                MotionData.SpriteMotionAppearances.Add(appearanceID);

                Logger.LogWarning($"[SpriteMotion] Loaded '{folderName}' for {appearanceID}: " +
                                  $"{motion.Sprites.Length} frames, {motion.Duration:F2}s, {motion.Sounds.Count} sfx");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SpriteMotion] Failed loading sprite motions for {appearanceID}: {ex}");
        }
    }

    /// <summary>
    /// Builds one animation from a folder of PNGs. Public because props reuse it: a prop folder
    /// and a motion folder have the same shape, and duplicating the decode path would mean two
    /// places to fix the next time an art tool exports something unusual.
    /// </summary>
    public static SpriteMotion Build(string motionDir, string label)
    {
        string jsonPath = Path.Combine(motionDir, "animation.json");
        AnimationSpec spec;

        if (File.Exists(jsonPath))
        {
            spec = SpriteMotionSpec.Parse(File.ReadAllText(jsonPath), out string error);
            if (spec == null)
            {
                Logger.LogError($"[SpriteMotion] '{label}/animation.json' rejected - {error}. Falling back to bundle.");
                return null;
            }
        }
        else
        {
            var pngs = new List<string>();
            foreach (string p in Directory.GetFiles(motionDir, "*.png"))
                pngs.Add(Path.GetFileName(p));

            if (pngs.Count == 0) return null;

            spec = SpriteMotionSpec.DefaultSpec(pngs);
            Logger.LogInfo($"[SpriteMotion] '{label}' has no animation.json; " +
                           $"using {pngs.Count} PNGs at {SpriteMotionSpec.DefaultFps}fps.");
        }

        bool point = string.Equals(spec.filter, "point", StringComparison.OrdinalIgnoreCase);

        var sprites = new List<Sprite>(spec.frames.Count);
        var textures = new List<Texture2D>(spec.frames.Count);
        var times = new List<double>(spec.frames.Count);

        foreach (var frame in spec.frames)
        {
            string pngPath = Path.Combine(motionDir, frame.sprite);
            if (!File.Exists(pngPath))
            {
                Logger.LogError($"[SpriteMotion] '{label}' frame '{frame.sprite}' not found, skipping.");
                continue;
            }

            Texture2D tex;
            try
            {
                // NOT ImageConversion.LoadImage - every overload forwards to a ReadOnlySpan variant
                // this game build's mscorlib does not expose, and throws MissingMethodException.
                // Lethe's decoder exists precisely because of that.
                tex = Lethe.Patches.PngDecoder.Load(File.ReadAllBytes(pngPath));
            }
            catch (Exception ex)
            {
                // The decoder handles 8-bit RGB/RGBA/grayscale only - no indexed, 16-bit or
                // interlaced. Several art tools export indexed by default, so name the file.
                Logger.LogError($"[SpriteMotion] '{label}' frame '{frame.sprite}' could not be decoded " +
                                $"({ex.Message}). Re-save it as a non-indexed 8-bit PNG. Skipping.");
                continue;
            }

            if (tex == null)
            {
                Logger.LogError($"[SpriteMotion] '{label}' frame '{frame.sprite}' decoded to nothing, skipping.");
                continue;
            }

            tex.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            double effectivePpu = SpriteMotionSpec.EffectivePpu(spec.ppu, frame.scale);
            var pivot = new Vector2(
                (float)SpriteMotionSpec.PivotX(frame.offset[0], tex.width, effectivePpu),
                (float)SpriteMotionSpec.PivotY(frame.offset[1], tex.height, effectivePpu));

            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, (float)effectivePpu);
            sprite.name = frame.sprite;

            // Loading happens in a LoadScene PREFIX, so the battle scene load is still ahead of us,
            // and a runtime-created asset with no scene owner gets collected by the automatic
            // Resources.UnloadUnusedAssets() during it. The symptom is not an error: the renderer
            // stays enabled with a valid material and simply draws nothing, because assigning a
            // destroyed Unity object reads back as null.
            //
            // hideFlags is the load-bearing line - it carries DontUnloadUnusedAsset. DontDestroyOnLoad
            // alone was tried and does NOT protect assets; it is kept only because it costs a line and
            // this cost three test cycles to find. Assets flagged this way are never auto-collected,
            // so MotionData.UnloadAll must Destroy them explicitly.
            tex.hideFlags = HideFlags.HideAndDontSave;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(tex);
            UnityEngine.Object.DontDestroyOnLoad(sprite);

            sprites.Add(sprite);
            textures.Add(tex);
            times.Add(frame.t);
        }

        if (sprites.Count == 0)
        {
            Logger.LogError($"[SpriteMotion] '{label}' produced no usable frames. Falling back to bundle.");
            return null;
        }

        return new SpriteMotion
        {
            Duration = spec.duration,
            Sprites = sprites.ToArray(),
            Times = times.ToArray(),
            Textures = textures.ToArray(),
            Sounds = LoadSounds(motionDir, spec, label)
        };
    }

    private static List<SoundCue> LoadSounds(string motionDir, AnimationSpec spec, string label)
    {
        var cues = new List<SoundCue>();

        foreach (var s in spec.sfx)
        {
            string path = Path.Combine(motionDir, s.file);
            if (!File.Exists(path))
            {
                Logger.LogError($"[SpriteMotion] '{label}' sfx '{s.file}' not found, skipping.");
                continue;
            }

            cues.Add(new SoundCue
            {
                StartTime = (float)s.t,
                ClipIn = (float)s.clipIn,
                Duration = (float)s.duration,
                WavData = File.ReadAllBytes(path),
                Triggered = false
            });
        }

        return cues;
    }
}
