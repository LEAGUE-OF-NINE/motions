using System;
using Addressable;
using SD;
using UnityEngine;

namespace Motions;

/// <summary>
/// Builds a CharacterAppearance for a "!motions_" ID by cloning a vanilla one.
///
/// Building the component from scratch would mean populating some sixty fields until Initialize
/// stops crashing, and an unset List in IL2CPP is an uncatchable null deref rather than an
/// exception. The donor arrives fully wired by the game; only its appearance ID is overwritten,
/// and the sidecar draws over its visuals. It also carries tuned character_weight, character_radius
/// and transform_Height, which is what makes knockback, targeting and camera framing behave.
/// </summary>
public static class AppearanceFactory
{
    private static readonly string[] SkinTypes = { "SD_Personality", "SD_Enemy", "SD_Abnormality" };

    public static CharacterAppearance Create(BattleUnitView view, string appearanceID, Transform parent)
    {
        try
        {
            if (!MotionData.CustomAppearanceBases.TryGetValue(appearanceID, out string donorID))
            {
                // Returning null here crashes the game a few frames later, inside BattleUnitView.Init
                // with an index-out-of-range that names nothing useful - so this message is the only
                // chance to say what actually went wrong. Listing what IS registered distinguishes
                // "typo in the ID" from "discovery never ran for this mod".
                string known = MotionData.CustomAppearanceBases.Count == 0
                    ? "(none registered at all - is there a motion_appearances/ folder, and does it contain appearance.json?)"
                    : string.Join(", ", MotionData.CustomAppearanceBases.Keys);

                Logger.LogError($"[Appearance] No registered base for '{appearanceID}'. Registered: {known}");
                return null;
            }

            Il2CppSystem.ValueTuple<GameObject, DelegateEvent> donor = null;

            foreach (var skinType in SkinTypes)
            {
                try
                {
                    donor = AddressableManager.Instance.LoadAssetSync<GameObject>(
                        skinType, donorID, parent, null,
                        new Il2CppSystem.Nullable<Vector3>(parent.position));
                }
                catch (Exception)
                {
                    continue;
                }

                if (donor?.Item1 != null) break;
            }

            var appearance = donor?.Item1?.GetComponent<CharacterAppearance>();
            if (appearance == null)
            {
                Logger.LogError($"[Appearance] Could not load donor '{donorID}' for '{appearanceID}'. " +
                                "Check that the base ID in appearance.json is a real appearance.");
                return null;
            }

            appearance.name = appearanceID;

            // Initialize runs BEFORE the ID is overwritten, so the appearance still carries the
            // donor's ID at that moment. That is fine and is why the Initialize postfix reads
            // unitView._unitModel.GetAppearanceID() first - the unit model already knows the custom
            // ID. This is the order Lethe uses and is known to work; do not reorder these two lines.
            appearance.Initialize(view);
            appearance.charInfo.appearanceID = appearanceID;

            HideDonorVisuals(appearance);

            Logger.LogWarning($"[Appearance] Built '{appearanceID}' from donor '{donorID}'.");
            return appearance;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Appearance] Failed building '{appearanceID}': {ex}");
            return null;
        }
    }

    /// <summary>
    /// A cloned donor brings its own parts, effect renderers and default effect objects. The sidecar
    /// only replaces the main renderer, so without this the donor's arms and sparks draw through.
    /// The list is empirical: if something still shows, it is one more renderer to add here.
    /// </summary>
    private static void HideDonorVisuals(CharacterAppearance appearance)
    {
        try
        {
            var parts = appearance.Sprenderer_charactermotion_Parts;
            if (parts != null)
                foreach (var renderer in parts)
                    if (renderer != null) renderer.enabled = false;

            var effects = appearance.sprenderer_charactermotion_Effects;
            if (effects != null)
                foreach (var renderer in effects)
                    if (renderer != null) renderer.enabled = false;

            var defaults = appearance.tarnsform_defaultEffects;
            if (defaults != null)
                foreach (var effect in defaults)
                    if (effect != null) effect.SetActive(false);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Appearance] Could not hide donor visuals: {ex}");
        }
    }
}
