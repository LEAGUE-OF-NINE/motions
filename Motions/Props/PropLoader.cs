using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Motions;

/// <summary>Reads the props array out of a character's CharacterVFX.json and loads each prop's art
/// up front, so nothing decodes a PNG mid-battle. Also builds instances, which PropRig and
/// PropWorld both need and neither should own.</summary>
public static class PropLoader
{
    /// <summary>Sorting orders straddling the sandbox renderer's 999, so a front prop draws over
    /// the character and a back prop behind it with no thought from the author.</summary>
    private const int DefaultFrontOrder = 1000;
    private const int DefaultBackOrder = 998;

    public static void LoadCharacterProps(string charDir, string appearanceID)
    {
        try
        {
            if (!MotionData.CustomAppearanceVFX.TryGetValue(appearanceID, out string jsonPath))
                return;

            // Parsed here rather than lazily so the gates can answer HasProps before a battle
            // starts. CharVFXParse.GetVFX reads the same cache, so this costs nothing.
            var vfx = CharVFXParse.Parse(jsonPath);
            MotionData.AppearanceVFXCache[appearanceID] = vfx;

            if (vfx == null || vfx.props == null || vfx.props.Length == 0) return;

            // Folders are relative to the JSON that declared them, not to charDir: nothing stores
            // a character's root, and "relative to the file you wrote it in" needs no new state.
            string root = Path.GetDirectoryName(jsonPath);

            var usable = new List<PropEntry>();

            foreach (var entry in vfx.props)
            {
                string complaint = PropSpec.Validate(entry);
                if (complaint != null)
                {
                    Logger.LogError($"[Props] {appearanceID}: prop rejected - {complaint}. Skipping.");
                    continue;
                }

                PropSpec.Normalize(entry);

                if (!string.IsNullOrEmpty(entry.folder) && !LoadArt(root, appearanceID, entry.folder))
                    continue;

                usable.Add(entry);
            }

            if (usable.Count == 0) return;

            MotionData.Props[appearanceID] = usable.ToArray();
            Logger.LogWarning($"[Props] Loaded {usable.Count} prop(s) for {appearanceID}.");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"[Props] Failed loading props for {appearanceID}: {ex}");
        }
    }

    private static bool LoadArt(string root, string appearanceID, string folder)
    {
        string key = appearanceID + "/" + folder;
        if (MotionData.PropArt.ContainsKey(key)) return true;

        string dir = Path.Combine(root, folder);
        if (!Directory.Exists(dir))
        {
            Logger.LogError($"[Props] {appearanceID}: folder '{folder}' not found at '{dir}'. Skipping.");
            return false;
        }

        var art = SpriteMotionLoader.Build(dir, $"prop {folder}");
        if (art == null || art.Sprites == null || art.Sprites.Length == 0)
        {
            Logger.LogError($"[Props] {appearanceID}: folder '{folder}' produced no usable frames. Skipping.");
            return false;
        }

        MotionData.PropArt[key] = art;
        Logger.LogWarning($"[Props] {appearanceID}: '{folder}' loaded, " +
                          $"{art.Sprites.Length} frames, {art.Duration:F2}s.");
        return true;
    }

    /// <summary>Builds one instance. Returns null and logs when the art is missing - the caller's
    /// cue to skip this prop rather than spawn an invisible object.</summary>
    public static GameObject CreateInstance(string appearanceID, PropEntry entry,
                                            out SpriteMotion art, out SpriteRenderer renderer)
    {
        art = null;
        renderer = null;

        if (!string.IsNullOrEmpty(entry.folder))
        {
            if (!MotionData.PropArt.TryGetValue(appearanceID + "/" + entry.folder, out art) || art == null)
            {
                Logger.LogError($"[Props] {appearanceID}: no loaded art for '{entry.folder}'.");
                return null;
            }

            var instance = new GameObject($"Prop_{entry.folder}");
            renderer = instance.AddComponent<SpriteRenderer>();
            renderer.sprite = art.Sprites[0];
            return instance;
        }

        var prefab = CharVFXParse.GetPrefab(appearanceID, entry.prefab);
        if (prefab == null)
        {
            Logger.LogError($"[Props] {appearanceID}: prefab '{entry.prefab}' not found in any bundle.");
            return null;
        }

        return Object.Instantiate(prefab);
    }

    /// <summary>Same off/on kick CharVFXParse gives an instantiated VFX prefab: bundle prefabs are
    /// commonly authored inactive, and particle systems need the re-trigger to play. Call only after
    /// parenting, positioning and scaling - a world-space particle system re-triggered at the
    /// prefab's own origin emits its first frame in the wrong place. No-op for sprite-built
    /// instances (folder set): a new GameObject is already active, and toggling would race
    /// PropRig's Hidden mirror.</summary>
    public static void ActivatePrefab(GameObject instance, PropEntry entry)
    {
        if (instance == null || !string.IsNullOrEmpty(entry.folder)) return;

        instance.SetActive(false);
        instance.SetActive(true);
    }

    /// <summary>
    /// Builds one prop, ready to use: created, parented, placed, scaled, sorted and kicked, in the
    /// order those steps have to happen in. Returns null and logs when the art is missing or
    /// anything throws - every caller's cue to stop trying.
    /// <para>
    /// A null <paramref name="parent"/> leaves the instance unparented and reads
    /// <paramref name="position"/> as a world point; otherwise it is local to the parent. A unit
    /// ring hangs off an effect root while world and target props do not, and that is the only
    /// difference between the three call sites - hence one sequence here, not three copies. The
    /// ordering is not arbitrary: scale after parenting, because SetParent preserves world scale,
    /// and ActivatePrefab last, because a re-triggered world-space particle system emits its first
    /// frame wherever the instance stands then.
    /// </para>
    /// </summary>
    public static GameObject Build(string appearanceID, PropEntry entry, Transform parent,
                                   Vector3 position, out SpriteMotion art, out SpriteRenderer renderer)
    {
        art = null;
        renderer = null;

        GameObject instance = null;
        try
        {
            instance = CreateInstance(appearanceID, entry, out art, out renderer);
            if (instance == null) return null;

            instance.transform.SetParent(parent);
            instance.transform.localRotation = Quaternion.identity;

            if (parent != null) instance.transform.localPosition = position;
            else instance.transform.position = position;

            instance.transform.localScale = Vector3.one * (float)entry.scale;

            ApplySorting(instance, entry);
            ActivatePrefab(instance, entry);

            return instance;
        }
        catch (System.Exception ex)
        {
            // Destroyed while still in scope: a half-built instance orphaned by a throw from
            // ApplySorting would otherwise sit in the scene with no reference to it.
            Logger.LogError($"[Props] {appearanceID}: building '{entry.folder ?? entry.prefab}' threw: {ex}");
            if (instance != null) Object.Destroy(instance);

            art = null;
            renderer = null;
            return null;
        }
    }

    /// <summary>
    /// Steps a folder-built prop's art to whatever frame its own age lands on, writing the sprite
    /// only when that frame changes. Prop art runs at a dozen frames a second against a sixty frame
    /// loop, so most calls would re-assign the sprite already showing - and SpriteRenderer.sprite is
    /// a heavier setter than a transform write.
    /// <para>
    /// <paramref name="frame"/> is the caller's memory of the last frame written; pass the field
    /// beside the renderer. Prefab-built props have no art and fall straight through.
    /// </para>
    /// </summary>
    public static void Advance(SpriteMotion art, SpriteRenderer renderer, double age, ref int frame)
    {
        if (art == null || renderer == null || art.Duration <= 0) return;

        int next = SpriteMotionSpec.FrameIndexAt(art.Times, age % art.Duration);
        if (next < 0 || next == frame) return;

        frame = next;
        renderer.sprite = art.Sprites[next];
    }

    /// <summary>Sorting layer and order for every renderer on the instance. Unit props are parented
    /// to an effect root, a plain transform carrying no sorting, so they need this as much as world
    /// props do.</summary>
    public static void ApplySorting(GameObject instance, PropEntry entry)
    {
        if (instance == null) return;

        int order = entry.order != 0
            ? entry.order
            : (entry.front ? DefaultFrontOrder : DefaultBackOrder);

        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (!string.IsNullOrEmpty(entry.layer)) renderer.sortingLayerName = entry.layer;
            renderer.sortingOrder = order;
        }
    }
}
